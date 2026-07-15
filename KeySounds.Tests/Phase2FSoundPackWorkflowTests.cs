using System.Text.Json;
using KeySounds.Application;
using KeySounds.Application.Profiles;
using KeySounds.Application.Routing;
using KeySounds.Application.Settings;
using KeySounds.Application.SoundPacks;
using KeySounds.Audio;
using KeySounds.Core.Audio;
using KeySounds.Core.Input;
using KeySounds.Core.Profiles;
using KeySounds.Core.Settings;
using KeySounds.Core.SoundPacks;
using KeySounds.Infrastructure.Persistence;
using KeySounds.ViewModels;
using NAudio.Wave;
using Xunit;

namespace KeySounds.Tests;

public sealed class Phase2FSoundPackWorkflowTests
{
    [Fact]
    public async Task DependencyGraphReportsActiveSavedKeyboardAndMissingReferences()
    {
        await using var fixture = await Phase2FFixture.CreateAsync();
        fixture.InstallPack("user-pack");
        await fixture.Catalog.RefreshAsync();
        var active = SoundProfileCloner.Clone(Phase2Defaults.CreateProfile(), "active", "Active"); active.Groups[KeySoundGroup.Alpha].SoundPackId = "user-pack";
        var inactive = SoundProfileCloner.Clone(Phase2Defaults.CreateProfile(), "inactive", "Inactive"); inactive.Groups[KeySoundGroup.NumberRow].SoundPackId = "missing-pack";
        await fixture.Repository.SaveAsync(active);
        await fixture.Repository.SaveAsync(inactive);
        await fixture.Settings.ReloadProfilesAsync();
        await fixture.Settings.ActivateAsync("active");
        fixture.Settings.Settings.KeyboardProfileAssignments["kbd-v1-test"] = "active";

        var service = new SoundPackDependencyService(fixture.Settings);
        var summary = service.GetPackDependencies("user-pack");

        Assert.True(summary.IsRequiredByActiveProfile);
        Assert.True(summary.IsRequiredByKeyboardAssignment);
        Assert.Contains(summary.MissingReferencedPackIds, id => id == "missing-pack");
        Assert.False(summary.CanRemoveUserPack);
        Assert.Contains(service.GetSoundSetReferences("user-pack", "click"), item => item.ProfileId == "active");
    }

    [Fact]
    public async Task SoundPacksViewModelImportsThroughPickerAndSelectsImportedPack()
    {
        await using var fixture = await Phase2FFixture.CreateAsync();
        var source = fixture.CreateSourcePack("imported-pack");
        await using var viewModel = fixture.CreateViewModel(new FakeFolderPicker(source), new FakeConflictResolver(SoundPackConflictPolicy.Cancel));

        viewModel.ImportCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Contains(viewModel.Packs, pack => pack.Id == "imported-pack");
        Assert.Equal("imported-pack", viewModel.Selected?.Id);
        Assert.Contains("imported", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SoundPacksViewModelHandlesCancellationAndDuplicateReplacePolicy()
    {
        await using var fixture = await Phase2FFixture.CreateAsync();
        await using var canceled = fixture.CreateViewModel(new FakeFolderPicker(null), new FakeConflictResolver(SoundPackConflictPolicy.Replace));
        canceled.ImportCommand.Execute(null);
        await WaitForIdleAsync(canceled);
        Assert.Contains("canceled", canceled.StatusText, StringComparison.OrdinalIgnoreCase);

        var first = fixture.CreateSourcePack("replace-pack", "First");
        await fixture.Importer.ImportFolderAsync(first, SoundPackConflictPolicy.Cancel);
        var replacement = fixture.CreateSourcePack("replace-pack", "Second");
        var conflicts = new FakeConflictResolver(SoundPackConflictPolicy.Replace);
        await using var replacing = fixture.CreateViewModel(new FakeFolderPicker(replacement), conflicts);
        replacing.ImportCommand.Execute(null);
        await WaitForIdleAsync(replacing);

        Assert.True(conflicts.Called);
        Assert.True(fixture.Catalog.TryGetPack("replace-pack", out var pack));
        Assert.Equal("Second", pack.Manifest!.Name);
    }

    [Fact]
    public async Task RemovalIsBlockedWhenReferencedAndRemovesUnreferencedUserPack()
    {
        await using var fixture = await Phase2FFixture.CreateAsync();
        fixture.InstallPack("blocked-pack");
        fixture.InstallPack("free-pack");
        await fixture.Catalog.RefreshAsync();
        var profile = SoundProfileCloner.Clone(Phase2Defaults.CreateProfile(), "blocking", "Blocking");
        profile.Groups[KeySoundGroup.Alpha].SoundPackId = "blocked-pack";
        await fixture.Repository.SaveAsync(profile);
        await fixture.Settings.ReloadProfilesAsync();
        await using var viewModel = fixture.CreateViewModel(new FakeFolderPicker(null), new FakeConflictResolver(SoundPackConflictPolicy.Cancel));
        await viewModel.RefreshAsync();

        viewModel.Selected = viewModel.Packs.Single(pack => pack.Id == "blocked-pack");
        viewModel.RemoveCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        Assert.Contains("blocked", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.True(fixture.Catalog.TryGetPack("blocked-pack", out _));

        viewModel.Selected = viewModel.Packs.Single(pack => pack.Id == "free-pack");
        viewModel.RemoveCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        Assert.False(fixture.Catalog.TryGetPack("free-pack", out _));
    }

    [Fact]
    public async Task CleanupRemovesOnlyManagedTemporaryArtifacts()
    {
        await using var fixture = await Phase2FFixture.CreateAsync();
        var tempDirectory = Path.Combine(fixture.Paths.SoundPacksDirectory, ".import-old");
        Directory.CreateDirectory(tempDirectory);
        var tempFile = Path.Combine(fixture.Paths.Root, "settings.json.tmp");
        await File.WriteAllTextAsync(tempFile, "partial");

        var result = await new StorageCleanupService(fixture.Paths).CleanupAsync();

        Assert.True(result.RemovedTempDirectories >= 1);
        Assert.True(result.RemovedTempFiles >= 1);
        Assert.False(Directory.Exists(tempDirectory));
        Assert.False(File.Exists(tempFile));
        Assert.True(Directory.Exists(Path.Combine(fixture.Paths.SoundPacksDirectory, "builtin")));
    }

    [Fact]
    public async Task PersistencePrivacyStoresStableKeyboardIdsButNoTransientHandlesOrKeyHistory()
    {
        await using var fixture = await Phase2FFixture.CreateAsync();
        fixture.Settings.Settings.KeyboardProfileAssignments["kbd-v1-privacy"] = "default";
        await new JsonApplicationSettingsRepository(fixture.Paths, new AtomicFileWriter()).SaveAsync(fixture.Settings.Settings);

        var json = await File.ReadAllTextAsync(fixture.Paths.SettingsFile);

        Assert.Contains("kbd-v1-privacy", json);
        Assert.DoesNotContain("DeviceHandle", json);
        Assert.DoesNotContain("ScanCode", json);
        Assert.DoesNotContain("VirtualKey", json);
        Assert.DoesNotContain("RawInput", json);
    }

    private static async Task WaitForIdleAsync(SoundPacksViewModel viewModel)
    {
        for (var i = 0; i < 100 && viewModel.IsBusy; i++) await Task.Delay(20);
        Assert.False(viewModel.IsBusy);
    }

    private sealed class FakeFolderPicker(string? path) : ISoundPackFolderPicker
    {
        public Task<string?> PickImportFolderAsync(CancellationToken token = default) => Task.FromResult(path);
        public Task<IReadOnlyList<string>> PickSampleFilesAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FakeConflictResolver(SoundPackConflictPolicy policy) : ISoundPackConflictResolver
    {
        public bool Called { get; private set; }
        public Task<SoundPackConflictPolicy> ResolveDuplicateAsync(InstalledSoundPack existing, CancellationToken token = default)
        {
            Called = true;
            return Task.FromResult(policy);
        }
    }

    private sealed class TestAudioEngine : IAudioEngine
    {
        public bool IsInitialized => true;
        public int Plays { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play(SoundTrigger trigger) => Plays++;
        public void PlayClick() => Plays++;
        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Phase2FFixture : IAsyncDisposable
    {
        private Phase2FFixture(string root, StoragePaths paths, SoundPackValidationService validator, SoundPackCatalogService catalog, ISoundSampleCache cache,
            SoundPackImportService importer, Phase2SettingsService settings, JsonProfileRepository repository, ProfileManagementService profileManagement, SoundPreviewService preview)
        {
            Root = root; Paths = paths; Validator = validator; Catalog = catalog; Cache = cache; Importer = importer; Settings = settings; Repository = repository; ProfileManagement = profileManagement; Preview = preview;
        }

        public string Root { get; }
        public StoragePaths Paths { get; }
        public SoundPackValidationService Validator { get; }
        public SoundPackCatalogService Catalog { get; }
        public ISoundSampleCache Cache { get; }
        public SoundPackImportService Importer { get; }
        public Phase2SettingsService Settings { get; }
        public JsonProfileRepository Repository { get; }
        public ProfileManagementService ProfileManagement { get; }
        public SoundPreviewService Preview { get; }

        public static async Task<Phase2FFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "KeySounds.Phase2F", Guid.NewGuid().ToString("N"));
            var paths = new StoragePaths(root);
            Directory.CreateDirectory(paths.SoundPacksDirectory);
            WritePack(Path.Combine(paths.SoundPacksDirectory, "builtin"), "builtin", "Built-in");
            var validator = new SoundPackValidationService();
            var catalog = new SoundPackCatalogService(paths, validator);
            await catalog.RefreshAsync();
            var cache = new SoundSampleCache(catalog, new NAudioSoundDecoder());
            var writer = new AtomicFileWriter();
            var repository = new JsonProfileRepository(paths, writer);
            var active = new ActiveProfileService(ProfileCompiler.Compile(Phase2Defaults.CreateProfile()));
            var router = new KeyboardSoundRouter(active, new(), new(), catalog);
            var settings = new Phase2SettingsService(new(paths, writer), repository, active, router, cache, catalog);
            await settings.InitializeAsync();
            var importer = new SoundPackImportService(paths, validator, catalog, () => settings.Profiles, cache);
            var profileManagement = new ProfileManagementService(repository, settings, new ProfileValidationService(catalog), writer);
            var preview = new SoundPreviewService(cache, new TestAudioEngine(), catalog, new());
            return new(root, paths, validator, catalog, cache, importer, settings, repository, profileManagement, preview);
        }

        public SoundPacksViewModel CreateViewModel(ISoundPackFolderPicker picker, ISoundPackConflictResolver conflicts)
            => new(Catalog, Validator, Importer, Settings, Preview, Cache, picker, conflicts, new SoundPackDependencyService(Settings), new StorageCleanupService(Paths),
                new SoundPackDraftService(Paths, Validator, Catalog, Importer, () => Settings.Profiles), ProfileManagement);

        public string CreateSourcePack(string id, string name = "Imported")
        {
            var source = Path.Combine(Root, "source-" + Guid.NewGuid().ToString("N"));
            WritePack(source, id, name);
            return source;
        }

        public void InstallPack(string id, string name = "Installed") => WritePack(Path.Combine(Paths.SoundPacksDirectory, id), id, name);

        public async ValueTask DisposeAsync()
        {
            await Preview.DisposeAsync();
            await Importer.DisposeAsync();
            await ProfileManagement.DisposeAsync();
            await Settings.DisposeAsync();
            await Cache.DisposeAsync();
            await Catalog.DisposeAsync();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }

        private static void WritePack(string root, string id, string name)
        {
            Directory.CreateDirectory(Path.Combine(root, "samples"));
            var file = id == "builtin" ? "key-click.wav" : "click.wav";
            using (var wave = new WaveFileWriter(Path.Combine(root, "samples", file), new WaveFormat(48000, 16, 1))) wave.Write(new byte[4800]);
            var manifest = new SoundPackManifest { Id = id, Name = name, Author = "Tests", Description = "Test pack", SoundSets = [new() { Id = "click", Name = "Click", Files = [$"samples/{file}"] }] };
            File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest));
        }
    }
}
