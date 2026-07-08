using KeySounds.Application.Profiles;
using KeySounds.Application.Routing;
using KeySounds.Core.Profiles;
using KeySounds.Core.Settings;
using KeySounds.Infrastructure.Persistence;
using KeySounds.Application.SoundPacks;
using KeySounds.Audio;
using KeySounds.Core.SoundPacks;
using KeySounds.Core.Audio;

namespace KeySounds.Application.Settings;

public sealed class Phase2SettingsService(JsonApplicationSettingsRepository settingsRepository, JsonProfileRepository profileRepository, ActiveProfileService activeProfiles, KeyboardSoundRouter router,
    ISoundSampleCache? sampleCache = null, SoundPackCatalogService? catalog = null, ISelectableAudioEngine? selectableAudio = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _activationGate = new(1, 1);
    private bool _shutdown;
    public ApplicationSettings Settings { get; private set; } = new();
    public IReadOnlyList<SoundProfile> Profiles { get; private set; } = [];
    public string? StatusMessage { get; private set; }
    public int ValidPackCount => catalog?.Packs.Count(pack => pack.Validation.IsValid) ?? 1;
    public int InvalidPackCount => catalog?.Packs.Count(pack => !pack.Validation.IsValid) ?? 0;
    public SampleCacheSnapshot CacheSnapshot => sampleCache?.GetSnapshot() ?? new(1, 0, 1, false, 0);

    public async Task InitializeAsync(CancellationToken token = default)
    {
        var loaded = await settingsRepository.LoadAsync(token);
        if (catalog is not null) await catalog.InitializeAsync(token);
        Settings = loaded.Settings;
        if (ApplicationSettingsMigration.Apply(Settings)) await settingsRepository.SaveAsync(Settings, token);
        selectableAudio?.ConfigureStartup(Settings.AudioOutputSelection, Settings.AudioLatencyMilliseconds);
        StatusMessage = loaded.RecoveryMessage;
        await ReloadProfilesAsync(token);
        var profiles = Profiles.ToList();
        var selected = profiles.FirstOrDefault(profile => profile.Id == Settings.ActiveProfileId) ?? profiles[0];
        if (sampleCache is not null)
        {
            var preparation = await sampleCache.PrepareAsync(GetRequiredSets(ProfileCompiler.Compile(selected)), token);
            if (!preparation.Success && selected.Id != "default")
            {
                selected = profiles.First(profile => profile.Id == "default");
                preparation = await sampleCache.PrepareAsync(GetRequiredSets(ProfileCompiler.Compile(selected)), token);
            }
            if (!preparation.Success) throw new InvalidOperationException(preparation.Message ?? "Required profile samples could not be prepared.");
        }
        Settings.ActiveProfileId = selected.Id;
        activeProfiles.Activate(ProfileCompiler.Compile(selected));
        router.GlobalVolume = Math.Clamp(Settings.GlobalVolume, 0, 1);
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken token = default) { Settings.SoundsEnabled = enabled; await settingsRepository.SaveAsync(Settings, token); }
    public async Task SetGlobalVolumeAsync(float volume, CancellationToken token = default) { Settings.GlobalVolume = Math.Clamp(volume, 0, 1); router.GlobalVolume = Settings.GlobalVolume; await settingsRepository.SaveAsync(Settings, token); }
    public async Task SaveAudioConfigurationAsync(AudioOutputSelection selection, int latency, CancellationToken token = default)
    {
        var oldSelection = Settings.AudioOutputSelection; var oldLatency = Settings.AudioLatencyMilliseconds; var oldLegacyId = Settings.AudioOutputDeviceId;
        Settings.AudioOutputSelection = selection; Settings.AudioLatencyMilliseconds = latency; Settings.AudioOutputDeviceId = selection.Kind == AudioOutputSelectionKind.SpecificDevice ? selection.DeviceId : null;
        try { await settingsRepository.SaveAsync(Settings, token); }
        catch { Settings.AudioOutputSelection = oldSelection; Settings.AudioLatencyMilliseconds = oldLatency; Settings.AudioOutputDeviceId = oldLegacyId; throw; }
    }
    public async Task ActivateAsync(string profileId, CancellationToken token = default)
    {
        await _activationGate.WaitAsync(token);
        try
        {
            if (_shutdown) throw new ObjectDisposedException(nameof(Phase2SettingsService));
            var profile = Profiles.FirstOrDefault(item => item.Id == profileId) ?? throw new ArgumentException("Unknown profile.", nameof(profileId));
            var compiled = ProfileCompiler.Compile(profile);
            var required = GetRequiredSets(compiled);
            if (sampleCache is not null)
            {
                var preparation = await sampleCache.PrepareAsync(required, token);
                if (!preparation.Success) throw new InvalidOperationException(preparation.Message ?? "Sample preparation failed.");
            }
            var previousProfile = activeProfiles.Current;
            var previousId = Settings.ActiveProfileId;
            activeProfiles.Activate(compiled);
            Settings.ActiveProfileId = profile.Id;
            try { await settingsRepository.SaveAsync(Settings, token); }
            catch { activeProfiles.Activate(previousProfile); Settings.ActiveProfileId = previousId; throw; }
            if (sampleCache is not null) await sampleCache.ReleaseUnusedAsync(required, token);
        }
        finally { _activationGate.Release(); }
    }

    public Task RefreshPacksAsync(CancellationToken token = default) => catalog?.RefreshAsync(token) ?? Task.CompletedTask;
    public async Task ReloadProfilesAsync(CancellationToken token = default)
    {
        var profiles = (await profileRepository.ListAsync(token)).ToList();
        var migration = new FallbackProfileMigrationService(profileRepository);
        foreach (var profile in profiles) await migration.MigrateAsync(profile, token);
        if (profiles.All(profile => profile.Id != "default"))
        {
            var fallback = Phase2Defaults.CreateProfile(); await profileRepository.SaveAsync(fallback, token); profiles.Insert(0, fallback);
        }
        Profiles = profiles.OrderBy(profile => profile.Id == "default" ? 0 : 1).ThenBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (profileRepository.LastLoadIssues.Count > 0) StatusMessage = string.Join(Environment.NewLine, profileRepository.LastLoadIssues);
    }
    private static IReadOnlyCollection<SoundSetReference> GetRequiredSets(CompiledSoundProfile profile)
    {
        var required = profile.Groups.Values.Where(item => item.Enabled)
            .SelectMany(item => new[] { item.DownSetId, item.UpSetId }.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => new SoundSetReference(item.PackId, id!))).ToList();
        var resolver = new KeyGroupResolver();
        foreach (var pair in profile.Overrides.Where(item => item.Value.Enabled))
        {
            profile.Groups.TryGetValue(resolver.Resolve(pair.Key), out var group);
            var pack = pair.Value.PackId ?? group?.PackId;
            if (pack is null) continue;
            foreach (var set in new[] { pair.Value.DownSetId, pair.Value.UpSetId }.Where(id => !string.IsNullOrWhiteSpace(id))) required.Add(new(pack, set!));
        }
        return required.Distinct().ToArray();
    }
    public async ValueTask DisposeAsync() { _shutdown = true; await _activationGate.WaitAsync(); _activationGate.Release(); _activationGate.Dispose(); }
}
