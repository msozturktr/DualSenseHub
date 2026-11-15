using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DualSenseHub.Core;

namespace DualSenseHub.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ControllerService _service = new();
    private readonly EffectEngine _engine;

    public ColorTabViewModel Color { get; }
    public DeviceTabViewModel Device { get; }
    public GameModesViewModel Games { get; }

    [ObservableProperty] private string _statusText = "DualSense aranıyor...";
    [ObservableProperty] private bool _showPermissionBanner;

    public string PermissionMessage =>
        "Denetleyiciye erişim izni yok. Proje klasöründe şu komutu çalıştırın: ./scripts/install-udev.sh (sudo gerektirir). " +
        "Ardından denetleyiciyi çıkarıp yeniden bağlayın.";

    public MainViewModel()
    {
        _engine = new EffectEngine(_service);
        Color = new ColorTabViewModel(_service, _engine);
        Device = new DeviceTabViewModel(_service);
        Games = new GameModesViewModel(_service, _engine);

        _service.StateChanged += () => Dispatcher.UIThread.Post(Refresh);
        _service.DeviceConnected += () => Dispatcher.UIThread.Post(Color.OnDeviceConnected);
        _service.Start();
    }

    private void Refresh()
    {
        ShowPermissionBanner = _service.PermissionDenied;
        var info = _service.Current;
        if (info == null) { StatusText = "DualSense bulunamadı. Denetleyiciyi bağlayın."; }
        else
        {
            string conn = info.Connection == ConnectionType.Bluetooth ? "Bluetooth" : info.Connection == ConnectionType.Usb ? "USB" : "?";
            string bat = _service.BatteryPercent is { } p ? $" · Batarya %{p} ({Device.TranslateStatus(_service.BatteryStatus)})" : "";
            StatusText = $"{info.ModelName} bağlı · {conn}{bat}";
        }
        Device.Refresh();
        Color.OnBatteryOrConnectionChanged();
    }

    public void Dispose()
    {
        Color.Dispose();
        Device.Dispose();
        Games.Dispose();
        _engine.Dispose();
        _service.Dispose();
    }
}
