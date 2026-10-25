using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using BambuBar.Core;
using static BambuBar.Tests.Program;

namespace BambuBar.Tests;

internal static class DiscoveryTests
{
    private static string Announcement(string location = "192.168.1.25", string serial = "22ETEST00000001", string name = "Workshop P2S") =>
        $"NOTIFY * HTTP/1.1\r\nNT: {PrinterDiscovery.DeviceType}\r\nUSN: {serial}\r\nLocation: {location}\r\nDevName.bambu.com: {name}\r\nDevModel.bambu.com: N7\r\nDevVersion.bambu.com: 01.03.00.00\r\nDevConnect.bambu.com: lan\r\n\r\n";
    public static void Packets()
    {
        var p = PrinterDiscovery.Parse(Announcement(), IPAddress.Parse("192.168.1.25"));
        Check(p is { Serial: "22ETEST00000001", Host: "192.168.1.25", Port: 8883, ModelCode: "N7", Name: "Workshop P2S", Firmware: "01.03.00.00", ConnectMode: "lan" }, "Bambu advertisement not decoded");
        var reply = Announcement("http://192.168.1.30:8080/description.xml", "uuid:22ETEST00000001::" + PrinterDiscovery.DeviceType)
            .Replace("NOTIFY * HTTP/1.1", "HTTP/1.1 200 OK").Replace("NT:", "st:").Replace("USN:", "Usn:");
        p = PrinterDiscovery.Parse(reply, IPAddress.Parse("192.168.1.30"));
        Check(p?.Host == "192.168.1.30" && p.Port == 8883 && p.Serial == "22ETEST00000001", "HTTP response, case or URI description port handling");
        p = PrinterDiscovery.Parse(Announcement("127.0.0.1:18801"), IPAddress.Loopback);
        Check(p?.Host == "127.0.0.1" && p.Port == 18801, "emulator port");
        p = PrinterDiscovery.Parse(Announcement(""), IPAddress.Parse("192.168.1.40"));
        Check(p?.Host == "192.168.1.40", "missing location must use sender");
    }
    public static void InvalidPackets()
    {
        foreach (var location in new[] { "evil.example", "http://evil.example/printer", "127.0.0.1:70000", "ftp://192.168.1.2", "http://user@192.168.1.2", "0.0.0.0", "255.255.255.255", "239.255.255.250" })
            Check(PrinterDiscovery.Parse(Announcement(location), IPAddress.Loopback) is null, $"invalid location accepted: {location}");
        Check(PrinterDiscovery.Parse(Announcement().Replace(PrinterDiscovery.DeviceType, "upnp:rootdevice"), IPAddress.Loopback) is null, "non-Bambu device");
        Check(PrinterDiscovery.Parse(Announcement(serial: "bad serial"), IPAddress.Loopback) is null, "invalid serial");
        Check(PrinterDiscovery.Parse(Announcement().Replace("Location:", "USN: OTHER\r\nLocation:"), IPAddress.Loopback) is null, "conflicting serial headers");
        Check(PrinterDiscovery.Parse(new string('a', 8193), IPAddress.Loopback) is null, "unbounded packet");
    }
    public static async Task UdpLifecycle()
    {
        var service = new PrinterDiscovery(emulator: true, listenerPorts: [0], lifetime: TimeSpan.FromSeconds(1));
        var disposed = false;
        try
        {
            service.Start(); service.Start();
            await Until(() => service.ListeningEndpoints.Count > 0);
            var endpoint = service.ListeningEndpoints[0];
            using var sender = new UdpClient(AddressFamily.InterNetwork);
            async Task Send(string text) => await sender.SendAsync(Encoding.UTF8.GetBytes(text), endpoint);
            await Send(Announcement("192.168.1.99")); await Task.Delay(100);
            Check(service.Printers.Count == 0, "emulator accepted non-loopback device");
            await Send(Announcement("127.0.0.1:18801"));
            await Until(() => service.Printers.Count == 1);
            await Send(Announcement("127.0.0.1:18801"));
            await Send(Announcement("127.0.0.1:18802", name: "Renamed"));
            await Until(() => service.Printers.SingleOrDefault()?.Port == 18802);
            Check(service.Printers.Count == 1 && service.Printers[0].Name == "Renamed", "duplicate serial or address update");
            await Until(() => service.Printers.Count == 0, 5);
            await service.DisposeAsync(); disposed = true;
            Check(service.ListeningEndpoints.Count == 0 && service.State == DiscoveryState.Stopped, "discovery sockets leaked");
            using var rebound = new UdpClient(endpoint);
        }
        finally { if (!disposed) await service.DisposeAsync(); }
    }
    public static async Task IdentityProbe()
    {
        await using var broker = new TestBroker();
        var refused = false;
        try { await PrinterIdentity.MatchesAsync(broker.Config, CancellationToken.None); }
        catch (AuthenticationException) { refused = true; }
        Check(refused && broker.ConnectPackets == 0, "untrusted DHCP endpoint accepted or credentials sent");
        Check(await PrinterIdentity.MatchesAsync(broker.Config, CancellationToken.None, emulator: true), "local emulator identity probe failed");
        Check(broker.ConnectPackets == 0, "identity probe must not send MQTT login");
    }
    public static async Task Fleet()
    {
        await using var service = new PrinterDiscovery(); service.Start();
        await Until(() => service.Printers.Count >= 11, 60);
        Check(service.Printers.Select(p => p.Serial).Distinct().Count() == 11, "fleet discovery duplicates or missing devices");
        Check(service.Printers.Any(p => p.ModelCode == "N7" && p.Port == 18801 && IPAddress.TryParse(p.Host, out _)), "fleet endpoint not discovered");
        Check(service.Printers.All(p => p.Serial != "01PF00000000012"), "offline fleet printer announced");
    }
}
