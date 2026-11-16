using System.Collections.Concurrent;
using System.Diagnostics;

namespace DualSenseHub.Core;

/// <summary>Continuous synth layers (amplitudes 0-1 before master gain) for the left and right voice coil.</summary>
public sealed record HdLayers(double EngineHz, double EngineL, double EngineR, double RoadL, double RoadR)
{
    public static readonly HdLayers Silent = new(40, 0, 0, 0, 0);
    public bool IsSilent => EngineL < 0.005 && EngineR < 0.005 && RoadL < 0.005 && RoadR < 0.005;
}

public enum HdSide { Left, Right, Both }

/// <summary>
/// True HD haptics over Bluetooth: streams 3 kHz stereo int8 PCM to the voice coils in output report 0x32 (142 bytes with the report ID, CRC32).
/// A dedicated thread sends one report every 32 frames (10.67 ms), generating each block just in time from the current
/// <see cref="HdLayers"/> plus queued transient bursts, so latency stays at about one block. The stream stops after
/// <see cref="IdleStopMs"/> of silence and restarts on demand; <see cref="Source"/> replaces the synth (used by the CLI test).
/// </summary>
public sealed class HdHaptics : IDisposable
{
    public const int SampleRate = 3000, FramesPerReport = 32, ReportSize = 142, IdleStopMs = 500;
    private const int PcmOffset = 13, CounterOffset = 10, CrcOffset = 138, MaxActiveBursts = 24;

    private static readonly uint CrcSeed = Crc32.Compute([0xA2]);

    private readonly Func<byte[], bool> _write;
    private readonly ConcurrentQueue<BurstItem> _bursts = new();
    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _stop;
    private long _lastWakeTicks;
    private volatile HdLayers _layers = HdLayers.Silent;

    private readonly record struct BurstItem(HdSide Side, double Hz, double Amp, double DurMs);

    /// <summary>0-1 overall scale applied to the mixed signal.</summary>
    public volatile float MasterGain = 0.8f;
    /// <summary>When set, called for each block instead of the synth; fills 64 interleaved L,R sbytes and returns false to end the stream.</summary>
    public Func<sbyte[], bool>? Source { get; set; }

    public long Reports { get; private set; }
    public long FailedWrites { get; private set; }
    public double MeanIntervalMs { get; private set; }
    public double MaxIntervalMs { get; private set; }

    public bool Streaming { get { lock (_gate) return _thread != null; } }

    /// <param name="write">Writes one raw report to the controller (must share the lock of the other output reports).</param>
    public HdHaptics(Func<byte[], bool> write) { _write = write; }

    /// <summary>Sets the continuous layers; non-silent layers (re)start the stream.</summary>
    public void SetLayers(HdLayers l)
    {
        _layers = l;
        if (!l.IsSilent) Wake();
    }

    /// <summary>Queues a decaying sine burst (<paramref name="durMs"/> long, amplitude 0-1) on one or both sides.</summary>
    public void Burst(HdSide side, double hz, double amp, double durMs)
    {
        if (amp <= 0.01) return;
        _bursts.Enqueue(new BurstItem(side, hz, Math.Min(1, amp), durMs));
        Wake();
    }

    /// <summary>Starts the stream if needed and keeps it alive for another <see cref="IdleStopMs"/>.</summary>
    public void Wake()
    {
        Interlocked.Exchange(ref _lastWakeTicks, Stopwatch.GetTimestamp());
        lock (_gate)
        {
            if (_thread != null || _stop) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "hd-haptics", Priority = ThreadPriority.Highest };
            _thread.Start();
        }
    }

    /// <summary>Stops the stream immediately (the controller falls silent); it can be woken again.</summary>
    public void Stop()
    {
        Thread? t;
        lock (_gate) t = _thread;
        _stop = true;
        t?.Join(1000);
        _stop = false;
        while (_bursts.TryDequeue(out _)) { }
        _layers = HdLayers.Silent;
    }

    /// <summary>Waits until the stream thread has ended (idle stop, <see cref="Source"/> finished or <see cref="Stop"/>).</summary>
    public bool WaitStopped(int timeoutMs)
    {
        Thread? t;
        lock (_gate) t = _thread;
        return t == null || t.Join(timeoutMs);
    }

    /// <summary>Fills the constant parts and the PCM block of an output report 0x32 and its CRC; <paramref name="counter"/> is the rolling byte of packet 0x11.</summary>
    public static void BuildReport(byte[] r, byte counter, ReadOnlySpan<sbyte> pcm)
    {
        r[0] = 0x32; r[1] = 0;                       // tag 0, seq 0 (as SAxense)
        r[2] = 0x91; r[3] = 7;                       // packet 0x11, sized
        r[4] = 0xFE; r[5] = r[6] = r[7] = r[8] = 0; r[9] = 0xFF; r[CounterOffset] = counter;
        r[11] = 0x92; r[12] = 64;                    // packet 0x12: 32 stereo frames
        for (int i = 0; i < 64; i++) r[PcmOffset + i] = (byte)pcm[i];
        BitConverter.TryWriteBytes(r.AsSpan(CrcOffset), Crc32.Compute(r.AsSpan(0, CrcOffset), CrcSeed));
    }

    private void Run()
    {
        long freq = Stopwatch.Frequency;
        long interval = freq * FramesPerReport / SampleRate;
        var report = new byte[ReportSize];
        var pcm = new sbyte[FramesPerReport * 2];
        var synth = new Synth(_bursts);
        byte counter = 0;
        long next = Stopwatch.GetTimestamp(), prevSend = 0, lastActive = next, sum = 0;
        long n = 0, max = 0;
        Reports = FailedWrites = 0; MeanIntervalMs = MaxIntervalMs = 0;
        try
        {
            while (!_stop)
            {
                // Sleep most of the wait, spin the last ~1.5 ms so the deadline is met without drift.
                long now = Stopwatch.GetTimestamp();
                long waitMs = (next - now) * 1000 / freq;
                if (waitMs > 2) Thread.Sleep((int)waitMs - 2);
                while (Stopwatch.GetTimestamp() < next) Thread.SpinWait(20);
                now = Stopwatch.GetTimestamp();

                bool cont = true;
                var src = Source;
                if (src != null) { cont = src(pcm); lastActive = now; }
                else
                {
                    float peak = synth.Fill(pcm, _layers, MasterGain);
                    if (peak > 0) lastActive = now;
                    else if ((now - lastActive) * 1000 / freq > IdleStopMs)
                    {
                        // Re-check under the gate: a producer may have woken us between the test and the exit.
                        lock (_gate)
                        {
                            if ((now - Interlocked.Read(ref _lastWakeTicks)) * 1000 / freq > IdleStopMs) { _thread = null; return; }
                        }
                        lastActive = now;
                    }
                }
                if (!cont) break;

                BuildReport(report, counter++, pcm);
                if (!_write(report)) FailedWrites++;
                long sent = Stopwatch.GetTimestamp();
                if (prevSend != 0)
                {
                    long d = sent - prevSend;
                    sum += d; n++; if (d > max) max = d;
                    Reports = n + 1; MeanIntervalMs = sum * 1000.0 / freq / n; MaxIntervalMs = max * 1000.0 / freq;
                }
                prevSend = sent;
                next += interval;
                if (sent - next > interval * 4) next = sent; // fell far behind (suspend, stall): resync instead of bursting
            }
        }
        finally
        {
            lock (_gate) _thread = null;
        }
    }

    public void Dispose() { Stop(); _stop = true; }

    /// <summary>Just-in-time synth: engine sine, lowpassed road noise and decaying bursts, per side; amplitudes are slewed to avoid clicks.</summary>
    private sealed class Synth
    {
        private readonly ConcurrentQueue<BurstItem> _queue;
        private readonly List<Active> _active = new();
        private double _phase, _hz = 40, _engL, _engR, _roadL, _roadR, _lpL, _lpR;
        private uint _rng = 0x9E3779B9;
        private const double Slew = 0.02, NoiseLp = 0.22, NoiseGain = 3.0, TwoPi = 2 * Math.PI;

        private struct Active { public HdSide Side; public double W, Phase, Amp, Tau; public int Age, Len; }

        public Synth(ConcurrentQueue<BurstItem> q) { _queue = q; }

        private double Noise()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return _rng / (double)uint.MaxValue * 2 - 1;
        }

        /// <summary>Fills one block; returns the peak absolute sample (0 means the block is silent).</summary>
        public float Fill(sbyte[] pcm, HdLayers l, float master)
        {
            while (_queue.TryDequeue(out var b))
            {
                if (_active.Count >= MaxActiveBursts) _active.RemoveAt(0);
                int len = Math.Max(4, (int)(b.DurMs * SampleRate / 1000));
                _active.Add(new Active { Side = b.Side, W = TwoPi * b.Hz / SampleRate, Amp = b.Amp, Tau = len / 4.0, Len = len });
            }
            float peak = 0;
            for (int i = 0; i < FramesPerReport; i++)
            {
                _hz += (l.EngineHz - _hz) * Slew;
                _engL += (l.EngineL - _engL) * Slew; _engR += (l.EngineR - _engR) * Slew;
                _roadL += (l.RoadL - _roadL) * Slew; _roadR += (l.RoadR - _roadR) * Slew;
                _phase += TwoPi * _hz / SampleRate; if (_phase > TwoPi) _phase -= TwoPi;
                double eng = Math.Sin(_phase) + 0.3 * Math.Sin(2 * _phase);
                _lpL += (Noise() - _lpL) * NoiseLp; _lpR += (Noise() - _lpR) * NoiseLp;
                double left = eng * _engL + _lpL * NoiseGain * _roadL;
                double right = eng * _engR + _lpR * NoiseGain * _roadR;
                for (int k = _active.Count - 1; k >= 0; k--)
                {
                    var a = _active[k];
                    double ramp = Math.Min(1, (a.Age + 1) / 6.0); // 2 ms attack
                    double v = Math.Sin(a.Phase) * a.Amp * Math.Exp(-a.Age / a.Tau) * ramp;
                    if (a.Side != HdSide.Right) left += v;
                    if (a.Side != HdSide.Left) right += v;
                    a.Phase += a.W; a.Age++;
                    if (a.Age >= a.Len) _active.RemoveAt(k); else _active[k] = a;
                }
                sbyte sl = Quantize(left, master), sr = Quantize(right, master);
                pcm[2 * i] = sl; pcm[2 * i + 1] = sr;
                peak = Math.Max(peak, Math.Max(Math.Abs((int)sl), Math.Abs((int)sr)));
            }
            return peak;
        }

        private static sbyte Quantize(double v, float master) => (sbyte)Math.Round(Math.Clamp(v * master, -1, 1) * 127);
    }
}
