using KeySounds.Core.Input;

namespace KeySounds.Core.Profiles;

public enum RepeatSoundMode { Suppress, PlayEveryRepeat, Throttled }
public enum VariationSelectionMode { Random, RoundRobin, AvoidImmediateRepeat }

public sealed class SoundProfile
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public float MasterVolume { get; set; } = 0.75f;
    public bool PlayOnKeyDown { get; set; } = true;
    public bool PlayOnKeyUp { get; set; }
    public RepeatSoundMode RepeatMode { get; set; } = RepeatSoundMode.Suppress;
    public int RepeatMinimumIntervalMilliseconds { get; set; } = 80;
    public Dictionary<KeySoundGroup, KeyGroupMapping> Groups { get; init; } = [];
    public Dictionary<KeyIdentifier, KeyOverride> KeyOverrides { get; init; } = [];
}

public sealed class KeyGroupMapping
{
    public bool Enabled { get; set; } = true;
    public required string SoundPackId { get; set; }
    public required string KeyDownSoundSetId { get; set; }
    public string? KeyUpSoundSetId { get; set; }
    public float Volume { get; set; } = 1f;
    public float PitchVariationSemitones { get; set; }
    public float VolumeVariation { get; set; }
    public VariationSelectionMode VariationMode { get; set; } = VariationSelectionMode.AvoidImmediateRepeat;
}

public sealed class KeyOverride
{
    public bool Enabled { get; set; } = true;
    public string? SoundPackId { get; set; }
    public string? KeyDownSoundSetId { get; set; }
    public string? KeyUpSoundSetId { get; set; }
    public float? Volume { get; set; }
    public float? PitchVariationSemitones { get; set; }
}
