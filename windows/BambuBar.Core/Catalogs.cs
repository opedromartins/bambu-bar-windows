using System.IO.Compression;
using System.Text.Json.Nodes;

namespace BambuBar.Core;

public sealed class Catalogs
{
    private readonly JsonObject filaments;
    private readonly JsonObject english;
    private readonly JsonObject localized;
    public Catalogs(string language = "en", string? root = null)
    {
        root ??= Path.Combine(AppContext.BaseDirectory, "Resources");
        filaments = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "filaments.json")))!.AsObject();
        english = LoadHms(root, "en");
        var hmsLanguage = TextCatalog.Normalize(language) == "zh-Hant" ? "zh-Hans" : TextCatalog.Normalize(language);
        localized = File.Exists(Path.Combine(root, "HMS", $"hms_{hmsLanguage}.json.deflate")) ? LoadHms(root, hmsLanguage) : english;
    }
    private static JsonObject LoadHms(string root, string lang)
    {
        using var input = File.OpenRead(Path.Combine(root, "HMS", $"hms_{lang}.json.deflate"));
        // make-hms.py uses raw DEFLATE (wbits=-15), without a zlib header.
        using var stream = new DeflateStream(input, CompressionMode.Decompress);
        return JsonNode.Parse(stream)!.AsObject();
    }
    public string? Issue(string table, string key) =>
        localized[table]?[key]?.ToString() ?? english[table]?[key]?.ToString();
    public (string Brand, string Product) Filament(string preset)
    {
        var entry = filaments["presets"]?[preset.ToUpperInvariant()] as JsonArray;
        return (entry?[0]?.ToString() ?? "", entry?[1]?.ToString() ?? "");
    }
    public string? Color(string tagCode, string preset, string color)
    {
        var parts = tagCode.ToUpperInvariant().Split('-');
        if (parts.Length == 2)
        {
            var letters = new string(parts[1].TakeWhile(char.IsLetter).ToArray());
            var number = int.TryParse(parts[1][letters.Length..], out var n) ? n.ToString() : "";
            if (filaments["colors"]?[$"GF{parts[0]}-{letters}{number}"] is { } named) return named.ToString();
        }
        return color.Length >= 6 ? filaments["colors"]?[$"{preset.ToUpperInvariant()}-{color[..6].ToUpperInvariant()}"]?.ToString() : null;
    }
}
