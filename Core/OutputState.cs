namespace DualSenseHub.Core;

[Flags]
public enum OutputGroups { None = 0, Rumble = 1, Lightbar = 2, Player = 4, Mic = 8, TriggerLeft = 16, TriggerRight = 32, LightbarSetup = 64 }

/// <summary>An adaptive trigger effect: mode byte plus 10 parameter bytes.</summary>
public sealed class TriggerEffect
{
    public byte Mode;
    public byte[] Params = new byte[10];

    public static TriggerEffect Off() => new() { Mode = 0x05 };

    /// <summary>Constant resistance from <paramref name="start"/> (0-9 zone) with given force (0-255).</summary>
    public static TriggerEffect Continuous(byte start, byte force)
        => new() { Mode = 0x01, Params = [start, force, 0, 0, 0, 0, 0, 0, 0, 0] };

    /// <summary>Resistance only between two positions.</summary>
    public static TriggerEffect Section(byte start, byte end, byte force)
        => new() { Mode = 0x02, Params = [start, end, force, 0, 0, 0, 0, 0, 0, 0] };

    /// <summary>Weapon click: start/end zones 2-7 (end &gt; start), strength 1-8.</summary>
    public static TriggerEffect Weapon(int start, int end, int strength)
    {
        ushort zones = (ushort)((1 << start) | (1 << end));
        return new TriggerEffect { Mode = 0x25, Params = [(byte)(zones & 0xFF), (byte)(zones >> 8), (byte)(strength - 1), 0, 0, 0, 0, 0, 0, 0] };
    }

    /// <summary>Vibrating trigger from <paramref name="start"/> zone, amplitude 1-8, frequency in Hz.</summary>
    public static TriggerEffect Vibration(int start, int amplitude, byte frequency)
    {
        ushort zones = (ushort)(0x03FF & ~((1 << start) - 1));
        uint amps = 0;
        for (int i = start; i < 10; i++) amps |= (uint)((amplitude - 1) & 7) << (3 * i);
        return new TriggerEffect
        {
            Mode = 0x26,
            Params = [(byte)(zones & 0xFF), (byte)(zones >> 8), (byte)amps, (byte)(amps >> 8), (byte)(amps >> 16), (byte)(amps >> 24), 0, 0, frequency, 0],
        };
    }
}

public sealed class OutputState
{
    public byte R, G, B;
    /// <summary>Player LED brightness: 0 high, 1 medium, 2 low.</summary>
    public byte LedBrightness;
    public byte PlayerMask;
    public bool MicLed;
    public byte RumbleStrong, RumbleWeak;
    public TriggerEffect LeftTrigger = TriggerEffect.Off();
    public TriggerEffect RightTrigger = TriggerEffect.Off();

    /// <summary>Builds the USB (0x02, 48 bytes) or Bluetooth (0x31, 78 bytes) output report for the given groups.</summary>
    public byte[] Build(OutputGroups groups, bool bluetooth, ref byte seq)
    {
        var c = new byte[47];
        byte f0 = 0, f1 = 0, f2 = 0;
        if (groups.HasFlag(OutputGroups.Rumble)) { f0 |= 0x03; f2 |= 0x04; c[2] = RumbleWeak; c[3] = RumbleStrong; }
        if (groups.HasFlag(OutputGroups.TriggerRight)) { f0 |= 0x04; c[10] = RightTrigger.Mode; Array.Copy(RightTrigger.Params, 0, c, 11, 10); }
        if (groups.HasFlag(OutputGroups.TriggerLeft)) { f0 |= 0x08; c[21] = LeftTrigger.Mode; Array.Copy(LeftTrigger.Params, 0, c, 22, 10); }
        if (groups.HasFlag(OutputGroups.Mic)) { f1 |= 0x01; c[8] = (byte)(MicLed ? 1 : 0); }
        if (groups.HasFlag(OutputGroups.Lightbar)) { f1 |= 0x04; c[44] = R; c[45] = G; c[46] = B; }
        // LIGHT_OUT releases the lightbar from firmware control so host colors apply (as hid-playstation does).
        if (groups.HasFlag(OutputGroups.LightbarSetup)) { f2 |= 0x02; c[41] = 0x02; }
        if (groups.HasFlag(OutputGroups.Player)) { f1 |= 0x10; f2 |= 0x01; c[42] = LedBrightness; c[43] = (byte)(PlayerMask & 0x1F); }
        c[0] = f0; c[1] = f1; c[38] = f2;

        if (!bluetooth)
        {
            var usb = new byte[48];
            usb[0] = 0x02;
            Array.Copy(c, 0, usb, 1, 47);
            return usb;
        }
        var bt = new byte[78];
        bt[0] = 0x31;
        bt[1] = (byte)((seq & 0x0F) << 4);
        bt[2] = 0x10;
        seq = (byte)((seq + 1) & 0x0F);
        Array.Copy(c, 0, bt, 3, 47);
        uint crc = Crc32.Compute(bt.AsSpan(0, 74), Crc32.Compute([0xA2]));
        BitConverter.TryWriteBytes(bt.AsSpan(74), crc);
        return bt;
    }
}
