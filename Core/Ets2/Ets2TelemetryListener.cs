using System.Net;
using System.Net.Sockets;

namespace DualSenseHub.Core.Ets2;

/// <summary>
/// Receives plugin datagrams on 127.0.0.1:47555 and exposes the latest packet.
/// A supervisor thread (re)binds the socket every 2 s on failure and survives receive errors until disposed.
/// </summary>
public sealed class Ets2TelemetryListener : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private const int RetryMs = 2000;
    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _stop;
    private Socket? _sock;
    private long _lastTicks;
    private long _count;
    private int _version;
    private long _rejected;
    private int _rejectVersion;
    private volatile string? _rejectReason;
    private volatile bool _listening;
    private volatile string? _error;
    private Ets2Packet _latest;

    public string? Error => _error;
    /// <summary>True while the socket is bound and receiving.</summary>
    public bool Listening => _listening;
    public long PacketCount => Interlocked.Read(ref _count);
    /// <summary>Protocol version byte of the last packet (0 if none).</summary>
    public int ProtocolVersion => Volatile.Read(ref _version);
    /// <summary>Datagrams that arrived but could not be parsed.</summary>
    public long RejectedCount => Interlocked.Read(ref _rejected);
    public string? LastRejectReason => _rejectReason;
    /// <summary>Version byte of the last rejected datagram (0 if unknown).</summary>
    public int LastRejectVersion => Volatile.Read(ref _rejectVersion);
    /// <summary>Substance name table from the plugin (index = substance id); empty until the DSHS datagram arrives.</summary>
    public string[] SubstanceNames => _substances;
    private volatile string[] _substances = [];
    public Ets2Packet Latest { get { lock (_gate) return _latest; } }
    /// <summary>Milliseconds since the last valid packet, or -1 if none was ever received.</summary>
    public long LastPacketAgeMs { get { long t = Interlocked.Read(ref _lastTicks); return t == 0 ? -1 : Environment.TickCount64 - t; } }
    /// <summary>True when a packet arrived within the last 2 seconds.</summary>
    public bool Connected { get { long a = LastPacketAgeMs; return a >= 0 && a < (long)Timeout.TotalMilliseconds; } }
    public event Action<Ets2Packet>? PacketReceived;

    public void Start(int port = Ets2Packet.Port)
    {
        lock (_gate)
        {
            if (_thread != null) return;
            _stop = false;
            _thread = new Thread(() => Supervise(port)) { IsBackground = true, Name = "ets2-udp" };
            _thread.Start();
        }
    }

    private void Supervise(int port)
    {
        var buf = new byte[1024];
        while (!_stop)
        {
            Socket? s = null;
            try
            {
                s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                s.ReceiveTimeout = 500;
                s.Bind(new IPEndPoint(IPAddress.Loopback, port));
                _sock = s;
                _error = null; _listening = true;
                EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                int failures = 0;
                while (!_stop)
                {
                    int n;
                    try { n = s.ReceiveFrom(buf, ref ep); failures = 0; }
                    catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut) { continue; }
                    catch (Exception) when (_stop) { break; }
                    catch (Exception e)
                    {
                        _error = "alma hatası: " + e.Message;
                        if (++failures >= 5) break; // rebind
                        Thread.Sleep(100);
                        continue;
                    }
                    if (Ets2Packet.TryParseSubstances(buf.AsSpan(0, n), out var names)) { _substances = names; continue; }
                    if (!Ets2Packet.TryParse(buf.AsSpan(0, n), out var p, out var why))
                    {
                        _rejectReason = why; Volatile.Write(ref _rejectVersion, n > 4 ? buf[4] : 0);
                        Interlocked.Increment(ref _rejected);
                        continue;
                    }
                    lock (_gate) _latest = p;
                    Volatile.Write(ref _version, buf[4]);
                    Interlocked.Increment(ref _count);
                    Interlocked.Exchange(ref _lastTicks, Environment.TickCount64);
                    try { PacketReceived?.Invoke(p); }
                    catch (Exception e) { Console.Error.WriteLine("ETS2 PacketReceived handler failed: " + e); }
                }
            }
            catch (Exception e) when (!_stop) { _error = e.Message; }
            catch (Exception) { }
            finally
            {
                _listening = false; _sock = null;
                try { s?.Dispose(); } catch { }
            }
            // Retry after a pause, waking early on shutdown.
            for (int w = 0; w < RetryMs && !_stop; w += 100) Thread.Sleep(100);
        }
    }

    public void Stop()
    {
        Thread? t;
        lock (_gate) { _stop = true; t = _thread; _thread = null; }
        try { _sock?.Dispose(); } catch { }
        t?.Join(1500);
        _listening = false;
        Interlocked.Exchange(ref _lastTicks, 0);
    }

    public void Dispose() => Stop();
}
