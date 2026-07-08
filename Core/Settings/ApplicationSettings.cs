using KeySounds.Core.Audio;

namespace KeySounds.Core.Settings;

public sealed class ApplicationSettings
{
    public int SchemaVersion { get; set; } = 2;
    public bool SoundsEnabled { get; set; } = true;
    public string? ActiveProfileId { get; set; }
    public string? AudioOutputDeviceId { get; set; }
    public AudioOutputSelection AudioOutputSelection { get; set; } = AudioOutputSelection.SystemDefault;
    public int AudioLatencyMilliseconds { get; set; } = 20;
    public float GlobalVolume { get; set; } = 1f;
    public bool MinimizeOnClose { get; set; }
    public Dictionary<string, string> KeyboardDeviceProfileAssignments { get; set; } = [];
}
