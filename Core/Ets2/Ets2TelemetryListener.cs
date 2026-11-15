using System.Net;
using System.Net.Sockets;

namespace DualSenseHub.Core.Ets2;

/// <summary>Receives plugin datagrams on 127.0.0.1:47555 and exposes the latest packet.</summary>
public sealed class Ets2TelemetryListener : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private UdpClient? _udp;
    private Thread? _thread;
    private long _lastTicks;
    private Ets2Packet _latest;

    public string? Error { get; private set; }
    public Ets2Packet Latest { get { lock (this) return _latest; } }
    /// <summary>True when a packet arrived within the last 2 seconds.</summary>
    public bool Connected => _lastTicks != 0 && Environment.TickCount64 - Interlocked.Read(ref _lastTicks) < (long)Timeout.TotalMilliseconds;
    public event Action<Ets2Packet>? PacketReceived;

    public void Start(int port = Ets2Packet.Port)
    {
        if (_udp != null) return;
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            Error = null;
        }
        catch (Exception e) { Error = e.Message; return; }
        var udp = _udp;
        _thread = new Thread(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                byte[] data;
                try { data = udp.Receive(ref ep); }
                catch { return; }
                if (!Ets2Packet.TryParse(data, out var p)) continue;
                lock (this) _latest = p;
                Interlocked.Exchange(ref _lastTicks, Environment.TickCount64);
                PacketReceived?.Invoke(p);
            }
        }) { IsBackground = true, Name = "ets2-udp" };
        _thread.Start();
    }

    public void Stop()
    {
        var u = _udp; _udp = null;
        u?.Dispose();
        _thread?.Join(500);
        _thread = null;
        Interlocked.Exchange(ref _lastTicks, 0);
    }

    public void Dispose() => Stop();
}
