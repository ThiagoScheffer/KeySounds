using KeySounds.Application.Profiles;
using KeySounds.Application.Routing;
using KeySounds.Core.Profiles;
using KeySounds.Core.Settings;
using KeySounds.Infrastructure.Persistence;

namespace KeySounds.Application.Settings;

public sealed class Phase2SettingsService(JsonApplicationSettingsRepository settingsRepository, JsonProfileRepository profileRepository, ActiveProfileService activeProfiles, KeyboardSoundRouter router)
{
    public ApplicationSettings Settings { get; private set; } = new();
    public IReadOnlyList<SoundProfile> Profiles { get; private set; } = [];
    public string? StatusMessage { get; private set; }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        var loaded = await settingsRepository.LoadAsync(token);
        Settings = loaded.Settings;
        StatusMessage = loaded.RecoveryMessage;
        var profiles = (await profileRepository.ListAsync(token)).ToList();
        if (profiles.All(profile => profile.Id != "default"))
        {
            var fallback = Phase2Defaults.CreateProfile();
            await profileRepository.SaveAsync(fallback, token);
            profiles.Insert(0, fallback);
        }
        Profiles = profiles;
        var selected = profiles.FirstOrDefault(profile => profile.Id == Settings.ActiveProfileId) ?? profiles[0];
        Settings.ActiveProfileId = selected.Id;
        activeProfiles.Activate(ProfileCompiler.Compile(selected));
        router.GlobalVolume = Math.Clamp(Settings.GlobalVolume, 0, 1);
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken token = default) { Settings.SoundsEnabled = enabled; await settingsRepository.SaveAsync(Settings, token); }
    public async Task SetGlobalVolumeAsync(float volume, CancellationToken token = default) { Settings.GlobalVolume = Math.Clamp(volume, 0, 1); router.GlobalVolume = Settings.GlobalVolume; await settingsRepository.SaveAsync(Settings, token); }
    public async Task ActivateAsync(string profileId, CancellationToken token = default)
    {
        var profile = Profiles.FirstOrDefault(item => item.Id == profileId) ?? throw new ArgumentException("Unknown profile.", nameof(profileId));
        activeProfiles.Activate(ProfileCompiler.Compile(profile));
        Settings.ActiveProfileId = profile.Id;
        await settingsRepository.SaveAsync(Settings, token);
    }
}
