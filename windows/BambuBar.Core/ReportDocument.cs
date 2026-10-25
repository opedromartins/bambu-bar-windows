using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BambuBar.Core;

public sealed class ReportDocument
{
    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(false, true);
    public JsonObject Print { get; private set; } = new();
    public Dictionary<string, (string Product, string Version)> Modules { get; } = [];
    public bool HasStatus => Print["gcode_state"] is not null;
    public void Reset() { Print = new(); Modules.Clear(); }
    public bool Apply(JsonObject message)
    {
        var changed = false;
        if (message["print"] is JsonObject p && (Text(p["command"]) is "" or "push_status"))
        {
            Print = Number(p["msg"]) == 0 && p["gcode_state"] is not null
                ? (JsonObject)p.DeepClone() : Merge(Print, p);
            changed = true;
        }
        if (message["info"] is JsonObject info && Text(info["command"]) == "get_version" && info["module"] is JsonArray modules)
        {
            foreach (var item in modules.OfType<JsonObject>())
                if (Text(item["name"]) is { Length: > 0 } name)
                    Modules[name] = (Text(item["product_name"]), Text(item["sw_ver"]));
            changed = true;
        }
        return changed;
    }
    public static JsonObject Merge(JsonObject current, JsonObject delta)
    {
        var result = (JsonObject)current.DeepClone();
        foreach (var (key, value) in delta)
        {
            var idKey = key == "lights_report" ? "node" : "id";
            if (value is JsonObject obj && result[key] is JsonObject old)
                result[key] = Merge(old, obj);
            else if (value is JsonArray array && result[key] is JsonArray previous && array.Count > 0 &&
                     array.All(n => n is JsonObject o && o[idKey] is not null))
            {
                var merged = (JsonArray)previous.DeepClone();
                foreach (var item in array.OfType<JsonObject>())
                {
                    var index = Enumerable.Range(0, merged.Count).FirstOrDefault(i =>
                        merged[i] is JsonObject o && Text(o[idKey]) == Text(item[idKey]), -1);
                    if (index < 0) merged.Add(item.DeepClone());
                    else merged[index] = Merge((JsonObject)merged[index]!, item);
                }
                result[key] = merged;
            }
            else result[key] = value?.DeepClone();
        }
        return result;
    }
    public static string Text(JsonNode? n) => n is JsonValue ? n.ToString() : "";
    public static JsonNode? Get(JsonNode? node, params string[] keys)
    {
        foreach (var key in keys) node = node is JsonObject obj ? obj[key] : null;
        return node;
    }
    public static double? Number(JsonNode? n) => double.TryParse(Text(n), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    public static int? Int(JsonNode? n) => Number(n) is { } d && d is >= int.MinValue and <= int.MaxValue ? (int)d : null;
    public static ulong? Hex(JsonNode? n) => ulong.TryParse(Text(n), NumberStyles.HexNumber,
        CultureInfo.InvariantCulture, out var value) ? value : null;
    public static JsonObject? Decode(ReadOnlySpan<byte> bytes)
    {
        try { return JsonNode.Parse(StrictUtf8.GetString(bytes)) as JsonObject; }
        catch (Exception ex) when (ex is JsonException or System.Text.DecoderFallbackException)
        {
            try { return JsonNode.Parse(System.Text.Encoding.Latin1.GetString(bytes)) as JsonObject; }
            catch (JsonException) { return null; }
        }
    }
}
