using System.Net;
using System.Net.Sockets;

namespace DualSenseHub.Core.Ets2;

/// <summary>Fake plugin: sends packets cycling idle, left, right, hazard and brake, so the app can be tested without the game.</summary>
public static class Ets2Sim
{
    public static async Task RunAsync(int seconds, CancellationToken ct = default)
    {
        using var udp = new UdpClient();
        var dest = new IPEndPoint(IPAddress.Loopback, Ets2Packet.Port);
        uint seq = 0;
        var end = DateTime.UtcNow.AddSeconds(seconds);
        var t0 = DateTime.UtcNow;
        while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
        {
            double t = (DateTime.UtcNow - t0).TotalSeconds;
            int phase = (int)(t / 3) % 5;       // 3 s each: idle, left, right, hazard, brake
            bool blink = (int)(t * 2.5) % 2 == 0; // ~1.25 Hz blink
            ushort flags = 16 | 32;
            if (phase == 1 && blink) flags |= 1;
            if (phase == 2 && blink) flags |= 2;
            if (phase == 3) { flags |= 4; if (blink) flags |= 3; }
            if (phase == 4) flags |= 8;
            float speed = (float)(10 + 12 * Math.Sin(t / 2) * Math.Sin(t / 2)); // m/s
            var p = new Ets2Packet(flags, speed, 900 + speed * 60, 2500, 300, 600, ++seq);
            await udp.SendAsync(p.ToBytes(), dest, ct).ConfigureAwait(false);
            try { await Task.Delay(33, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }
}
