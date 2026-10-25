namespace BambuBar.Core;

public static class PrintStages
{
    public static readonly IReadOnlyDictionary<int, string> Keys = new Dictionary<int, string>
    {
        [0] = "Printing",
        [1] = "Auto bed leveling",
        [2] = "Preheating heatbed",
        [3] = "Vibration compensation",
        [4] = "Changing filament",
        [5] = "M400 pause",
        [6] = "Paused: filament ran out",
        [7] = "Heating hotend",
        [8] = "Calibrating extrusion",
        [9] = "Scanning bed surface",
        [10] = "Inspecting first layer",
        [11] = "Identifying build plate",
        [12] = "Calibrating Micro Lidar",
        [13] = "Homing toolhead",
        [14] = "Cleaning nozzle tip",
        [15] = "Checking extruder temperature",
        [16] = "Paused by user",
        [17] = "Paused: front cover fell off",
        [18] = "Calibrating Micro Lidar",
        [19] = "Calibrating flow ratio",
        [20] = "Paused: nozzle temperature fault",
        [21] = "Paused: heatbed temperature fault",
        [22] = "Unloading filament",
        [23] = "Paused: skipped steps",
        [24] = "Loading filament",
        [25] = "Calibrating motor noise",
        [26] = "Paused: AMS offline",
        [27] = "Paused: heatbreak fan too slow",
        [28] = "Paused: chamber temperature fault",
        [29] = "Cooling chamber",
        [30] = "Paused by G-code",
        [31] = "Motor noise showoff",
        [32] = "Paused: nozzle clumping",
        [33] = "Paused: cutter error",
        [34] = "Paused: first layer error",
        [35] = "Paused: nozzle clog",
        [36] = "Measuring motion precision",
        [37] = "Enhancing motion precision",
        [38] = "Measuring motion accuracy",
        [39] = "Calibrating nozzle offset",
        [40] = "High-temperature bed leveling",
        [41] = "Checking quick-release lever",
        [42] = "Checking door and top cover",
        [43] = "Calibrating laser",
        [44] = "Checking platform",
        [45] = "Checking BirdsEye camera",
        [46] = "Calibrating BirdsEye camera",
        [47] = "Bed leveling, phase 1",
        [48] = "Bed leveling, phase 2",
        [49] = "Heating chamber",
        [50] = "Cooling heatbed",
        [51] = "Printing calibration lines",
        [52] = "Checking material",
        [53] = "Calibrating Live View camera",
        [54] = "Waiting for heatbed temperature",
        [55] = "Checking material position",
        [56] = "Calibrating cutter offset",
        [57] = "Measuring surface",
        [58] = "Thermal preconditioning",
        [59] = "Homing blade holder",
        [60] = "Calibrating camera offset",
        [61] = "Calibrating blade holder",
        [62] = "Hotend pick-and-place test",
        [63] = "Waiting for chamber temperature",
        [64] = "Preparing hotend",
        [65] = "Calibrating clumping detection",
        [66] = "Purifying chamber air",
        [67] = "Measuring rotary attachment",
        [68] = "Moving to purge chute",
        [69] = "Cooling nozzle",
        [70] = "Centering toolhead",
        [71] = "Arc fitting",
        [72] = "Detecting hotend type",
        [73] = "Detecting build plate alignment",
        [74] = "Checking heatbed for objects",
        [75] = "Checking heatbed underside",
        [76] = "Pre-extruding",
        [77] = "Preparing AMS",
    };
    public static string Describe(PrinterStatus status, string? language = null)
    {
        var key = status.StateKey;
        if (!status.Canceled && status.State is not (JobState.Failed or JobState.Finished) &&
            status.StageId is { } id && id != 255 && Keys.TryGetValue(id, out var stage))
        {
            if (status.State == JobState.Paused ? stage.StartsWith("Paused", StringComparison.Ordinal) || id == 5 : id != 0 || status.State == JobState.Printing)
                key = stage;
        }
        if (status.State == JobState.Finished) key = "Ready to remove";
        return TextCatalog.Translate(key, language ?? TextCatalog.Language);
    }
}
