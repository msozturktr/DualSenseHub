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

    /// <summary>Build identity: changes with every compile, so a replaced binary is detected.</summary>
    public static string Identity { get; } = typeof(SingleInstance).Module.ModuleVersionId.ToString("N");

    /// <summary>True when the executable this process runs from was replaced or removed on disk.</summary>
    public static bool ExecutableDeleted()
    {
        try { return (new FileInfo("/proc/self/exe").LinkTarget ?? "").EndsWith("(deleted)"); }
        catch { return false; }
    }

    public event Action? QuitRequested;
    public Func<string>? StatusProvider { get; set; }
    /// <summary>Handles "record &lt;seconds|stop&gt;"; gets the argument and returns the reply line.</summary>
    public Func<string, string>? RecordHandler { get; set; }

    /// <summary>
    /// Returns true when this process is the primary instance; otherwise signals the running one and returns false.
    /// A running instance with a different build identity (or a deleted executable) is asked to quit and replaced.
    /// </summary>
    public bool TryBecomePrimary()
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        if (File.Exists(_path) && Query("hello " + Identity, out var reply))
        {
            if (reply != "bye") return false; // same build (or legacy instance that just showed its window)
            while (DateTime.UtcNow < deadline && Query("ping", out _)) Thread.Sleep(100);
        }
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

    /// <summary>Sends one command line to the running instance. Returns false if none answers on the socket; reply may be empty for legacy instances.</summary>
    public static bool Query(string command, out string reply)
    {
        reply = "";
        try
        {
            using var c = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            c.SendTimeout = 1000; c.ReceiveTimeout = 1500;
            c.Connect(new UnixDomainSocketEndPoint(SocketPath()));
            c.Send(Encoding.UTF8.GetBytes(command + "\n"));
            var buf = new byte[4096];
            try { int n = c.Receive(buf); reply = Encoding.UTF8.GetString(buf, 0, n).Trim(); } catch (SocketException) { }
            return true;
        }
        catch { return false; }
    }

    private static string SocketPath()
    {
        var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Path.GetTempPath();
        return Path.Combine(dir, "dualsensehub.sock");
    }

    private void Accept()
    {
        var l = _listener;
        while (l != null)
        {
            try
            {
                using var c = l.Accept();
                var buf = new byte[256];
                c.ReceiveTimeout = 1000;
                int n = c.Receive(buf);
                var cmd = Encoding.UTF8.GetString(buf, 0, n).Trim();
                string reply = "ok";
                try
                {
                    if (cmd.StartsWith("hello "))
                    {
                        if (cmd[6..] == Identity && !ExecutableDeleted()) { reply = "same"; ShowRequested?.Invoke(); }
                        else { reply = "bye"; Reply(c, reply); QuitRequested?.Invoke(); continue; }
                    }
                    else if (cmd == "quit") { reply = "bye"; Reply(c, reply); QuitRequested?.Invoke(); continue; }
                    else if (cmd == "status") reply = StatusProvider?.Invoke() ?? "starting";
                    else if (cmd == "ping") reply = "pong";
                    else if (cmd == "record" || cmd.StartsWith("record ")) reply = RecordHandler?.Invoke(cmd[6..].Trim()) ?? "error: not ready";
                    else ShowRequested?.Invoke();
                }
                catch (Exception e) { Console.Error.WriteLine(e); }
                Reply(c, reply);
            }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) when (_listener == null) { return; }
            catch (Exception) { Thread.Sleep(100); }
        }
    }

    private static void Reply(Socket c, string s) { try { c.Send(Encoding.UTF8.GetBytes(s + "\n")); } catch { } }

    public void Dispose()
    {
        var l = _listener; _listener = null;
        try { l?.Dispose(); File.Delete(_path); } catch { }
    }
}
