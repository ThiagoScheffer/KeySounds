using KeySounds.Application.Diagnostics;
using KeySounds.Application.Settings;
using KeySounds.Application.SystemIntegration;
using KeySounds.Infrastructure.Persistence;

namespace KeySounds.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly Phase2SettingsService _settings;
    private readonly StartupRegistrationService _startup;
    private readonly DiagnosticReportService _diagnostics;
    private readonly StoragePaths _paths;
    private string? _status;

    public SettingsViewModel(Phase2SettingsService settings, StartupRegistrationService startup, DiagnosticReportService diagnostics, StoragePaths paths)
    {
        _settings = settings; _startup = startup; _diagnostics = diagnostics; _paths = paths;
        ExportDiagnosticsCommand = new(ExportDiagnosticsAsync, onError: _ => Status = "Diagnostics export failed.");
        CompleteFirstRunCommand = new(CompleteFirstRunAsync, onError: _ => Status = "Could not save first-run preferences.");
    }

    public bool StartWithWindows { get => _settings.Settings.StartWithWindows; set => PersistStartup(value); }
    public bool MinimizeToTray { get => _settings.Settings.MinimizeToTray; set => Persist(s => s.MinimizeToTray = value); }
    public bool CloseToTray { get => _settings.Settings.CloseToTray; set => Persist(s => s.CloseToTray = value); }
    public bool ShowWindowOnStartup { get => _settings.Settings.ShowWindowOnStartup; set => Persist(s => s.ShowWindowOnStartup = value); }
    public bool EnableLocalCrashReports { get => _settings.Settings.EnableLocalCrashReports; set => Persist(s => s.EnableLocalCrashReports = value); }
    public bool SoundsEnabled { get => _settings.Settings.SoundsEnabled; set => _ = SetSoundsEnabledAsync(value); }
    public bool HasCompletedFirstRun => _settings.Settings.HasCompletedFirstRun;
    public string AppDataPath => _paths.Root;
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "development";
    public string PrivacySummary => "Key events are processed locally and transiently. Diagnostics are local and sanitized.";
    public string? Status { get => _status; private set => Set(ref _status, value); }
    public AsyncCommand ExportDiagnosticsCommand { get; }
    public AsyncCommand CompleteFirstRunCommand { get; }

    public void RefreshAll()
    {
        Raise(nameof(StartWithWindows)); Raise(nameof(MinimizeToTray)); Raise(nameof(CloseToTray)); Raise(nameof(ShowWindowOnStartup));
        Raise(nameof(EnableLocalCrashReports)); Raise(nameof(SoundsEnabled)); Raise(nameof(HasCompletedFirstRun));
    }

    private async Task SetSoundsEnabledAsync(bool enabled)
    {
        try { await _settings.SetEnabledAsync(enabled); Status = enabled ? "Sounds enabled." : "Sounds disabled."; RefreshAll(); }
        catch { Status = "Could not save sounds-enabled preference."; }
    }

    private async void Persist(Action<Core.Settings.ApplicationSettings> update)
    {
        try { await _settings.UpdatePhase3PreferencesAsync(update); Status = "Preference saved."; RefreshAll(); }
        catch { Status = "Could not save preference."; RefreshAll(); }
    }

    private async void PersistStartup(bool enabled)
    {
        var previous = _settings.Settings.StartWithWindows;
        try
        {
            _startup.SetEnabled(enabled, Environment.ProcessPath ?? AppContext.BaseDirectory);
            await _settings.UpdatePhase3PreferencesAsync(settings => settings.StartWithWindows = enabled);
            Status = enabled ? "Start with Windows enabled." : "Start with Windows disabled.";
        }
        catch
        {
            try { _startup.SetEnabled(previous, Environment.ProcessPath ?? AppContext.BaseDirectory); } catch { }
            Status = "Could not update Windows startup registration.";
        }
        finally { RefreshAll(); }
    }

    private async Task ExportDiagnosticsAsync()
    {
        var directory = Path.Combine(_paths.Root, "diagnostics");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
        await _diagnostics.ExportAsync(path);
        Status = $"Diagnostics exported to {path}";
    }

    private async Task CompleteFirstRunAsync()
    {
        await _settings.UpdatePhase3PreferencesAsync(settings => settings.HasCompletedFirstRun = true);
        Status = "First-run setup completed.";
        RefreshAll();
    }
}
