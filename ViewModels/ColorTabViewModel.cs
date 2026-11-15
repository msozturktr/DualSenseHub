using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseHub.Core;

namespace DualSenseHub.ViewModels;

public sealed record EffectOption(LightEffect Effect, string Label);

public sealed class Preset
{
    public Preset(string name, Color color, Action<Color> apply)
    {
        Name = name;
        Brush = new SolidColorBrush(color);
        Apply = new RelayCommand(() => apply(color));
    }
    public string Name { get; }
    public IBrush Brush { get; }
    public ICommand Apply { get; }
}

public sealed partial class ColorTabViewModel : ObservableObject, IDisposable
{
    private readonly ControllerService _service;
    private readonly EffectEngine _engine;
    private readonly AppSettings _app;
    private readonly List<NamedProfile> _profiles;
    private readonly Timer _saveTimer;
    private bool _loading = true;

    public ObservableCollection<EffectOption> Effects { get; } =
    [
        new(LightEffect.Static, "Sabit"), new(LightEffect.Breathing, "Nefes"), new(LightEffect.Rainbow, "Gökkuşağı"),
        new(LightEffect.Blink, "Yanıp sönme"), new(LightEffect.Battery, "Batarya rengi"),
    ];
    public ObservableCollection<Preset> Presets { get; } = [];
    public ObservableCollection<string> ProfileNames { get; } = [];
    public string[] PlayerBrightnessOptions { get; } = ["Yüksek", "Orta", "Düşük"];

    [ObservableProperty] private Color _color = Avalonia.Media.Color.Parse("#0050FF");
    [ObservableProperty] private double _brightness = 100;
    [ObservableProperty] private double _speed = 3;
    [ObservableProperty] private EffectOption _selectedEffect;
    [ObservableProperty] private bool _p1;
    [ObservableProperty] private bool _p2;
    [ObservableProperty] private bool _p3;
    [ObservableProperty] private bool _p4;
    [ObservableProperty] private bool _p5;
    [ObservableProperty] private int _playerBrightness;
    [ObservableProperty] private bool _micLed;
    [ObservableProperty] private bool _restoreOnStartup;
    [ObservableProperty] private string? _selectedProfile;
    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private string _message = "";

    public bool ShowSpeed => SelectedEffect.Effect is LightEffect.Breathing or LightEffect.Rainbow or LightEffect.Blink;
    public bool ShowColor => SelectedEffect.Effect != LightEffect.Rainbow && SelectedEffect.Effect != LightEffect.Battery;

    public ColorTabViewModel(ControllerService service, EffectEngine engine)
    {
        _service = service; _engine = engine;
        _app = ConfigStore.LoadSettings();
        _profiles = ConfigStore.LoadProfiles();
        _saveTimer = new Timer(_ => PersistLast(), null, Timeout.Infinite, Timeout.Infinite);
        _selectedEffect = Effects[0];

        foreach (var (n, c) in new (string, string)[]
        {
            ("Kırmızı", "#FF0000"), ("Turuncu", "#FF6A00"), ("Sarı", "#FFD800"), ("Yeşil", "#00FF40"),
            ("Camgöbeği", "#00FFFF"), ("Mavi", "#0050FF"), ("Mor", "#8A2BE2"), ("Pembe", "#FF2D95"), ("Beyaz", "#FFFFFF"),
        })
            Presets.Add(new Preset(n, Avalonia.Media.Color.Parse(c), col => Color = col));
        foreach (var p in _profiles) ProfileNames.Add(p.Name);

        RestoreOnStartup = _app.RestoreOnStartup;
        LoadFrom(_app.Last);
        _loading = false;
        if (_app.HasSaved && _app.RestoreOnStartup) ApplyAll();
    }

    private void LoadFrom(LightSettings s)
    {
        bool was = _loading; _loading = true;
        Color = Avalonia.Media.Color.Parse(s.ColorHex);
        Brightness = s.Brightness;
        Speed = s.Speed;
        SelectedEffect = Effects.First(e => e.Effect == s.Effect);
        P1 = (s.PlayerMask & 1) != 0; P2 = (s.PlayerMask & 2) != 0; P3 = (s.PlayerMask & 4) != 0;
        P4 = (s.PlayerMask & 8) != 0; P5 = (s.PlayerMask & 16) != 0;
        PlayerBrightness = Math.Clamp(s.PlayerBrightness, 0, 2);
        MicLed = s.MicLed;
        _loading = was;
    }

    private LightSettings Snapshot() => new()
    {
        Effect = SelectedEffect.Effect,
        ColorHex = $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}",
        Brightness = (int)Math.Round(Brightness),
        Speed = Math.Round(Speed, 1),
        PlayerMask = (P1 ? 1 : 0) | (P2 ? 2 : 0) | (P3 ? 4 : 0) | (P4 ? 8 : 0) | (P5 ? 16 : 0),
        PlayerBrightness = PlayerBrightness,
        MicLed = MicLed,
    };

    private void ApplyAll()
    {
        var s = Snapshot();
        _engine.Apply(s);
        _service.SetPlayerLeds((byte)s.PlayerMask, (byte)s.PlayerBrightness);
        _service.SetMicLed(s.MicLed);
    }

    private void Changed()
    {
        if (_loading) return;
        ApplyAll();
        _saveTimer.Change(500, Timeout.Infinite);
    }

    private void PersistLast()
    {
        _app.Last = Snapshot();
        _app.HasSaved = true;
        _app.RestoreOnStartup = RestoreOnStartup;
        ConfigStore.SaveSettings(_app);
    }

    partial void OnColorChanged(Color value) => Changed();
    partial void OnBrightnessChanged(double value) => Changed();
    partial void OnSpeedChanged(double value) => Changed();
    partial void OnSelectedEffectChanged(EffectOption value)
    {
        OnPropertyChanged(nameof(ShowSpeed));
        OnPropertyChanged(nameof(ShowColor));
        Changed();
    }
    partial void OnP1Changed(bool value) => Changed();
    partial void OnP2Changed(bool value) => Changed();
    partial void OnP3Changed(bool value) => Changed();
    partial void OnP4Changed(bool value) => Changed();
    partial void OnP5Changed(bool value) => Changed();
    partial void OnPlayerBrightnessChanged(int value) => Changed();
    partial void OnMicLedChanged(bool value) => Changed();
    partial void OnRestoreOnStartupChanged(bool value) { if (!_loading) _saveTimer.Change(100, Timeout.Infinite); }

    /// <summary>After a reconnect, re-apply the last color when the user wants restoring (or something was already set this session).</summary>
    public void OnDeviceConnected()
    {
        if (RestoreOnStartup || _app.HasSaved && !_loading) ApplyAll();
    }

    public void OnBatteryOrConnectionChanged() { /* battery effect polls the service itself */ }

    [RelayCommand]
    private void SaveProfile()
    {
        var name = ProfileName.Trim();
        if (name.Length == 0) { Message = "Profil adı girin."; return; }
        _profiles.RemoveAll(p => p.Name == name);
        _profiles.Add(new NamedProfile { Name = name, Settings = Snapshot() });
        ConfigStore.SaveProfiles(_profiles);
        if (!ProfileNames.Contains(name)) ProfileNames.Add(name);
        SelectedProfile = name;
        Message = $"'{name}' kaydedildi.";
    }

    [RelayCommand]
    private void LoadProfile()
    {
        var p = _profiles.FirstOrDefault(x => x.Name == SelectedProfile);
        if (p == null) { Message = "Bir profil seçin."; return; }
        _loading = true;
        LoadFrom(p.Settings);
        _loading = false;
        Changed();
        ProfileName = p.Name;
        Message = $"'{p.Name}' yüklendi.";
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        var name = SelectedProfile;
        if (name == null) return;
        _profiles.RemoveAll(p => p.Name == name);
        ConfigStore.SaveProfiles(_profiles);
        ProfileNames.Remove(name);
        Message = $"'{name}' silindi.";
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        if (_app.HasSaved) PersistLast();
    }
}
