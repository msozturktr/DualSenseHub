using System.Net;
using System.Net.Sockets;

namespace DualSenseHub.Core.Ets2;

/// <summary>
/// Fake plugin: sends packets so the app can be tested without the game. 3 s phases cycle idle, left, right, hazard, brake
/// (the brake phase includes a wheel lockup); gears change every 4 s (through neutral), rpm sweeps up each gear,
/// suspension bumps and a landing happen periodically, the parking brake is set in the idle phase.
/// </summary>
public static class Ets2Sim
{
    private const int Wheels = 6;
    private const float Radius = 0.5f, RpmLimit = 2500;
    private const byte LeftMask = 0b010101; // wheels 0, 2, 4 are on the left

    /// <summary>Builds the packet for simulated time <paramref name="t"/> seconds.</summary>
    public static Ets2Packet Frame(double t, uint seq)
    {
        int phase = (int)(t / 3) % 5;       // idle, left, right, hazard, brake
        bool blink = (int)(t * 2.5) % 2 == 0; // ~1.25 Hz blink
        ushort flags = 16 | 32;
        if (phase == 1 && blink) flags |= 1;
        if (phase == 2 && blink) flags |= 2;
        if (phase == 3) { flags |= 4; if (blink) flags |= 3; }
        if (phase == 4) flags |= 8;
        bool parking = phase == 0 && t % 3 > 1.5;
        if (parking) flags |= 128;
        float speed = (float)(10 + 12 * Math.Sin(t / 2) * Math.Sin(t / 2)); // m/s
        float brake = phase == 4 ? 0.8f : 0;

        double inGear = t % 4;
        int gear = 1 + (int)(t / 4) % 6;
        if (inGear > 3.8) gear = 0; // neutral blip before the next shift
        float rpm = (float)(900 + (RpmLimit - 900) * 0.95 * Math.Pow(inGear / 4, 1.5));

        var susp = new float[Ets2Packet.MaxWheels];
        var vel = new float[Ets2Packet.MaxWheels];
        var radius = new float[Ets2Packet.MaxWheels];
        byte ground = (1 << Wheels) - 1;
        for (int i = 0; i < Wheels; i++)
        {
            susp[i] = 0.1f + 0.002f * (float)Math.Sin(t * 3 + i);   // flat-road noise, stays inside the deadzone
            vel[i] = speed / (2 * MathF.PI * Radius);
            radius[i] = Radius;
        }
        double b1 = t % 7 - 0.5;   // left-side bump on wheels 0 and 2
        if (b1 is > 0 and < 0.2) { float d = 0.05f * MathF.Sin((float)(b1 / 0.2 * Math.PI)); susp[0] += d; susp[2] += d; }
        double b2 = t % 6.5 - 4;   // right-side bump on wheel 5
        if (b2 is > 0 and < 0.2) susp[5] += 0.05f * MathF.Sin((float)(b2 / 0.2 * Math.PI));
        double b3 = t % 9 - 6;     // right wheels leave the ground, then land
        if (b3 is > 0 and < 0.4) ground &= LeftMask;
        if (phase == 4 && t % 3 > 1.5) vel[2] *= 0.1f; // lockup on wheel 2 (left) during the second half of the brake phase

        return new Ets2Packet(flags, speed, rpm, RpmLimit, 300, 600, seq, (float)(0.5 + 0.5 * Math.Sin(t)),
            brake, 8, gear, Wheels, ground, LeftMask, susp, vel, radius);
    }

    public static async Task RunAsync(int seconds, CancellationToken ct = default, int port = Ets2Packet.Port)
    {
        using var udp = new UdpClient();
        var dest = new IPEndPoint(IPAddress.Loopback, port);
        uint seq = 0;
        var end = DateTime.UtcNow.AddSeconds(seconds);
        var t0 = DateTime.UtcNow;
        while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
        {
            double t = (DateTime.UtcNow - t0).TotalSeconds;
            await udp.SendAsync(Frame(t, ++seq).ToBytes(), dest, ct).ConfigureAwait(false);
            try { await Task.Delay(33, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }
}
