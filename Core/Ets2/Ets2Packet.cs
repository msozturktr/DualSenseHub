using System.Buffers.Binary;

namespace DualSenseHub.Core.Ets2;

/// <summary>Decoded telemetry datagram; see ets2-plugin/PROTOCOL.md.</summary>
public readonly record struct Ets2Packet(
    ushort Flags, float Speed, float Rpm, float RpmLimit, float Fuel, float FuelCapacity, uint Seq, float Throttle = 0,
    float EffBrake = 0, float AirPressure = 0, int Gear = 0, int WheelCount = 0, byte GroundMask = 0, byte LeftMask = 0,
    float[]? Susp = null, float[]? WheelVel = null, float[]? Radius = null, float InputThrottle = -1,
    float WearTotal = 0, float AccelX = 0, float AccelY = 0, float AccelZ = 0, float CruiseSpeed = 0, int Retarder = 0, int RetarderSteps = 0, byte[]? Substance = null)
{
    public const int MaxWheels = 8;
    public const int SizeV1 = 36;
    public const int SizeV2 = 148;
    public const int SizeV3 = 152; // version 3 (adds raw input throttle)
    public const int SizeV4 = 176; // version 4 (adds wear, acceleration, cruise control, retarder)
    public const int Size = 184;   // version 5 (adds per-wheel substance index)
    private const int MinSize = 32; // version-1 packets from older plugin builds lack throttle
    public const int Port = 47555;

    /// <summary>Raw pedal position when the plugin sends it (v3), else the effective throttle.</summary>
    public float ColorThrottle => InputThrottle >= 0 ? InputThrottle : Throttle;
    public double AccelMagnitude => Math.Sqrt(AccelX * (double)AccelX + AccelY * (double)AccelY + AccelZ * (double)AccelZ);
    public double CruiseKmh => CruiseSpeed * 3.6;
    public bool LeftBlinker => (Flags & 1) != 0;
    public bool RightBlinker => (Flags & 2) != 0;
    public bool Hazard => (Flags & 4) != 0;
    public bool Brake => (Flags & 8) != 0;
    public bool EngineOn => (Flags & 16) != 0;
    public bool ElectricOn => (Flags & 32) != 0;
    public bool Paused => (Flags & 64) != 0;
    public bool ParkingBrake => (Flags & 128) != 0;
    public double SpeedKmh => Speed * 3.6;
    public double FuelFraction => FuelCapacity > 0 ? Math.Clamp(Fuel / FuelCapacity, 0, 1) : 1;

    public static bool TryParse(ReadOnlySpan<byte> d, out Ets2Packet p) => TryParse(d, out p, out _);

    /// <summary>Forward compatible: any version >= 1 is accepted and the fields known to this build are read from the prefix.</summary>
    public static bool TryParse(ReadOnlySpan<byte> d, out Ets2Packet p, out string? reject)
    {
        p = default; reject = null;
        if (d.Length < MinSize) { reject = $"çok kısa ({d.Length} bayt)"; return false; }
        if (d[0] != (byte)'D' || d[1] != (byte)'S' || d[2] != (byte)'H' || d[3] != (byte)'1') { reject = "tanınmayan başlık"; return false; }
        if (d[4] < 1) { reject = $"geçersiz sürüm v{d[4]}"; return false; }
        bool v2 = d[4] >= 2 && d.Length >= SizeV2;
        p = new Ets2Packet(
            BinaryPrimitives.ReadUInt16LittleEndian(d[6..]),
            BinaryPrimitives.ReadSingleLittleEndian(d[8..]),
            BinaryPrimitives.ReadSingleLittleEndian(d[12..]),
            BinaryPrimitives.ReadSingleLittleEndian(d[16..]),
            BinaryPrimitives.ReadSingleLittleEndian(d[20..]),
            BinaryPrimitives.ReadSingleLittleEndian(d[24..]),
            BinaryPrimitives.ReadUInt32LittleEndian(d[28..]),
            d.Length >= SizeV1 ? BinaryPrimitives.ReadSingleLittleEndian(d[32..]) : 0);
        if (!v2) return true;
        p = p with
        {
            EffBrake = BinaryPrimitives.ReadSingleLittleEndian(d[36..]),
            AirPressure = BinaryPrimitives.ReadSingleLittleEndian(d[40..]),
            Gear = BinaryPrimitives.ReadInt32LittleEndian(d[44..]),
            WheelCount = Math.Min((int)d[48], MaxWheels),
            GroundMask = d[49],
            LeftMask = d[50],
            Susp = ReadFloats(d[52..]),
            WheelVel = ReadFloats(d[84..]),
            Radius = ReadFloats(d[116..]),
        };
        if (d[4] >= 3 && d.Length >= SizeV3) p = p with { InputThrottle = BinaryPrimitives.ReadSingleLittleEndian(d[148..]) };
        if (d[4] >= 4 && d.Length >= SizeV4)
            p = p with
            {
                WearTotal = BinaryPrimitives.ReadSingleLittleEndian(d[152..]),
                AccelX = BinaryPrimitives.ReadSingleLittleEndian(d[156..]),
                AccelY = BinaryPrimitives.ReadSingleLittleEndian(d[160..]),
                AccelZ = BinaryPrimitives.ReadSingleLittleEndian(d[164..]),
                CruiseSpeed = BinaryPrimitives.ReadSingleLittleEndian(d[168..]),
                Retarder = d[172],
                RetarderSteps = d[173],
            };
        if (d[4] >= 5 && d.Length >= Size) p = p with { Substance = d.Slice(176, MaxWheels).ToArray() };
        return true;
    }

    /// <summary>Substance name table datagram: "DSHS", u8 count, then count NUL-terminated names.</summary>
    public static bool TryParseSubstances(ReadOnlySpan<byte> d, out string[] names)
    {
        names = [];
        if (d.Length < 5 || d[0] != (byte)'D' || d[1] != (byte)'S' || d[2] != (byte)'H' || d[3] != (byte)'S') return false;
        var list = new List<string>(d[4]);
        int pos = 5;
        for (int i = 0; i < d[4] && pos <= d.Length; i++)
        {
            int end = d[pos..].IndexOf((byte)0);
            if (end < 0) end = d.Length - pos;
            list.Add(System.Text.Encoding.ASCII.GetString(d.Slice(pos, end)));
            pos += end + 1;
        }
        names = list.ToArray();
        return true;
    }

    public static byte[] SubstanceTableBytes(IReadOnlyList<string> names)
    {
        var ms = new MemoryStream();
        ms.Write("DSHS"u8); ms.WriteByte((byte)Math.Min(names.Count, 255));
        foreach (var n in names.Take(255)) { ms.Write(System.Text.Encoding.ASCII.GetBytes(n)); ms.WriteByte(0); }
        return ms.ToArray();
    }

    private static float[] ReadFloats(ReadOnlySpan<byte> d)
    {
        var f = new float[MaxWheels];
        for (int i = 0; i < MaxWheels; i++) f[i] = BinaryPrimitives.ReadSingleLittleEndian(d[(i * 4)..]);
        return f;
    }

    public byte[] ToBytes()
    {
        var b = new byte[Size];
        "DSH1"u8.CopyTo(b);
        b[4] = 5;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), Flags);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8), Speed);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(12), Rpm);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(16), RpmLimit);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(20), Fuel);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(24), FuelCapacity);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), Seq);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(32), Throttle);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(36), EffBrake);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(40), AirPressure);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(44), Gear);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(148), InputThrottle >= 0 ? InputThrottle : Throttle);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(152), WearTotal);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(156), AccelX);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(160), AccelY);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(164), AccelZ);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(168), CruiseSpeed);
        b[172] = (byte)Math.Clamp(Retarder, 0, 255); b[173] = (byte)Math.Clamp(RetarderSteps, 0, 255);
        b[48] = (byte)Math.Min(WheelCount, MaxWheels); b[49] = GroundMask; b[50] = LeftMask;
        for (int i = 0; i < MaxWheels; i++)
        {
            if (Susp != null && i < Susp.Length) BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(52 + i * 4), Susp[i]);
            if (WheelVel != null && i < WheelVel.Length) BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(84 + i * 4), WheelVel[i]);
            if (Radius != null && i < Radius.Length) BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(116 + i * 4), Radius[i]);
            if (Substance != null && i < Substance.Length) b[176 + i] = Substance[i];
        }
        return b;
    }
}
