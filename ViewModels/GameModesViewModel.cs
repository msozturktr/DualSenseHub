using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseHub.Core;
using DualSenseHub.Core.Ets2;

namespace DualSenseHub.ViewModels;

/// <summary>Host for game modes; currently ETS2 (UDP telemetry from the plugin).</summary>
public sealed partial class GameModesViewModel : ObservableObject, IDisposable
{
    private readonly ILightOutput _output;
    private readonly EffectEngine _engine;
    private readonly Ets2Mode _ets2 = new();
    private readonly DispatcherTimer _ui = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Timer _saveTimer;
    private bool _loading = true;

    public ObservableCollection<IGameMode> Modes { get; } = [];
    public string[] SourceOptions { get; } = ["Hız", "Devir (RPM)"];

    [ObservableProperty] private IGameMode? _selected;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _connectionText = "Mod durdurulmuş.";
    [ObservableProperty] private string _readout = "";
    [ObservableProperty] private bool _isEts2Selected;

    [ObservableProperty] private bool _blinkers;
    [ObservableProperty] private bool _brake;
    [ObservableProperty] private bool _engineOff;
    [ObservableProperty] private bool _lowFuel;
    [ObservableProperty] private bool _gradient;
    [ObservableProperty] private bool _dimWhenPaused;
    [ObservableProperty] private int _sourceIndex;
    [ObservableProperty] private string _blinkerColor = "";
    [ObservableProperty] private string _brakeColor = "";
    [ObservableProperty] private string _lowFuelColor = "";

    public string ButtonText => IsRunning ? "Durdur" : "Başlat";

    public GameModesViewModel(ILightOutput output, EffectEngine engine)
    {
        _output = output; _engine = engine;
        Modes.Add(_ets2);
        Selected = _ets2;
        var s = ConfigStore.LoadEts2();
        _ets2.Settings = s;
        Blinkers = s.Blinkers; Brake = s.Brake; EngineOff = s.EngineOff; LowFuel = s.LowFuel; Gradient = s.Gradient;
        DimWhenPaused = s.DimWhenPaused; SourceIndex = (int)s.Source;
        BlinkerColor = s.BlinkerColor; BrakeColor = s.BrakeColor; LowFuelColor = s.LowFuelColor;
        _loading = false;
        _saveTimer = new Timer(_ => ConfigStore.SaveEts2(_ets2.Settings), null, Timeout.Infinite, Timeout.Infinite);
        _ui.Tick += (_, _) => UpdateReadout();
    }

    partial void OnSelectedChanged(IGameMode? value) => IsEts2Selected = value == _ets2;
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(ButtonText));

    partial void OnBlinkersChanged(bool value) => Edit(s => s.Blinkers = value);
    partial void OnBrakeChanged(bool value) => Edit(s => s.Brake = value);
    partial void OnEngineOffChanged(bool value) => Edit(s => s.EngineOff = value);
    partial void OnLowFuelChanged(bool value) => Edit(s => s.LowFuel = value);
    partial void OnGradientChanged(bool value) => Edit(s => s.Gradient = value);
    partial void OnDimWhenPausedChanged(bool value) => Edit(s => s.DimWhenPaused = value);
    partial void OnSourceIndexChanged(int value) => Edit(s => s.Source = value == 1 ? GradientSource.Rpm : GradientSource.Speed);
    partial void OnBlinkerColorChanged(string value) => Edit(s => s.BlinkerColor = value);
    partial void OnBrakeColorChanged(string value) => Edit(s => s.BrakeColor = value);
    partial void OnLowFuelColorChanged(string value) => Edit(s => s.LowFuelColor = value);

    private void Edit(Action<Ets2Settings> change)
    {
        if (_loading) return;
        change(_ets2.Settings);
        _saveTimer.Change(400, Timeout.Infinite);
    }

    [RelayCommand]
    private void Toggle()
    {
        if (Selected is not { } mode) return;
        if (IsRunning)
        {
            mode.Stop(); IsRunning = false; _ui.Stop();
            _engine.Resume();
            ConnectionText = "Mod durdurulmuş."; Readout = "";
            return;
        }
        _engine.Pause();
        mode.Start(_output);
        IsRunning = true;
        _ui.Start();
        UpdateReadout();
    }

    private void UpdateReadout()
    {
        if (_ets2.Listener.Error is { } err) { ConnectionText = $"UDP 47555 açılamadı: {err}"; Readout = ""; return; }
        if (!_ets2.Connected) { ConnectionText = "Bağlantı yok (oyun/eklenti bekleniyor)"; Readout = ""; return; }
        var p = _ets2.Listener.Latest;
        ConnectionText = "Bağlı";
        var on = new List<string>();
        if (p.LeftBlinker) on.Add("Sol sinyal");
        if (p.RightBlinker) on.Add("Sağ sinyal");
        if (p.Hazard) on.Add("Dörtlü");
        if (p.Brake) on.Add("Fren");
        if (p.Paused) on.Add("Duraklatıldı");
        Readout = $"Hız: {p.SpeedKmh:F0} km/h   Devir: {p.Rpm:F0}/{p.RpmLimit:F0}\n" +
                  $"Yakıt: {p.Fuel:F0}/{p.FuelCapacity:F0} L (%{p.FuelFraction * 100:F0})   Motor: {(p.EngineOn ? "açık" : "kapalı")}\n" +
                  $"Aktif: {(on.Count == 0 ? "-" : string.Join(", ", on))}   Paket #{p.Seq}";
    }

    public void Dispose()
    {
        _ui.Stop();
        _saveTimer.Dispose();
        ConfigStore.SaveEts2(_ets2.Settings);
        if (IsRunning) _ets2.Stop();
        _ets2.Dispose();
    }
}
