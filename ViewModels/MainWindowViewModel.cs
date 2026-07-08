using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using KeySounds.Application;
using KeySounds.Application.Settings;

namespace KeySounds.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly KeyboardSoundCoordinator _coordinator;
    private readonly Phase2SettingsService? _settings;
    public MainWindowViewModel(KeyboardSoundCoordinator coordinator, Phase2SettingsService? settings = null) { _coordinator = coordinator; _settings = settings; coordinator.StatusChanged += OnChanged; }
    public bool IsEnabled { get => _coordinator.IsEnabled; set { _coordinator.IsEnabled = value; Raise(); Persist(() => _settings?.SetEnabledAsync(value)); } }
    public string InputStatus => _coordinator.InputStatus;
    public string AudioStatus => _coordinator.AudioStatus;
    public long ClickCount => _coordinator.ClickCount;
    public string? ErrorMessage => _shutdownError ?? _coordinator.ErrorMessage;
    public IReadOnlyList<string> Profiles => _settings?.Profiles.Select(profile => profile.Name).ToList() ?? [];
    public string? SelectedProfile
    {
        get => _settings?.Profiles.FirstOrDefault(profile => profile.Id == _settings.Settings.ActiveProfileId)?.Name;
        set { var profile = _settings?.Profiles.FirstOrDefault(item => item.Name == value); if (profile is not null) Persist(() => _settings!.ActivateAsync(profile.Id)); Raise(); }
    }
    public double GlobalVolume
    {
        get => _settings?.Settings.GlobalVolume ?? 1;
        set { Persist(() => _settings?.SetGlobalVolumeAsync((float)value)); Raise(); }
    }
    public string? SettingsStatus => _settings?.StatusMessage;
    public string SoundPackStatus => _settings is null ? "Built-in pack" : $"{_settings.ValidPackCount} valid, {_settings.InvalidPackCount} invalid packs";
    public string SampleCacheStatus
    {
        get { var snapshot = _settings?.CacheSnapshot; return snapshot is null ? "1 sample" : $"{snapshot.LoadedSampleCount} samples, {snapshot.DecodedBytes / 1024d:F1} KiB decoded"; }
    }
    public async Task StartAsync(CancellationToken token = default)
    {
        if (_settings is not null) { await _settings.InitializeAsync(token); _coordinator.IsEnabled = _settings.Settings.SoundsEnabled; }
        await _coordinator.StartAsync(token);
        Raise(nameof(Profiles)); Raise(nameof(SelectedProfile)); Raise(nameof(GlobalVolume)); Raise(nameof(IsEnabled)); Raise(nameof(SettingsStatus)); Raise(nameof(SoundPackStatus)); Raise(nameof(SampleCacheStatus));
    }
    public Task StopAsync() => _coordinator.StopAsync();
    public void ReportShutdownError(Exception exception) { _shutdownError = exception.Message; Raise(nameof(ErrorMessage)); }
    private void OnChanged(object? s, EventArgs e) => Dispatcher.UIThread.Post(() => { Raise(nameof(InputStatus)); Raise(nameof(AudioStatus)); Raise(nameof(ClickCount)); Raise(nameof(ErrorMessage)); });
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private string? _shutdownError;
    private async void Persist(Func<Task?> operation)
    {
        try { var task = operation(); if (task is not null) await task; }
        catch (Exception ex) { _shutdownError = ex.Message; Raise(nameof(ErrorMessage)); }
    }
}
