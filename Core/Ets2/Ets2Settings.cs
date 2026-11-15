namespace DualSenseHub.Core.Ets2;

public enum GradientSource { Throttle, Speed, Rpm }

/// <summary>What the five player LEDs show as a left-to-right bar while no blinker is active.</summary>
public enum LedGauge { Off, Fuel, Cruise, Retarder }

/// <summary>User-configurable ETS2 rules; persisted in ets2.json.</summary>
public sealed class Ets2Settings
{
    public const int CurrentVersion = 4;
    /// <summary>Settings schema version; 0 when the file predates versioning (see <see cref="Migrate"/>).</summary>
    public int Version { get; set; } = CurrentVersion;
    public bool Blinkers { get; set; } = true;
    public bool Brake { get; set; } = true;
    public bool EngineOff { get; set; } = true;
    public bool LowFuel { get; set; } = true;
    public bool Gradient { get; set; } = true;
    /// <summary>Start/stop the mode automatically when the game starts/stops sending packets.</summary>
    public bool AutoStart { get; set; } = true;
    public bool DimWhenPaused { get; set; } = true;
    public GradientSource Source { get; set; } = GradientSource.Throttle;
    /// <summary>Speed (km/h) mapped to full red in speed gradient mode.</summary>
    public int MaxSpeedKmh { get; set; } = 110;
    // Haptics (rumble and adaptive triggers); levels are 0-100 %.
    public bool RoadFeel { get; set; } = true;
    public int RoadFeelLevel { get; set; } = 60;
    public bool GearShift { get; set; } = true;
    public int GearShiftLevel { get; set; } = 40;
    /// <summary>R2: light accelerator with a kickdown detent; <see cref="GasTriggerLevel"/> is the detent strength.</summary>
    public bool GasTrigger { get; set; } = true;
    public int GasTriggerLevel { get; set; } = 60;
    /// <summary>Kickdown position in percent of R2 travel.</summary>
    public int KickdownPoint { get; set; } = 80;
    /// <summary>L2: brake resistance that follows the brake input, hard when the parking brake is set.</summary>
    public bool BrakeTrigger { get; set; } = true;
    public int BrakeTriggerLevel { get; set; } = 60;
    /// <summary>Legacy single trigger setting (version 1 files); read once by <see cref="Migrate"/>, never written.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? Triggers { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? TriggersLevel { get; set; }
    public bool EngineRumble { get; set; }
    public int EngineRumbleLevel { get; set; } = 50;
    /// <summary>Strong two-motor jolt on impacts (damage jump or acceleration spike).</summary>
    public bool CrashRumble { get; set; } = true;
    public int CrashRumbleLevel { get; set; } = 80;
    /// <summary>Player LED gauge shown when no blinker is active.</summary>
    public LedGauge Gauge { get; set; } = LedGauge.Off;
    public bool Abs { get; set; } = true;
    public int AbsLevel { get; set; } = 70;
    public string BlinkerColor { get; set; } = "FF8C00";
    public string BrakeColor { get; set; } = "FF0000";
    public string LowFuelColor { get; set; } = "FF5000";
    /// <summary>Gradient end colors; defaults stay clear of the red/amber used by brake and blinkers.</summary>
    public string GradientLowColor { get; set; } = "00C8FF";
    public string GradientHighColor { get; set; } = "B400FF";

    /// <summary>Upgrades settings loaded from an older file in place.</summary>
    public void Migrate()
    {
        if (Version < 2)
        {
            Source = GradientSource.Throttle; // files saved before throttle existed carry a stale "Speed"
            if (Triggers is { } t) { GasTrigger = BrakeTrigger = t; }
            if (TriggersLevel is { } l) { GasTriggerLevel = BrakeTriggerLevel = l; }
        }
        // Version 3 added CrashRumble/CrashRumbleLevel/Gauge; missing keys already deserialize to their defaults.
        // Version 4 replaced the fixed green→red gradient with GradientLowColor/GradientHighColor (defaults apply).
        Triggers = null; TriggersLevel = null;
        Version = CurrentVersion;
    }

    public static (byte R, byte G, byte B) ParseHex(string hex, (byte, byte, byte) fallback)
    {
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 6 && int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return fallback;
    }
}
