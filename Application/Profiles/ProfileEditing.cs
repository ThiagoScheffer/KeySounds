using System.Text.Json;
using System.Text.RegularExpressions;
using KeySounds.Application.SoundPacks;
using KeySounds.Core.Input;
using KeySounds.Core.Profiles;

namespace KeySounds.Application.Profiles;

public enum ProfileValidationSeverity { Warning, Error }
public sealed record ProfileValidationIssue(string Code, string Message, ProfileValidationSeverity Severity, string Path);

public static class SoundProfileCloner
{
    public static SoundProfile Clone(SoundProfile source, string? id = null, string? name = null) => new()
    {
        SchemaVersion = source.SchemaVersion, Id = id ?? source.Id, Name = name ?? source.Name, Description = source.Description, Enabled = source.Enabled,
        MasterVolume = source.MasterVolume, PlayOnKeyDown = source.PlayOnKeyDown, PlayOnKeyUp = source.PlayOnKeyUp,
        RepeatMode = source.RepeatMode, RepeatMinimumIntervalMilliseconds = source.RepeatMinimumIntervalMilliseconds,
        Groups = source.Groups.ToDictionary(item => item.Key, item => new KeyGroupMapping
        {
            Enabled = item.Value.Enabled, SoundPackId = item.Value.SoundPackId, KeyDownSoundSetId = item.Value.KeyDownSoundSetId,
            KeyUpSoundSetId = item.Value.KeyUpSoundSetId, Volume = item.Value.Volume, PitchVariationSemitones = item.Value.PitchVariationSemitones,
            VolumeVariation = item.Value.VolumeVariation, VariationMode = item.Value.VariationMode
        }),
        KeyOverrides = source.KeyOverrides.ToDictionary(item => item.Key, item => new KeyOverride
        {
            Enabled = item.Value.Enabled, SoundPackId = item.Value.SoundPackId, KeyDownSoundSetId = item.Value.KeyDownSoundSetId,
            KeyUpSoundSetId = item.Value.KeyUpSoundSetId, Volume = item.Value.Volume, PitchVariationSemitones = item.Value.PitchVariationSemitones,
            VolumeVariation = item.Value.VolumeVariation, VariationMode = item.Value.VariationMode
        })
    };
}

public sealed class ProfileValidationService(SoundPackCatalogService catalog)
{
    private static readonly Regex SafeId = new("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);
    public IReadOnlyList<ProfileValidationIssue> Validate(SoundProfile profile)
    {
        var issues = new List<ProfileValidationIssue>();
        if (string.IsNullOrWhiteSpace(profile.Id) || !SafeId.IsMatch(profile.Id)) issues.Add(Error("InvalidId", "Profile ID is invalid.", "id"));
        if (profile.SchemaVersion != 1) issues.Add(Error("UnsupportedSchema", "Only profile schema version 1 is supported.", "schemaVersion"));
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 128) issues.Add(Error("InvalidName", "Profile name is required and must not exceed 128 characters.", "name"));
        if (profile.MasterVolume is < 0 or > 1) issues.Add(Error("InvalidMasterVolume", "Master volume must be between 0 and 1.", "masterVolume"));
        if (profile.RepeatMinimumIntervalMilliseconds is < 20 or > 2000) issues.Add(Error("InvalidRepeatInterval", "Repeat interval must be between 20 and 2000 ms.", "repeatMinimumIntervalMilliseconds"));
        foreach (var pair in profile.Groups)
        {
            var path = $"groups.{pair.Key}"; var mapping = pair.Value;
            if (mapping.Volume is < 0 or > 1) issues.Add(Error("InvalidVolume", "Group volume must be between 0 and 1.", path + ".volume"));
            if (mapping.PitchVariationSemitones is < 0 or > 3) issues.Add(Error("InvalidPitch", "Pitch variation must be between 0 and 3 semitones.", path + ".pitchVariationSemitones"));
            if (mapping.VolumeVariation is < 0 or > .2f) issues.Add(Error("InvalidVolumeVariation", "Volume variation must be between 0 and 0.2.", path + ".volumeVariation"));
            ValidateReference(mapping.SoundPackId, mapping.KeyDownSoundSetId, path + ".keyDownSoundSetId", issues, true);
            if (!string.IsNullOrWhiteSpace(mapping.KeyUpSoundSetId)) ValidateReference(mapping.SoundPackId, mapping.KeyUpSoundSetId, path + ".keyUpSoundSetId", issues, true);
        }
        foreach (var pair in profile.KeyOverrides)
        {
            var path = $"overrides.{pair.Key.ScanCode:X4}:{(pair.Key.IsExtended ? 1 : 0)}"; var value = pair.Value;
            if (value.Volume is < 0 or > 1) issues.Add(Error("InvalidOverrideVolume", "Override volume must be between 0 and 1.", path + ".volume"));
            if (value.PitchVariationSemitones is < 0 or > 3) issues.Add(Error("InvalidOverridePitch", "Override pitch variation must be between 0 and 3.", path + ".pitchVariationSemitones"));
            if (value.VolumeVariation is < 0 or > .2f) issues.Add(Error("InvalidOverrideVariation", "Override volume variation must be between 0 and 0.2.", path + ".volumeVariation"));
            if (value.SoundPackId is not null && value.KeyDownSoundSetId is not null) ValidateReference(value.SoundPackId, value.KeyDownSoundSetId, path + ".keyDownSoundSetId", issues, true);
        }
        return issues;
    }

    private void ValidateReference(string? packId, string? setId, string path, List<ProfileValidationIssue> issues, bool required)
    {
        if (string.IsNullOrWhiteSpace(packId)) { issues.Add(Error("MissingPack", "A sound pack must be selected.", path)); return; }
        if (!catalog.TryGetPack(packId, out var pack) || !pack.Validation.IsValid) { issues.Add(Error("MissingPack", $"Sound pack '{packId}' is unavailable.", path)); return; }
        if (required && string.IsNullOrWhiteSpace(setId)) { issues.Add(Error("MissingSoundSet", "A sound set must be selected.", path)); return; }
        if (!string.IsNullOrWhiteSpace(setId) && !catalog.TryGetFiles(packId, setId, out _)) issues.Add(Error("MissingSoundSet", $"Sound set '{setId}' is unavailable.", path));
    }
    private static ProfileValidationIssue Error(string code, string message, string path) => new(code, message, ProfileValidationSeverity.Error, path);
}

public sealed class EditableProfileSession
{
    private readonly ProfileValidationService _validator;
    private SoundProfile _baseline;
    public EditableProfileSession(SoundProfile saved, ProfileValidationService validator) { _validator = validator; _baseline = SoundProfileCloner.Clone(saved); WorkingCopy = SoundProfileCloner.Clone(saved); }
    public SoundProfile Original => SoundProfileCloner.Clone(_baseline);
    public SoundProfile WorkingCopy { get; private set; }
    public bool HasUnsavedChanges => !SemanticEquals(_baseline, WorkingCopy);
    public IReadOnlyList<ProfileValidationIssue> ValidationIssues => _validator.Validate(WorkingCopy);
    public bool IsValid => ValidationIssues.All(issue => issue.Severity != ProfileValidationSeverity.Error);
    public void Revert() => WorkingCopy = SoundProfileCloner.Clone(_baseline);
    public void AcceptSaved(SoundProfile saved) { _baseline = SoundProfileCloner.Clone(saved); WorkingCopy = SoundProfileCloner.Clone(saved); }
    public void AddOrUpdateOverride(KeyIdentifier key, KeyOverride value) => WorkingCopy.KeyOverrides[key] = value;
    public bool RemoveOverride(KeyIdentifier key) => WorkingCopy.KeyOverrides.Remove(key);
    private static bool SemanticEquals(SoundProfile left, SoundProfile right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
}
