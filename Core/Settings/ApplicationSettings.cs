using KeySounds.Core.Audio;

namespace KeySounds.Core.Settings;

public sealed class ApplicationSettings
{
    public int SchemaVersion { get; set; } = 4;
    public bool SoundsEnabled { get; set; } = true;
    public string? ActiveProfileId { get; set; }
    public string? AudioOutputDeviceId { get; set; }
    public AudioOutputSelection AudioOutputSelection { get; set; } = AudioOutputSelection.SystemDefault;
    public int AudioLatencyMilliseconds { get; set; } = 20;
    public float GlobalVolume { get; set; } = 1f;
    public bool MinimizeOnClose { get; set; }
    public Dictionary<string, string> KeyboardDeviceProfileAssignments { get; set; } = [];
    public Dictionary<string, string> KeyboardProfileAssignments { get; set; } = new(StringComparer.Ordinal);
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool ShowWindowOnStartup { get; set; } = true;
    public bool HasCompletedFirstRun { get; set; }
    public bool EnableLocalCrashReports { get; set; } = true;
    public int DiagnosticReportRetentionDays { get; set; } = 14;
}
