namespace DualSenseHub.Core;

/// <summary>
/// Owns discovery (2 s polling for hot-plug), the hidraw handle and all output paths.
/// Lightbar uses sysfs (multi_intensity + brightness) and falls back to raw reports.
/// </summary>
public sealed class ControllerService : ILightOutput, IHapticOutput, IDisposable
{
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private int _sysfsBrightnessSetFor; // hash of led path whose brightness is already 255
    private bool _ledDenied, _hidDenied;
    private string? _lastKey;

    public ControllerInfo? Current { get; private set; }
    public HidrawDevice? Hid { get; private set; }
    public string? HidError { get; private set; }
    public int? BatteryPercent { get; private set; }
    public string? BatteryStatus { get; private set; }
    public FirmwareInfo? Firmware { get; private set; }
    public string? PairingMac { get; private set; }

    public bool Connected => Current != null;
    public bool PermissionDenied => Current != null && (_ledDenied || _hidDenied);

    /// <summary>Raised from a worker thread whenever connection, battery or permission state changes.</summary>
    public event Action? StateChanged;
    /// <summary>Raised from a worker thread right after a controller is (re)connected and opened.</summary>
    public event Action? DeviceConnected;
    public event Action<InputState>? InputReceived;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            Poll();
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try { Poll(); } catch { /* keep polling */ }
            }
        }, ct).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    /// <summary>One discovery pass; also used synchronously by the CLI.</summary>
    public void Poll()
    {
        bool changed = false, connected = false;
        lock (_lock)
        {
            var found = DeviceDiscovery.Scan().FirstOrDefault();
            if (found == null)
            {
                if (Current != null) { Disconnect(); changed = true; }
            }
            else
            {
                string key = found.HidrawPath + "|" + found.LightbarLed + "|" + found.BatteryDir;
                if (key != _lastKey || Hid == null && !_hidDenied)
                {
                    bool newDevice = key != _lastKey;
                    if (newDevice) Disconnect();
                    _lastKey = key;
                    Current = found;
                    OpenHid(found);
                    changed = true; connected = true;
                }
                var (pct, status) = DeviceDiscovery.ReadBattery(found);
                if (pct != BatteryPercent || status != BatteryStatus) { BatteryPercent = pct; BatteryStatus = status; changed = true; }
                bool denied = found.LightbarLed != null && !CanOpenForWrite(System.IO.Path.Combine(found.LightbarLed, "multi_intensity"));
                if (denied != _ledDenied) { _ledDenied = denied; changed = true; }
            }
        }
        if (changed) StateChanged?.Invoke();
        if (connected) DeviceConnected?.Invoke();
    }

    private void OpenHid(ControllerInfo info)
    {
        Hid?.Dispose();
        Hid = HidrawDevice.TryOpen(info.HidrawPath, info.Connection == ConnectionType.Bluetooth, out var err, out var denied);
        HidError = err; _hidDenied = denied;
        Firmware = null; PairingMac = null;
        if (Hid != null)
        {
            Hid.InputReceived += s => InputReceived?.Invoke(s);
            Hid.Disconnected += () => Task.Run(Poll);
            Firmware = Hid.ReadFirmwareInfo();
            PairingMac = Hid.ReadMac();
        }
    }

    private void Disconnect()
    {
        Hid?.Dispose();
        Hid = null; Current = null; HidError = null; _hidDenied = false; _ledDenied = false;
        BatteryPercent = null; BatteryStatus = null; Firmware = null; PairingMac = null;
        _lastKey = null; _sysfsBrightnessSetFor = 0;
    }

    private static bool CanOpenForWrite(string path)
    {
        try { using var f = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); return true; }
        catch { return false; }
    }

    private static bool WriteSysfs(string path, string text)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            var bytes = System.Text.Encoding.ASCII.GetBytes(text);
            f.Write(bytes, 0, bytes.Length);
            f.Flush();
            return true;
        }
        catch { return false; }
    }

    // ---- ILightOutput ----

    private string? _lightbarSetupFor;

    public void SetLightbar(byte r, byte g, byte b)
    {
        if (Program.ScreenshotDir != null) return;
        lock (_lock)
        {
            var info = Current;
            if (info == null) return;
            var hid = Hid;
            if (hid != null)
            {
                hid.Output.R = r; hid.Output.G = g; hid.Output.B = b;
                // LIGHT_OUT takes control from firmware; send it once per device, repeating it fades the lightbar dark.
                var groups = OutputGroups.Lightbar;
                if (_lightbarSetupFor != info.HidrawPath) groups |= OutputGroups.LightbarSetup;
                if (hid.Send(groups)) { _lightbarSetupFor = info.HidrawPath; return; }
            }
            if (info.LightbarLed != null)
            {
                int h = info.LightbarLed.GetHashCode();
                if (_sysfsBrightnessSetFor != h)
                {
                    if (WriteSysfs(System.IO.Path.Combine(info.LightbarLed, "brightness"), "255")) _sysfsBrightnessSetFor = h;
                }
                if (WriteSysfs(System.IO.Path.Combine(info.LightbarLed, "multi_intensity"), $"{r} {g} {b}")) return;
            }
            hid?.Send(OutputGroups.Lightbar);
        }
    }

    public void SetPlayerLeds(byte mask) => SetPlayerLeds(mask, null);

    public void SetPlayerLeds(byte mask, byte? brightness)
    {
        if (Program.ScreenshotDir != null) return;
        lock (_lock)
        {
            var info = Current;
            if (info == null) return;
            var hid = Hid;
            if (hid != null)
            {
                hid.Output.PlayerMask = mask;
                if (brightness.HasValue) hid.Output.LedBrightness = brightness.Value;
                if (hid.Send(OutputGroups.Player)) return;
            }
            for (int i = 0; i < info.PlayerLeds.Length && i < 5; i++)
                WriteSysfs(System.IO.Path.Combine(info.PlayerLeds[i], "brightness"), ((mask >> i) & 1) != 0 ? "1" : "0");
        }
    }

    public bool SetMicLed(bool on)
    {
        if (Program.ScreenshotDir != null) return false;
        lock (_lock)
        {
            var hid = Hid;
            if (hid == null) return false;
            hid.Output.MicLed = on;
            return hid.Send(OutputGroups.Mic);
        }
    }

    public bool SetRumble(byte strong, byte weak)
    {
        if (Program.ScreenshotDir != null) return false;
        lock (_lock)
        {
            var hid = Hid;
            if (hid == null) return false;
            hid.Output.RumbleStrong = strong; hid.Output.RumbleWeak = weak;
            return hid.Send(OutputGroups.Rumble);
        }
    }

    public bool SetTriggers(TriggerEffect left, TriggerEffect right)
    {
        if (Program.ScreenshotDir != null) return false;
        lock (_lock)
        {
            var hid = Hid;
            if (hid == null) return false;
            hid.Output.LeftTrigger = left; hid.Output.RightTrigger = right;
            return hid.Send(OutputGroups.TriggerLeft | OutputGroups.TriggerRight);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        lock (_lock) Disconnect();
    }
}
