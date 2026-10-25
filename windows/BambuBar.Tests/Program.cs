using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using BambuBar.Core;

namespace BambuBar.Tests;

internal static class Program
{
    private static int passed;
    internal static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void Test(string name, Action test)
    { test(); passed++; Console.WriteLine($"PASS {name}"); }
    private static async Task Test(string name, Func<Task> test)
    { await test(); passed++; Console.WriteLine($"PASS {name}"); }
    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();
    private static ReportDocument Doc(string print)
    { var d = new ReportDocument(); d.Apply(Json("{\"print\":" + print + "}")); return d; }
    internal static async Task Until(Func<bool> condition, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        { if (DateTime.UtcNow > deadline) throw new TimeoutException("Expected condition did not occur."); await Task.Delay(50); }
    }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var catalogs = new Catalogs("pt-BR");
            Test("xcstrings languages, positional parameters and translated stage keys", FeatureTests.Localizations);
            Test("packed/legacy telemetry, fans, doors, storage and active nozzle", FeatureTests.Telemetry);
            Test("AMS drying, humidity scale, multicolor, nozzle ranges, severity and cancellation", FeatureTests.AmsAndErrors);
            Test("projected remaining time, stable ETA, pause/offline and clock boundaries", FeatureTests.Clock);
            Test("notification baselines, transitions, cancellation, recurrence and deduplication", FeatureTests.Notifications);
            await Test("IP-only setup reads verified certificate serial without MQTT login", FeatureTests.ManualIdentity);
            Test("SSDP advertisements, search replies, names, models and endpoints", DiscoveryTests.Packets);
            Test("malformed discovery packets and non-printer endpoints are rejected", DiscoveryTests.InvalidPackets);
            await Test("UDP discovery deduplicates, follows announcements, expires and releases sockets", DiscoveryTests.UdpLifecycle);
            await Test("DHCP identity probes validate TLS before any MQTT credentials", DiscoveryTests.IdentityProbe);
            Test("partial reports preserve previous fields and match numeric/string ids", () =>
            {
                var d = Doc("""{"msg":0,"gcode_state":"RUNNING","mc_percent":10,"ams":{"ams":[{"id":0,"tray":[{"id":"1","tray_type":"PLA","remain":80}]}]},"lights_report":[{"node":"chamber_light","mode":"off"}]}""");
                d.Apply(Json("""{"print":{"mc_percent":20,"ams":{"ams":[{"id":"0","tray":[{"id":1,"remain":70}]}]},"lights_report":[{"node":"chamber_light","mode":"on"}]}}"""));
                var s = ReportParser.Parse(d, catalogs);
                Check(s.Progress == 20 && s.State == JobState.Printing && s.Ams.Single().Spools.Single().Material == "PLA" &&
                    s.Ams.Single().Spools.Single().Remain == 70 && s.Light == true, "merge lost fields");
            });
            Test("full reports remove unplugged AMS and cleared errors", () =>
            {
                var d = Doc("""{"gcode_state":"RUNNING","ams":{"ams":[{"id":0}]},"print_error":1234}""");
                d.Apply(Json("""{"print":{"msg":0,"gcode_state":"IDLE"}}"""));
                Check(ReportParser.Parse(d, catalogs).Ams.Count == 0 && d.Print["print_error"] is null, "full report did not replace state");
            });
            Test("empty arrays clear old entries and command acknowledgements never modify status", () =>
            {
                var d = Doc("""{"gcode_state":"RUNNING","hms":[{"attr":1,"code":40000}]}""");
                d.Apply(Json("""{"print":{"hms":[]}}"""));
                d.Apply(Json("""{"print":{"command":"pause","result":"success","gcode_state":"PAUSE"}}"""));
                Check(d.Print["hms"]!.AsArray().Count == 0 && ReportParser.Parse(d, catalogs).State == JobState.Printing, "ack altered state");
            });
            Test("legacy temperatures, unknown data and control lock defaults", () =>
            {
                var s = ReportParser.Parse(Doc("""{"nozzle_temper":"220.5","nozzle_target_temper":230,"bed_temper":55}"""), catalogs);
                Check(s.Nozzle.Current == 220.5 && s.Nozzle.Target == 230 && s.Bed.Current == 55 && s.Bed.Target is null &&
                    s.Progress is null && !s.ControlsLocked && s.Chamber is null, "unknown readings became zero");
            });
            Test("packed temperatures use extruder ids rather than order", () =>
            {
                var s = ReportParser.Parse(Doc("""{"device":{"extruder":{"info":[{"id":1,"temp":16384220},{"id":0,"temp":14418130}]},"bed":{"info":{"temp":3604535}}}}"""), catalogs);
                Check(s.Nozzle.Current == 210 && s.Nozzle.Target == 220 && s.SecondNozzle?.Current == 220 &&
                    s.SecondNozzle.Target == 250 && s.Bed.Current == 55, "packed temperature mismatch");
            });
            Test("model capabilities hide non-existent sensors and light", () =>
            {
                var s = ReportParser.Parse(Doc("""{"chamber_temper":32,"lights_report":[{"node":"chamber_light","mode":"on"}]}"""), catalogs, PrinterModel.Find("N1"));
                Check(s.Chamber is null && s.Light is null, "A1 mini got chamber hardware");
                Check(PrinterModel.Find("", "094TEST")?.Nozzles == 2 && PrinterModel.Find("BL-P003")?.Name == "X1E", "model lookup");
            });
            Test("firmware locks and localized HMS errors; cancellations are not faults", () =>
            {
                var s = ReportParser.Parse(Doc("""{"fun":"20000000","print_error":50348032}"""), catalogs);
                Check(s.ControlsLocked && s.Issues.Count == 1 && s.Issues[0].Message.Contains("eixo Z"), "lock or Portuguese HMS missing");
                Check(ReportParser.Parse(Doc("""{"print_error":50348044}"""), catalogs).Issues.Count == 0, "cancel classified as fault");
            });
            Test("external spool does not claim remaining grams", () =>
            {
                var s = ReportParser.Parse(Doc("""{"vt_tray":{"tray_type":"PLA","remain":99,"tray_weight":1000},"ams":{"tray_now":254}}"""), catalogs);
                Check(s.External is { Active: true, Grams: null, Remain: null }, "external spool claims weight");
            });
            Test("AMS existence bits, grams, color names and preset vendors", () =>
            {
                var s = ReportParser.Parse(Doc("""{"ams":{"tray_exist_bits":"1","ams":[{"id":0,"tray":[{"id":0,"tray_type":"PLA","remain":81,"tray_weight":1000,"tray_info_idx":"GFA01","tag_uid":"ABCD","tray_id_name":"A01-G1","tray_color":"61C680FF"},{"id":1,"tray_type":"PLA","remain":50}]}]}}"""), catalogs);
                Check(s.Ams[0].Spools[0] is { Grams: 810, Brand: "Bambu Lab", ColorName: "Grass Green" } &&
                    !s.Ams[0].Spools[1].Loaded && s.Ams[0].Spools[1].Remain is null, "filament mapping");
                Check(catalogs.Filament("GFSNL02").Brand == "SUNLU", "third-party preset");
            });
            Test("real P2S fixture parses with HMS catalog and firmware", () =>
            {
                var sample = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "p2s-sample.json")))!;
                var d = new ReportDocument(); d.Apply(sample["pushall"]!.AsObject()); d.Apply(sample["get_version"]!.AsObject());
                var s = ReportParser.Parse(d, catalogs);
                Check(d.HasStatus && s.Nozzle.Current is not null && s.Ams.Count > 0 && !string.IsNullOrEmpty(s.Firmware), "real fixture failed");
            });
            Test("invalid JSON and scalar packets are ignored; Latin1 fallback", () =>
            {
                Check(ReportDocument.Decode("{bad"u8) is null && ReportDocument.Decode("[]"u8) is null, "invalid JSON accepted");
                var bytes = Encoding.Latin1.GetBytes("{\"print\":{\"subtask_name\":\"café\"}}");
                Check(ReportDocument.Decode(bytes)?["print"]?["subtask_name"]?.ToString() == "café", "Latin1 fallback");
            });
            Test("unexpected nested field types and duplicate extruder ids do not crash parsing", () =>
            {
                var d = Doc("""{"ams":"bad","info":4,"device":{"bed":[],"ctc":false,"extruder":{"info":[{"id":0,"temp":220},{"id":0,"temp":230}]}}}""");
                var s = ReportParser.Parse(d, catalogs);
                Check(s.Ams.Count == 0 && s.Nozzle.Current == 230, "malformed nested data");
            });
            Test("emulator bypass restricted to loopback; desktop cannot opt into fleet hostname", () =>
            {
                Check(PrinterTrust.AllowEmulator("127.0.0.1", false) && PrinterTrust.AllowEmulator("::1", false), "loopback blocked");
                Check(!PrinterTrust.AllowEmulator("192.168.1.2", false) && !PrinterTrust.AllowEmulator("fleet", false), "nonlocal bypass");
                var rejected = false;
                try { new PrinterConfig("Test", "192.168.1.2", "SERIAL").Validate(true); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "emulator allowed LAN device");
            });
            Test("certificate chain, serial identity, expiry and private CA isolation", CertificateTests);
            await Test("TLS failure never sends MQTT credentials", async () =>
            {
                await using var broker = new TestBroker();
                await using var session = new PrinterSession(broker.Config, "12345678", catalogs);
                session.Start(); await Until(() => session.Phase == ConnectionPhase.Certificate);
                Check(broker.ConnectPackets == 0, "credentials sent before validating certificate");
            });
            await Test("authentication refusal is terminal", async () =>
            {
                await using var broker = new TestBroker { RefuseAuthentication = true };
                await using var session = new PrinterSession(broker.Config, "12345678", catalogs, emulator: true);
                session.Start(); await Until(() => session.Phase == ConnectionPhase.Unauthorized);
                await Task.Delay(300); Check(broker.ConnectPackets == 1, "retried rejected credentials");
            });
            await Test("a silent broker never becomes online and reconnects after the report deadline", async () =>
            {
                await using var broker = new TestBroker { SuppressReports = true };
                await using var session = new PrinterSession(broker.Config, "12345678", catalogs, emulator: true, retryScale: TimeSpan.FromMilliseconds(10));
                session.Start(); await Until(() => broker.ConnectPackets >= 2, 35);
                Check(session.Status is null && session.Phase != ConnectionPhase.Online, "silent broker marked online");
            });
            await Test("reconnect, command refusal, ack timeout and malformed reports", async () =>
            {
                await using var broker = new TestBroker();
                await using var session = new PrinterSession(broker.Config, "12345678", catalogs, emulator: true, retryScale: TimeSpan.FromMilliseconds(10));
                session.Start(); await Until(() => session.Phase == ConnectionPhase.Online);
                broker.DropConnection(); await Until(() => broker.ConnectPackets >= 2 && session.Phase == ConnectionPhase.Online);
                broker.CommandResult = "failed";
                var refused = false;
                try { await session.CommandAsync("pause"); } catch (IOException) { refused = true; }
                Check(refused, "command refusal swallowed");
                broker.CommandResult = null;
                var timedOut = false;
                try { await session.CommandAsync("pause"); } catch (TimeoutException) { timedOut = true; }
                Check(timedOut, "missing acknowledgement reported success");
                broker.CommandResult = "success";
                await session.CommandAsync("pause");
                Check(session.Phase == ConnectionPhase.Online, "malformed packet broke session");
            });
            if (args.Contains("--integration"))
            {
                await Test("Docker fleet announces all online printers over UDP", DiscoveryTests.Fleet);
                await Test("Docker fleet: models, offline, auth, TLS and every control", () => FleetTests(catalogs));
            }
            Console.WriteLine($"{passed} scenarios passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void CertificateTests()
    {
        using var rootKey = RSA.Create(2048);
        var req = new CertificateRequest("CN=Test Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(10));
        using var leafKey = RSA.Create(2048);
        var leafReq = new CertificateRequest("CN=SERIAL", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var leaf = leafReq.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), [1, 2, 3]);
        using var expired = leafReq.Create(root, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), [1, 2, 4]);
        var missingNameRequest = new CertificateRequest("OU=SERIAL", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var missingName = missingNameRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), [1, 2, 5]);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "root.pem"), root.ExportCertificatePem());
            var trust = new PrinterTrust(directory);
            Check(trust.Validate(leaf, null, "SERIAL"), "valid private root failed");
            Check(!trust.Validate(leaf, null, "OTHER"), "serial mismatch accepted");
            Check(!trust.Validate(expired, null, "SERIAL"), "expired certificate accepted");
            Check(!trust.Validate(missingName, null, "SERIAL"), "organization used as printer identity");
            Check(!new PrinterTrust().Validate(leaf, null, "SERIAL"), "unbundled CA accepted");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task FleetTests(Catalogs catalogs)
    {
        var host = Environment.GetEnvironmentVariable("FLEET_HOST") ?? "127.0.0.1";
        await using var session = new PrinterSession(new("Fleet", host, "22EF00000000001", "N7", 18801), "FLT00001", catalogs, emulator: true, containerTest: host == "fleet");
        session.Start(); await Until(() => session.Phase == ConnectionPhase.Online, 60);
        Check(session.Status!.Ams.Count > 0, "no AMS data");
        await session.CommandAsync("pause"); await Until(() => session.Status?.State == JobState.Paused);
        await session.CommandAsync("resume"); await Until(() => session.Status?.State == JobState.Printing);
        await session.CommandAsync("print_speed", speed: 1); await Until(() => session.Status?.Speed == 1);
        await session.CommandAsync("ledctrl", light: false); await Until(() => session.Status?.Light == false);
        await session.CommandAsync("ledctrl", light: true); await Until(() => session.Status?.Light == true);
        await session.CommandAsync("stop"); await Until(() => session.Status?.State == JobState.Failed);
        await using var wrong = new PrinterSession(new("Wrong", host, "22EF00000000001", "N7", 18801), "BAD00000", catalogs, true, host == "fleet");
        wrong.Start(); await Until(() => wrong.Phase == ConnectionPhase.Unauthorized);
        await using var untrusted = new PrinterSession(new("TLS", host, "22EF00000000001", "N7", 18801), "FLT00001", catalogs, containerTest: host == "fleet");
        untrusted.Start(); await Until(() => untrusted.Phase == ConnectionPhase.Certificate);
        await using var h2 = new PrinterSession(new("H2D", host, "094F00000000005", "O1D", 18805), "FLT00005", catalogs, true, host == "fleet");
        h2.Start(); await Until(() => h2.Phase == ConnectionPhase.Online);
        Check(h2.Status!.SecondNozzle is not null && h2.Status.Chamber is not null, "H2D hardware");
        await using var offline = new PrinterSession(new("Offline", host, "01PF00000000012", "C12", 18812), "FLT00012", catalogs, true, host == "fleet");
        offline.Start(); await Until(() => offline.Phase == ConnectionPhase.Offline);
    }
}
