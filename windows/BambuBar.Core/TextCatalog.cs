using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BambuBar.Core;

public static partial class TextCatalog
{
    private static readonly Lazy<JsonObject> Strings = new(() => JsonNode.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Resources", "Localizable.xcstrings")))!["strings"]!.AsObject());
    private static string language = Normalize(CultureInfo.CurrentUICulture.Name);
    public static string Language { get => language; set => language = Normalize(value); }
    public static IReadOnlyList<string> Languages => Strings.Value.SelectMany(s => (s.Value?["localizations"] as JsonObject ?? []).Select(l => l.Key)).Distinct().Order().ToArray();
    public static bool Contains(string key) => Strings.Value.ContainsKey(key);
    public static string Normalize(string value)
    {
        if (value.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
        if (value.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return value.Contains("Hant", StringComparison.OrdinalIgnoreCase) || value.Contains("TW", StringComparison.OrdinalIgnoreCase) || value.Contains("HK", StringComparison.OrdinalIgnoreCase) ? "zh-Hant" : "zh-Hans";
        var code = value.Split('-')[0].ToLowerInvariant();
        return new[] { "en", "cs", "de", "es", "fr", "hu", "it", "ja", "ko", "nl", "pl", "ru", "sv", "tr", "uk" }.Contains(code) ? code : "en";
    }
    public static string Get(string key, params object?[] args) => Translate(key, Language, args);
    public static string Translate(string key, string lang, params object?[] args)
    {
        if (!Strings.Value.TryGetPropertyValue(key, out var entry)) throw new ArgumentException($"Unknown Localizable.xcstrings key: {key}");
        var variants = entry?["localizations"];
        var normalized = Normalize(lang);
        var quantity = args.FirstOrDefault(a => a is byte or short or int or long or float or double or decimal);
        var count = quantity is null ? 0 : Convert.ToDouble(quantity, CultureInfo.InvariantCulture);
        string? Value(JsonNode? node)
        {
            if (node?["stringUnit"]?["value"] is { } direct) return direct.ToString();
            var plural = node?["variations"]?["plural"];
            return (plural?[Plural(normalized, count)] ?? plural?["other"] ?? plural?["one"])?["stringUnit"]?["value"]?.ToString();
        }
        var format = Value(variants?[normalized]) ?? Value(variants?["en"]) ?? key;
        var index = 0;
        var culture = CultureInfo.GetCultureInfo(Normalize(lang));
        return Placeholder().Replace(format, match =>
        {
            if (match.Value == "%%") return "%";
            var i = match.Groups["position"].Success ? int.Parse(match.Groups["position"].Value, CultureInfo.InvariantCulture) - 1 : index++;
            return i < args.Length ? Convert.ToString(args[i], culture) ?? "" : match.Value;
        });
    }
    [GeneratedRegex(@"%%|%(?:(?<position>\d+)\$)?(?:@|lld|llu|ld|d|u|f)")]
    private static partial Regex Placeholder();
    private static string Plural(string lang, double count)
    {
        if (lang is "ja" or "ko" or "zh-Hans" or "zh-Hant") return "other";
        if (count != Math.Truncate(count)) return lang is "cs" ? "many" : "other";
        var n = Math.Abs((long)Math.Clamp(count, long.MinValue + 1d, long.MaxValue));
        var ten = n % 10; var hundred = n % 100;
        if (lang is "ru" or "uk") return ten == 1 && hundred != 11 ? "one" : ten is >= 2 and <= 4 && hundred is not (>= 12 and <= 14) ? "few" : "many";
        if (lang == "pl") return n == 1 ? "one" : ten is >= 2 and <= 4 && hundred is not (>= 12 and <= 14) ? "few" : "many";
        if (lang == "cs") return n == 1 ? "one" : n is >= 2 and <= 4 ? "few" : "other";
        if (lang is "pt-BR" or "fr") return n is 0 or 1 ? "one" : n % 1000000 == 0 ? "many" : "other";
        return n == 1 ? "one" : "other";
    }
}
