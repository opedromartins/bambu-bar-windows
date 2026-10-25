using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace BambuBar.Core;

public sealed class PrinterTrust : IDisposable
{
    private readonly X509Certificate2Collection anchors = [];
    public PrinterTrust(string? directory = null)
    {
        directory ??= Path.Combine(AppContext.BaseDirectory, "Resources", "Certificates");
        foreach (var file in Directory.EnumerateFiles(directory, "*.pem")) anchors.ImportFromPemFile(file);
        if (anchors.Count == 0) throw new InvalidOperationException("Missing Bambu certificate authorities.");
    }
    public static string? CommonName(X509Certificate2 certificate)
    {
        // SimpleName can fall back to OU/O or SAN when CN is absent. The printer identity must be its CN.
        var commonNames = certificate.SubjectName.EnumerateRelativeDistinguishedNames()
            .Where(rdn => !rdn.HasMultipleElements && rdn.GetSingleElementType().Value == "2.5.4.3")
            .Select(rdn => rdn.GetSingleElementValue()).ToArray();
        return commonNames.Length == 1 ? commonNames[0] : null;
    }
    public bool Validate(X509Certificate2? certificate, X509Chain? peerChain, string serial)
    {
        if (certificate is null || !string.Equals(CommonName(certificate), serial, StringComparison.OrdinalIgnoreCase)) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(anchors);
        chain.ChainPolicy.ExtraStore.AddRange(anchors);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        if (peerChain is not null)
            foreach (var element in peerChain.ChainElements) chain.ChainPolicy.ExtraStore.Add(element.Certificate);
        return chain.Build(certificate);
    }
    public static bool AllowEmulator(string host, bool containerTest) =>
        IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip) || containerTest && host == "fleet";
    public void Dispose() { foreach (var anchor in anchors) anchor.Dispose(); }
}
