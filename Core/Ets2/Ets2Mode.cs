using System.Diagnostics;

namespace DualSenseHub.Core.Ets2;

/// <summary>Drives lightbar and player LEDs from ETS2 telemetry. Priority: blinkers, brake, engine off, low fuel, gradient.</summary>
public sealed class Ets2Mode : IGameMode, IDisposable
{
    private const byte LeftMask = 0b00011, RightMask = 0b11000, AllMask = 0b11111;
    private const long BlinkHoldMs = 700;

    private readonly Ets2TelemetryListener _listener = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Timer? _timer;
    private ILightOutput? _out;
    private (byte, byte, byte, byte)? _last;
    private long _lastBlinkOnMs = -100000;

    public string Name => "Euro Truck Simulator 2";
    public Ets2Settings Settings { get; set; } = new();
    public Ets2TelemetryListener Listener => _listener;
    public bool Running => _out != null;
    public bool Connected => _listener.Connected;

    public void Start(ILightOutput output)
    {
        _out = output;
        _last = null;
        _listener.Start();
        _timer = new Timer(_ => Tick(), null, 0, 33);
    }

    public void Stop()
    {
        _timer?.Dispose(); _timer = null;
        _listener.Stop();
        var o = _out; _out = null;
        o?.SetPlayerLeds(0);
    }

    private void Tick()
    {
        var o = _out;
        if (o == null || !_listener.Connected) return;
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
                _ => p.Throttle,
            };
            (r, g, b) = Hsv(120 * (1 - Math.Clamp(t, 0, 1)));
        }

        if (s.DimWhenPaused && p.Paused) { r = (byte)(r * 0.12); g = (byte)(g * 0.12); b = (byte)(b * 0.12); }
        return (r, g, b, mask);
    }

    /// <summary>Hue 0 (red) to 120 (green) at full saturation/value.</summary>
    private static (byte, byte, byte) Hsv(double h)
    {
        double r = h < 60 ? 1 : (120 - h) / 60;
        double g = h < 60 ? h / 60 : 1;
        return ((byte)(r * 255), (byte)(g * 255), 0);
    }

    public void Dispose() { Stop(); _listener.Dispose(); }
}
