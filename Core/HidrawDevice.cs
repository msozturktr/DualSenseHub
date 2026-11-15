using System.Runtime.InteropServices;
using System.Text;

namespace DualSenseHub.Core;

public sealed record FirmwareInfo(string BuildDate, string BuildTime, string HardwareVersion, string FirmwareVersion);

/// <summary>Raw /dev/hidraw access for a DualSense: output reports, input reports and feature reports.</summary>
public sealed class HidrawDevice : IDisposable
{
    private readonly int _fd;
    private readonly object _writeLock = new();
    private readonly Thread? _reader;
    private volatile bool _stop;
    private byte _seq;

    public string Path { get; }
    public bool IsBluetooth { get; }
    public bool CanWrite { get; }
    public OutputState Output { get; } = new();
    public event Action<InputState>? InputReceived;
    public event Action? Disconnected;

    private HidrawDevice(string path, bool bluetooth, int fd, bool canWrite)
    {
        Path = path; IsBluetooth = bluetooth; _fd = fd; CanWrite = canWrite;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "hidraw-reader" };
        _reader.Start();
    }

    /// <summary>Opens the device; on failure returns null with an error text and a permission flag.</summary>
    public static HidrawDevice? TryOpen(string path, bool bluetooth, out string? error, out bool permissionDenied)
    {
        error = null; permissionDenied = false;
        bool rw = true;
        int fd = Native.open(path, Native.O_RDWR | Native.O_CLOEXEC);
        if (fd < 0)
        {
            int errno = Marshal.GetLastWin32Error();
            if (errno == Native.EACCES || errno == Native.EPERM) { permissionDenied = true; error = "Permission denied"; return null; }
            error = $"open failed (errno {errno})";
            return null;
        }
        return new HidrawDevice(path, bluetooth, fd, rw);
    }

    private void ReadLoop()
    {
        var buf = new byte[128];
        var pfd = new Native.PollFd { fd = _fd, events = Native.POLLIN };
        while (!_stop)
        {
            int pr = Native.poll(ref pfd, 1, 250);
            if (_stop) break;
            if (pr < 0)
            {
                if (Marshal.GetLastWin32Error() == Native.EINTR) continue;
                break;
            }
            if (pr == 0) continue;
            if ((pfd.revents & ~Native.POLLIN) != 0 && (pfd.revents & Native.POLLIN) == 0) break; // HUP/ERR
            nint n = Native.read(_fd, buf, (nuint)buf.Length);
            if (n < 0)
            {
                int e = Marshal.GetLastWin32Error();
                if (e == Native.EAGAIN || e == Native.EINTR) continue;
                break;
            }
            var st = InputState.Parse(buf, (int)n);
            if (st != null) InputReceived?.Invoke(st);
        }
        if (!_stop) Disconnected?.Invoke();
    }

    /// <summary>Sends the given output groups using the current <see cref="Output"/> state.</summary>
    public bool Send(OutputGroups groups)
    {
        if (!CanWrite || _stop) return false;
        lock (_writeLock)
        {
            var report = Output.Build(groups, IsBluetooth, ref _seq);
            nint n = Native.write(_fd, report, (nuint)report.Length);
            return n == report.Length;
        }
    }

    public byte[]? GetFeatureReport(byte id, int length)
    {
        if (_stop) return null;
        var buf = new byte[length];
        buf[0] = id;
        lock (_writeLock)
        {
            int r = Native.ioctl(_fd, Native.HidIocGFeature(length), buf);
            return r < 0 ? null : buf;
        }
    }

    public FirmwareInfo? ReadFirmwareInfo()
    {
        var b = GetFeatureReport(0x20, 64);
        if (b == null) return null;
        string date = Encoding.ASCII.GetString(b, 1, 11).TrimEnd('\0', ' ');
        string time = Encoding.ASCII.GetString(b, 12, 8).TrimEnd('\0', ' ');
        uint hw = BitConverter.ToUInt32(b, 24);
        uint fw = BitConverter.ToUInt32(b, 28);
        return new FirmwareInfo(date, time, $"{hw >> 16}.{hw & 0xFFFF} (0x{hw:X8})", $"0x{fw:X8}");
    }

    public string? ReadMac()
    {
        var b = GetFeatureReport(0x09, 20);
        if (b == null) return null;
        return string.Join(':', Enumerable.Range(1, 6).Reverse().Select(i => b[i].ToString("x2")));
    }

    public void Dispose()
    {
        if (_stop) return;
        _stop = true;
        _reader?.Join(500);
        Native.close(_fd);
    }
}
