using System.Diagnostics;

namespace DualSenseHub.Core.Ets2;

/// <summary>Drives lightbar and player LEDs from ETS2 telemetry. Priority: blinkers, brake, engine off, low fuel, gradient.</summary>
public sealed class Ets2Mode : IGameMode, IDisposable
{
    private const byte LeftMask = 0b00011, RightMask = 0b11000, AllMask = 0b11111;
    private const long BlinkHoldMs = 700;

    private readonly Ets2TelemetryListener _listener;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Timer? _timer;
    private ILightOutput? _out;
    private (byte, byte, byte, byte)? _last;
    private long _lastBlinkOnMs = -100000;

    private readonly Ets2Haptics _haptics = new();
    private readonly object _tickLock = new();
    private IHapticOutput? _hap;
    private byte _lastStrong, _lastWeak;
    private long _lastRumbleMs = -100000;
    private TriggerEffect? _lastLeft, _lastRight;
    private const long RumbleKeepaliveMs = 500;

    public Ets2Mode(Ets2TelemetryListener listener) { _listener = listener; }

    public string Name => "Euro Truck Simulator 2";
    public Ets2Settings Settings { get; set; } = new();
    public Ets2TelemetryListener Listener => _listener;
    public bool Running => _out != null;
    public bool Connected => _listener.Connected;
    /// <summary>Latest haptic frame summary for the live readout (which feature is buzzing).</summary>
    public string LastHaptics { get; private set; } = "";

    public void Start(ILightOutput output)
    {
        _out = output;
        _hap = output as IHapticOutput;
        _last = null;
        _lastStrong = _lastWeak = 0; _lastLeft = _lastRight = null;
        _timer = new Timer(_ => Tick(), null, 0, 33);
    }

    public void Stop()
    {
        _timer?.Dispose(); _timer = null;
        lock (_tickLock)
        {
            // Rumble off and triggers released when the mode stops.
            _hap?.SetRumble(0, 0);
            _hap?.SetTriggers(TriggerEffect.Off(), TriggerEffect.Off());
            _hap = null;
            var o = _out; _out = null;
            o?.SetPlayerLeds(0);
        }
    }

    private void Tick()
    {
        lock (_tickLock)
        {
            var o = _out;
            if (o == null) return;
            if (_hap != null) TickHaptics(_hap);
            if (!_listener.Connected) return;
            TickLights(o);
        }
    }

    private void TickHaptics(IHapticOutput h)
    {
        long now = _clock.ElapsedMilliseconds;
        _haptics.SubstanceNames = _listener.SubstanceNames;
        var f = _haptics.Update(_listener.Latest, _listener.Connected, Settings, now);
        LastHaptics = $"motor {f.Strong}/{f.Weak} · {f.Active}" + (_haptics.CurrentSurface.Length > 0 ? $" · zemin: {_haptics.CurrentSurface}" : "") + $" · pürüz: {_haptics.Roughness.L:F2}/{_haptics.Roughness.R:F2} (eşik 0.12)";
        bool changed = f.Strong != _lastStrong || f.Weak != _lastWeak;
        if (changed || (f.Strong | f.Weak) != 0 && now - _lastRumbleMs >= RumbleKeepaliveMs)
        {
            h.SetRumble(f.Strong, f.Weak);
            _lastStrong = f.Strong; _lastWeak = f.Weak; _lastRumbleMs = now;
        }
        if (!f.Left.SameAs(_lastLeft) || !f.Right.SameAs(_lastRight))
        {
            h.SetTriggers(f.Left, f.Right);
            _lastLeft = f.Left; _lastRight = f.Right;
        }
    }

    private void TickLights(ILightOutput o)
    {
        var (r, g, b, mask) = Compute(_listener.Latest, Settings, _clock.ElapsedMilliseconds, ref _lastBlinkOnMs);
        if (_last == (r, g, b, mask)) return;
        _last = (r, g, b, mask);
        o.SetLightbar(r, g, b);
        o.SetPlayerLeds(mask);
    }

    /// <summary>Pure rule evaluation, also used by tests.</summary>
    public static (byte R, byte G, byte B, byte Mask) Compute(Ets2Packet p, Ets2Settings s, long nowMs, ref long lastBlinkOnMs)
    {
        byte r = 0, g = 0, b = 0, mask = 0;
        bool hazard = p.Hazard || (p.LeftBlinker && p.RightBlinker);
        bool anyLit = p.LeftBlinker || p.RightBlinker;
        if (anyLit) lastBlinkOnMs = nowMs;
        bool blinkerActive = s.Blinkers && (anyLit || p.Hazard || nowMs - lastBlinkOnMs < BlinkHoldMs);

        if (blinkerActive)
        {
            if (anyLit)
            {
                (r, g, b) = Ets2Settings.ParseHex(s.BlinkerColor, (255, 140, 0));
                if (hazard) mask = AllMask;
                else mask = (byte)((p.LeftBlinker ? LeftMask : 0) | (p.RightBlinker ? RightMask : 0));
            }
            // else: dark phase of the blink cycle
        }
        else if (s.Brake && p.Brake) (r, g, b) = Ets2Settings.ParseHex(s.BrakeColor, (255, 0, 0));
        else if (s.EngineOff && !p.EngineOn) { }
        else if (s.LowFuel && p.FuelCapacity > 0 && p.FuelFraction < 0.15 && p.SpeedKmh < 2)
        {
            var (fr, fg, fb) = Ets2Settings.ParseHex(s.LowFuelColor, (255, 80, 0));
            double k = 0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(nowMs / 1000.0 * 2 * Math.PI / 1.6));
            (r, g, b) = ((byte)(fr * k), (byte)(fg * k), (byte)(fb * k));
        }
        else if (s.Gradient)
        {
            double t = s.Source switch
            {
                GradientSource.Speed => p.SpeedKmh / Math.Max(1, s.MaxSpeedKmh),
                GradientSource.Rpm => p.Rpm / (p.RpmLimit > 0 ? p.RpmLimit : 2500),
                _ => p.ColorThrottle,
            };
            t = Math.Clamp(t, 0, 1);
            var lo = Ets2Settings.ParseHex(s.GradientLowColor, (0, 200, 255));
            var hi = Ets2Settings.ParseHex(s.GradientHighColor, (180, 0, 255));
            (r, g, b) = ((byte)(lo.R + (hi.R - lo.R) * t), (byte)(lo.G + (hi.G - lo.G) * t), (byte)(lo.B + (hi.B - lo.B) * t));
        }

        if (!blinkerActive) mask = GaugeMask(p, s, nowMs);

        if (s.DimWhenPaused && p.Paused) { r = (byte)(r * 0.12); g = (byte)(g * 0.12); b = (byte)(b * 0.12); }
        return (r, g, b, mask);
    }

    /// <summary>Left-to-right bar of <paramref name="n"/> lit player LEDs (bit 0 is the leftmost).</summary>
    private static byte Bar(int n) => (byte)((1 << Math.Clamp(n, 0, 5)) - 1);

    /// <summary>Player LED mask for the selected gauge source; 0 when the gauge is off or the source is inactive.</summary>
    public static byte GaugeMask(Ets2Packet p, Ets2Settings s, long nowMs)
    {
        switch (s.Gauge)
        {
            case LedGauge.Fuel:
                if (p.FuelCapacity <= 0) return 0;
                if (p.FuelFraction < 0.15) return (byte)(nowMs / 400 % 2 == 0 ? 1 : 0); // single blinking LED
                return Bar((int)Math.Ceiling(p.FuelFraction * 5));
            case LedGauge.Cruise:
                if (p.CruiseSpeed <= 0) return 0;
                return Bar(Math.Max(1, (int)Math.Ceiling(p.CruiseKmh / Math.Max(1, s.MaxSpeedKmh) * 5)));
            case LedGauge.Retarder:
                if (p.RetarderSteps <= 0 || p.Retarder <= 0) return 0;
                return Bar((int)Math.Ceiling(Math.Min(p.Retarder, p.RetarderSteps) * 5.0 / p.RetarderSteps));
            default: return 0;
        }
    }


    public void Dispose() => Stop();
}
