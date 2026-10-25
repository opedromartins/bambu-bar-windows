using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using BambuBar.Core;

namespace BambuBar.Tests;

// A deliberately tiny TLS MQTT fixture: tests transport failures and acknowledgements, not a replacement client.
internal sealed class TestBroker : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly X509Certificate2 certificate;
    private readonly Task server;
    private readonly ConcurrentBag<Task> connections = [];
    private TcpClient? current;
    private int connectPackets;
    public int ConnectPackets => Volatile.Read(ref connectPackets);
    public bool RefuseAuthentication { get; set; }
    public bool SuppressReports { get; set; }
    public string? CommandResult { get; set; } = "success";
    public PrinterConfig Config { get; }
    public TestBroker()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=SERIAL", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        listener.Start();
        Config = new("Fixture", "127.0.0.1", "SERIAL", "N7", ((IPEndPoint)listener.LocalEndpoint).Port);
        server = Accept();
    }
    public void DropConnection() => current?.Dispose();
    private async Task Accept()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var socket = await listener.AcceptTcpClientAsync(stop.Token);
                current = socket; connections.Add(Serve(socket));
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
    }
    private async Task Serve(TcpClient socket)
    {
        using (socket)
        using (var stream = new SslStream(socket.GetStream()))
        {
            try
            {
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                    ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 }, stop.Token);
                while (!stop.IsCancellationRequested)
                {
                    var header = new byte[1]; await stream.ReadExactlyAsync(header, stop.Token);
                    var type = header[0] >> 4;
                    var length = 0; var multiplier = 1;
                    do
                    {
                        await stream.ReadExactlyAsync(header.AsMemory(), stop.Token);
                        length += (header[0] & 127) * multiplier; multiplier *= 128;
                    } while ((header[0] & 128) != 0 && multiplier <= 2097152);
                    var body = new byte[length]; await stream.ReadExactlyAsync(body, stop.Token);
                    if (type == 1)
                    {
                        Interlocked.Increment(ref connectPackets);
                        await stream.WriteAsync(new byte[] { 0x20, 2, 0, RefuseAuthentication ? (byte)5 : (byte)0 }, stop.Token);
                        if (RefuseAuthentication) return;
                    }
                    else if (type == 8)
                        await stream.WriteAsync(new byte[] { 0x90, 3, body[0], body[1], 0 }, stop.Token);
                    else if (type == 12)
                        await stream.WriteAsync(new byte[] { 0xd0, 0 }, stop.Token);
                    else if (type == 14) return;
                    else if (type == 3 && body.Length > 4)
                    {
                        var topicLength = body[0] * 256 + body[1];
                        if (topicLength + 2 > body.Length)
                        {
                            await stream.WriteAsync(new byte[] { 0x90, 3, body[0], body[1], 0 }, stop.Token);
                            continue;
                        }
                        var json = ReportDocument.Decode(body.AsSpan(2 + topicLength));
                        if (json?["pushing"] is not null)
                        {
                            if (SuppressReports) continue;
                            await Publish(stream, "{malformed");
                            await Publish(stream, """{"print":{"msg":0,"gcode_state":"RUNNING","mc_percent":42}}""");
                        }
                        else if ((json?["print"] ?? json?["system"]) is JsonObject command && CommandResult is { } result)
                        {
                            var key = json!["print"] is not null ? "print" : "system";
                            await Publish(stream, new JsonObject { [key] = new JsonObject {
                                ["command"] = command["command"]!.DeepClone(), ["sequence_id"] = command["sequence_id"]!.DeepClone(),
                                ["result"] = result, ["reason"] = "fixture refusal" } }.ToJsonString());
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException or ObjectDisposedException) { }
        }
    }
    private async Task Publish(SslStream stream, string json)
    {
        var topic = Encoding.UTF8.GetBytes("device/SERIAL/report"); var payload = Encoding.UTF8.GetBytes(json);
        var bytes = new List<byte> { 0x30 }; var length = topic.Length + 2 + payload.Length;
        do { var next = (byte)(length % 128); length /= 128; bytes.Add((byte)(next | (length > 0 ? 128 : 0))); } while (length > 0);
        bytes.Add((byte)(topic.Length >> 8)); bytes.Add((byte)topic.Length); bytes.AddRange(topic); bytes.AddRange(payload);
        await stream.WriteAsync(bytes.ToArray(), stop.Token);
    }
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync(); listener.Stop(); current?.Dispose(); await server;
        await Task.WhenAll(connections); certificate.Dispose(); stop.Dispose();
    }
}
