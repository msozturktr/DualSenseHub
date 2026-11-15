using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseHub.Core;

namespace DualSenseHub.ViewModels;

public sealed partial class DeviceTabViewModel : ObservableObject, IDisposable
{
    private readonly ControllerService _service;
    private readonly DispatcherTimer _uiTimer;
    private InputState? _latest;

    [ObservableProperty] private string _model = "-";
    [ObservableProperty] private string _battery = "-";
    [ObservableProperty] private string _connection = "-";
    [ObservableProperty] private string _mac = "-";
    [ObservableProperty] private string _firmware = "-";
    [ObservableProperty] private string _hardware = "-";
    [ObservableProperty] private string _hidrawPath = "-";
    [ObservableProperty] private string _sysfsPaths = "-";
    [ObservableProperty] private string _hidStatus = "";
    [ObservableProperty] private InputState? _live;
    [ObservableProperty] private string _sensorText = "";
    [ObservableProperty] private string _testMessage = "";

    public DeviceTabViewModel(ControllerService service)
    {
        _service = service;
        _service.InputReceived += s => _latest = s;
        _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => Tick());
        _uiTimer.Start();
        Refresh();
    }

    public string TranslateStatus(string? s) => s switch
    {
        "Charging" => "şarj oluyor",
        "Discharging" => "deşarj",
        "Full" => "dolu",
        "Not charging" => "şarj olmuyor",
        null => "?",
        _ => s,
    };

    public void Refresh()
    {
        var i = _service.Current;
        if (i == null)
        {
            Model = Battery = Connection = Mac = Firmware = Hardware = HidrawPath = SysfsPaths = "-";
            HidStatus = "";
            return;
        }
        Model = i.ModelName;
        Battery = _service.BatteryPercent is { } p ? $"%{p} ({TranslateStatus(_service.BatteryStatus)})" : "-";
        Connection = i.Connection switch { ConnectionType.Bluetooth => "Bluetooth", ConnectionType.Usb => "USB", _ => "?" };
        Mac = _service.PairingMac ?? i.Mac ?? "-";
        var fw = _service.Firmware;
        Firmware = fw == null ? "-" : $"{fw.FirmwareVersion} (derleme {fw.BuildDate} {fw.BuildTime})";
        Hardware = fw?.HardwareVersion ?? "-";
        HidrawPath = i.HidrawPath;
        SysfsPaths = string.Join("\n", new[] { i.LightbarLed, i.BatteryDir, i.HidSysPath }.Where(x => x != null));
        HidStatus = _service.Hid != null ? "hidraw erişimi: açık" : $"hidraw erişimi: kapalı ({_service.HidError ?? "?"})";
    }

    private void Tick()
    {
        var s = _latest;
        if (s == null || ReferenceEquals(s, Live)) return;
        Live = s;
        SensorText = $"Gyro   X {s.GyroX,6}  Y {s.GyroY,6}  Z {s.GyroZ,6}\nAccel  X {s.AccelX,6}  Y {s.AccelY,6}  Z {s.AccelZ,6}\n" +
                     $"Sol çubuk ({s.Lx},{s.Ly})  Sağ çubuk ({s.Rx},{s.Ry})  L2 {s.L2}  R2 {s.R2}";
    }

    [RelayCommand]
    private async Task Rumble(string kind)
    {
        byte strong = kind is "strong" or "both" ? (byte)255 : (byte)0;
        byte weak = kind is "weak" or "both" ? (byte)255 : (byte)0;
        if (!_service.SetRumble(strong, weak)) { TestMessage = "Titreşim gönderilemedi (hidraw erişimi yok)."; return; }
        TestMessage = "";
        await Task.Delay(600);
        _service.SetRumble(0, 0);
    }

    [RelayCommand]
    private void Trigger(string kind)
    {
        TriggerEffect e = kind switch
        {
            "continuous" => TriggerEffect.Continuous(2, 200),
            "section" => TriggerEffect.Section(3, 7, 220),
            "weapon" => TriggerEffect.Weapon(2, 5, 8),
            "vibration" => TriggerEffect.Vibration(2, 6, 30),
            _ => TriggerEffect.Off(),
        };
        TestMessage = _service.SetTriggers(e, e) ? "" : "Tetik efekti gönderilemedi (hidraw erişimi yok).";
    }

    public void Dispose()
    {
        _uiTimer.Stop();
        _service.SetTriggers(TriggerEffect.Off(), TriggerEffect.Off());
        _service.SetRumble(0, 0);
    }
}
