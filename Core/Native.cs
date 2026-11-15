using System.Runtime.InteropServices;

namespace DualSenseHub.Core;

/// <summary>Minimal libc bindings used for /dev/hidraw access (read, write, feature-report ioctl, poll).</summary>
internal static class Native
{
    public const int O_RDONLY = 0, O_RDWR = 2, O_NONBLOCK = 0x800, O_CLOEXEC = 0x80000;
    public const int EACCES = 13, EPERM = 1, ENOENT = 2, EIO = 5, ENODEV = 19, EAGAIN = 11, EINTR = 4;
    public const short POLLIN = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd { public int fd; public short events; public short revents; }

    [DllImport("libc.so.6", SetLastError = true)] public static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc.so.6", SetLastError = true)] public static extern int close(int fd);
    [DllImport("libc.so.6", SetLastError = true)] public static extern nint read(int fd, byte[] buf, nuint count);
    [DllImport("libc.so.6", SetLastError = true)] public static extern nint write(int fd, byte[] buf, nuint count);
    [DllImport("libc.so.6", SetLastError = true)] public static extern int ioctl(int fd, ulong request, byte[] buf);
    [DllImport("libc.so.6", SetLastError = true)] public static extern int poll(ref PollFd fds, ulong nfds, int timeout);

    [DllImport("libc.so.6")] private static extern nint realpath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint resolved);
    [DllImport("libc.so.6")] private static extern void free(nint p);

    /// <summary>Canonical path with all symlinks resolved (sysfs class links are relative to link parents).</summary>
    public static string RealPath(string path)
    {
        nint p = realpath(path, 0);
        if (p == 0) return path;
        try { return Marshal.PtrToStringUTF8(p) ?? path; } finally { free(p); }
    }

    /// <summary>HIDIOCGFEATURE(len) = _IOC(READ|WRITE, 'H', 0x07, len).</summary>
    public static ulong HidIocGFeature(int len) => (3UL << 30) | ((ulong)len << 16) | (0x48UL << 8) | 0x07UL;
}
