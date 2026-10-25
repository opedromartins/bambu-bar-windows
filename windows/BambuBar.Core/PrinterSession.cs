using System.Collections.Concurrent;
using System.Buffers;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Formatter;
using static BambuBar.Core.ReportDocument;

namespace BambuBar.Core;

public sealed class PrinterSession : IAsyncDisposable
{
    public PrinterConfig Config { get; }
    public PrinterStatus? Status { get; private set; }
    public ConnectionPhase Phase { get; private set; }
    public event Action? Changed;
    private readonly string accessCode;
    private Catalogs catalogs;
    private readonly bool emulator;
    private readonly bool containerTest;
    private readonly PrinterTrust trust;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> pending = new();
    private readonly SemaphoreSlim sends = new(1);
    private readonly object reportLock = new();
    private readonly ReportDocument document = new();
    private IMqttClient? client;
    private Task? worker;
    private long sequence;
    private long lastMessageTicks;
    private bool certificateRejected;
    private bool authenticationRefused;
    private long refreshRequested;
    private readonly TimeSpan retryScale;

    public PrinterSession(PrinterConfig config, string accessCode, Catalogs catalogs,
        bool emulator = false, bool containerTest = false, TimeSpan? retryScale = null)
    {
        config.Validate(emulator, containerTest);
        if (accessCode.Length != 8 || !accessCode.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException(TextCatalog.Get("8-character access code"));
        Config = config; this.accessCode = accessCode; this.catalogs = catalogs;
        this.emulator = emulator; this.containerTest = containerTest;
        this.retryScale = retryScale ?? TimeSpan.FromSeconds(1);
        trust = new();
    }
    public void Start() { worker ??= RunAsync(lifetime.Token); }
    private void SetPhase(ConnectionPhase phase) { Phase = phase; Changed?.Invoke(); }
    public void Refresh() => Interlocked.Exchange(ref refreshRequested, 1);
    public void ChangeLanguage(string language) { catalogs = new(language); Refresh(); }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var delays = new[] { 2, 4, 8, 15, 30 };
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetPhase(ConnectionPhase.Connecting);
                certificateRejected = false;
                authenticationRefused = false;
                lock (reportLock) document.Reset();
                using var mqtt = new MqttClientFactory().CreateMqttClient();
                client = mqtt;
                mqtt.DisconnectedAsync += e =>
                {
                    if ((int)(e.ConnectResult?.ResultCode ?? 0) is 4 or 5 or 134 or 135) authenticationRefused = true;
                    return Task.CompletedTask;
                };
                mqtt.ApplicationMessageReceivedAsync += e =>
                {
                    HandleMessage(e.ApplicationMessage.Payload.ToArray());
                    return Task.CompletedTask;
                };
                var options = new MqttClientOptionsBuilder()
                    .WithClientId("bambubar-win-" + Guid.NewGuid().ToString("N")[..10])
                    .WithCleanSession()
                    .WithTcpServer(Config.Host, Config.Port)
                    .WithCredentials("bblp", accessCode)
                    .WithProtocolVersion(MqttProtocolVersion.V311)
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                    .WithTimeout(TimeSpan.FromSeconds(12))
                    .WithTlsOptions(tls => tls.UseTls().WithSslProtocols(SslProtocols.Tls12)
                        .WithCertificateValidationHandler(context =>
                        {
                            var valid = emulator && PrinterTrust.AllowEmulator(Config.Host, containerTest) ||
                                trust.Validate(context.Certificate as X509Certificate2, context.Chain, Config.Serial);
                            certificateRejected = !valid;
                            return valid;
                        })).Build();
                Interlocked.Exchange(ref lastMessageTicks, DateTime.UtcNow.Ticks);
                var result = await mqtt.ConnectAsync(options, cancellationToken);
                if ((int)result.ResultCode is 4 or 5 or 134 or 135) { SetPhase(ConnectionPhase.Unauthorized); break; }
                if ((int)result.ResultCode != 0) throw new InvalidOperationException($"MQTT {(int)result.ResultCode}");
                await mqtt.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter($"device/{Config.Serial}/report").Build(), cancellationToken);
                await PublishRaw(new JsonObject { ["info"] = new JsonObject { ["sequence_id"] = "0", ["command"] = "get_version" } }, cancellationToken);
                await RequestFull(cancellationToken);
                var fullAt = DateTime.UtcNow;
                var startedAt = fullAt;
                while (mqtt.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    var silence = DateTime.UtcNow - new DateTime(Interlocked.Read(ref lastMessageTicks), DateTimeKind.Utc);
                    if (Phase == ConnectionPhase.Online) attempt = 0;
                    if (silence.TotalSeconds > 95 || Phase == ConnectionPhase.Connecting && (DateTime.UtcNow - startedAt).TotalSeconds > 25) break;
                    if (Interlocked.Exchange(ref refreshRequested, 0) != 0 ||
                        (DateTime.UtcNow - fullAt).TotalMinutes > 5 || silence.TotalSeconds > 60 && (DateTime.UtcNow - fullAt).TotalSeconds > 10)
                    {
                        await RequestFull(cancellationToken);
                        fullAt = DateTime.UtcNow;
                    }
                }
                if (mqtt.IsConnected) await mqtt.DisconnectAsync(cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                if (certificateRejected) { SetPhase(ConnectionPhase.Certificate); break; }
                // MQTTnet exposes refusal codes on its connection exception. Do not retry credentials.
                if (authenticationRefused)
                { SetPhase(ConnectionPhase.Unauthorized); break; }
                SetPhase(ConnectionPhase.Offline);
            }
            finally
            {
                client = null;
                foreach (var completion in pending.Values) completion.TrySetException(new IOException(TextCatalog.Get("The connection to the printer closed.")));
                pending.Clear();
            }
            if (cancellationToken.IsCancellationRequested) break;
            SetPhase(ConnectionPhase.Offline);
            try { await Task.Delay(retryScale * delays[Math.Min(attempt++, delays.Length - 1)], cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }
    private void HandleMessage(byte[] payload)
    {
        Interlocked.Exchange(ref lastMessageTicks, DateTime.UtcNow.Ticks);
        var message = Decode(payload);
        if (message is null) return;
        foreach (var key in new[] { "print", "system" })
            if (message[key] is JsonObject body && body["result"] is not null &&
                pending.TryRemove(Text(body["sequence_id"]), out var completion))
            {
                if (Text(body["result"]).Equals("success", StringComparison.OrdinalIgnoreCase)) completion.TrySetResult(true);
                else completion.TrySetException(new IOException(Text(body["reason"]) is { Length: > 0 } reason ? reason : TextCatalog.Get("The printer refused the command.")));
            }
        lock (reportLock)
        {
            if (!document.Apply(message) || !document.HasStatus) return;
            var next = ReportParser.Parse(document, catalogs, PrinterModel.Find(Config.ModelCode, Config.Serial));
            if (Status is { } previous && next.Progress == previous.Progress && next.RemainingMinutes == previous.RemainingMinutes && next.State == previous.State && next.TaskId == previous.TaskId && next.Job == previous.Job)
                next = next with { UpdatedAt = previous.UpdatedAt };
            Status = next;
        }
        SetPhase(ConnectionPhase.Online);
    }
    private Task RequestFull(CancellationToken ct) => PublishRaw(new JsonObject {
        ["pushing"] = new JsonObject { ["sequence_id"] = "0", ["command"] = "pushall" } }, ct);
    private async Task PublishRaw(JsonObject message, CancellationToken ct)
    {
        var mqtt = client;
        if (mqtt?.IsConnected != true) throw new IOException(TextCatalog.Get("Printer offline"));
        await sends.WaitAsync(ct);
        try
        {
            await mqtt.PublishAsync(new MqttApplicationMessageBuilder().WithTopic($"device/{Config.Serial}/request")
                .WithPayload(message.ToJsonString()).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).Build(), ct);
        }
        finally { sends.Release(); }
    }
    public async Task CommandAsync(string command, int? speed = null, bool? light = null)
    {
        if (Phase != ConnectionPhase.Online || Status is null) throw new IOException(TextCatalog.Get("Printer offline"));
        if (command is not ("pause" or "resume" or "stop" or "print_speed" or "ledctrl")) throw new ArgumentException(TextCatalog.Get("The printer refused the command."));
        if (command != "ledctrl" && Status.ControlsLocked) throw new InvalidOperationException(TextCatalog.Get("The printer accepts this only in LAN Only mode with Developer Mode on (Settings → Network on its screen)."));
        if (command == "print_speed" && speed is not (>= 1 and <= 4)) throw new ArgumentException(TextCatalog.Get("Speed"));
        if (command == "ledctrl" && light is null) throw new ArgumentException(TextCatalog.Get("Light"));
        var id = Interlocked.Increment(ref sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var body = new JsonObject { ["command"] = command, ["sequence_id"] = id };
        if (speed is not null) body["param"] = speed.Value.ToString();
        if (command == "ledctrl")
        {
            body["led_node"] = "chamber_light"; body["led_mode"] = light == true ? "on" : "off";
            body["led_on_time"] = 500; body["led_off_time"] = 500; body["loop_times"] = 0; body["interval_time"] = 0;
        }
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await PublishRaw(new JsonObject { [command == "ledctrl" ? "system" : "print"] = body }, lifetime.Token);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
        }
        finally { pending.TryRemove(id, out _); }
    }
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        if (worker is not null) await worker;
        lifetime.Dispose(); sends.Dispose();
        trust.Dispose();
    }
}
