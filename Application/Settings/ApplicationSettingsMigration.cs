using KeySounds.Core.Audio;
using KeySounds.Core.Settings;

namespace KeySounds.Application.Settings;

public static class ApplicationSettingsMigration
{
    public static bool Apply(ApplicationSettings settings)
    {
        if (settings.SchemaVersion >= 2) return false;
        settings.AudioOutputSelection = string.IsNullOrWhiteSpace(settings.AudioOutputDeviceId) ? AudioOutputSelection.SystemDefault : AudioOutputSelection.Specific(settings.AudioOutputDeviceId);
        settings.AudioLatencyMilliseconds = settings.AudioLatencyMilliseconds is >= 10 and <= 100 ? settings.AudioLatencyMilliseconds : 20;
        settings.SchemaVersion = 2; return true;
    }
}
