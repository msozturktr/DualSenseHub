namespace DualSenseHub.Core;

[Flags]
public enum Buttons
{
    None = 0, Square = 1 << 0, Cross = 1 << 1, Circle = 1 << 2, Triangle = 1 << 3,
    L1 = 1 << 4, R1 = 1 << 5, L2 = 1 << 6, R2 = 1 << 7, Share = 1 << 8, Options = 1 << 9,
    L3 = 1 << 10, R3 = 1 << 11, PS = 1 << 12, Touchpad = 1 << 13, Mic = 1 << 14,
    DpadUp = 1 << 15, DpadRight = 1 << 16, DpadDown = 1 << 17, DpadLeft = 1 << 18,
}

public readonly record struct TouchPoint(bool Active, int Id, int X, int Y);

public sealed class InputState
{
    public byte Lx, Ly, Rx, Ry, L2, R2;
    public Buttons Buttons;
    public short GyroX, GyroY, GyroZ, AccelX, AccelY, AccelZ;
    public TouchPoint Touch1, Touch2;
    public int BatteryPercent;
    public string BatteryStatus = "";

    public const int TouchWidth = 1920, TouchHeight = 1080;

    /// <summary>Parses a USB (0x01, 64 bytes) or Bluetooth (0x31, 78 bytes) full input report.</summary>
    public static InputState? Parse(byte[] b, int len)
    {
        int o;
        if (len >= 64 && b[0] == 0x01) o = 1;
        else if (len >= 78 && b[0] == 0x31) o = 2;
        else return null;

        var s = new InputState { Lx = b[o], Ly = b[o + 1], Rx = b[o + 2], Ry = b[o + 3], L2 = b[o + 4], R2 = b[o + 5] };
        byte b0 = b[o + 7], b1 = b[o + 8], b2 = b[o + 9];
        var bt = Buttons.None;
        if ((b0 & 0x10) != 0) bt |= Buttons.Square;
        if ((b0 & 0x20) != 0) bt |= Buttons.Cross;
        if ((b0 & 0x40) != 0) bt |= Buttons.Circle;
        if ((b0 & 0x80) != 0) bt |= Buttons.Triangle;
        switch (b0 & 0x0F)
        {
            case 0: bt |= Buttons.DpadUp; break;
            case 1: bt |= Buttons.DpadUp | Buttons.DpadRight; break;
            case 2: bt |= Buttons.DpadRight; break;
            case 3: bt |= Buttons.DpadRight | Buttons.DpadDown; break;
            case 4: bt |= Buttons.DpadDown; break;
            case 5: bt |= Buttons.DpadDown | Buttons.DpadLeft; break;
            case 6: bt |= Buttons.DpadLeft; break;
            case 7: bt |= Buttons.DpadLeft | Buttons.DpadUp; break;
        }
        if ((b1 & 0x01) != 0) bt |= Buttons.L1;
        if ((b1 & 0x02) != 0) bt |= Buttons.R1;
        if ((b1 & 0x04) != 0) bt |= Buttons.L2;
        if ((b1 & 0x08) != 0) bt |= Buttons.R2;
        if ((b1 & 0x10) != 0) bt |= Buttons.Share;
        if ((b1 & 0x20) != 0) bt |= Buttons.Options;
        if ((b1 & 0x40) != 0) bt |= Buttons.L3;
        if ((b1 & 0x80) != 0) bt |= Buttons.R3;
        if ((b2 & 0x01) != 0) bt |= Buttons.PS;
        if ((b2 & 0x02) != 0) bt |= Buttons.Touchpad;
        if ((b2 & 0x04) != 0) bt |= Buttons.Mic;
        s.Buttons = bt;

        s.GyroX = BitConverter.ToInt16(b, o + 15); s.GyroY = BitConverter.ToInt16(b, o + 17); s.GyroZ = BitConverter.ToInt16(b, o + 19);
        s.AccelX = BitConverter.ToInt16(b, o + 21); s.AccelY = BitConverter.ToInt16(b, o + 23); s.AccelZ = BitConverter.ToInt16(b, o + 25);
        s.Touch1 = ParseTouch(b, o + 32);
        s.Touch2 = ParseTouch(b, o + 36);

        byte st = b[o + 52];
        int cap = st & 0x0F, chg = st >> 4;
        s.BatteryPercent = chg == 2 ? 100 : Math.Min(cap * 10 + 5, 100);
        s.BatteryStatus = chg switch { 0 => "Discharging", 1 => "Charging", 2 => "Full", _ => "Unknown" };
        return s;
    }

    private static TouchPoint ParseTouch(byte[] b, int i)
    {
        bool active = (b[i] & 0x80) == 0;
        int x = b[i + 1] | ((b[i + 2] & 0x0F) << 8);
        int y = (b[i + 2] >> 4) | (b[i + 3] << 4);
        return new TouchPoint(active, b[i] & 0x7F, x, y);
    }
}
