using System.Net;
using System.Net.Sockets;

namespace DualSenseHub.Core.Ets2;

/// <summary>
/// Fake plugin: sends packets so the app can be tested without the game. 3 s phases cycle idle, left, right, hazard, brake
/// (the brake phase includes a wheel lockup); a crash (damage jump + acceleration spike) every 12 s at t%12=9, cruise control on at t%20 in [2,12), retarder cycling 0-4, gears change every 4 s (through neutral), rpm sweeps up each gear,
/// a 30 s surface cycle (seams, hard hit, gravel, rumble strip, landing) repeats, the parking brake is set in the idle phase.
/// </summary>
public static class Ets2Sim
{
    private const int Wheels = 6;
    private const float Radius = 0.5f, RpmLimit = 2500;
    /// <summary>Substance name table the sim announces (indices used by <see cref="Frame"/>).</summary>
    public static readonly string[] SubstanceNames = ["road", "gravel", "rumble"];
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
        // 30 s cycle: [0,10) smooth road with seams (<= 1.3 m/s suspension speed), hard hit at 10.5, gravel [12,20),
        // rumble strip on the right side [20,24), road again [24,30) with a landing at 26.
        double c = t % 30;
        var subst = new byte[Ets2Packet.MaxWheels];
        if (c is >= 12 and < 20) for (int i = 0; i < Wheels; i++) subst[i] = 1;
        if (c is >= 20 and < 24) for (int i = 0; i < Wheels; i++) if ((LeftMask >> i & 1) == 0) subst[i] = 2;
        double seam = t % 2.5;     // road seam on wheels 1 and 3
        if (seam < 0.1) { float d = 0.04f * MathF.Sin((float)(seam / 0.1 * Math.PI)); susp[1] += d; susp[3] += d; }
        double b1 = c - 10.5;      // hard hit on left wheels 0 and 2 (about 3 m/s)
        if (b1 is > 0 and < 0.1) { float d = 0.12f * MathF.Sin((float)(b1 / 0.1 * Math.PI)); susp[0] += d; susp[2] += d; }
        double b3 = c - 26;        // right wheels leave the ground, then land
        if (b3 is > 0 and < 0.4) ground &= LeftMask;
        if (phase == 4 && t % 3 > 1.5) vel[2] *= 0.1f; // lockup on wheel 2 (left) during the second half of the brake phase

        // Normal driving accelerations that must not count as a crash: braking, bumps.
        float az = phase == 4 ? -6f : 0.5f * MathF.Sin((float)t);
        float ay = 0;
        if (b1 is > 0 and < 0.1) ay = 14 * MathF.Sin((float)(b1 / 0.1 * Math.PI));
        // Crash every 12 s at t%12 = 9: one frame of a hard impact spike plus a damage jump.
        double cr = t % 12 - 9;
        float ax = 0;
        if (cr is >= 0 and < 0.05) { ax = -55; ay = 12; az = 30; }
        float wear = 0.1f + 0.03f * (int)((t + 3) / 12);
        // Cruise control on for t%20 in [2,12) at 80 km/h; retarder steps through 0..4 of 4.
        float cruise = t % 20 is >= 2 and < 12 ? 80 / 3.6f : 0;
        int retarder = (int)(t / 3) % 5;
        return new Ets2Packet(flags, speed, rpm, RpmLimit, 300, 600, seq, (float)(0.5 + 0.5 * Math.Sin(t)),
            brake, 8, gear, Wheels, ground, LeftMask, susp, vel, radius,
            WearTotal: wear, AccelX: ax, AccelY: ay, AccelZ: az, CruiseSpeed: cruise, Retarder: retarder, RetarderSteps: 4, Substance: subst);
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
            if (seq % 150 == 0) await udp.SendAsync(Ets2Packet.SubstanceTableBytes(SubstanceNames), dest, ct).ConfigureAwait(false);
            await udp.SendAsync(Frame(t, ++seq).ToBytes(), dest, ct).ConfigureAwait(false);
            try { await Task.Delay(33, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }
}
