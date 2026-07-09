using System.Text.Json;
using KeySounds.Application;
using KeySounds.Application.Profiles;
using KeySounds.Application.Routing;
using KeySounds.Application.Settings;
using KeySounds.Application.SoundPacks;
using KeySounds.Core.Audio;
using KeySounds.Core.Input;
using KeySounds.Core.Profiles;
using KeySounds.Core.SoundPacks;
using KeySounds.Infrastructure.Persistence;
using KeySounds.ViewModels;
using NAudio.Wave;
using Xunit;

namespace KeySounds.Tests;

public sealed class Phase2CProfileTests
{
    [Fact]
    public async Task EditableSessionIsDeeplyIsolatedDirtyAndRevertible()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var original = Phase2Defaults.CreateProfile(); var session = new EditableProfileSession(original, fixture.Validator);
        session.WorkingCopy.Groups[KeySoundGroup.Alpha].Volume = .25f;
        Assert.True(session.HasUnsavedChanges); Assert.Equal(1f, original.Groups[KeySoundGroup.Alpha].Volume);
        session.AddOrUpdateOverride(new(0x1E, false), new() { Enabled = false }); Assert.True(session.HasUnsavedChanges);
        session.Revert(); Assert.False(session.HasUnsavedChanges); Assert.Empty(session.WorkingCopy.KeyOverrides); Assert.Equal(1f, session.WorkingCopy.Groups[KeySoundGroup.Alpha].Volume);
    }

    [Fact]
    public async Task CreateDuplicateRenameAndDeletePreserveDeepCopiesAndStableIds()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var created = await fixture.Management.CreateAsync("Gaming"); Assert.True(created.Success);
        created.Profile!.KeyOverrides[new(0x1E, false)] = new() { Enabled = false }; await fixture.Repository.SaveAsync(created.Profile); await fixture.Settings.ReloadProfilesAsync();
        var duplicate = await fixture.Management.DuplicateAsync(created.Profile); Assert.True(duplicate.Success); Assert.NotEqual(created.Profile.Id, duplicate.Profile!.Id);
        duplicate.Profile.KeyOverrides[new(0x1E, false)].Enabled = true; Assert.False(created.Profile.KeyOverrides[new(0x1E, false)].Enabled);
        var oldId = duplicate.Profile.Id; var renamed = await fixture.Management.RenameAsync(duplicate.Profile, "Renamed"); Assert.True(renamed.Success); Assert.Equal(oldId, renamed.Profile!.Id);
        Assert.True((await fixture.Management.DeleteAsync(renamed.Profile)).Success);
    }

    [Fact]
    public async Task FallbackAndActiveProfilesCannotBeDeleted()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var fallback = fixture.Settings.Profiles.Single(profile => profile.Id == "default");
        Assert.Equal("FallbackProtected", (await fixture.Management.DeleteAsync(fallback)).ErrorCode);
        var created = (await fixture.Management.CreateAsync("Active")).Profile!; await fixture.Settings.ActivateAsync(created.Id);
        Assert.Equal("ActiveProfile", (await fixture.Management.DeleteAsync(created)).ErrorCode);
    }

    [Fact]
    public async Task InvalidSaveRetainsDirtyStateAndValidSaveClearsIt()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var session = new EditableProfileSession(Phase2Defaults.CreateProfile(), fixture.Validator); session.WorkingCopy.MasterVolume = 4;
        Assert.False((await fixture.Management.SaveAsync(session, false)).Success); Assert.True(session.HasUnsavedChanges);
        session.WorkingCopy.MasterVolume = .5f; Assert.True((await fixture.Management.SaveAsync(session, false)).Success); Assert.False(session.HasUnsavedChanges);
    }

    [Fact]
    public async Task ValidationIdentifiesMissingPackSetAndFieldPaths()
    {
        await using var fixture = await ProfileFixture.CreateAsync(); var profile = Phase2Defaults.CreateProfile();
        profile.Groups[KeySoundGroup.Alpha].SoundPackId = "missing"; profile.RepeatMinimumIntervalMilliseconds = 2;
        var issues = fixture.Validator.Validate(profile);
        Assert.Contains(issues, issue => issue.Code == "MissingPack" && issue.Path.Contains("Alpha")); Assert.Contains(issues, issue => issue.Code == "InvalidRepeatInterval" && issue.Path == "repeatMinimumIntervalMilliseconds");
    }

    [Fact]
    public async Task ValidationRejectsProfileWithNoAudibleRoutes()
    {
        await using var fixture = await ProfileFixture.CreateAsync(); var profile = Phase2Defaults.CreateProfile();
        profile.PlayOnKeyDown = false; profile.PlayOnKeyUp = true;
        foreach (var mapping in profile.Groups.Values) mapping.KeyUpSoundSetId = null;

        var issues = fixture.Validator.Validate(profile);

        Assert.Contains(issues, issue => issue.Code == "NoAudibleMappings");
    }

    [Fact]
    public async Task ImportAsCopyAndExportRoundTripWithoutRuntimeState()
    {
        await using var fixture = await ProfileFixture.CreateAsync(); var fallback = fixture.Settings.Profiles.Single(profile => profile.Id == "default");
        var exported = Path.Combine(fixture.Root, "exported.json"); Assert.True((await fixture.Management.ExportAsync(fallback, exported)).Success);
        var json = await File.ReadAllTextAsync(exported); Assert.DoesNotContain("DeviceHandle", json); Assert.DoesNotContain("RootDirectory", json);
        var imported = await fixture.Management.ImportAsync(exported, ProfileImportConflictPolicy.ImportAsCopy); Assert.True(imported.Success); Assert.NotEqual("default", imported.Profile!.Id);
        Assert.Equal(1, imported.Profile.SchemaVersion);
    }

    [Fact]
    public async Task OneKeyCaptureStoresOnlyPhysicalIdentityAndIgnoresFollowingEvents()
    {
        var input = new CaptureInput(); var audio = new CaptureAudio(); await using var coordinator = new KeyboardSoundCoordinator(input, audio); await coordinator.StartAsync();
        var capture = coordinator.CaptureNextKeyAsync(); input.Send(0x1E, false); input.Send(0x30, true);
        Assert.Equal(new KeyIdentifier(0x1E, false), await capture); Assert.Equal("A", KeyLabelFormatter.Format(new(0x1E, false))); Assert.Contains("0x7F", KeyLabelFormatter.Format(new(0x7F, true)));
    }

    private sealed class CaptureInput : IKeyboardInputSource
    {
        public event EventHandler<KeyboardInputEvent>? InputReceived; public event EventHandler<InputFailureEventArgs>? Failure { add { } remove { } }
        public void Send(ushort scan, bool extended) => InputReceived?.Invoke(this, new(scan, 0, KeyboardAction.KeyDown, 1, extended, 0));
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class CaptureAudio : IAudioEngine
    {
        public bool IsInitialized { get; private set; } public Task InitializeAsync(CancellationToken cancellationToken = default) { IsInitialized = true; return Task.CompletedTask; }
        public void Play(SoundTrigger trigger) { } public void PlayClick() { } public Task ShutdownAsync(CancellationToken cancellationToken = default) { IsInitialized = false; return Task.CompletedTask; } public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ProfileFixture : IAsyncDisposable
    {
        private ProfileFixture(string root, StoragePaths paths, JsonProfileRepository repository, SoundPackCatalogService catalog, Phase2SettingsService settings, ProfileValidationService validator, ProfileManagementService management)
        { Root = root; Paths = paths; Repository = repository; Catalog = catalog; Settings = settings; Validator = validator; Management = management; }
        public string Root { get; } public StoragePaths Paths { get; } public JsonProfileRepository Repository { get; } public SoundPackCatalogService Catalog { get; }
        public Phase2SettingsService Settings { get; } public ProfileValidationService Validator { get; } public ProfileManagementService Management { get; }
        public static async Task<ProfileFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "KeySounds.ProfileTests", Guid.NewGuid().ToString("N")); var paths = new StoragePaths(root); Directory.CreateDirectory(paths.SoundPacksDirectory);
            var packRoot = Path.Combine(paths.SoundPacksDirectory, "builtin"); Directory.CreateDirectory(Path.Combine(packRoot, "samples"));
            using (var wave = new WaveFileWriter(Path.Combine(packRoot, "samples", "key-click.wav"), new WaveFormat(48000, 16, 1))) wave.Write(new byte[4800]);
            var manifest = new SoundPackManifest { Id = "builtin", Name = "Built-in", SoundSets = [new() { Id = "click", Name = "Click", Files = ["samples/key-click.wav"] }] };
            await File.WriteAllTextAsync(Path.Combine(packRoot, "manifest.json"), JsonSerializer.Serialize(manifest));
            var catalog = new SoundPackCatalogService(paths, new()); await catalog.RefreshAsync();
            var writer = new AtomicFileWriter(); var repository = new JsonProfileRepository(paths, writer); var active = new ActiveProfileService(ProfileCompiler.Compile(Phase2Defaults.CreateProfile()));
            var router = new KeyboardSoundRouter(active, new(), new(), catalog); var settings = new Phase2SettingsService(new(paths, writer), repository, active, router); await settings.InitializeAsync();
            var validator = new ProfileValidationService(catalog); var management = new ProfileManagementService(repository, settings, validator, writer);
            return new(root, paths, repository, catalog, settings, validator, management);
        }
        public async ValueTask DisposeAsync() { await Management.DisposeAsync(); await Settings.DisposeAsync(); await Catalog.DisposeAsync(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
