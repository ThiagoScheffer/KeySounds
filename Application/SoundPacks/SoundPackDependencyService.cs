using KeySounds.Application.Settings;
using KeySounds.Core.Profiles;

namespace KeySounds.Application.SoundPacks;

public sealed record SoundPackProfileReference(string ProfileId, string ProfileName, bool IsActive, bool IsSaved);
public sealed record SoundPackKeyboardReference(string KeyboardId, string ProfileId, string ProfileName);

public sealed record SoundPackDependencySummary(
    string PackId,
    IReadOnlyList<SoundPackProfileReference> Profiles,
    IReadOnlyList<SoundPackKeyboardReference> KeyboardAssignments,
    IReadOnlyList<string> MissingReferencedPackIds)
{
    public bool IsRequiredByActiveProfile => Profiles.Any(item => item.IsActive);
    public bool IsRequiredByKeyboardAssignment => KeyboardAssignments.Count > 0;
    public bool IsReferencedBySavedProfiles => Profiles.Any(item => item.IsSaved);
    public bool CanRemoveUserPack => !IsRequiredByActiveProfile && !IsRequiredByKeyboardAssignment && !IsReferencedBySavedProfiles;
}

public sealed class SoundPackDependencyService(Phase2SettingsService settings)
{
    public SoundPackDependencySummary GetPackDependencies(string packId)
    {
        var profileReferences = settings.Profiles
            .Where(profile => ProfileReferencesPack(profile, packId))
            .Select(profile => new SoundPackProfileReference(profile.Id, profile.Name, profile.Id == settings.Settings.ActiveProfileId, true))
            .ToArray();

        var keyboardReferences = settings.Settings.KeyboardProfileAssignments
            .Select(assignment => (assignment.Key, Profile: settings.Profiles.FirstOrDefault(profile => profile.Id == assignment.Value), assignment.Value))
            .Where(item => item.Profile is not null && ProfileReferencesPack(item.Profile, packId))
            .Select(item => new SoundPackKeyboardReference(item.Key, item.Profile!.Id, item.Profile.Name))
            .ToArray();

        return new(packId, profileReferences, keyboardReferences, GetMissingReferencedPackIds());
    }

    public IReadOnlyList<string> GetMissingReferencedPackIds()
    {
        var installed = new HashSet<string>(settings.ValidPackIds, StringComparer.OrdinalIgnoreCase);
        return settings.Profiles
            .SelectMany(GetReferencedPackIds)
            .Where(id => !installed.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<SoundPackProfileReference> GetSoundSetReferences(string packId, string soundSetId)
        => settings.Profiles
            .Where(profile => ProfileReferencesSoundSet(profile, packId, soundSetId))
            .Select(profile => new SoundPackProfileReference(profile.Id, profile.Name, profile.Id == settings.Settings.ActiveProfileId, true))
            .ToArray();

    private static bool ProfileReferencesPack(SoundProfile profile, string packId)
        => GetReferencedPackIds(profile).Any(id => string.Equals(id, packId, StringComparison.OrdinalIgnoreCase));

    private static bool ProfileReferencesSoundSet(SoundProfile profile, string packId, string soundSetId)
    {
        static bool Mapping(KeyGroupMapping mapping, string packId, string soundSetId)
            => string.Equals(mapping.SoundPackId, packId, StringComparison.OrdinalIgnoreCase)
               && (string.Equals(mapping.KeyDownSoundSetId, soundSetId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(mapping.KeyUpSoundSetId, soundSetId, StringComparison.OrdinalIgnoreCase));

        static bool Override(KeyOverride value, string packId, string soundSetId)
            => string.Equals(value.SoundPackId, packId, StringComparison.OrdinalIgnoreCase)
               && (string.Equals(value.KeyDownSoundSetId, soundSetId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value.KeyUpSoundSetId, soundSetId, StringComparison.OrdinalIgnoreCase));

        return profile.Groups.Values.Any(mapping => Mapping(mapping, packId, soundSetId))
               || profile.KeyOverrides.Values.Any(value => Override(value, packId, soundSetId));
    }

    private static IEnumerable<string> GetReferencedPackIds(SoundProfile profile)
    {
        foreach (var mapping in profile.Groups.Values)
            if (!string.IsNullOrWhiteSpace(mapping.SoundPackId))
                yield return mapping.SoundPackId;
        foreach (var value in profile.KeyOverrides.Values)
            if (!string.IsNullOrWhiteSpace(value.SoundPackId))
                yield return value.SoundPackId!;
    }
}
