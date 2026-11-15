using Avalonia;

namespace DualSenseHub;

sealed class Program
{
    public static SingleInstance? Instance { get; private set; }

    /// <summary>Set by the hidden --screenshots flag: no single-instance, no UDP, no output reports, no config writes.</summary>
    public static string? ScreenshotDir { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        int si = Array.IndexOf(args, "--screenshots");
        if (si >= 0 && si + 1 < args.Length)
        {
            ScreenshotDir = Path.GetFullPath(args[si + 1]);
            Directory.CreateDirectory(ScreenshotDir);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        if (Cli.IsCliRun(args)) return Cli.Run(args);
        Instance = new SingleInstance();
        if (!Instance.TryBecomePrimary()) return 0;
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { Instance.Dispose(); }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
