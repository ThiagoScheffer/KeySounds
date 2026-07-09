using KeySounds.Core.Audio;
using KeySounds.Core.Settings;

namespace KeySounds.Application.Settings;

public static class ApplicationSettingsMigration
{
    public static bool Apply(ApplicationSettings settings)
    {
        var changed = false;
        if (settings.SchemaVersion < 2)
        {
            settings.AudioOutputSelection = string.IsNullOrWhiteSpace(settings.AudioOutputDeviceId) ? AudioOutputSelection.SystemDefault : AudioOutputSelection.Specific(settings.AudioOutputDeviceId);
            settings.AudioLatencyMilliseconds = settings.AudioLatencyMilliseconds is >= 10 and <= 100 ? settings.AudioLatencyMilliseconds : 20; settings.SchemaVersion = 2; changed = true;
        }
        if (settings.SchemaVersion < 3)
        {
            settings.KeyboardProfileAssignments = new(settings.KeyboardDeviceProfileAssignments, StringComparer.Ordinal); settings.SchemaVersion = 3; changed = true;
        }
        if (settings.SchemaVersion < 4)
        {
            settings.MinimizeToTray = true;
            settings.CloseToTray = true;
            settings.ShowWindowOnStartup = true;
            settings.EnableLocalCrashReports = true;
            settings.DiagnosticReportRetentionDays = settings.DiagnosticReportRetentionDays is >= 1 and <= 365 ? settings.DiagnosticReportRetentionDays : 14;
            settings.StartWithWindows = false;
            settings.HasCompletedFirstRun = false;
            settings.SchemaVersion = 4;
            changed = true;
        }
        return changed;
    }
}
