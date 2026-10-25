namespace BambuBar.Core;

public sealed record PrinterCapability(int NozzleMax, int BedMax, int? ChamberMax, bool AuxFan, bool ChamberFan, string Volume);
public static class PrinterCapabilities
{
    private static readonly Dictionary<string, PrinterCapability> Profiles = new()
    {
        ["N7"] = new(300, 110, null, true, true, "256 × 256 × 256 mm"),
        ["C12"] = new(300, 100, null, true, true, "256 × 256 × 250 mm"),
        ["C11"] = new(300, 100, null, true, true, "256 × 256 × 250 mm"),
        ["BL-P001"] = new(300, 110, null, true, true, "256 × 256 × 250 mm"),
        ["BL-P002"] = new(300, 110, null, true, true, "256 × 256 × 250 mm"),
        ["C13"] = new(320, 110, 60, true, true, "256 × 256 × 250 mm"),
        ["N6"] = new(300, 120, 65, true, true, "256 × 256 × 261 mm"),
        ["N2S"] = new(300, 100, null, false, false, "256 × 256 × 256 mm"),
        ["N1"] = new(300, 80, null, false, false, "180 × 180 × 180 mm"),
        ["N9"] = new(300, 80, null, false, false, "330 × 320 × 325 mm"),
        ["O1D"] = new(350, 120, 65, true, true, "350 × 320 × 325 mm"),
        ["O1E"] = new(350, 120, 65, true, true, "350 × 320 × 325 mm"),
        ["O1C"] = new(350, 120, 65, true, true, "330 × 320 × 325 mm"),
        ["O1S"] = new(350, 120, 65, true, true, "340 × 320 × 340 mm"),
    };
    public static PrinterCapability? Find(string code) => Profiles.GetValueOrDefault(code);
    public static string Volume(string code) => Find(code)?.Volume ?? "—";
}
