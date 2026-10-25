using System.Text.Json;
using BambuBar.Core;

namespace BambuBar.Desktop.Services;

internal static class WindowsChecks
{
    public static int Run()
    {
        if (!OperatingSystem.IsWindows()) return 1;
        var directory = Path.Combine(Path.GetTempPath(), "bambu-bar-check-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(false, directory);
            const string code = "CHECK123";
            var settings = new Settings { PrimarySerial = "SERIAL" };
            settings.Printers.Add(new(new PrinterConfig("Check", "127.0.0.1", "SERIAL"), store.Protect(code)));
            store.Save(settings);
            var loaded = new SettingsStore(false, directory).Load();
            if (store.Unprotect(loaded.Printers.Single().ProtectedCode) != code ||
                File.ReadAllText(Path.Combine(directory, "settings.json")).Contains(code) || loaded.PrimarySerial != "SERIAL")
                throw new InvalidDataException("DPAPI persistence round trip failed.");
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{invalid");
            try { store.Load(); return 1; } catch (JsonException) { }
            Console.WriteLine("Windows DPAPI and settings checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
