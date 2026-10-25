using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BambuBar.Core;

namespace BambuBar.Desktop.Services;

public sealed record SavedPrinter(PrinterConfig Config, string ProtectedCode);
public sealed class Settings
{
    public string Language { get; set; } = TextCatalog.Normalize(CultureInfo.CurrentUICulture.Name);
    public bool NotifyFinished { get; set; } = true;
    public bool NotifyFailed { get; set; } = true;
    public bool NotifyPaused { get; set; } = true;
    public bool NotifyStarted { get; set; }
    public string Theme { get; set; } = "system";
    public string? PrimarySerial { get; set; }
    public List<SavedPrinter> Printers { get; set; } = [];
}
public sealed class SettingsStore(bool ephemeral, string? directory = null)
{
    private readonly string path = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bambu Bar"), "settings.json");
    private readonly Dictionary<string, string> ephemeralSecrets = [];
    public Settings Load()
    {
        if (ephemeral || !File.Exists(path)) return new();
        // Corrupt/unreadable settings must be reported, never silently overwritten.
        return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid settings.");
    }
    public void Save(Settings settings)
    {
        if (ephemeral) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
    public string Protect(string code)
    {
        if (ephemeral) { var token = Guid.NewGuid().ToString(); ephemeralSecrets[token] = code; return token; }
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Credential storage requires Windows DPAPI.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(code), null, DataProtectionScope.CurrentUser));
    }
    public string Unprotect(string encrypted)
    {
        if (ephemeral) return ephemeralSecrets[encrypted];
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Credential storage requires Windows DPAPI.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser));
    }
}
