using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using DualSenseHub.ViewModels;
using DualSenseHub.Views;

namespace DualSenseHub;

public partial class App : Application
{
    private MainViewModel? _vm;
    private MainWindow? _window;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _quitting;
    private NativeMenuItem? _autoItem;
    private NativeMenuItem? _profilesItem;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _vm = new MainViewModel();
            _window = new MainWindow { DataContext = _vm };
            if (Program.ScreenshotDir != null)
            {
                _window.Width = 1100; _window.Height = 760;
                _window.Show();
                _ = CaptureScreenshots(Program.ScreenshotDir);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            _window.Closing += (_, e) =>
            {
                if (_quitting) return;
                e.Cancel = true;
                _window.Hide();
            };
            desktop.Exit += (_, _) => { _vm.Dispose(); };
            SetupTray();
            if (Program.Instance != null)
            {
                Program.Instance.ShowRequested += () => Dispatcher.UIThread.Post(ShowWindow);
                Program.Instance.QuitRequested += () => Dispatcher.UIThread.Post(Quit);
                Program.Instance.StatusProvider = () => _vm.Games.StatusReport();
            }
            bool hidden = desktop.Args?.Any(a => a == "--tray" || a == "--minimized") == true;
            if (!hidden) ShowWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Hidden dev mode (--screenshots dir): renders each tab to PNG, then exits.</summary>
    private async Task CaptureScreenshots(string dir)
    {
        try
        {
            var w = _window!;
            await Task.Delay(1500);
            var tabs = w.GetVisualDescendants().OfType<TabControl>().First();
            async Task Select(int i) { tabs.SelectedIndex = i; await Task.Delay(700); }
            void Save(string name)
            {
                const double scale = 1.5;
                var size = w.Bounds.Size;
                var px = new Avalonia.PixelSize((int)(size.Width * scale), (int)(size.Height * scale));
                using var bmp = new RenderTargetBitmap(px, new Avalonia.Vector(96 * scale, 96 * scale));
                bmp.Render(w);
                bmp.Save(Path.Combine(dir, name));
            }
            await Select(0); Save("color.png");
            await Select(1); Save("device.png");
            await Select(2); Save("game-modes.png");
            var sv = w.GetVisualDescendants().OfType<GameModesTabView>().First().GetVisualDescendants().OfType<ScrollViewer>().First();
            var head = sv.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Titreşim ve tetikler");
            if (head != null && head.TranslatePoint(new Avalonia.Point(0, 0), (Avalonia.Visual)sv.Content!) is { } pt)
            {
                sv.Offset = new Avalonia.Vector(0, Math.Max(0, pt.Y - 12));
                await Task.Delay(700);
                Save("haptics.png");
            }
        }
        catch (Exception e) { Console.Error.WriteLine("screenshots failed: " + e); }
        finally { Quit(); }
    }

    private void ShowWindow()
    {
        if (_window == null) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Quit()
    {
        _quitting = true;
        _desktop?.Shutdown();
    }

    private void SetupTray()
    {
        var vm = _vm!;
        var menu = new NativeMenu();
        var open = new NativeMenuItem("Aç");
        open.Click += (_, _) => ShowWindow();
        _profilesItem = new NativeMenuItem("Renk profilleri") { Menu = new NativeMenu() };
        _autoItem = new NativeMenuItem("ETS2 otomatik başlat") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = vm.Games.AutoStart };
        _autoItem.Click += (_, _) => vm.Games.AutoStart = !vm.Games.AutoStart;
        vm.Games.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameModesViewModel.AutoStart)) _autoItem.IsChecked = vm.Games.AutoStart;
        };
        var quit = new NativeMenuItem("Çıkış");
        quit.Click += (_, _) => Quit();
        menu.Add(open); menu.Add(_profilesItem); menu.Add(_autoItem); menu.Add(new NativeMenuItemSeparator()); menu.Add(quit);

        RebuildProfiles();
        vm.Color.ProfileNames.CollectionChanged += (_, _) => RebuildProfiles();

        var icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://DualSenseHub/Assets/icon.png"))),
            ToolTipText = "DualSense Hub",
            Menu = menu,
        };
        icon.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, new TrayIcons { icon });
        _window!.Icon = icon.Icon;
    }

    private void RebuildProfiles()
    {
        if (_profilesItem?.Menu is not { } m || _vm == null) return;
        m.Items.Clear();
        if (_vm.Color.ProfileNames.Count == 0)
        {
            m.Add(new NativeMenuItem("(kayıtlı profil yok)") { IsEnabled = false });
            return;
        }
        foreach (var name in _vm.Color.ProfileNames.ToList())
        {
            var item = new NativeMenuItem(name);
            item.Click += (_, _) => { _vm.Color.SelectedProfile = name; _vm.Color.LoadProfileCommand.Execute(null); };
            m.Add(item);
        }
    }
}
