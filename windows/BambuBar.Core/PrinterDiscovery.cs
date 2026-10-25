using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace BambuBar.Core;

public sealed record DiscoveredPrinter(string Serial, string Host, int Port, string Name, string ModelCode,
    string? Firmware, string? ConnectMode, DateTimeOffset LastSeen)
{
    public PrinterConfig Config => new(Name, Host, Serial, ModelCode, Port);
}
public enum DiscoveryState { Stopped, Searching, NetworkUnavailable, Limited }

// Bambu's LAN advertisements and M-SEARCH replies. This service never requests or sends credentials.
public sealed class PrinterDiscovery(bool emulator = false, int[]? listenerPorts = null, TimeSpan? lifetime = null) : IAsyncDisposable
{
    public const string DeviceType = "urn:bambulab-com:device:3dprinter:1";
    public static readonly byte[] SearchPacket = Encoding.ASCII.GetBytes(
        $"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1990\r\nMAN: \"ssdp:discover\"\r\nMX: 3\r\nST: {DeviceType}\r\n\r\n");
    private static readonly IPAddress Group = IPAddress.Parse("239.255.255.250");
    private readonly object gate = new();
    private readonly Dictionary<string, DiscoveredPrinter> printers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<UdpClient> sockets = [];
    private readonly Dictionary<UdpClient, HashSet<IPAddress>> memberships = [];
    private readonly CancellationTokenSource stop = new();
    private Task? worker;
    private int requested = 1;
    private DiscoveryState state;
    public event Action? Changed;
    public event Action<DiscoveredPrinter>? Found;
    public DiscoveryState State { get { lock (gate) return state; } }
    public IReadOnlyList<DiscoveredPrinter> Printers
    { get { lock (gate) return printers.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray(); } }
    public IReadOnlyList<IPEndPoint> ListeningEndpoints
    { get { lock (gate) return sockets.Select(s => (IPEndPoint)s.Client.LocalEndPoint!).ToArray(); } }

    public void Start()
    {
        if (worker is not null) return;
        NetworkChange.NetworkAddressChanged += NetworkChanged;
        worker = Run(stop.Token);
    }
    public void Search() => Interlocked.Exchange(ref requested, 1);
    private void NetworkChanged(object? sender, EventArgs args) => Search();
    private void SetState(DiscoveryState value)
    {
        lock (gate) { if (value == state) return; state = value; }
        Changed?.Invoke();
    }
    private UdpClient? Open(int port)
    {
        var socket = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            socket.ExclusiveAddressUse = false;
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(emulator ? IPAddress.Loopback : IPAddress.Any, port));
            socket.EnableBroadcast = true;
            socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
            lock (gate) sockets.Add(socket);
            memberships[socket] = [];
            return socket;
        }
        catch (SocketException) { socket.Dispose(); return null; }
    }
    private async Task Run(CancellationToken ct)
    {
        var receivers = new List<Task>();
        var primary = Open(0); // Dedicated reply port coexists with Bambu Studio's listeners.
        if (primary is not null) receivers.Add(Receive(primary, ct));
        var listening = 0;
        foreach (var port in listenerPorts ?? (emulator ? [2121] : new[] { 2021, 1990 }))
            if (Open(port) is { } socket) { listening++; receivers.Add(Receive(socket, ct)); }
        try
        {
            var nextSearch = DateTimeOffset.MinValue;
            while (!ct.IsCancellationRequested)
            {
                if (Interlocked.Exchange(ref requested, 0) != 0 || DateTimeOffset.UtcNow >= nextSearch)
                {
                    var interfaces = emulator ? [] : Interfaces();
                    SetState(primary is null || listening == 0 ? DiscoveryState.Limited :
                        !emulator && interfaces.Length == 0 ? DiscoveryState.NetworkUnavailable : DiscoveryState.Searching);
                    if (!emulator && primary is not null) await SendSearch(primary, interfaces, ct);
                    nextSearch = DateTimeOffset.UtcNow.AddSeconds(30);
                }
                var removed = false;
                lock (gate)
                {
                    foreach (var serial in printers.Where(p => DateTimeOffset.UtcNow - p.Value.LastSeen > (lifetime ?? TimeSpan.FromMinutes(2))).Select(p => p.Key).ToArray())
                    { printers.Remove(serial); removed = true; }
                }
                if (removed) Changed?.Invoke();
                await Task.Delay(500, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            UdpClient[] snapshot; lock (gate) snapshot = sockets.ToArray();
            foreach (var socket in snapshot) socket.Dispose();
            await Task.WhenAll(receivers);
            lock (gate) sockets.Clear();
        }
    }
    private static (IPAddress Address, IPAddress Broadcast)[] Interfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => (a.Address, new IPAddress(a.Address.GetAddressBytes().Zip(a.IPv4Mask.GetAddressBytes(), (b, mask) => (byte)(b | ~mask)).ToArray())))
                .Distinct().ToArray();
        }
        catch (NetworkInformationException) { return []; }
    }
    private async Task SendSearch(UdpClient primary, (IPAddress Address, IPAddress Broadcast)[] interfaces, CancellationToken ct)
    {
        var addresses = interfaces.Select(i => i.Address).ToHashSet();
        foreach (var (socket, joined) in memberships)
        {
            if (socket == primary) continue;
            foreach (var old in joined.Where(ip => !addresses.Contains(ip)).ToArray())
            {
                try { socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership, new MulticastOption(Group, old)); }
                catch (SocketException) { }
                joined.Remove(old);
            }
            foreach (var address in addresses.Where(ip => !joined.Contains(ip)))
            {
                try { socket.JoinMulticastGroup(Group, address); joined.Add(address); }
                catch (SocketException) { }
            }
        }
        var successes = 0;
        foreach (var (address, broadcast) in interfaces)
        {
            try { primary.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes()); }
            catch (SocketException) { continue; }
            foreach (var destination in new[] { new IPEndPoint(Group, 2021), new IPEndPoint(Group, 1990), new IPEndPoint(broadcast, 2021) })
            {
                try { await primary.SendAsync(SearchPacket, destination, ct); successes++; }
                catch (SocketException) { }
            }
        }
        if (interfaces.Length > 0 && successes == 0) SetState(DiscoveryState.Limited);
    }
    private async Task Receive(UdpClient socket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try { packet = await socket.ReceiveAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException)
            {
                if (ct.IsCancellationRequested) break;
                try { await Task.Delay(250, ct); } catch (OperationCanceledException) { break; }
                continue;
            }
            if (packet.Buffer.Length > 8192) continue;
            var printer = Parse(Encoding.UTF8.GetString(packet.Buffer), packet.RemoteEndPoint.Address);
            if (printer is null || emulator && !PrinterTrust.AllowEmulator(printer.Host, false)) continue;
            bool changed;
            lock (gate)
            {
                changed = !printers.TryGetValue(printer.Serial, out var old) || old != printer with { LastSeen = old.LastSeen };
                printers[printer.Serial] = printer;
            }
            Found?.Invoke(printer);
            if (changed) Changed?.Invoke();
        }
    }
    public static DiscoveredPrinter? Parse(string text, IPAddress sender)
    {
        if (text.Length > 8192 || text.Contains('\0')) return null;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 2 || !(lines[0].Equals("NOTIFY * HTTP/1.1", StringComparison.OrdinalIgnoreCase) ||
            lines[0].StartsWith("HTTP/1.1 200 ", StringComparison.OrdinalIgnoreCase) || lines[0].Equals("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase))) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) break;
            var colon = line.IndexOf(':'); if (colon <= 0) continue;
            var key = line[..colon].Trim(); var value = line[(colon + 1)..].Trim();
            if (headers.TryGetValue(key, out var previous) && previous != value && (key.Equals("USN", StringComparison.OrdinalIgnoreCase) || key.Equals("Location", StringComparison.OrdinalIgnoreCase))) return null;
            headers.TryAdd(key, value);
        }
        if (!new[] { headers.GetValueOrDefault("NT"), headers.GetValueOrDefault("ST") }.Any(h => h?.Equals(DeviceType, StringComparison.OrdinalIgnoreCase) == true)) return null;
        var serial = headers.GetValueOrDefault("USN", "");
        if (serial.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase)) serial = serial[5..].Split("::")[0];
        if (serial.Length is < 1 or > 64 || !serial.All(char.IsAsciiLetterOrDigit)) return null;
        var location = headers.GetValueOrDefault("Location", "");
        IPAddress address; var port = 8883;
        if (location.Length == 0) address = sender;
        else if (IPAddress.TryParse(location, out var bare)) address = bare;
        else
        {
            var hasScheme = location.Contains("://", StringComparison.Ordinal);
            if (!Uri.TryCreate(hasScheme ? location : "mqtts://" + location, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https" or "mqtts") || uri.UserInfo.Length != 0 ||
                !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip)) return null;
            address = ip;
            if (uri.Scheme == "mqtts" && uri.Port != -1) port = uri.Port;
        }
        if (port is < 1 or > 65535 || address.Equals(IPAddress.Any) || address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6Any) || address.IsIPv6Multicast ||
            address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] >= 224) return null;
        var name = headers.GetValueOrDefault("DevName.bambu.com", serial);
        name = new string(name.Where(c => !char.IsControl(c)).Take(80).ToArray()).Trim();
        return new(serial.ToUpperInvariant(), address.ToString(), port, name.Length > 0 ? name : serial,
            headers.GetValueOrDefault("DevModel.bambu.com", ""), headers.GetValueOrDefault("DevVersion.bambu.com"),
            headers.GetValueOrDefault("DevConnect.bambu.com"), DateTimeOffset.UtcNow);
    }
    public async ValueTask DisposeAsync()
    {
        NetworkChange.NetworkAddressChanged -= NetworkChanged;
        await stop.CancelAsync();
        if (worker is not null) await worker;
        SetState(DiscoveryState.Stopped);
        stop.Dispose();
    }
}
