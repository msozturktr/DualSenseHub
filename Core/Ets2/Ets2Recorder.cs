using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DualSenseHub.Core.Ets2;

/// <summary>
/// Writes one CSV row per received telemetry packet (inputs, per-wheel road-feel internals and the resulting haptic frame)
/// for offline tuning. Runs its own <see cref="Ets2Haptics"/> instance so it works without a controller.
/// </summary>
public sealed class Ets2Recorder : IDisposable
{
    private readonly Ets2TelemetryListener _listener;
    private readonly Func<Ets2Settings> _settings;
    private readonly object _gate = new();
    private StreamWriter? _w;
    private Ets2Haptics _haptics = new();
    private Stopwatch _clock = new();
    private Timer? _stopTimer;
    private long _rows;

    public Ets2Recorder(Ets2TelemetryListener listener, Func<Ets2Settings> settings)
    {
        _listener = listener; _settings = settings;
        _listener.PacketReceived += OnPacket;
    }

    public bool Recording { get { lock (_gate) return _w != null; } }

    public static string Header()
    {
        var sb = new StringBuilder("t_ms,speed_kmh,rpm,gear,throttle,brake,ground,wheels");
        foreach (var n in new[] { "susp", "vel", "base", "exc" })
            for (int i = 0; i < Ets2Packet.MaxWheels; i++) sb.Append(',').Append(n).Append(i);
        return sb.Append(",wear,accel,strong,weak,active").ToString();
    }

    public string Start(int seconds)
    {
        lock (_gate)
        {
            StopLocked();
            var dir = Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "dualsensehub");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"rec-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _w = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = false };
            _w.WriteLine(Header());
            _haptics = new Ets2Haptics(); _clock = Stopwatch.StartNew(); _rows = 0;
            // Only unpaused driving counts: typing the command alt-tabs out of ETS2, which pauses the game.
            _targetMs = Math.Max(1, seconds) * 1000L; _activeMs = 0; _lastMs = -1;
            _stopTimer = new Timer(_ => Stop(), null, 30 * 60 * 1000L, Timeout.Infinite); // safety cap
            return path;
        }
    }

    /// <summary>Stops recording; returns the number of rows written (0 when idle).</summary>
    public long Stop() { lock (_gate) return StopLocked(); }

    private long StopLocked()
    {
        _stopTimer?.Dispose(); _stopTimer = null;
        var w = _w; _w = null;
        if (w == null) return 0;
        try { w.Flush(); w.Dispose(); } catch { }
        return _rows;
    }

    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private long _targetMs, _activeMs, _lastMs = -1;

    private void OnPacket(Ets2Packet p)
    {
        lock (_gate)
        {
            if (_w == null) return;
            long now = _clock.ElapsedMilliseconds;
            long delta = _lastMs < 0 ? 0 : now - _lastMs;
            _lastMs = now;
            if (p.Paused) return;
            _activeMs += Math.Min(delta, 250);
            if (_activeMs >= _targetMs) { StopLocked(); return; }
            var f = _haptics.Update(p, true, _settings(), now);
            var d = _haptics.Diag;
            var sb = new StringBuilder(256);
            sb.Append(now).Append(',').Append(F(p.SpeedKmh)).Append(',').Append(F(p.Rpm)).Append(',').Append(p.Gear).Append(',')
              .Append(F(p.ColorThrottle)).Append(',').Append(F(p.EffBrake)).Append(',').Append(p.GroundMask).Append(',').Append(p.WheelCount);
            for (int i = 0; i < Ets2Packet.MaxWheels; i++) sb.Append(',').Append(p.Susp != null && i < p.Susp.Length ? F(p.Susp[i]) : "0");
            foreach (var arr in new[] { d.Vel, d.Base, d.Excess })
                for (int i = 0; i < Ets2Packet.MaxWheels; i++) sb.Append(',').Append(F(arr[i]));
            sb.Append(',').Append(F(p.WearTotal)).Append(',').Append(F(p.AccelMagnitude)).Append(',').Append(f.Strong).Append(',').Append(f.Weak)
              .Append(',').Append(f.Active.Replace(',', ';'));
            _w.WriteLine(sb.ToString());
            _rows++;
        }
    }

    public void Dispose() { _listener.PacketReceived -= OnPacket; Stop(); }
}
