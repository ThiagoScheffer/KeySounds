using KeySounds.Core.Profiles;
using KeySounds.Infrastructure.Persistence;

namespace KeySounds.Application.Profiles;

public sealed class FallbackProfileMigrationService(JsonProfileRepository repository)
{
    public async Task<bool> MigrateAsync(SoundProfile profile, CancellationToken token = default)
    {
        var changed = false;
        foreach (var mapping in profile.Groups.Values)
        {
            if (string.IsNullOrWhiteSpace(mapping.SoundPackId) || mapping.SoundPackId == "fallback") { mapping.SoundPackId = "builtin"; changed = true; }
            if (string.IsNullOrWhiteSpace(mapping.KeyDownSoundSetId)) { mapping.KeyDownSoundSetId = "click"; changed = true; }
        }
        foreach (var keyOverride in profile.KeyOverrides.Values)
            if (keyOverride.SoundPackId == "fallback") { keyOverride.SoundPackId = "builtin"; changed = true; }
        if (profile.Id == "default" && !HasAudibleRoute(profile))
        {
            profile.PlayOnKeyDown = true;
            profile.PlayOnKeyUp = false;
            foreach (var mapping in profile.Groups.Values.Where(mapping => string.IsNullOrWhiteSpace(mapping.KeyDownSoundSetId))) mapping.KeyDownSoundSetId = "click";
            changed = true;
        }
        if (changed) await repository.SaveAsync(profile, token);
        return changed;
    }

    private static bool HasAudibleRoute(SoundProfile profile)
    {
        if (!profile.Enabled) return false;
        foreach (var mapping in profile.Groups.Values.Where(mapping => mapping.Enabled))
        {
            if (profile.PlayOnKeyDown && !string.IsNullOrWhiteSpace(mapping.KeyDownSoundSetId)) return true;
            if (profile.PlayOnKeyUp && !string.IsNullOrWhiteSpace(mapping.KeyUpSoundSetId)) return true;
        }
        foreach (var keyOverride in profile.KeyOverrides.Values.Where(value => value.Enabled))
        {
            if (profile.PlayOnKeyDown && !string.IsNullOrWhiteSpace(keyOverride.KeyDownSoundSetId)) return true;
            if (profile.PlayOnKeyUp && !string.IsNullOrWhiteSpace(keyOverride.KeyUpSoundSetId)) return true;
        }
        return false;
    }
}
