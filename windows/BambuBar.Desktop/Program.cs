using Avalonia;

namespace BambuBar.Desktop;

internal static class Program
{
    public static string[] Arguments { get; private set; } = [];
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        if (args.Contains("--smoke-test") && !args.Contains("--demo"))
        { Console.Error.WriteLine("--smoke-test requires --demo (no real printer connections)."); return 2; }
        if (args.Contains("--storage-self-test")) return Services.WindowsChecks.Run();
        if (args.Contains("--notification-self-test")) return Services.DesktopNotifier.VerifyDeliveryApi();
        if (args.Contains("--uninstall-notifications")) return Services.DesktopNotifier.RemoveRegistration();
        // A lifetime marker lets the installer avoid replacing files while any instance is running.
        using var runningMarker = OperatingSystem.IsWindows() ? new Mutex(false, @"Local\BambuBar.Windows.Running") : null;
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
