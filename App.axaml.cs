using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
