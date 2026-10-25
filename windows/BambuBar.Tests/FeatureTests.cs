using System.Net;
using System.Security.Authentication;
using System.Text.Json.Nodes;
using BambuBar.Core;
using static BambuBar.Tests.Program;

namespace BambuBar.Tests;

internal static class FeatureTests
{
    private static PrinterStatus Parse(string json)
    { var doc = new ReportDocument(); doc.Apply(JsonNode.Parse(json)!.AsObject()); return ReportParser.Parse(doc, new Catalogs("en"), PrinterModel.Find("N7")); }
    public static void Localizations()
    {
        Check(TextCatalog.Languages.Count == 18, "Not all source languages loaded");
        Check(TextCatalog.Translate("Layer %lld of %lld", "en", 3, 8) == "Layer 3 of 8", "positional placeholders");
        Check(TextCatalog.Translate("%lld%%", "pt-BR", 64) == "64%", "escaped percent");
        foreach (var language in TextCatalog.Languages)
            Check(TextCatalog.Translate("Print finished", language).Length > 0 && TextCatalog.Translate("Nozzle", language).Length > 0, "missing language");
        Check(TextCatalog.Translate("Nozzle", "pt-BR") != "Nozzle", "Portuguese not sourced from xcstrings");
        Check(TextCatalog.Translate("%lld printers", "pt-BR", 1) == "1 impressora" && TextCatalog.Translate("%lld printers", "pt-BR", 2) == "2 impressoras", "Portuguese plural selection");
        Check(TextCatalog.Translate("%lld spool(s) loaded", "pt-BR", 3) == "3 carretéis carregados", "plural strings fell back to English");
        Check(PrintStages.Keys.Values.All(TextCatalog.Contains), "stage key missing from xcstrings");
    }
    public static void Telemetry()
    {
        var s = Parse("""{"print":{"gcode_state":"RUNNING","stg_cur":7,"task_id":"42","stat":"00800000","wifi_signal":"-53dBm","sdcard":true,"nozzle_type":"HH01","nozzle_diameter":"0.4","heatbreak_fan_speed":"15","cooling_fan_speed":7,"big_fan1_speed":255,"big_fan2_speed":8,"plate_idx":2,"upgrade_state":{"new_version_state":1},"device":{"airduct":{"parts":[{"id":16,"state":80},{"id":32,"state":42},{"id":48,"state":60}]},"extruder":{"state":16,"info":[{"id":1,"temp":16384220},{"id":0,"temp":14418130}]}}}}""");
        Check(s.StageId == 7 && s.TaskId == "42" && s.DoorOpen == true && s.WifiSignal == -53 && s.HasSdCard == true, "machine telemetry");
        Check(s.PartFan == 80 && s.AuxFan == 42 && s.ExhaustFan == 60 && s.HeatbreakFan == 100, "packed fans");
        Check(s.LeftNozzleInUse == true && s.NozzleDiameter == .4 && s.NozzleType == "HH01" && s.PlateIndex == 2 && s.FirmwareUpdateAvailable, "nozzle metadata");
        s = Parse("""{"print":{"cooling_fan_speed":7,"big_fan1_speed":255,"big_fan2_speed":15}}""");
        Check(s.PartFan == 47 && s.AuxFan == 100 && s.ExhaustFan == 100 && s.DoorOpen is null, "legacy fans or missing readings");
    }
    public static void AmsAndErrors()
    {
        var s = Parse("""{"print":{"gcode_state":"FAILED","print_error":50348044,"hms":[{"attr":50331904,"code":65537},{"attr":50331904,"code":131074}],"ams":{"ams":[{"id":0,"humidity":2,"dry_time":45,"tray":[{"id":0,"tray_type":"PLA","remain":80,"tray_weight":1000,"tray_color":"112233FF","cols":["FF0000FF","0000FFFF"],"nozzle_temp_min":190,"nozzle_temp_max":230}]}]}}}""");
        Check(s.Canceled && s.StateKey == "Canceled", "cancel not distinguished from failure");
        Check(s.Ams[0].HumidityLevel == 4 && s.Ams[0].DryingMinutes == 45 && s.Ams[0].Humidity is null, "AMS scale/drying");
        var spool = s.Ams[0].Spools[0];
        Check(spool.Colors.SequenceEqual(new[] { "FF0000", "0000FF" }) && spool.NozzleMin == 190 && spool.NozzleMax == 230 && spool.Grams == 800, "multicolor/range/grams");
        Check(s.Issues.Count == 2 && s.Issues[0].Severity == IssueSeverity.Fatal && s.Issues[1].Severity == IssueSeverity.Serious && s.Issues[0].Wiki.Host == "wiki.bambulab.com", "HMS severity/wiki");
        Check(PrintStages.Describe(new PrinterStatus { State = JobState.Printing, StageId = 7 }, "en") == "Heating hotend", "stage text");
        Check(PrintStages.Describe(new PrinterStatus { State = JobState.Paused, StageId = 6 }, "pt-BR") != "Paused: filament ran out", "localized pause reason");
        Check(PrintStages.Describe(new PrinterStatus { State = JobState.Idle, StageId = 0 }, "en") == "Ready", "idle mislabelled printing");
    }
    public static void Clock()
    {
        var origin = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var s = new PrinterStatus { State = JobState.Printing, RemainingMinutes = 62, UpdatedAt = origin };
        Check(s.ProjectedMinutes(origin.AddSeconds(90)) == 60.5, "remaining time does not count down");
        Check(s.FinishAt(origin) == s.FinishAt(origin.AddSeconds(90)), "ETA drifts between reports");
        Check((s with { State = JobState.Paused }).ProjectedMinutes(origin.AddHours(1)) == 62, "paused clock counts down");
        Check(s.ProjectedMinutes(origin.AddHours(1), online: false) == 62, "offline clock counts down");
        Check(s.ProjectedMinutes(origin.AddHours(5)) == 0 && s.ProjectedMinutes(origin.AddHours(-1)) == 62, "clock boundaries");
        Check(new PrinterStatus().ProjectedMinutes(origin) is null, "unknown remaining time became zero");
        Check((s with { State = JobState.Paused }).FinishAt(origin) is null && s.FinishAt(origin, online: false) is null, "ETA shown while paused/offline");
        Check((s with { State = JobState.Preparing, RemainingMinutes = 0 }).ProjectedMinutes(origin) is null, "unknown preparation estimate became zero");
    }
    public static void Notifications()
    {
        var tracker = new PrintNotifications();
        var running = new PrinterStatus { State = JobState.Printing, Job = "Test", TaskId = "1" };
        Check(tracker.Observe("A", running).Count == 0, "first report generates stale toast");
        var paused = running with { State = JobState.Paused, StageId = 6 };
        Check(tracker.Observe("A", paused).Single().TitleKey == "Print paused", "pause not notified");
        Check(tracker.Observe("A", paused).Count == 0, "duplicate pause");
        tracker.Observe("A", running);
        Check(tracker.Observe("A", running with { State = JobState.Finished }).Single().TitleKey == "Print finished", "finish not notified");
        Check(tracker.Observe("A", running with { TaskId = "2" }).Single().TitleKey == "Print started", "new job not notified");
        Check(tracker.Observe("A", running with { State = JobState.Failed, Canceled = true }).Single().TitleKey == "Print canceled", "cancel notified as failure");
        Check(tracker.Observe("B", paused).Count == 0, "printers share notification baseline");
        tracker.Reset("A"); Check(tracker.Observe("A", running).Count == 0, "reconnection produces stale notification");
        var error = running with { Issues = [new("HMS", "test") { Severity = IssueSeverity.Fatal }] };
        Check(tracker.Observe("A", error).Single().Kind == "error" && tracker.Observe("A", error).Count == 0, "error deduplication");
        tracker.Observe("A", running);
        Check(tracker.Observe("A", error).Count == 1, "cleared error recurrence not notified");
    }
    public static async Task ManualIdentity()
    {
        await using var broker = new TestBroker();
        var serial = await PrinterIdentity.ReadSerialAsync(broker.Config.Host, broker.Config.Port, CancellationToken.None, emulator: true);
        Check(serial == "SERIAL" && broker.ConnectPackets == 0, "IP-only setup sent credentials or failed to read CN");
        var rejected = false;
        try { await PrinterIdentity.ReadSerialAsync(broker.Config.Host, broker.Config.Port, CancellationToken.None); }
        catch (AuthenticationException) { rejected = true; }
        Check(rejected && broker.ConnectPackets == 0, "IP-only setup accepted unknown certificate authority");
    }
}
