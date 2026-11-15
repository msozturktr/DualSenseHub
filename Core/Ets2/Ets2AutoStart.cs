namespace DualSenseHub.Core.Ets2;

/// <summary>
/// Starts the ETS2 mode when packets arrive and stops it after 5 s of silence. Call <see cref="Tick"/> periodically
/// from a single thread. A manual stop while connected suppresses auto-restart until the game disconnects.
/// </summary>
public sealed class Ets2AutoStart
{
    public const long StopAfterMs = 5000;
    private readonly Ets2TelemetryListener _listener;
    private readonly Func<bool> _isRunning;
    private readonly Action _start, _stop;
    private bool _suppressed;

    public bool Enabled { get; set; } = true;
    /// <summary>True when the current run was started automatically (manual runs are never auto-stopped).</summary>
    public bool AutoStarted { get; private set; }
    public event Action<string>? Transition;

    public Ets2AutoStart(Ets2TelemetryListener listener, Func<bool> isRunning, Action start, Action stop)
    { _listener = listener; _isRunning = isRunning; _start = start; _stop = stop; }

    public void NotifyManualStart() => AutoStarted = false;
    public void NotifyManualStop() { AutoStarted = false; if (_listener.Connected) _suppressed = true; }

    public void Tick()
    {
        long age = _listener.LastPacketAgeMs;
        bool connected = _listener.Connected;
        if (!connected && _suppressed) _suppressed = false;
        bool running = _isRunning();
        if (!running) AutoStarted = false;
        if (running && connected) AutoStarted = true; // adopt manual runs once the game is seen
        if (!Enabled) return;
        if (!running && connected && !_suppressed)
        {
            Transition?.Invoke("connected");
            AutoStarted = true;
            _start();
        }
        else if (running && AutoStarted && (age < 0 || age > StopAfterMs))
        {
            Transition?.Invoke("disconnected");
            _stop();
            AutoStarted = false;
        }
    }
}
