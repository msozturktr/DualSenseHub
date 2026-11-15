using System.Buffers.Binary;

namespace DualSenseHub.Core.Ets2;

/// <summary>Decoded telemetry datagram; see ets2-plugin/PROTOCOL.md.</summary>
public readonly record struct Ets2Packet(
    ushort Flags, float Speed, float Rpm, float RpmLimit, float Fuel, float FuelCapacity, uint Seq)
{
    public const int Size = 32;
    public const int Port = 47555;

    public bool LeftBlinker => (Flags & 1) != 0;
    public bool RightBlinker => (Flags & 2) != 0;
    public bool Hazard => (Flags & 4) != 0;
    public bool Brake => (Flags & 8) != 0;
    public bool EngineOn => (Flags & 16) != 0;
    public bool ElectricOn => (Flags & 32) != 0;
    public bool Paused => (Flags & 64) != 0;
    public double SpeedKmh => Speed * 3.6;
    public double FuelFraction => FuelCapacity > 0 ? Math.Clamp(Fuel / FuelCapacity, 0, 1) : 1;

    public static bool TryParse(ReadOnlySpan<byte> d, out Ets2Packet p)
    {
        p = default;
        if (d.Length < Size || d[0] != (byte)'D' || d[1] != (byte)'S' || d[2] != (byte)'H' || d[3] != (byte)'1' || d[4] != 1) return false;
        p = new Ets2Packet(
            BinaryPrimitives.ReadUInt16LittleEndian(d[6..]),
            BinaryPrimitives.ReadSingleLittleEndian(d[8..]),
            BinaryPrimitives.ReadSingleLittleEndian(d[12..]),
            BinaryPrimitives.ReadSingleLittleEndian(d[16..]),
            BinaryPrimitives.ReadSingleLittleEndian(d[20..]),
            BinaryPrimitives.ReadSingleLittleEndian(d[24..]),
            BinaryPrimitives.ReadUInt32LittleEndian(d[28..]));
        return true;
    }

    public byte[] ToBytes()
    {
        var b = new byte[Size];
        "DSH1"u8.CopyTo(b);
        b[4] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), Flags);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8), Speed);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(12), Rpm);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(16), RpmLimit);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(20), Fuel);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(24), FuelCapacity);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), Seq);
        return b;
    }
}
