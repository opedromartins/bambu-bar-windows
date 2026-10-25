using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using BambuBar.Core;
using SkiaSharp;

namespace BambuBar.Desktop.Controls;

// The original Blender mesh, projected and depth-sorted locally. No browser, network or GPU prerequisite.
public sealed class Printer3DView : Control
{
    private sealed class Mesh
    {
        public string Name { get; set; } = "";
        public float[][] Vertices { get; set; } = [];
        public float[][] Faces { get; set; } = [];
        public int Part { get; set; } = -1;
        public int Spool { get; set; } = -1;
    }
    private sealed class Model
    {
        public string Shape { get; set; } = "";
        public float PartHeight { get; set; }
        public Mesh[] Meshes { get; set; } = [];
        public Vector3 Center;
        public float Size;
    }
    private sealed record Face(float Depth, SKPoint A, SKPoint B, SKPoint C, SKColor Color);
    private sealed class Frame(Rect bounds, SKPoint[] points, SKColor[] colors) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null || points.Length == 0) return;
            using var lease = feature.Lease();
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
            using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, points, null, colors);
            // Supersampling smooths silhouette edges even on the CPU raster backend.
            using var surface = SKSurface.Create(new SKImageInfo(Math.Max(1, (int)bounds.Width * 2), Math.Max(1, (int)bounds.Height * 2)));
            if (surface is null) { lease.SkCanvas.DrawVertices(vertices, SKBlendMode.Modulate, paint); return; }
            surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.Scale(2);
            surface.Canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
            using var image = surface.Snapshot(); paint.FilterQuality = SKFilterQuality.High;
            lease.SkCanvas.DrawImage(image, new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height), paint);
        }
    }
    private static readonly Dictionary<string, Model> Models = [];
    private readonly Model model;
    private readonly DispatcherTimer animation;
    private PrinterStatus? status;
    private bool online;
    private Point? lastDrag;
    private double yaw = .42;
    private double pitch = .22;
    private double zoom = 1;
    public int TriangleCount => model.Meshes.Sum(m => m.Faces.Length);
    public bool IsInteracting => lastDrag is not null;
    public void StopAnimation() => animation.Stop();
    public (double Yaw, double Pitch, double Zoom) Camera => (yaw, pitch, zoom);
    public int VisibleParts => status?.State == JobState.Finished ? 32 : status is { } s && (s.Active || s.State is JobState.Failed)
        ? Math.Clamp((int)Math.Ceiling((s.Progress ?? 0) / 100 * 32), s.State == JobState.Preparing ? 0 : 1, 32) : 0;
    public Printer3DView(string shape)
    {
        if (!Models.TryGetValue(shape, out var loaded))
        {
            using var input = AssetLoader.Open(new Uri($"avares://BambuBar/Assets/Models/{shape}.json.gz"));
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            loaded = JsonSerializer.Deserialize<Model>(gzip, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            var vertices = loaded.Meshes.SelectMany(m => m.Vertices).Select(v => new Vector3(v[0], v[1], v[2])).ToArray();
            var min = vertices.Aggregate(Vector3.Min); var max = vertices.Aggregate(Vector3.Max);
            loaded.Center = (min + max) / 2;
            loaded.Size = Math.Max(max.Z - min.Z, Math.Max(max.X - min.X, max.Y - min.Y));
            Models[shape] = loaded;
        }
        model = loaded;
        Focusable = true; Cursor = new Cursor(StandardCursorType.Hand); ClipToBounds = true;
        ToolTip.SetTip(this, TextCatalog.Get("Printer model"));
        animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        animation.Tick += (_, _) => { if (TopLevel.GetTopLevel(this)?.IsVisible == true && online && status?.State == JobState.Printing) InvalidateVisual(); };
        AttachedToVisualTree += (_, _) => animation.Start();
        DetachedFromVisualTree += (_, _) => { animation.Stop(); lastDrag = null; };
    }
    public void Update(PrinterStatus? value, bool connected) { status = value; online = connected; ToolTip.SetTip(this, TextCatalog.Get("Printer model")); InvalidateVisual(); }
    public void Orbit(double horizontal, double vertical)
    { yaw += horizontal; pitch = Math.Clamp(pitch + vertical, -.8, .8); InvalidateVisual(); }
    public void Zoom(double factor) { zoom = Math.Clamp(zoom * factor, .65, 1.7); InvalidateVisual(); }
    public void ResetView() { yaw = .42; pitch = .22; zoom = 1; InvalidateVisual(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); if (e.ClickCount == 2) { ResetView(); return; }
        lastDrag = e.GetPosition(this); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (lastDrag is not { } last) return;
        var current = e.GetPosition(this); Orbit((current.X - last.X) * .012, (current.Y - last.Y) * .008); lastDrag = current; e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); lastDrag = null; e.Pointer.Capture(null); e.Handled = true; }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); lastDrag = null; }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    { base.OnPointerWheelChanged(e); Zoom(Math.Exp(e.Delta.Y * .08)); e.Handled = true; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key) { case Key.Left: Orbit(-.12, 0); break; case Key.Right: Orbit(.12, 0); break;
            case Key.Up: Orbit(0, -.08); break; case Key.Down: Orbit(0, .08); break; case Key.Home: ResetView(); break;
            case Key.Add: case Key.OemPlus: Zoom(1.1); break; case Key.Subtract: case Key.OemMinus: Zoom(1 / 1.1); break; default: return; }
        e.Handled = true;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var matrix = Matrix4x4.CreateRotationZ((float)yaw) * Matrix4x4.CreateRotationX((float)pitch);
        var factor = (float)(Math.Min(Bounds.Width / .95, Bounds.Height) * .82 * zoom);
        var time = (float)(System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
        var moving = online && status?.State == JobState.Printing && lastDrag is null;
        var fraction = status?.State == JobState.Finished ? 1 : (float)Math.Clamp((status?.Progress ?? 0) / 100, 0, 1);
        var spools = status?.Ams.FirstOrDefault()?.Spools ?? [];
        var active = spools.FirstOrDefault(s => s.Active) ?? status?.External;
        var partColor = ParseColor(active?.Color);
        var faces = new List<Face>(TriangleCount);
        foreach (var mesh in model.Meshes)
        {
            if (mesh.Part >= VisibleParts && mesh.Part >= 0) continue;
            if (status is not null && status.Ams.Count == 0 && mesh.Name.Contains("AMS", StringComparison.Ordinal)) continue;
            var spool = mesh.Spool >= 0 ? spools.FirstOrDefault(s => s.Slot == mesh.Spool) : null;
            if (mesh.Spool >= 0 && spool?.Loaded != true) continue;
            var offset = Vector3.Zero;
            var bedSlinger = model.Shape is "mini" or "bedSlinger";
            if (!bedSlinger && mesh.Name.Contains("BedCarriage")) offset.Z += model.PartHeight * (1 - fraction);
            if (bedSlinger && mesh.Name.Contains("Gantry")) offset.Z += model.PartHeight * (fraction - 1);
            if (moving && mesh.Name.Contains("Toolhead")) { offset.X += MathF.Sin(time * 1.1f) * .035f; offset.Y += MathF.Cos(time * .8f) * .025f; }
            if (moving && bedSlinger && mesh.Name.Contains("/Bed")) offset.Y += MathF.Sin(time * .7f) * .025f;
            var transformed = mesh.Vertices.Select(v => Vector3.Transform((new Vector3(v[0], v[1], v[2]) + offset - model.Center) / model.Size, matrix)).ToArray();
            SKPoint Project(Vector3 p)
            { var perspective = 3f / (3f + p.Y); return new((float)Bounds.Width / 2 + p.X * factor * perspective, (float)Bounds.Height * .51f - p.Z * factor * perspective); }
            foreach (var face in mesh.Faces)
            {
                var a = transformed[(int)face[0]]; var b = transformed[(int)face[1]]; var c = transformed[(int)face[2]];
                var normal = Vector3.Cross(b - a, c - a); if (normal.LengthSquared() < 1e-14) continue;
                normal = Vector3.Normalize(normal);
                if (normal.Y > .05 && face[6] >= .99) continue;
                var light = .5f + .65f * Math.Max(0, Vector3.Dot(normal, Vector3.Normalize(new Vector3(-.5f, -.8f, 1))));
                if (status?.Light == false && (mesh.Part >= 0 || mesh.Name.Contains("Toolhead"))) light *= .6f;
                var rgb = mesh.Part >= 0 ? partColor : mesh.Spool >= 0 ? ParseColor(spool?.Color) : new Vector3(face[3], face[4], face[5]);
                if (mesh.Name.Contains("Lamp")) rgb = status?.Light == true ? new Vector3(1, .95f, .8f) : new Vector3(.15f);
                byte Channel(float value) => (byte)Math.Clamp((int)(value * light * 255), 0, 255);
                faces.Add(new((a.Y + b.Y + c.Y) / 3, Project(a), Project(b), Project(c), new SKColor(Channel(rgb.X), Channel(rgb.Y), Channel(rgb.Z), (byte)(face[6] * 255))));
            }
        }
        faces.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        var points = new SKPoint[faces.Count * 3]; var colors = new SKColor[points.Length];
        for (var i = 0; i < faces.Count; i++)
        { var face = faces[i]; points[i * 3] = face.A; points[i * 3 + 1] = face.B; points[i * 3 + 2] = face.C; colors[i * 3] = colors[i * 3 + 1] = colors[i * 3 + 2] = face.Color; }
        context.Custom(new Frame(new Rect(Bounds.Size), points, colors));
    }
    private static Vector3 ParseColor(string? hex) => Color.TryParse("#" + hex, out var c) ? new(c.R / 255f, c.G / 255f, c.B / 255f) : new(.5f, .8f, .65f);
}
