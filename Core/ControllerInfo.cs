namespace DualSenseHub.Core;

public enum ConnectionType { Unknown, Usb, Bluetooth }

/// <summary>Everything discovered about one connected DualSense in sysfs.</summary>
public sealed record ControllerInfo(
    string HidrawPath,
    string HidSysPath,
    ConnectionType Connection,
    string ProductId,
    string? Mac,
    string? LightbarLed,
    string[] PlayerLeds,
    string? MicLed,
    string? BatteryDir)
{
    public string ModelName => ProductId.Equals("0DF2", StringComparison.OrdinalIgnoreCase) ? "DualSense Edge" : "DualSense";
}
