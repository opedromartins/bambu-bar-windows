using System.Net;

namespace BambuBar.Core;

public sealed record PrinterConfig(string Name, string Host, string Serial, string ModelCode = "", int Port = 8883)
{
    public void Validate(bool emulator = false, bool containerTest = false)
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Host) ||
            string.IsNullOrWhiteSpace(Serial) || !Serial.All(char.IsAsciiLetterOrDigit) || Port is < 1 or > 65535)
            throw new ArgumentException(TextCatalog.Get("Set up your printer on the Printer page."));
        if (!IPAddress.TryParse(Host, out var ip) && !(containerTest && Host == "fleet"))
            throw new ArgumentException(TextCatalog.Get("Only the IP address is needed: it's on the printer's screen under Settings → Network. Bambu Bar reads the serial number from the printer."));
        // The container-only test executable can address the fleet service. The desktop never opts into this.
        if (emulator && !(ip is not null && IPAddress.IsLoopback(ip)) && !(containerTest && Host == "fleet"))
            throw new ArgumentException(TextCatalog.Get("No connection"));
    }
}

public sealed record PrinterModel(string Code, string Name, string Prefix, string Shape,
    bool Chamber, bool Light, int Nozzles = 1)
{
    public static readonly PrinterModel[] All = [
        new("N7", "P2S", "22E", "enclosed", true, true),
        new("C12", "P1S", "01P", "enclosed", false, true),
        new("C11", "P1P", "01S", "openFrame", false, true),
        new("BL-P001", "X1 Carbon", "00M", "enclosed", true, true),
        new("BL-P002", "X1", "00W", "enclosed", true, true),
        new("C13", "X1E", "03W", "enclosed", true, true),
        new("N6", "X2D", "20P", "enclosed", true, true, 2),
        new("N2S", "A1", "039", "bedSlinger", false, false),
        new("N1", "A1 mini", "030", "mini", false, false),
        new("N9", "A2L", "26A", "bedSlinger", false, false),
        new("O1D", "H2D", "094", "wide", true, true, 2),
        new("O1E", "H2D Pro", "239", "wide", true, true, 2),
        new("O1C", "H2C", "31B", "wide", true, true, 2),
        new("O1S", "H2S", "093", "wide", true, true)
    ];
    public static PrinterModel? Find(string code, string serial = "")
    {
        code = code.ToUpperInvariant() switch { "BL-P003" => "C13", "A12" or "A04" => "N1",
            "A11" => "N2S", "O2D" => "O1E", "O1C2" => "O1C", var c => c };
        return All.FirstOrDefault(m => m.Code == code) ??
            All.FirstOrDefault(m => serial.StartsWith(m.Prefix, StringComparison.OrdinalIgnoreCase));
    }
}

public enum JobState { Idle, Preparing, Printing, Paused, Finished, Failed }
public enum ConnectionPhase { Idle, Connecting, Online, Offline, Unauthorized, Certificate }
public sealed record Temperature(double? Current = null, double? Target = null);
public sealed record Spool(int Id, int Unit, int Slot, bool Loaded, string Material, string Product,
    string Brand, string Color, string? ColorName, int? Remain, int? Weight, bool Active)
{
    public int? Grams => Remain is { } r && Weight is { } w ? (int)Math.Round(r * w / 100d) : null;
    public IReadOnlyList<string> Colors { get; init; } = [];
    public int? NozzleMin { get; init; }
    public int? NozzleMax { get; init; }
}
public sealed record AmsUnit(int Id, string Kind, int? Humidity, double? Temperature, IReadOnlyList<Spool> Spools)
{
    public int? HumidityLevel { get; init; }
    public int? DryingMinutes { get; init; }
}
public enum IssueSeverity { Fatal = 1, Serious = 2, Common = 3, Info = 4 }
public sealed record PrinterIssue(string Code, string Message)
{
    public IssueSeverity Severity { get; init; } = IssueSeverity.Common;
    public Uri Wiki => new("https://wiki.bambulab.com/en/hms/home");
}
public sealed record PrinterStatus
{
    public JobState State { get; init; }
    public string Job { get; init; } = "";
    public double? Progress { get; init; }
    public double? RemainingMinutes { get; init; }
    public int? Layer { get; init; }
    public int? TotalLayers { get; init; }
    public Temperature Nozzle { get; init; } = new();
    public Temperature? SecondNozzle { get; init; }
    public Temperature Bed { get; init; } = new();
    public Temperature? Chamber { get; init; }
    public bool? Light { get; init; }
    public bool ControlsLocked { get; init; }
    public int? Speed { get; init; }
    public string? Firmware { get; init; }
    public IReadOnlyList<AmsUnit> Ams { get; init; } = [];
    public Spool? External { get; init; }
    public IReadOnlyList<PrinterIssue> Issues { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public int? StageId { get; init; }
    public string? TaskId { get; init; }
    public bool Canceled { get; init; }
    public bool? LeftNozzleInUse { get; init; }
    public int? PartFan { get; init; }
    public int? AuxFan { get; init; }
    public int? ExhaustFan { get; init; }
    public int? HeatbreakFan { get; init; }
    public bool? DoorOpen { get; init; }
    public int? WifiSignal { get; init; }
    public bool? HasSdCard { get; init; }
    public double? NozzleDiameter { get; init; }
    public string? NozzleType { get; init; }
    public bool? LiveViewEnabled { get; init; }
    public int? PlateIndex { get; init; }
    public bool FirmwareUpdateAvailable { get; init; }
    public bool Active => State is JobState.Printing or JobState.Preparing or JobState.Paused;
    public string StateKey => Canceled && State == JobState.Failed ? "Canceled" : State switch {
        JobState.Preparing => "Preparing", JobState.Printing => "Printing", JobState.Paused => "Paused", JobState.Finished => "Finished", JobState.Failed => "Failed", _ => "Ready" };
    public double? ProjectedMinutes(DateTimeOffset now, bool online = true) => RemainingMinutes is not { } minutes || State == JobState.Preparing && minutes == 0 ? null :
        Math.Max(0, minutes - (online && State == JobState.Printing ? Math.Max(0, (now - UpdatedAt).TotalMinutes) : 0));
    public DateTimeOffset? FinishAt(DateTimeOffset now, bool online = true) => online && State is (JobState.Printing or JobState.Preparing) && RemainingMinutes is > 0
        ? UpdatedAt.AddMinutes(Math.Min(RemainingMinutes.Value, 525600)) : null;
}
