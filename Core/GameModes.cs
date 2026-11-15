namespace DualSenseHub.Core;

/// <summary>Small output surface handed to game modes.</summary>
public interface ILightOutput
{
    void SetLightbar(byte r, byte g, byte b);
    /// <summary>5-bit mask, bit 0 = leftmost player LED.</summary>
    void SetPlayerLeds(byte mask);
}

/// <summary>A pluggable game integration (e.g. ETS2 telemetry) that drives the controller lights.</summary>
public interface IGameMode
{
    string Name { get; }
    void Start(ILightOutput output);
    void Stop();
}
