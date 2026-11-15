using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseHub.Core;

namespace DualSenseHub.ViewModels;

/// <summary>Placeholder host for game modes; phase 4 (ETS2 telemetry) registers its IGameMode here.</summary>
public sealed partial class GameModesViewModel : ObservableObject
{
    private readonly ILightOutput _output;
    private readonly EffectEngine _engine;

    public ObservableCollection<IGameMode> Modes { get; } = [];
    [ObservableProperty] private IGameMode? _active;
    public bool IsEmpty => Modes.Count == 0;

    public GameModesViewModel(ILightOutput output, EffectEngine engine)
    {
        _output = output; _engine = engine;
        Modes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Toggle(IGameMode mode)
    {
        if (Active == mode) { mode.Stop(); Active = null; _engine.Resume(); return; }
        Active?.Stop();
        _engine.Pause();
        mode.Start(_output);
        Active = mode;
    }
}
