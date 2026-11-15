using System.Net.Sockets;
using System.Text;

namespace DualSenseHub;

/// <summary>Single-instance guard over a unix domain socket in $XDG_RUNTIME_DIR.</summary>
public sealed class SingleInstance : IDisposable
{
    private readonly string _path;
    private Socket? _listener;
    public event Action? ShowRequested;

    public SingleInstance()
    {
        var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Path.GetTempPath();
        _path = Path.Combine(dir, "dualsensehub.sock");
    }

    /// <summary>Returns true when this process is the primary instance; otherwise signals the running one and returns false.</summary>
    public bool TryBecomePrimary()
    {
        if (SignalExisting()) return false;
        try { File.Delete(_path); } catch { }
        try
        {
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_path));
            _listener.Listen(4);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Single-instance socket failed: " + e.Message);
            _listener?.Dispose(); _listener = null;
            return true; // do not block startup
        }
        new Thread(Accept) { IsBackground = true, Name = "single-instance" }.Start();
        return true;
    }

    private bool SignalExisting()
    {
        if (!File.Exists(_path)) return false;
        try
        {
            using var c = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            c.SendTimeout = 1000;
            c.Connect(new UnixDomainSocketEndPoint(_path));
            c.Send(Encoding.ASCII.GetBytes("show\n"));
            return true;
        }
        catch { return false; }
    }

    private void Accept()
    {
        var l = _listener;
        while (l != null)
        {
            try
            {
                using var c = l.Accept();
                var buf = new byte[16];
                c.ReceiveTimeout = 1000;
                c.Receive(buf);
                try { ShowRequested?.Invoke(); } catch (Exception e) { Console.Error.WriteLine(e); }
            }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) when (_listener == null) { return; }
            catch (Exception) { Thread.Sleep(100); }
        }
    }

    public void Dispose()
    {
        var l = _listener; _listener = null;
        try { l?.Dispose(); File.Delete(_path); } catch { }
    }
}
