using Avalonia;

namespace DualSenseHub;

sealed class Program
{
    public static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
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
