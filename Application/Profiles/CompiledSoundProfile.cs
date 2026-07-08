using KeySounds.Core.Input;
using KeySounds.Core.Profiles;

namespace KeySounds.Application.Profiles;

public sealed record CompiledGroupMapping(bool Enabled, string PackId, string DownSetId, string? UpSetId, float Gain, float PitchVariation, float VolumeVariation, VariationSelectionMode VariationMode);
public sealed record CompiledKeyMapping(bool Enabled, string? PackId, string? DownSetId, string? UpSetId, float? Gain, float? PitchVariation);

public sealed class CompiledSoundProfile
{
    public required string Id { get; init; }
    public required float MasterGain { get; init; }
    public required bool Enabled { get; init; }
    public required bool PlayOnKeyDown { get; init; }
    public required bool PlayOnKeyUp { get; init; }
    public required RepeatSoundMode RepeatMode { get; init; }
    public required int RepeatMinimumIntervalMilliseconds { get; init; }
    public required IReadOnlyDictionary<KeySoundGroup, CompiledGroupMapping> Groups { get; init; }
    public required IReadOnlyDictionary<KeyIdentifier, CompiledKeyMapping> Overrides { get; init; }
}

public static class ProfileCompiler
{
    public static CompiledSoundProfile Compile(SoundProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id)) throw new ArgumentException("Profile ID is required.");
        var groups = profile.Groups.ToDictionary(x => x.Key, x => new CompiledGroupMapping(x.Value.Enabled, x.Value.SoundPackId, x.Value.KeyDownSoundSetId, x.Value.KeyUpSoundSetId,
            Math.Clamp(x.Value.Volume, 0, 1), Math.Clamp(x.Value.PitchVariationSemitones, 0, 3), Math.Clamp(x.Value.VolumeVariation, 0, .2f), x.Value.VariationMode));
        var overrides = profile.KeyOverrides.ToDictionary(x => x.Key, x => new CompiledKeyMapping(x.Value.Enabled, x.Value.SoundPackId, x.Value.KeyDownSoundSetId, x.Value.KeyUpSoundSetId,
            x.Value.Volume is null ? null : Math.Clamp(x.Value.Volume.Value, 0, 1), x.Value.PitchVariationSemitones is null ? null : Math.Clamp(x.Value.PitchVariationSemitones.Value, 0, 3)));
        return new CompiledSoundProfile { Id = profile.Id, Enabled = profile.Enabled, MasterGain = Math.Clamp(profile.MasterVolume, 0, 1), PlayOnKeyDown = profile.PlayOnKeyDown,
            PlayOnKeyUp = profile.PlayOnKeyUp, RepeatMode = profile.RepeatMode, RepeatMinimumIntervalMilliseconds = Math.Clamp(profile.RepeatMinimumIntervalMilliseconds, 20, 2000), Groups = groups, Overrides = overrides };
    }
}

public sealed class ActiveProfileService(CompiledSoundProfile initial)
{
    private CompiledSoundProfile _current = initial;
    public CompiledSoundProfile Current => Volatile.Read(ref _current);
    public void Activate(CompiledSoundProfile profile) => Volatile.Write(ref _current, profile);
}
