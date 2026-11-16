namespace DualSenseHub.Core;

/// <summary>Small output surface handed to game modes.</summary>
public interface ILightOutput
{
    void SetLightbar(byte r, byte g, byte b);
    /// <summary>5-bit mask, bit 0 = leftmost player LED.</summary>
    void SetPlayerLeds(byte mask);
}

/// <summary>Rumble and adaptive trigger output; implemented by the controller service. Both return false when no write path exists.</summary>
public interface IHapticOutput
{
    bool SetRumble(byte strong, byte weak);
    bool SetTriggers(TriggerEffect left, TriggerEffect right);
    /// <summary>HD voice-coil haptics (Bluetooth only); null when the connection cannot stream them.</summary>
    HdHaptics? GetHdHaptics();
}

/// <summary>A pluggable game integration (e.g. ETS2 telemetry) that drives the controller lights.</summary>
public interface IGameMode
{
    string Name { get; }
    void Start(ILightOutput output);
    void Stop();
}
