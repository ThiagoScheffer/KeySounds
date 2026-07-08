namespace KeySounds.Core.Audio;

public readonly record struct SoundId(string PackId, string SoundSetId, string FileId);

public readonly record struct SoundTrigger(SoundId SoundId, float Gain, float PitchSemitones);

public enum AudioDeviceAvailability { Active, Disabled, Unplugged, NotPresent, Unknown }
public sealed record AudioOutputDevice(string Id, string DisplayName, bool IsSystemDefault, AudioDeviceAvailability Availability);
public enum AudioOutputSelectionKind { SystemDefault, SpecificDevice }
public readonly record struct AudioOutputSelection(AudioOutputSelectionKind Kind, string? DeviceId)
{
    public static AudioOutputSelection SystemDefault => new(AudioOutputSelectionKind.SystemDefault, null);
    public static AudioOutputSelection Specific(string deviceId) => string.IsNullOrWhiteSpace(deviceId) ? throw new ArgumentException("A specific output requires a device ID.") : new(AudioOutputSelectionKind.SpecificDevice, deviceId);
    public bool IsValid => Kind == AudioOutputSelectionKind.SystemDefault || (Kind == AudioOutputSelectionKind.SpecificDevice && !string.IsNullOrWhiteSpace(DeviceId));
}
public sealed record AudioOutputResolution(bool Success, string? EffectiveDeviceId, string? EffectiveDisplayName, bool IsFallback, string? ErrorCode = null, string? ErrorMessage = null);
public sealed record AudioDeviceSwitchResult(bool Success, AudioOutputSelection RequestedSelection, string? EffectiveDeviceId, string? EffectiveDeviceName, bool IsFallback, string? ErrorCode = null, string? ErrorMessage = null);
public enum AudioEngineState { Uninitialized, Initializing, Running, Switching, Unavailable, Stopping, Stopped, Faulted }
public sealed record AudioEngineStatus(AudioEngineState State, AudioOutputSelection ConfiguredSelection, string? EffectiveDeviceId, string? EffectiveDeviceName,
    bool IsFallbackActive, int LatencyMilliseconds, int ActiveVoiceCount, string? ErrorCode = null, string? ErrorMessage = null, long SuppressedDuringSwitch = 0);
