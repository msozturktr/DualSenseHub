namespace DualSenseHub.Core.Ets2;

public enum GradientSource { Speed, Rpm, Throttle }

/// <summary>User-configurable ETS2 rules; persisted in ets2.json.</summary>
public sealed class Ets2Settings
{
    public bool Blinkers { get; set; } = true;
    public bool Brake { get; set; } = true;
    public bool EngineOff { get; set; } = true;
    public bool LowFuel { get; set; } = true;
    public bool Gradient { get; set; } = true;
    public bool DimWhenPaused { get; set; } = true;
    public GradientSource Source { get; set; } = GradientSource.Throttle;
    /// <summary>Speed (km/h) mapped to full red in speed gradient mode.</summary>
    public int MaxSpeedKmh { get; set; } = 110;
    public string BlinkerColor { get; set; } = "FF8C00";
    public string BrakeColor { get; set; } = "FF0000";
    public string LowFuelColor { get; set; } = "FF5000";

    public static (byte R, byte G, byte B) ParseHex(string hex, (byte, byte, byte) fallback)
    {
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 6 && int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return fallback;
    }
}
