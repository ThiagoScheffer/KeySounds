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
    private readonly DispatcherTimer _counterTimer;
    public MainWindowViewModel(KeyboardSoundCoordinator coordinator, Phase2SettingsService? settings = null, ProfilesPageViewModel? profilesPage = null, AudioSettingsViewModel? audioPage = null, KeyboardsViewModel? keyboardsPage = null, SoundPacksViewModel? soundPacksPage = null)
    {
        _coordinator = coordinator; _settings = settings; ProfilesPage = profilesPage; AudioPage = audioPage; KeyboardsPage = keyboardsPage; SoundPacksPage = soundPacksPage; coordinator.StatusChanged += OnChanged;
        if (_settings is not null) _settings.Changed += OnSettingsChanged;
        _counterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => { Raise(nameof(ClickCount)); Raise(nameof(TriggerDiagnostics)); });
    }
    public ProfilesPageViewModel? ProfilesPage { get; }
    public AudioSettingsViewModel? AudioPage { get; }
    public KeyboardsViewModel? KeyboardsPage { get; }
    public SoundPacksViewModel? SoundPacksPage { get; }
    public bool IsEnabled { get => _coordinator.IsEnabled; set { _coordinator.IsEnabled = value; Raise(); Persist(() => _settings?.SetEnabledAsync(value)); } }
    public string InputStatus => _coordinator.InputStatus;
    public string AudioStatus => _coordinator.AudioStatus;
    public long ClickCount => _coordinator.ClickCount;
    public string TriggerDiagnostics => _coordinator.TriggerDiagnostics;
    public string? ErrorMessage => _shutdownError ?? _coordinator.ErrorMessage;
    public IReadOnlyList<string> Profiles => _settings?.Profiles.Select(profile => profile.Name).ToList() ?? [];
    public string? SelectedProfile
    {
        get => _settings?.Profiles.FirstOrDefault(profile => profile.Id == _settings.Settings.ActiveProfileId)?.Name;
        set
        {
            var profile = _settings?.Profiles.FirstOrDefault(item => item.Name == value);
            if (profile is null) return;
            if (ProfilesPage?.IsDirty == true) { _shutdownError = "Save or revert profile edits before switching the active profile."; Raise(nameof(ErrorMessage)); return; }
            Persist(async () => { await _settings!.ActivateAsync(profile.Id); ProfilesPage?.RefreshFromServices(); Raise(nameof(SelectedProfile)); return; });
        }
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
    public string KeyboardSummary => KeyboardsPage is null ? "Keyboard assignments unavailable" : $"{KeyboardsPage.Devices.Count} connected keyboard(s), {_settings?.Settings.KeyboardProfileAssignments.Count ?? 0} saved assignment(s)";
    public string RecentWarning => ErrorMessage ?? SettingsStatus ?? AudioPage?.Message ?? SoundPacksPage?.StatusText ?? "No recent warnings.";
    public async Task StartAsync(CancellationToken token = default)
    {
        if (_settings is not null) { await _settings.InitializeAsync(token); _coordinator.IsEnabled = _settings.Settings.SoundsEnabled; }
        if (ProfilesPage is not null) await ProfilesPage.InitializeAsync();
        await _coordinator.StartAsync(token);
        if (AudioPage is not null) await AudioPage.InitializeAsync();
        if (KeyboardsPage is not null) await KeyboardsPage.InitializeAsync(token);
        if (SoundPacksPage is not null) await SoundPacksPage.InitializeAsync(token);
        _counterTimer.Start();
        Raise(nameof(Profiles)); Raise(nameof(SelectedProfile)); Raise(nameof(GlobalVolume)); Raise(nameof(IsEnabled)); Raise(nameof(SettingsStatus)); Raise(nameof(SoundPackStatus)); Raise(nameof(SampleCacheStatus));
        Raise(nameof(ProfilesPage));
        Raise(nameof(AudioPage));
        Raise(nameof(KeyboardsPage));
        Raise(nameof(SoundPacksPage));
        Raise(nameof(KeyboardSummary));
        Raise(nameof(RecentWarning));
    }
    public Task StopAsync() { _counterTimer.Stop(); return _coordinator.StopAsync(); }
    public void ReportShutdownError(Exception exception) { _shutdownError = exception.Message; Raise(nameof(ErrorMessage)); }
    private void OnChanged(object? s, EventArgs e) => Dispatcher.UIThread.Post(() => { Raise(nameof(InputStatus)); Raise(nameof(AudioStatus)); Raise(nameof(ClickCount)); Raise(nameof(ErrorMessage)); });
    private void OnSettingsChanged(object? s, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        Raise(nameof(Profiles)); Raise(nameof(SelectedProfile)); Raise(nameof(SettingsStatus)); Raise(nameof(SoundPackStatus)); Raise(nameof(SampleCacheStatus)); Raise(nameof(KeyboardSummary)); Raise(nameof(RecentWarning));
        ProfilesPage?.RefreshFromServices(); SoundPacksPage?.Rebuild();
    });
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private string? _shutdownError;
    private async void Persist(Func<Task?> operation)
    {
        try { var task = operation(); if (task is not null) await task; }
        catch { _shutdownError = "The setting could not be saved."; Raise(nameof(ErrorMessage)); }
    }
}
