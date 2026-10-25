using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace BambuBar.Core;

public static class PrinterIdentity
{
    // DHCP follow-up probes only TLS. MQTT credentials are never sent to an advertised address here.
    public static async Task<bool> MatchesAsync(PrinterConfig config, CancellationToken cancellationToken, bool emulator = false)
    {
        config.Validate(emulator);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        using var trust = new PrinterTrust();
        await client.ConnectAsync(config.Host, config.Port, deadline.Token);
        using var stream = new SslStream(client.GetStream(), false, (_, certificate, chain, _) =>
            emulator && PrinterTrust.AllowEmulator(config.Host, false) || trust.Validate(certificate as X509Certificate2, chain, config.Serial));
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = config.Serial,
            EnabledSslProtocols = SslProtocols.Tls12, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, deadline.Token);
        return stream.IsAuthenticated;
    }
    public static async Task<string> ReadSerialAsync(string host, int port, CancellationToken cancellationToken, bool emulator = false)
    {
        new PrinterConfig("identity", host, "identity", Port: port).Validate(emulator);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var client = new TcpClient(); using var trust = new PrinterTrust();
        string? serial = null;
        await client.ConnectAsync(host, port, deadline.Token);
        using var stream = new SslStream(client.GetStream(), false, (_, cert, chain, _) =>
        {
            if (cert is not X509Certificate2 leaf) return false;
            var name = PrinterTrust.CommonName(leaf);
            if (string.IsNullOrEmpty(name) || name.Length > 64 || !name.All(char.IsAsciiLetterOrDigit)) return false;
            if (!(emulator && PrinterTrust.AllowEmulator(host, false)) && !trust.Validate(leaf, chain, name)) return false;
            serial = name.ToUpperInvariant(); return true;
        });
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host, EnabledSslProtocols = SslProtocols.Tls12,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, deadline.Token);
        return serial ?? throw new AuthenticationException(TextCatalog.Get("its certificate doesn't name it."));
    }
}
