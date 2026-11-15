using System.Diagnostics;

namespace DualSenseHub.Core;

/// <summary>Runs lightbar effects on a timer and pushes colors to an <see cref="ILightOutput"/>.</summary>
public sealed class EffectEngine : IDisposable
{
    private readonly ControllerService _service;
    private readonly Timer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _lock = new();
    private LightSettings _settings = new();
    private (byte, byte, byte)? _last;
    private bool _paused;

    public EffectEngine(ControllerService service)
    {
        _service = service;
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Apply(LightSettings s)
    {
        lock (_lock)
        {
            _settings = s.Clone();
            _last = null;
            _timer.Change(0, s.Effect == LightEffect.Static ? Timeout.Infinite : s.Effect == LightEffect.Battery ? 1000 : 33);
        }
    }

    /// <summary>Re-sends the current color (e.g. after a reconnect).</summary>
    public void Refresh() => Apply(_settings);

    public void Pause()
    {
        lock (_lock) { _paused = true; _timer.Change(Timeout.Infinite, Timeout.Infinite); }
    }

    public void Resume()
    {
        lock (_lock) _paused = false;
        Refresh();
    }

    private void Tick()
    {
        LightSettings s;
        lock (_lock) { if (_paused) return; s = _settings; }
        var c = Compute(s, _clock.Elapsed.TotalSeconds, _service.BatteryPercent);
        lock (_lock)
        {
            if (_last == c) return;
            _last = c;
        }
        _service.SetLightbar(c.Item1, c.Item2, c.Item3);
    }

    /// <summary>Computes the lightbar color for the given settings at time <paramref name="t"/> seconds.</summary>
    public static (byte R, byte G, byte B) Compute(LightSettings s, double t, int? battery)
    {
        double period = Math.Max(0.2, s.Speed);
        double k = Math.Clamp(s.Brightness, 0, 100) / 100.0;
        var (r, g, b) = s.ParseColor();
        double fr = r, fg = g, fb = b;
        switch (s.Effect)
        {
            case LightEffect.Breathing:
                k *= (1 - Math.Cos(2 * Math.PI * t / period)) / 2;
                break;
            case LightEffect.Rainbow:
                (fr, fg, fb) = Hsv(t / period * 360.0);
                break;
            case LightEffect.Blink:
                if ((t / period) % 1.0 >= 0.5) k = 0;
                break;
            case LightEffect.Battery:
                (fr, fg, fb) = Hsv(Math.Clamp(battery ?? 100, 0, 100) * 1.2); // 0% red -> 100% green
                break;
        }
        return ((byte)Math.Round(fr * k), (byte)Math.Round(fg * k), (byte)Math.Round(fb * k));
    }

    private static (double, double, double) Hsv(double hue)
    {
        hue = ((hue % 360) + 360) % 360;
        double x = 1 - Math.Abs(hue / 60 % 2 - 1);
        (double r, double g, double b) = (int)(hue / 60) switch
        {
            0 => (1.0, x, 0.0), 1 => (x, 1.0, 0.0), 2 => (0.0, 1.0, x),
            3 => (0.0, x, 1.0), 4 => (x, 0.0, 1.0), _ => (1.0, 0.0, x),
        };
        return (r * 255, g * 255, b * 255);
    }

    public void Dispose() => _timer.Dispose();
}
