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
    private readonly Ets2TelemetryListener _listener = new();
    private readonly Ets2Mode _ets2;
    private readonly Ets2AutoStart _auto;
    private readonly DispatcherTimer _ui = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Timer _saveTimer;
    private bool _loading = true;

    public ObservableCollection<IGameMode> Modes { get; } = [];
    public string[] SourceOptions { get; } = ["Gaz pedalı", "Hız", "Devir (RPM)"];
    public string[] GaugeOptions { get; } = ["Kapalı", "Yakıt", "Hız sabitleyici", "Retarder kademesi"];

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
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private int _sourceIndex;
    [ObservableProperty] private string _blinkerColor = "";
    [ObservableProperty] private string _brakeColor = "";
    [ObservableProperty] private string _lowFuelColor = "";
    [ObservableProperty] private string _gradientLowColor = "";
    [ObservableProperty] private string _gradientHighColor = "";

    [ObservableProperty] private bool _roadFeel;
    [ObservableProperty] private double _roadFeelLevel;
    [ObservableProperty] private bool _gearShift;
    [ObservableProperty] private double _gearShiftLevel;
    [ObservableProperty] private bool _gasTrigger;
    [ObservableProperty] private double _gasTriggerLevel;
    [ObservableProperty] private double _kickdownPoint;
    [ObservableProperty] private bool _brakeTrigger;
    [ObservableProperty] private double _brakeTriggerLevel;
    [ObservableProperty] private bool _engineRumble;
    [ObservableProperty] private double _engineRumbleLevel;
    [ObservableProperty] private bool _crashRumble;
    [ObservableProperty] private double _crashRumbleLevel;
    [ObservableProperty] private int _gaugeIndex;
    [ObservableProperty] private bool _abs;
    [ObservableProperty] private double _absLevel;

    public string ButtonText => IsRunning ? "Durdur" : "Başlat";

    public GameModesViewModel(ILightOutput output, EffectEngine engine)
    {
        _output = output; _engine = engine;
        _ets2 = new Ets2Mode(_listener);
        _auto = new Ets2AutoStart(_listener, () => IsRunning, StartMode, StopMode);
        Modes.Add(_ets2);
        Selected = _ets2;
        var s = ConfigStore.LoadEts2();
        _ets2.Settings = s;
        Blinkers = s.Blinkers; Brake = s.Brake; EngineOff = s.EngineOff; LowFuel = s.LowFuel; Gradient = s.Gradient;
        DimWhenPaused = s.DimWhenPaused; AutoStart = s.AutoStart; SourceIndex = (int)s.Source;
        BlinkerColor = s.BlinkerColor; BrakeColor = s.BrakeColor; LowFuelColor = s.LowFuelColor;
        GradientLowColor = s.GradientLowColor; GradientHighColor = s.GradientHighColor;
        RoadFeel = s.RoadFeel; RoadFeelLevel = s.RoadFeelLevel;
        GearShift = s.GearShift; GearShiftLevel = s.GearShiftLevel;
        GasTrigger = s.GasTrigger; GasTriggerLevel = s.GasTriggerLevel; KickdownPoint = s.KickdownPoint;
        BrakeTrigger = s.BrakeTrigger; BrakeTriggerLevel = s.BrakeTriggerLevel;
        EngineRumble = s.EngineRumble; EngineRumbleLevel = s.EngineRumbleLevel;
        CrashRumble = s.CrashRumble; CrashRumbleLevel = s.CrashRumbleLevel; GaugeIndex = (int)s.Gauge;
        Abs = s.Abs; AbsLevel = s.AbsLevel;
        _loading = false;
        _saveTimer = new Timer(_ => ConfigStore.SaveEts2(_ets2.Settings), null, Timeout.Infinite, Timeout.Infinite);
        _auto.Enabled = s.AutoStart;
        _ui.Tick += (_, _) => { _auto.Tick(); UpdateReadout(); };
        if (Program.ScreenshotDir == null) _listener.Start();
        _ui.Start();
    }

    partial void OnSelectedChanged(IGameMode? value) => IsEts2Selected = value == _ets2;
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(ButtonText));

    partial void OnBlinkersChanged(bool value) => Edit(s => s.Blinkers = value);
    partial void OnBrakeChanged(bool value) => Edit(s => s.Brake = value);
    partial void OnEngineOffChanged(bool value) => Edit(s => s.EngineOff = value);
    partial void OnLowFuelChanged(bool value) => Edit(s => s.LowFuel = value);
    partial void OnGradientChanged(bool value) => Edit(s => s.Gradient = value);
    partial void OnAutoStartChanged(bool value) { _auto.Enabled = value; Edit(s => s.AutoStart = value); }
    partial void OnDimWhenPausedChanged(bool value) => Edit(s => s.DimWhenPaused = value);
    partial void OnSourceIndexChanged(int value) => Edit(s => s.Source = (GradientSource)Math.Clamp(value, 0, 2));
    partial void OnBlinkerColorChanged(string value) => Edit(s => s.BlinkerColor = value);
    partial void OnBrakeColorChanged(string value) => Edit(s => s.BrakeColor = value);
    partial void OnLowFuelColorChanged(string value) => Edit(s => s.LowFuelColor = value);
    partial void OnGradientLowColorChanged(string value) => Edit(s => s.GradientLowColor = value);
    partial void OnGradientHighColorChanged(string value) => Edit(s => s.GradientHighColor = value);

    partial void OnRoadFeelChanged(bool value) => Edit(s => s.RoadFeel = value);
    partial void OnRoadFeelLevelChanged(double value) => Edit(s => s.RoadFeelLevel = (int)Math.Round(value));
    partial void OnGearShiftChanged(bool value) => Edit(s => s.GearShift = value);
    partial void OnGearShiftLevelChanged(double value) => Edit(s => s.GearShiftLevel = (int)Math.Round(value));
    partial void OnGasTriggerChanged(bool value) => Edit(s => s.GasTrigger = value);
    partial void OnGasTriggerLevelChanged(double value) => Edit(s => s.GasTriggerLevel = (int)Math.Round(value));
    partial void OnKickdownPointChanged(double value) => Edit(s => s.KickdownPoint = (int)Math.Round(value));
    partial void OnBrakeTriggerChanged(bool value) => Edit(s => s.BrakeTrigger = value);
    partial void OnBrakeTriggerLevelChanged(double value) => Edit(s => s.BrakeTriggerLevel = (int)Math.Round(value));
    partial void OnEngineRumbleChanged(bool value) => Edit(s => s.EngineRumble = value);
    partial void OnEngineRumbleLevelChanged(double value) => Edit(s => s.EngineRumbleLevel = (int)Math.Round(value));
    partial void OnCrashRumbleChanged(bool value) => Edit(s => s.CrashRumble = value);
    partial void OnCrashRumbleLevelChanged(double value) => Edit(s => s.CrashRumbleLevel = (int)Math.Round(value));
    partial void OnGaugeIndexChanged(int value) => Edit(s => s.Gauge = (LedGauge)Math.Clamp(value, 0, 3));
    partial void OnAbsChanged(bool value) => Edit(s => s.Abs = value);
    partial void OnAbsLevelChanged(double value) => Edit(s => s.AbsLevel = (int)Math.Round(value));

    private void Edit(Action<Ets2Settings> change)
    {
        if (_loading) return;
        change(_ets2.Settings);
        _saveTimer.Change(400, Timeout.Infinite);
    }

    [RelayCommand]
    private void Toggle()
    {
        if (IsRunning) { _auto.NotifyManualStop(); StopMode(); }
        else { _auto.NotifyManualStart(); StartMode(); }
    }

    private void StartMode()
    {
        if (IsRunning) return;
        _engine.Pause();
        _ets2.Start(_output);
        IsRunning = true;
        UpdateReadout();
    }

    private void StopMode()
    {
        if (!IsRunning) return;
        _ets2.Stop(); IsRunning = false;
        _engine.Resume();
        UpdateReadout();
    }

    private void UpdateReadout()
    {
        if (Program.ScreenshotDir != null)
        {
            ConnectionText = "Bağlantı yok (oyun/eklenti bekleniyor)\nUDP 47555: dinleniyor | paket: 0 | son paket: - | protokol: -";
            Readout = ""; return;
        }
        string state = _listener.Listening ? "dinleniyor" : $"port hatası: {_listener.Error ?? "başlatılıyor"}";
        long age = _listener.LastPacketAgeMs;
        string ageText = age < 0 ? "-" : $"{age / 1000.0:F1} sn önce";
        string ver = _listener.ProtocolVersion == 0 ? "-" : $"v{_listener.ProtocolVersion}";
        string info = $"UDP {Ets2Packet.Port}: {state} | paket: {_listener.PacketCount} | son paket: {ageText} | protokol: {ver}";
        if (!_ets2.Connected)
        {
            string head = !_listener.Listening ? "Dinleyici çalışmıyor"
                : _listener.RejectedCount > 0 && _listener.PacketCount == 0 && _listener.LastRejectVersion > 0
                    ? $"Paket geliyor ama çözülemedi (sürüm v{_listener.LastRejectVersion}: {_listener.LastRejectReason})"
                : _listener.RejectedCount > 0 && _listener.PacketCount == 0 ? $"Paket geliyor ama çözülemedi ({_listener.LastRejectReason})"
                : "Bağlantı yok (oyun/eklenti bekleniyor)";
            ConnectionText = head + "\n" + info;
            Readout = ""; return;
        }
        var p = _listener.Latest;
        ConnectionText = (IsRunning ? "Bağlı, mod çalışıyor" : "Bağlı, mod durdurulmuş") + "\n" + info;
        var on = new List<string>();
        if (p.LeftBlinker) on.Add("Sol sinyal");
        if (p.RightBlinker) on.Add("Sağ sinyal");
        if (p.Hazard) on.Add("Dörtlü");
        if (p.Brake) on.Add("Fren");
        if (p.Paused) on.Add("Duraklatıldı");
        Readout = $"Hız: {p.SpeedKmh:F0} km/h   Devir: {p.Rpm:F0}/{p.RpmLimit:F0}   Gaz: %{p.ColorThrottle * 100:F0}\n" +
                  $"Yakıt: {p.Fuel:F0}/{p.FuelCapacity:F0} L (%{p.FuelFraction * 100:F0})   Motor: {(p.EngineOn ? "açık" : "kapalı")}\n" +
                  $"Aktif: {(on.Count == 0 ? "-" : string.Join(", ", on))}   Paket #{p.Seq}\n" +
                  $"Titreşim: {_ets2.LastHaptics}";
    }

    /// <summary>Plain-text state for <c>dualsensehub --status</c>.</summary>
    public string StatusReport() =>
        $"listening={_listener.Listening} port={Ets2Packet.Port} packets={_listener.PacketCount} rejected={_listener.RejectedCount} " +
        $"lastVersion={_listener.ProtocolVersion} lastRejectVersion={_listener.LastRejectVersion} lastRejectReason={_listener.LastRejectReason ?? "-"} " +
        $"ageMs={_listener.LastPacketAgeMs} connected={_listener.Listening && _ets2.Connected} modeRunning={IsRunning} error={_listener.Error ?? "-"}";

    public void Dispose()
    {
        _ui.Stop();
        _saveTimer.Dispose();
        ConfigStore.SaveEts2(_ets2.Settings);
        if (IsRunning) _ets2.Stop();
        _ets2.Dispose();
        _listener.Dispose();
    }
}
