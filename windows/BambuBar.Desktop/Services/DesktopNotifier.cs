using BambuBar.Core;
#if WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
#endif

namespace BambuBar.Desktop.Services;

internal sealed class DesktopNotifier : IDisposable
{
    private readonly bool enabled;
    public event Action<string>? Activated;
    private void RaiseActivation(string serial) => Activated?.Invoke(serial);
    public DesktopNotifier(bool enabled)
    {
        this.enabled = enabled;
#if WINDOWS
        if (enabled) ToastNotificationManagerCompat.OnActivated += HandleActivation;
#endif
    }
    public void Post(string serial, string printer, PrintNotice notice)
    {
        if (!enabled) return;
#if WINDOWS
        new ToastContentBuilder().AddArgument("serial", serial)
            .AddText(TextCatalog.Get("%@: %@", printer, TextCatalog.Get(notice.TitleKey)))
            .AddText(notice.Body.Replace("**", ""))
            .Show(toast => { toast.Tag = notice.Kind; toast.Group = serial; });
#endif
    }
#if WINDOWS
    private void HandleActivation(ToastNotificationActivatedEventArgsCompat args)
    {
        var values = ToastArguments.Parse(args.Argument);
        if (values.Contains("serial")) RaiseActivation(values["serial"]);
    }
#endif
    public void Dispose()
    {
#if WINDOWS
        if (enabled) ToastNotificationManagerCompat.OnActivated -= HandleActivation;
#endif
        Activated = null;
    }
    public static int VerifyDeliveryApi()
    {
        if (!OperatingSystem.IsWindows()) return 1;
        try
        {
            using var service = new DesktopNotifier(true);
            service.Post("selftest", TextCatalog.Get("Bambu Bar"), new PrintNotice("test", "Info", TextCatalog.Get("Bambu Bar")));
            Console.WriteLine("Windows notification registration and delivery API passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    public static int RemoveRegistration()
    {
#if WINDOWS
        try { ToastNotificationManagerCompat.Uninstall(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
#else
        return 0;
#endif
    }
}
