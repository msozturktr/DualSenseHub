using Avalonia;

namespace DualSenseHub;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Cli.IsCliRun(args)) return Cli.Run(args);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
