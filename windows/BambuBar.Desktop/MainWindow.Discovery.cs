using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using BambuBar.Core;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using System.Net;

namespace BambuBar.Desktop;

public sealed partial class MainWindow
{
    private PrinterDiscovery? discovery;
    private readonly CancellationTokenSource discoveryLifetime = new();
    private readonly HashSet<string> followingAddress = [];
    private readonly Dictionary<string, DateTimeOffset> addressAttempts = [];
    private ContentControl? discoveryResults;
    private Action<DiscoveredPrinter>? pickDiscovered;
    private IReadOnlyList<DiscoveredPrinter>? discoveryPreview;

    private void StartDiscovery()
    {
        if (demo) return;
        discovery = new PrinterDiscovery(emulator);
        discovery.Changed += () => Dispatcher.UIThread.Post(() => { if (!Stopping) UpdateDiscoveryResults(); });
        discovery.Found += printer => Dispatcher.UIThread.Post(() => { if (!Stopping) _ = FollowAddress(printer); });
        discovery.Start();
    }
    private Control DiscoverySection(Action<DiscoveredPrinter> pick)
    {
        pickDiscovered = pick;
        discoveryResults = new ContentControl();
        var search = ActionButton("refresh", L("Search Again"), () => discovery?.Search(), enabled: !demo);
        var section = Stack(Columns("*,Auto", Kicker(L("Choose a printer")), search),
            new ScrollViewer { Content = discoveryResults, MaxHeight = 300, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        UpdateDiscoveryResults(); return section;
    }
    private void UpdateDiscoveryResults()
    {
        if (discoveryResults is null || Stopping) return;
        var content = new StackPanel { Spacing = 10 };
        var printers = discoveryPreview ?? discovery?.Printers ?? [];
        if (demo && discoveryPreview is null)
        {
            content.Children.Add(Text(L("This copy"), 12, Muted));
        }
        else if (discovery?.State == DiscoveryState.NetworkUnavailable)
            content.Children.Add(Text(L("No connection"), 12, Muted));
        else if (discovery?.State == DiscoveryState.Limited)
            content.Children.Add(Text(L("Bambu Bar hasn't found your printer yet. Type its IP address under “Enter address manually”."), 12, Muted));
        else if (printers.Count == 0)
            content.Children.Add(Text(L("Looking for Bambu Lab printers on your network…"), 12, Muted));
        foreach (var printer in printers)
        {
            var found = printer;
            var model = PrinterModel.Find(found.ModelCode, found.Serial)?.Name ?? "Bambu Lab";
            var description = Stack(Text(found.Name, 16), Text($"{model} · {found.Host}:{found.Port} · {found.Serial}", 11, Muted));
            description.Spacing = 5;
            var added = settings.Printers.Any(p => p.Config.Serial.Equals(found.Serial, StringComparison.OrdinalIgnoreCase));
            var action = added ? Button(L("Connected"), () => { }, false) :
                ActionButton("plus", L("Add Printer…"), () => pickDiscovered?.Invoke(found), enabled: !storageFailed && !demo);
            action.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(Card(Columns("*,Auto", description, action)));
        }
        discoveryResults.Content = content;
    }
    private async Task FollowAddress(DiscoveredPrinter found)
    {
        var entry = entries.FirstOrDefault(e => e.Config.Serial.Equals(found.Serial, StringComparison.OrdinalIgnoreCase));
        if (entry?.Session?.Phase is not (ConnectionPhase.Offline or ConnectionPhase.Certificate) || followingAddress.Contains(found.Serial) ||
            entry.Config.Host == found.Host && entry.Config.Port == found.Port) return;
        var attemptKey = found.Serial;
        if (addressAttempts.TryGetValue(attemptKey, out var last) && DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(30)) return;
        addressAttempts[attemptKey] = DateTimeOffset.UtcNow;
        followingAddress.Add(found.Serial);
        var original = entry.Config;
        var identityVerified = false;
        try
        {
            var updated = original with { Host = found.Host, Port = found.Port };
            await PrinterIdentity.MatchesAsync(updated, discoveryLifetime.Token, emulator);
            identityVerified = true;
            // A form edit, deletion, reconnection or app shutdown can have happened while TLS was checked.
            if (Stopping || !entries.Contains(entry) || entry.Config != original || entry.Session?.Phase is not (ConnectionPhase.Offline or ConnectionPhase.Certificate)) return;
            var index = settings.Printers.FindIndex(p => p.Config.Serial.Equals(found.Serial, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            var saved = settings.Printers[index];
            var session = new PrinterSession(updated, store.Unprotect(saved.ProtectedCode), new Catalogs(settings.Language), emulator);
            settings.Printers[index] = saved with { Config = updated };
            try { Save(); }
            catch { settings.Printers[index] = saved; await session.DisposeAsync(); throw; }
            var previousSession = entry.Session;
            entry.Config = updated; entry.Session = session; ObserveSession(entry); session.Start();
            await previousSession.DisposeAsync();
            if (Stopping || !entries.Contains(entry) || entry.Session != session) return;
            if (page != "settings") BuildShell();
            UpdateDiscoveryResults();
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException or OperationCanceledException or ArgumentException || ex is IOException && !identityVerified) { }
        catch (Exception ex) { if (!Stopping) Notify(L("%@: %@", L("Error"), ex.Message)); }
        finally { followingAddress.Remove(found.Serial); }
    }
    private async Task PreviewDiscovery()
    {
        if (!demo) throw new InvalidOperationException("Discovery UI checks require demo mode.");
        var found = PrinterDiscovery.Parse($"NOTIFY * HTTP/1.1\r\nNT: {PrinterDiscovery.DeviceType}\r\nUSN: 22ETEST00000001\r\nLocation: 192.168.1.25\r\nDevName.bambu.com: Workshop P2S\r\nDevModel.bambu.com: N7\r\n\r\n", IPAddress.Parse("192.168.1.25"))!;
        discoveryPreview = [found, found with { Serial = "039TEST00000002", Host = "192.168.1.30", Name = "A1 Desk", ModelCode = "N2S" }];
        try
        {
            page = "settings"; BuildShell(); await Task.Delay(150);
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height), new Vector(96, 96)))
            { bitmap.Render(this); bitmap.Save("build/windows/preview-discovery.png"); }
            detail.Content = Editor(null); await Task.Delay(100);
            pickDiscovered!.Invoke(found); await Task.Delay(100);
            var boxes = detail.GetVisualDescendants().OfType<TextBox>().ToArray();
            if (!boxes.Any(b => b.Text == found.Host) || !boxes.Any(b => b.Text == found.Serial) || !boxes.Any(b => b.Text == found.Name))
                throw new InvalidOperationException("Discovered printer did not prefill the add form.");
            var code = boxes.Single(b => b.PasswordChar == '●'); code.Text = "12345678";
            UpdateDiscoveryResults();
            if (code.Text != "12345678" || !detail.GetVisualDescendants().Contains(code))
                throw new InvalidOperationException("A discovery refresh replaced access-code input.");
            pickDiscovered.Invoke(discoveryPreview[1]);
            if (code.Text != "") throw new InvalidOperationException("Switching discovered printers retained another printer's code.");
            Console.WriteLine("Discovery UI: list, prefill, preserved form input and printer switch passed (no network in demo).");
        }
        finally { discoveryPreview = null; page = "printer"; BuildShell(); }
    }
}
