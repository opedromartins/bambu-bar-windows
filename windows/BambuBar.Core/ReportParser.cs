using System.Text.Json.Nodes;
using static BambuBar.Core.ReportDocument;

namespace BambuBar.Core;

public static class ReportParser
{
    private static Temperature Unpack(JsonNode? n) => Int(n) is { } packed
        ? new(packed & 0xffff, (packed >> 16) & 0xffff) : new();
    private static int? Percent(JsonNode? n) => Int(n) is >= 0 and <= 100 ? Int(n) : null;
    private static int? Fan(JsonNode? n) => Int(n) is { } v && v >= 0 ? Math.Clamp((int)Math.Round(v / (v <= 15 ? 15d : 255d) * 100), 0, 100) : null;
    public static PrinterStatus Parse(ReportDocument doc, Catalogs catalogs, PrinterModel? model = null)
    {
        var p = doc.Print;
        var d = p["device"] as JsonObject;
        var extruders = (Get(d, "extruder", "info") as JsonArray)?.OfType<JsonObject>()
            .Select((e, i) => (Id: Int(e["id"]) ?? i, Value: e)).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.Last().Value) ?? [];
        var current = ((Int(Get(d, "extruder", "state")) ?? 0) >> 4) & 15;
        if (!extruders.ContainsKey(current)) current = 0;
        extruders.TryGetValue(0, out var right);
        extruders.TryGetValue(1, out var left);
        extruders.TryGetValue(current, out var active);
        var nozzle = right?["temp"] is { } packedNozzle ? Unpack(packedNozzle) :
            new Temperature(Number(p["nozzle_temper"]), Number(p["nozzle_target_temper"]));
        var bedPacked = Get(d, "bed", "info", "temp") ?? d?["bed_temp"];
        var bed = bedPacked is not null ? Unpack(bedPacked) : new Temperature(Number(p["bed_temper"]), Number(p["bed_target_temper"]));
        var chamber = Get(d, "ctc", "info", "temp") is { } cp ? Unpack(cp) :
            new Temperature(Number(Get(p, "info", "temp")) ?? Number(p["chamber_temper"]));
        if (model?.Chamber == false || chamber.Current is null or <= 0 or > 120) chamber = null;
        var activeId = Int(Get(p, "ams", "tray_now"));
        activeId = activeId switch { 255 => null, 254 => 254, _ => activeId };
        if (Int(active?["snow"]) is { } snow && snow != 0xffff)
        {
            var unit = snow >> 8; var slot = snow & 255;
            activeId = unit is 254 or 255 ? slot == 255 ? null : 254 : unit >= 128 ? unit : unit * 4 + (slot & 3);
        }
        var exist = Hex(Get(p, "ams", "tray_exist_bits"));
        var rfid = Hex(Get(p, "ams", "tray_is_bbl_bits"));
        var units = new List<AmsUnit>();
        foreach (var u in (Get(p, "ams", "ams") as JsonArray ?? []).OfType<JsonObject>())
        {
            if (Int(u["id"]) is not { } id) continue;
            var kind = id >= 128 || doc.Modules.ContainsKey($"n3s/{id}") ? "AMS HT" :
                doc.Modules.ContainsKey($"n3f/{id}") ? "AMS 2 Pro" :
                doc.Modules.ContainsKey($"ams_f1/{id}") || model?.Shape is "mini" or "bedSlinger" ? "AMS Lite" : "AMS";
            if (kind == "AMS" && Hex(u["info"]) is { } amsInfo)
                kind = (amsInfo & 15) switch { 2 => "AMS Lite", 3 => "AMS 2 Pro", 4 => "AMS HT", _ => "AMS" };
            var spools = new List<Spool>();
            foreach (var t in (u["tray"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Int(t["id"]) is not { } slot) continue;
                var global = id >= 128 ? id : id * 4 + slot;
                var bit = id >= 128 ? 16 + id - 128 : id * 4 + slot;
                var loaded = exist is null || bit is < 0 or > 63 || (exist & (1UL << bit)) != 0;
                var tagged = rfid is null || bit is < 0 or > 63 || (rfid & (1UL << bit)) != 0;
                spools.Add(ParseSpool(t, global, id, slot, loaded, tagged, activeId == global, catalogs));
            }
            units.Add(new(id, kind, kind == "AMS Lite" || Int(u["humidity_raw"]) == 0 ? null : Percent(u["humidity_raw"]),
                Number(u["temp"]) is > 0 and <= 100 ? Number(u["temp"]) : null, spools.OrderBy(s => s.Slot).ToArray())
            { HumidityLevel = kind != "AMS Lite" && Int(u["humidity"]) is >= 1 and <= 5 ? 6 - Int(u["humidity"]) : null,
              DryingMinutes = Int(u["dry_time"]) is > 0 ? Int(u["dry_time"]) : null });
        }
        var extNode = (p["vir_slot"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault() ?? p["vt_tray"] as JsonObject;
        var external = extNode is null ? null : ParseSpool(extNode, 254, 255, 0, true, true, activeId == 254, catalogs) with { Remain = null, Weight = null };
        var issues = new List<PrinterIssue>();
        foreach (var h in (p["hms"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (Number(h["attr"]) is not { } attr || Number(h["code"]) is not { } code) continue;
            var a = unchecked((uint)(long)attr); var c = unchecked((uint)(long)code);
            if ((c & 0xffff) < 0x4000 && (c >> 16) == 0) continue;
            var key = $"{a:X8}{c:X8}";
            var pretty = $"HMS_{a >> 16:X4}_{a & 0xffff:X4}_{c >> 16:X4}_{c & 0xffff:X4}";
            issues.Add(new(pretty, catalogs.Issue("hms", key) ?? TextCatalog.Get("The printer reported %@.", pretty))
            { Severity = Enum.IsDefined((IssueSeverity)(c >> 16)) ? (IssueSeverity)(c >> 16) : IssueSeverity.Common });
        }
        if (Number(p["print_error"]) is { } error && error != 0)
        {
            var key = $"{unchecked((uint)(long)error):X8}";
            var value = unchecked((uint)(long)error);
            if (key is not ("0300400C" or "0500400E") && !issues.Any(i => i.Code.Replace("_", "").EndsWith(key, StringComparison.Ordinal)))
                issues.Add(new(key, catalogs.Issue("error", key) ?? TextCatalog.Get("The printer reported error %@.", key))
                { Severity = ((value & 0xf000) >> 12) switch { 4 => IssueSeverity.Fatal, 8 => IssueSeverity.Serious, _ => IssueSeverity.Common } });
        }
        var rawName = Text(p["subtask_name"]);
        if (rawName.Length == 0) rawName = Text(p["gcode_file"]).Replace('\\', '/').Split('/').Last();
        foreach (var extension in new[] { ".gcode.3mf", ".3mf", ".gcode" })
            if (rawName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) rawName = rawName[..^extension.Length];
        if (!rawName.Contains(' ')) rawName = rawName.Replace('_', ' ');
        var light = (p["lights_report"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(l => Text(l["node"]) == "chamber_light");
        int? partFan = Fan(p["cooling_fan_speed"]), auxFan = Fan(p["big_fan1_speed"]), exhaustFan = Fan(p["big_fan2_speed"]);
        foreach (var fan in (Get(d, "airduct", "parts") as JsonArray ?? []).OfType<JsonObject>())
            if (Int(fan["id"]) is { } id && Int(fan["state"]) is { } fanState)
            {
                var percent = Math.Clamp(fanState & 255, 0, 100);
                switch (id >> 4) { case 1: partFan = percent; break; case 2: auxFan = percent; break; case 3: exhaustFan = percent; break; }
            }
        var nozzleInfo = (Get(d, "nozzle", "info") as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
        var taskId = new[] { Text(p["task_id"]), Text(p["subtask_id"]) }.FirstOrDefault(id => id.Length > 0 && id != "0");
        var nozzleType = Text(nozzleInfo?["type"] ?? p["nozzle_type"]);
        var rtsp = Text(Get(p, "ipcam", "rtsp_url"));
        return new()
        {
            State = Text(p["gcode_state"]).ToUpperInvariant() switch {
                "RUNNING" => JobState.Printing, "PAUSE" => JobState.Paused, "PREPARE" or "SLICING" => JobState.Preparing,
                "FINISH" => JobState.Finished, "FAILED" => JobState.Failed, _ => JobState.Idle },
            Job = rawName.Trim(), Progress = Number(p["mc_percent"]) is { } progress ? Math.Clamp(progress, 0, 100) : null,
            RemainingMinutes = Number(p["mc_remaining_time"]) is >= 0 ? Number(p["mc_remaining_time"]) : null,
            Layer = Int(p["layer_num"]), TotalLayers = Int(p["total_layer_num"]), Nozzle = nozzle,
            SecondNozzle = left?["temp"] is { } lp ? Unpack(lp) : null, Bed = bed, Chamber = chamber,
            Light = model?.Light == false || light is null ? null : Text(light["mode"]) != "off",
            ControlsLocked = ((Hex(p["fun"]) ?? 0) & 0x20000000) != 0, Speed = Int(p["spd_lvl"]),
            Firmware = doc.Modules.GetValueOrDefault("ota").Version, Ams = units.OrderBy(u => u.Id).ToArray(), External = external,
            Issues = issues.OrderBy(i => i.Severity).ToArray(), StageId = Int(p["stg_cur"]) ?? Int(Get(p, "stage", "_id")), TaskId = taskId,
            Canceled = Text(p["gcode_state"]).Equals("FAILED", StringComparison.OrdinalIgnoreCase) && Int(p["print_error"]) is (50348044 or 83902478),
            LeftNozzleInUse = left is null ? null : current == 1,
            PartFan = partFan, AuxFan = model?.Shape is "mini" or "bedSlinger" ? null : auxFan,
            ExhaustFan = model?.Shape is "mini" or "bedSlinger" ? null : exhaustFan, HeatbreakFan = Fan(p["heatbreak_fan_speed"]),
            DoorOpen = model?.Light == false || Hex(p["stat"]) is not { } bits ? null : (bits & 0x00800000) != 0,
            WifiSignal = int.TryParse(Text(p["wifi_signal"]).Replace("dBm", "", StringComparison.OrdinalIgnoreCase), out var dbm) ? dbm : null,
            HasSdCard = p["sdcard"] is JsonValue sd && sd.TryGetValue<bool>(out var present) ? present : null,
            NozzleDiameter = Number(nozzleInfo?["diameter"] ?? p["nozzle_diameter"]), NozzleType = nozzleType.Length == 0 ? null : nozzleType,
            PlateIndex = Int(p["plate_idx"]), LiveViewEnabled = rtsp.Length == 0 ? null : rtsp != "disable",
            FirmwareUpdateAvailable = Int(Get(p, "upgrade_state", "new_version_state")) == 1
        };
    }
    private static Spool ParseSpool(JsonObject t, int id, int unit, int slot, bool exists, bool tagged, bool active, Catalogs catalogs)
    {
        var material = Text(t["tray_type"]);
        var state = Int(t["state"]);
        var loaded = exists && material.Length > 0 && (state is null or <= 3 || (state & 9) == 9);
        var preset = Text(t["tray_info_idx"]);
        var tag = Text(t["tag_uid"]);
        var bambu = tagged && tag.Length > 0 && tag.Any(c => c != '0');
        var (brand, product) = catalogs.Filament(preset);
        if (bambu) brand = "Bambu Lab";
        if (Text(t["tray_sub_brands"]) is { Length: > 0 } name) product = name;
        if (product.Length == 0) product = material;
        var color = Text(t["tray_color"]);
        return new(id, unit, slot, loaded, loaded ? material : "", loaded ? product : "", loaded ? brand : "",
            color.Length >= 6 ? color[..6] : "808080", loaded ? catalogs.Color(Text(t["tray_id_name"]), preset, color) : null,
            loaded ? Percent(t["remain"]) : null, loaded && Int(t["tray_weight"]) is > 0 and <= 10000 ? Int(t["tray_weight"]) : null, active && loaded)
        { Colors = loaded ? (t["cols"] as JsonArray ?? []).Select(Text).Where(c => c.Length >= 6 && c[..6].All(char.IsAsciiHexDigit)).Select(c => c[..6]).ToArray() : [],
          NozzleMin = loaded && Int(t["nozzle_temp_min"]) is > 0 ? Int(t["nozzle_temp_min"]) : null,
          NozzleMax = loaded && Int(t["nozzle_temp_max"]) is > 0 ? Int(t["nozzle_temp_max"]) : null };
    }
}
