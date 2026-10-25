using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using BambuBar.Desktop.Controls;
using BambuBar.Core;

namespace BambuBar.Desktop;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, Printer3DView> interactiveViews = [];
    private sealed class HeatGauge(double value, IBrush tint, IBrush track) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.DrawRectangle(track, null, new Rect(0, 0, Bounds.Width, Bounds.Height), 2, 2);
            var width = Bounds.Width * Math.Clamp(value / 350, 0, 1);
            if (width > 0) context.DrawRectangle(tint, null, new Rect(0, 0, width, Bounds.Height), 2, 2);
        }
    }
    private TextBlock? sidebarStatus;
    private void UpdateSidebar()
    {
        if (sidebarStatus is null) return;
        sidebarStatus.Text = selected is null ? L("No printer") : PhaseText(selected);
        sidebarStatus.Foreground = selected?.Phase == ConnectionPhase.Online ? StatusColor(selected.Status) : Muted;
    }
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    private IBrush Ink => Brush(Dark ? "#F0F4F1" : "#23362D");
    private IBrush Hairline => Brush(Dark ? "#16FFFFFF" : "#180D3825");
    private TextBlock Text(string value, double size = 14, IBrush? color = null) => new()
    {
        Text = value, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap,
        FontWeight = size >= 20 ? FontWeight.SemiBold : FontWeight.Normal
    };
    private static StackPanel Stack(params Control[] items)
    {
        var result = new StackPanel { Spacing = 12 };
        foreach (var item in items) result.Children.Add(item);
        return result;
    }
    private static StackPanel Row(params Control[] items)
    {
        var result = Stack(items); result.Orientation = Orientation.Horizontal; return result;
    }
    private Border Card(Control content) => new()
    {
        Background = CardBrush, CornerRadius = new CornerRadius(18), Padding = new Thickness(20), Child = content,
        BorderBrush = Hairline, BorderThickness = new Thickness(1),
        BoxShadow = BoxShadows.Parse(Dark ? "0 6 20 0 #10000000" : "0 6 20 0 #080D3825")
    };
    private Button Button(string title, Action action, bool enabled = true)
    {
        var button = new Button { Content = title, IsEnabled = enabled };
        button.Click += (_, _) => action(); return button;
    }
    private Control Glyph(string name, double size = 18, IBrush? color = null)
    {
        var path = name switch
        {
            "printer" => "M6 9V3H18V9 M6 17H4A2 2 0 0 1 2 15V11A2 2 0 0 1 4 9H20A2 2 0 0 1 22 11V15A2 2 0 0 1 20 17H18 M6 14H18V22H6Z M18 12H19",
            "spool" => "M21 12A9 9 0 1 1 3 12A9 9 0 1 1 21 12 M15 12A3 3 0 1 1 9 12A3 3 0 1 1 15 12 M8 4L10 9 M14 15L16 20",
            "grid" => "M3 3H10V10H3Z M14 3H21V10H14Z M3 14H10V21H3Z M14 14H21V21H14Z",
            "settings" => "M9 3L10 1H14L15 3L16 5L19 5L21 9L19 12L21 15L19 19L16 19L14 23H10L8 19H5L3 15L5 12L3 9L5 5H8Z M16 12A4 4 0 1 1 8 12A4 4 0 1 1 16 12",
            "pause" => "M8 5V19 M16 5V19",
            "play" => "M7 4L20 12L7 20Z",
            "stop" => "M6 6H18V18H6Z",
            "light" => "M9 18H15 M10 22H14 M8 14C3 10 6 3 12 3C18 3 21 10 16 14L15 16H9Z",
            "refresh" => "M20 7V2 M20 7H15 M20 7A9 9 0 1 0 21 16",
            "clock" => "M21 12A9 9 0 1 1 3 12A9 9 0 1 1 21 12 M12 6V12L16 14",
            "nozzle" => "M9 3H15V13L18 17V20H6V17L9 13Z M11 23H13",
            "bed" => "M3 16H21L18 21H6Z M7 12C3 9 10 6 7 3 M12 12C8 9 15 6 12 3 M17 12C13 9 20 6 17 3",
            "temperature" => "M10 14V5A2 2 0 0 1 14 5V14A5 5 0 1 1 10 14 M12 10V18",
            "arrow" => "M5 12H19 M14 7L19 12L14 17",
            "chevron" => "M8 5L15 12L8 19",
            "check" => "M5 12L10 17L20 6",
            "plus" => "M12 4V20 M4 12H20",
            "shield" => "M12 2L21 6V12C21 17 16 21 12 23C8 21 3 17 3 12V6Z M8 12L11 15L17 9",
            "layers" => "M12 2L23 8L12 14L1 8Z M1 13L12 19L23 13 M1 18L12 24L23 18",
            _ => "M4 12H20 M12 4V20"
        };
        return new Avalonia.Controls.Shapes.Path
        {
            Data = StreamGeometry.Parse(path), Stroke = color ?? Muted, StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center
        };
    }
    private Control Label(string icon, string label, IBrush? color = null, double fontSize = 13) =>
        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Glyph(icon, 15, color), Text(label, fontSize, color) } };
    private Control Kicker(string title) => new TextBlock
    { Text = title.ToUpperInvariant(), FontSize = 10, LetterSpacing = 1.3, FontWeight = FontWeight.SemiBold, Foreground = Muted, Margin = new Thickness(1, 8, 0, 0) };
    private Button ActionButton(string icon, string title, Action action, string style = "", bool enabled = true)
    {
        var button = Button(title, action, enabled);
        button.Content = Label(icon, title, style == "danger" ? Brush(Dark ? "#FF989C" : "#B84551") : style == "primary" ? Brush("#113B28") : Ink);
        if (style.Length > 0) button.Classes.Add(style);
        return button;
    }
    private Control Badge(string text, IBrush color) => new Border
    {
        Background = new SolidColorBrush(((SolidColorBrush)color).Color, .12), CornerRadius = new CornerRadius(20),
        Padding = new Thickness(9, 4), HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Child = Row(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), Background = color, VerticalAlignment = VerticalAlignment.Center }, Text(text, 11, color))
    };
    private Control Columns(string widths, params Control[] items)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(widths), ColumnSpacing = 12 };
        for (var i = 0; i < items.Length; i++) { Grid.SetColumn(items[i], i); grid.Children.Add(items[i]); }
        return grid;
    }
    private Control Heading(string title, string subtitle, Control? action = null)
    {
        var texts = Stack(Text(title, 26), Text(subtitle, 12, Muted)); texts.Spacing = 4;
        if (action is null) return texts;
        action.VerticalAlignment = VerticalAlignment.Center;
        return Columns("*,Auto", texts, action);
    }
    private void Navigate(string section) { page = section; BuildShell(); }
    private void BuildShell()
    {
        if (Stopping) return;
        TextCatalog.Language = settings.Language;
        if (notice.Parent is Panel oldGrid) oldGrid.Children.Remove(notice);
        if (detail.Parent is ContentControl oldScroll) oldScroll.Content = null;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brush(Dark ? "#1A2220" : "#EDF1EE");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("224,*"), RowDefinitions = new RowDefinitions("*,Auto") };
        var sideGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(14, 47, 14, 20), RowSpacing = 23 };
        var brand = Row(new Image { Source = LoadImage("avares://BambuBar/Assets/app.ico"), Width = 25, Height = 25 }, Text("Bambu Bar", 16));
        brand.Margin = new Thickness(8, 0, 0, 0); sideGrid.Children.Add(brand);
        var picker = new ComboBox { ItemsSource = entries, SelectedItem = selected, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, MinHeight = 36 };
        picker.SelectionChanged += (_, _) => { selected = picker.SelectedItem as Entry; RenderPage(); };
        sidebarStatus = Text("", 11, Green); UpdateSidebar();
        var pickerContent = Stack(picker, Row(Glyph("printer", 15, Green), sidebarStatus));
        pickerContent.Spacing = 8;
        var pickerCard = Card(pickerContent); pickerCard.Padding = new Thickness(10); pickerCard.CornerRadius = new CornerRadius(12);
        Grid.SetRow(pickerCard, 1); sideGrid.Children.Add(pickerCard);
        var navigation = new StackPanel { Spacing = 5 };
        foreach (var (id, icon, title) in new[] { ("fleet", "grid", L("All Printers")), ("printer", "printer", L("Printer")),
            ("filament", "spool", L("Filament")), ("settings", "settings", L("Settings")) })
        {
            var nav = Button(title, () => Navigate(id)); nav.Classes.Add("nav");
            if (page == id) nav.Classes.Add("selected");
            nav.Content = Label(icon, title, page == id ? Brushes.White : Muted, 12);
            nav.HorizontalAlignment = HorizontalAlignment.Stretch; nav.HorizontalContentAlignment = HorizontalAlignment.Left;
            navigation.Children.Add(nav);
        }
        Grid.SetRow(navigation, 2); sideGrid.Children.Add(navigation);
        var footer = Stack(Label("shield", L("Privacy"), Muted, 11),
            Text(demo ? L("This copy") : emulator ? L("This copy") : "Bambu Bar · 1.3.1", 10, Muted));
        footer.Margin = new Thickness(8, 0, 0, 0); footer.Spacing = 6; Grid.SetRow(footer, 3); sideGrid.Children.Add(footer);
        grid.Children.Add(new Border { Child = sideGrid, Background = Brush(Dark ? "#F01C2320" : "#BDE3EAE5"), BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 1, 0) });
        var glow = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.Parse(Dark ? "#EE173126" : "#D9EDF6EE"), 0),
                new GradientStop(Color.Parse(Dark ? "#F019201D" : "#DFEFF2F0"), .55), new GradientStop(Color.Parse(Dark ? "#EC192723" : "#CCE8F0EE"), 1)]
        };
        detail.Margin = new Thickness(30, 49, 30, 28); detail.HorizontalAlignment = HorizontalAlignment.Stretch;
        var scroll = new ScrollViewer { Content = detail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var main = new Border { Background = glow, Child = scroll }; Grid.SetColumn(main, 1); grid.Children.Add(main);
        var drag = new Border { Height = 34, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 145, 0) };
        drag.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        Grid.SetColumnSpan(drag, 2); grid.Children.Add(drag);
        grid.Children.Add(new Border { Background = Brush(Dark ? "#F019201D" : "#EFEFF4F0"), [Grid.RowProperty] = 1, [Grid.ColumnSpanProperty] = 2 });
        notice.Foreground = Ink;
        Grid.SetRow(notice, 1); Grid.SetColumnSpan(notice, 2); grid.Children.Add(notice);
        Content = grid; RenderPage(); LanguageChanged?.Invoke();
        if (quickPanel?.IsVisible == true && !quickModal) BuildQuickShell();
    }
    private void RenderPage()
    {
        if (Stopping) return;
        detail.Content = page switch
        { "settings" => SettingsPage(), "fleet" => FleetPage(), "filament" => FilamentPage(), _ => PrinterPage() };
    }

    private Bitmap LoadImage(string uri)
    {
        if (!images.TryGetValue(uri, out var image)) { image = new Bitmap(AssetLoader.Open(new Uri(uri))); images[uri] = image; }
        return image;
    }
    private Bitmap TintLayer(Bitmap source, string key, Color color)
    {
        if (images.TryGetValue(key, out var cached)) return cached;
        var format = source.Format ?? PixelFormat.Bgra8888;
        if (format != PixelFormat.Bgra8888 && format != PixelFormat.Rgba8888) return source;
        var tinted = new WriteableBitmap(source.PixelSize, source.Dpi, format, source.AlphaFormat ?? AlphaFormat.Premul);
        using (var buffer = tinted.Lock())
        {
            var bytes = new byte[buffer.RowBytes * source.PixelSize.Height];
            source.CopyPixels(new PixelRect(source.PixelSize), buffer.Address, bytes.Length, buffer.RowBytes);
            Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            var red = format == PixelFormat.Bgra8888 ? 2 : 0;
            var blue = red == 2 ? 0 : 2;
            for (var y = 0; y < source.PixelSize.Height; y++)
            for (var x = 0; x < source.PixelSize.Width; x++)
            {
                var i = y * buffer.RowBytes + x * 4;
                bytes[i + red] = (byte)(bytes[i + red] * color.R / 255);
                bytes[i + 1] = (byte)(bytes[i + 1] * color.G / 255);
                bytes[i + blue] = (byte)(bytes[i + blue] * color.B / 255);
            }
            Marshal.Copy(bytes, 0, buffer.Address, bytes.Length);
        }
        images[key] = tinted;
        return tinted;
    }
    private Control PrinterImage(Entry entry, double height, bool interactive = false)
    {
        var model = PrinterModel.Find(entry.Config.ModelCode, entry.Config.Serial);
        var shape = model?.Shape ?? "enclosed";
        if (interactive)
        {
            var key = $"{entry.Config.Serial}/{shape}/{height}";
            if (!interactiveViews.TryGetValue(key, out var view))
            { view = new Printer3DView(shape) { Height = height, Width = height * 480 / 576 }; interactiveViews[key] = view; }
            if (view.Parent is Panel oldParent) oldParent.Children.Remove(view);
            view.Update(entry.Status, entry.Phase == ConnectionPhase.Online);
            return view;
        }
        Bitmap Layer(string name) => LoadImage($"avares://BambuBar/Assets/Printers/{shape}-{name}.png");
        var s = entry.Status;
        var spoolList = s?.Ams.FirstOrDefault()?.Spools ?? [];
        var hasAms = s is null || spoolList.Count > 0;
        var layers = new Grid { Width = height * 480 / 576, Height = height };
        var lit = s?.Light != false && entry.Phase == ConnectionPhase.Online;
        layers.Children.Add(new Image { Source = Layer(hasAms ? lit ? "body" : "dark" : lit ? "bare" : "baredark"), Stretch = Stretch.Uniform });
        void Tint(string layer, Color color, double? fraction = null)
        {
            var image = new Image { Source = TintLayer(Layer(layer), $"{shape}/{layer}/{color}", color), Stretch = Stretch.Uniform };
            if (fraction is { } progress)
            {
                using var stream = AssetLoader.Open(new Uri($"avares://BambuBar/Assets/Printers/{shape}.json"));
                var anchors = JsonNode.Parse(stream)!;
                var scale = height / (anchors["size"]?[1]?.GetValue<double>() ?? 576);
                var part = anchors["part"]!;
                var top = (part["y"]!.GetValue<double>() + part["h"]!.GetValue<double>() * (1 - progress)) * scale;
                image.Clip = new RectangleGeometry(new Rect(0, top, layers.Width, Math.Max(0, height - top)));
            }
            layers.Children.Add(image);
        }
        foreach (var spool in spoolList.Where(sp => sp.Loaded && sp.Slot is >= 0 and < 4))
            Tint($"spool{spool.Slot}", FilamentColor(spool));
        if (s?.State is JobState.Printing or JobState.Paused or JobState.Finished)
            Tint("part", FilamentColor(spoolList.FirstOrDefault(sp => sp.Active)), Math.Clamp((s.Progress ?? 0) / 100, .03, 1));
        if (shape is "enclosed" or "wide") layers.Children.Add(new Image { Source = Layer("sheen"), Stretch = Stretch.Uniform, Opacity = .55 });
        return layers;
    }
    private static Color FilamentColor(Spool? spool) => Color.TryParse("#" + spool?.Color, out var c) ? c : Color.Parse("#8AD6AC");
    private Control PrinterPage()
    {
        if (selected is not { } e) return Stack(Heading(L("Connect your printer"),
            L("Connect your printer")),
            ActionButton("plus", L("Add Printer…"), () => Navigate("settings"), "primary"));
        var s = e.Status;
        var model = PrinterModel.Find(e.Config.ModelCode, e.Config.Serial);
        var online = e.Phase == ConnectionPhase.Online && s is not null && !busy;
        var activeSpool = s?.Ams.SelectMany(u => u.Spools).FirstOrDefault(sp => sp.Active) ?? (s?.External?.Active == true ? s.External : null);
        var accent = activeSpool is null ? Green : new SolidColorBrush(FilamentColor(activeSpool));
        var refresh = ActionButton("refresh", L("Refresh"), () => e.Session?.Refresh()); refresh.Classes.Add("quiet");
        var result = Stack(Heading(e.Config.Name, $"Bambu Lab {model?.Name ?? ""} · {e.Config.Host}" + (string.IsNullOrEmpty(s?.Firmware) ? "" : $" · {s.Firmware}"), refresh));
        result.Spacing = 18;
        var hero = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*"), ColumnSpacing = 20 };
        var image = PrinterImage(e, 258, true); image.VerticalAlignment = VerticalAlignment.Center; hero.Children.Add(image);
        var badge = Badge(PhaseText(e), StatusColor(s));
        Control light = model?.Light == false ? new Border() : ActionButton("light", L("Light"), () => _ = Send(e, "ledctrl", light: s?.Light != true), enabled: online);
        if (light is Button lightButton) lightButton.Classes.Add("quiet");
        var top = Columns("*,Auto", badge, light);
        var job = Text(s?.Job is { Length: > 0 } name ? name : L("Ready to print"), 22);
        var material = Text(s is null ? L("Waiting for the printer") : PrintStages.Describe(s, settings.Language), 12, Muted);
        var number = Text(Value(s?.Progress), 62); number.FontWeight = FontWeight.SemiBold; number.LetterSpacing = -2;
        var percent = Text("%", 25, Muted); percent.VerticalAlignment = VerticalAlignment.Bottom; percent.Margin = new Thickness(0, 0, 0, 8);
        var progress = Row(number, percent); progress.Spacing = 3;
        var layers = Stack(Text($"{s?.Layer?.ToString() ?? "—"}/{s?.TotalLayers?.ToString() ?? "—"}", 13), Text(L("layers"), 10, Muted));
        layers.Spacing = 2; layers.VerticalAlignment = VerticalAlignment.Bottom; layers.Margin = new Thickness(0, 0, 0, 12); layers.HorizontalAlignment = HorizontalAlignment.Right;
        var progressLine = Columns("*,Auto", progress, layers);
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = s?.Progress ?? 0, Height = 7, Foreground = accent, Background = Brush(Dark ? "#16FFFFFF" : "#15315F47") };
        var finish = s?.FinishAt(DateTimeOffset.UtcNow, e.Phase == ConnectionPhase.Online)?.ToLocalTime().ToString("HH:mm") ?? "—";
        var info = Stack(top, Stack(job, material), progressLine, bar,
            Columns("*,Auto", Label("clock", L(s?.State == JobState.Paused ? "%@ left · paused" : "%@ left", Remaining(s)), Ink, 11), Text(finish == "—" ? "" : L("Done %@", finish), 11, Muted)));
        info.Spacing = 12; info.VerticalAlignment = VerticalAlignment.Center;
        ((StackPanel)info.Children[1]).Spacing = 4;
        Grid.SetColumn(info, 1); hero.Children.Add(info);
        var heroCard = Card(hero); heroCard.Padding = new Thickness(16, 18, 22, 20); heroCard.CornerRadius = new CornerRadius(24);
        result.Children.Add(heroCard);
        var pause = s?.State == JobState.Paused;
        var pauseButton = ActionButton(pause ? "play" : "pause", pause ? L("Resume") : L("Pause"),
            () => _ = Send(e, pause ? "resume" : "pause"), enabled: online && !s!.ControlsLocked && s.State is JobState.Printing or JobState.Preparing or JobState.Paused);
        pauseButton.HorizontalAlignment = HorizontalAlignment.Stretch; pauseButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        var stop = ActionButton("stop", L("Stop"), () => _ = Send(e, "stop"), "danger",
            online && !s!.ControlsLocked && s.State is JobState.Printing or JobState.Preparing or JobState.Paused);
        stop.HorizontalAlignment = HorizontalAlignment.Stretch; stop.HorizontalContentAlignment = HorizontalAlignment.Center;
        var speeds = new ComboBox { ItemsSource = SpeedNames(),
            SelectedIndex = Math.Clamp((s?.Speed ?? 2) - 1, 0, 3), IsEnabled = online && !s!.ControlsLocked, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        speedPicker = speeds;
        speeds.SelectionChanged += (_, _) => { if (speeds.SelectedIndex >= 0) _ = Send(e, "print_speed", speeds.SelectedIndex + 1); };
        result.Children.Add(Columns("*,*,190", pauseButton, stop, speeds));
        if (e.Phase != ConnectionPhase.Online) result.Children.Add(Card(Stack(Text(PhaseText(e), 14), Text(L("Bambu Bar keeps trying in the background."), 12, Muted),
            Button(L("Change…"), () => Navigate("settings")))));
        if (s?.ControlsLocked == true) result.Children.Add(Text(L("Turn on LAN Only and Developer Mode on the printer to control prints from Bambu Bar."), 12, Muted));
        result.Children.Add(Kicker(L("Temperatures")));
        var temps = new List<Control>();
        void Temp(string icon, string title, Temperature? t, string color)
        {
            var value = Row(Text(Value(t?.Current), 29), Text("°", 19, Muted)); value.Spacing = 1;
            var gauge = new HeatGauge(t?.Current ?? 0, Brush(color), Hairline) { Height = 3,
                HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 3, 0, 0) };
            var contents = Stack(Label(icon, title, Brush(color), 11), value, gauge); contents.Spacing = 8;
            var card = Card(contents); card.Padding = new Thickness(16, 15);
            ToolTip.SetTip(card, $"{L("Heating")}: {Value(t?.Target)} °C"); temps.Add(card);
        }
        Temp("nozzle", s?.SecondNozzle is null ? L("Nozzle") : L("Right nozzle"), s?.Nozzle, "#F49B72");
        Temp("bed", L("Bed"), s?.Bed, "#DBBA74");
        if (s?.SecondNozzle is not null) Temp("nozzle", L("Left nozzle"), s.SecondNozzle, "#F49B72");
        if (model?.Chamber != false) Temp("temperature", L("Chamber"), s?.Chamber, "#91B9BA");
        result.Children.Add(Columns(string.Join(',', temps.Select(_ => "*")), temps.ToArray()));
        if (s is not null) result.Children.Add(Telemetry(s, model));
        foreach (var issue in s?.Issues ?? []) result.Children.Add(ErrorCard(issue));
        var filamentHeader = Columns("*,Auto", Kicker(L("Filament")), ActionButton("arrow", L("Show"), () => Navigate("filament")));
        result.Children.Add(filamentHeader);
        if (s?.Ams.FirstOrDefault() is { } ams) result.Children.Add(AmsCard(ams, true));
        else if (s?.External is { } external) result.Children.Add(SpoolCard(external));
        else result.Children.Add(Card(Text(L("No AMS connected"), 12, Muted)));
        return result;
    }
    private Control SpoolDisc(Spool spool, double size = 43)
    {
        var c = FilamentColor(spool);
        IBrush spoolBrush = new RadialGradientBrush { Center = new RelativePoint(.35, .25, RelativeUnit.Relative),
            GradientStops = [new GradientStop(c, 0), new GradientStop(Color.FromRgb((byte)(c.R * .65), (byte)(c.G * .65), (byte)(c.B * .65)), 1)] };
        if (spool.Colors.Count > 1)
            spoolBrush = new LinearGradientBrush { GradientStops = [.. spool.Colors.Select((hex, index) =>
                new GradientStop(Color.TryParse("#" + hex, out var color) ? color : c, index / (double)(spool.Colors.Count - 1)))] };
        var disc = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
            Background = spoolBrush,
            BorderThickness = new Thickness(4), BorderBrush = Brush(Dark ? "#B6474F49" : "#C5D6DFD8"),
            Child = new Border { Width = size * .18, Height = size * .18, CornerRadius = new CornerRadius(size), Background = Brush("#AF19271F"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Opacity = spool.Loaded ? 1 : .22
        };
        var ring = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(size), BorderThickness = new Thickness(1.5), BorderBrush = spool.Active ? Green : Brushes.Transparent, Child = disc };
        return ring;
    }
    private Control AmsCard(AmsUnit unit, bool compact = false)
    {
        var humidity = unit.Humidity is { } rh ? L("%lld%% RH", rh) : unit.HumidityLevel is { } level ? L("Humidity %lld/5", level) : "—";
        var header = Columns("*,Auto", Text($"{unit.Kind.ToUpperInvariant()} {(unit.Id >= 128 ? (unit.Id - 127).ToString() : ((char)('A' + unit.Id)).ToString())}", 10, Muted),
            Text(L("%@ · %@", humidity, unit.Temperature is { } temp ? L("%lld°", (int)temp) : "—"), 10, Muted));
        var trays = new UniformGrid { Columns = Math.Max(1, unit.Spools.Count), Margin = new Thickness(0, 8, 0, 0) };
        foreach (var s in unit.Spools)
        {
            var name = s.Loaded ? compact ? s.Material : s.Product : L("Empty");
            var tray = Stack(SpoolDisc(s), Text(name, 11), Text(s.Grams is { } g ? $"≈ {g} g" : s.Remain is { } r ? $"{r}%" : "—", 10, Muted));
            tray.Spacing = 5; tray.HorizontalAlignment = HorizontalAlignment.Center;
            if (!compact && s.Loaded && s.Brand.Length > 0) tray.Children.Insert(1, Text(s.Brand, 10, Muted));
            var range = s.NozzleMin is { } low && s.NozzleMax is { } high && high >= low ? L("Prints at %lld–%lld °C", low, high) : "";
            if (!compact && range.Length > 0) tray.Children.Add(Text(range, 10, Muted));
            foreach (var child in tray.Children.OfType<TextBlock>()) { child.HorizontalAlignment = HorizontalAlignment.Center; child.TextAlignment = TextAlignment.Center; }
            ToolTip.SetTip(tray, $"{s.Brand} {s.Product} · {s.ColorName ?? s.Material} {range}"); trays.Children.Add(tray);
        }
        var contents = Stack(header, trays); contents.Spacing = 5;
        if (unit.DryingMinutes is { } drying) contents.Children.Add(Text(L("Drying · %@ left", Duration(drying)), 11, Muted));
        var card = Card(contents); card.Padding = new Thickness(17, 14); return card;
    }
    private Control FilamentPage()
    {
        var result = Stack(Heading(L("Filament"), L("Spools in your AMS and on the external holder"))); result.Spacing = 20;
        var s = selected?.Status;
        if (s is null) { result.Children.Add(Card(Text(L("Waiting for the printer"), 14, Muted))); return result; }
        var loaded = s.Ams.SelectMany(u => u.Spools).Where(sp => sp.Loaded).ToArray();
        var grams = loaded.Where(sp => sp.Grams is not null).Sum(sp => sp.Grams ?? 0);
        var amount = loaded.All(sp => sp.Grams is null) ? "—" : (loaded.Any(sp => sp.Grams is null) ? "≥ " : "") + $"{grams:N0} g";
        var summary = Card(Columns("*,Auto", Stack(Label("spool", L("AMS spools"), Muted), Text(amount, 34)),
            Badge(L("%lld spool(s) loaded", s.Ams.SelectMany(u => u.Spools).Count(sp => sp.Loaded)), Green)));
        result.Children.Add(summary);
        foreach (var unit in s.Ams) result.Children.Add(AmsCard(unit));
        if (s.External is { } ext) { result.Children.Add(Kicker(L("External spool"))); result.Children.Add(SpoolCard(ext)); }
        if (s.Ams.Count == 0 && s.External is null) result.Children.Add(Card(Text(L("No AMS connected"), 14, Muted)));
        return result;
    }
    private Control SpoolCard(Spool s)
    {
        var details = Stack(Text(s.Loaded ? $"{s.Brand} {s.Product}".Trim() : L("Empty"), 16), Text(s.ColorName ?? s.Material, 11, Muted)); details.Spacing = 4;
        if (s.NozzleMin is { } low && s.NozzleMax is { } high && high >= low) details.Children.Add(Text(L("Prints at %lld–%lld °C", low, high), 11, Muted));
        if (s.Unit == 255) details.Children.Add(Text(L("The external holder can’t weigh the spool."), 11, Muted));
        var amount = s.Grams is { } g ? $"≈ {g} g" : s.Remain is { } r ? $"{r}%" : "—";
        var contents = Columns("Auto,*,Auto", SpoolDisc(s), details, Text(amount, 18));
        return Card(contents);
    }
    private Control FleetPage()
    {
        var result = Stack(Heading(L("All Printers"),
            L("Bambu Bar watches all of them at once."), Badge(L("%lld printers", entries.Count), Green)));
        result.Spacing = 18;
        foreach (var entry in entries.OrderBy(e => e.Status?.State is JobState.Failed or JobState.Paused ? 0 : 1).ThenBy(e => e.Status?.RemainingMinutes ?? double.MaxValue))
        {
            var e = entry; var s = e.Status;
            var details = Stack(Columns("*,Auto", Text(e.Config.Name, 19), Badge(PhaseText(e), StatusColor(s))),
                Text(s?.Job is { Length: > 0 } job ? job : L("Ready to print"), 12, Muted),
                new ProgressBar { Minimum = 0, Maximum = 100, Value = s?.Progress ?? 0, Height = 5, Foreground = StatusColor(s), Background = Hairline },
                Columns("*,Auto", Text($"{Value(s?.Progress)}% · {Remaining(s)}", 13), ActionButton("arrow", L("Open"), () => { selected = e; Navigate("printer"); })));
            details.VerticalAlignment = VerticalAlignment.Center;
            result.Children.Add(Card(Columns("125,*", PrinterImage(e, 150), details)));
        }
        if (entries.Count == 0) result.Children.Add(ActionButton("plus", L("Add Printer…"), () => Navigate("settings"), "primary"));
        return result;
    }
}
