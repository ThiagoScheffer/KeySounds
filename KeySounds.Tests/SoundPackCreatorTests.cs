using System.Text.Json;
using KeySounds.Application;
using KeySounds.Application.Profiles;
using KeySounds.Application.SoundPacks;
using KeySounds.Core.Input;
using KeySounds.Core.Profiles;
using KeySounds.Core.SoundPacks;
using KeySounds.Infrastructure.Persistence;
using NAudio.Wave;
using Xunit;

namespace KeySounds.Tests;

public sealed class SoundPackCreatorTests
{
    [Fact]
    public async Task DraftCopiesWavsToSafeRelativeManifestPaths()
    {
        using var fixture = new CreatorFixture();
        var draft = fixture.Drafts.CreateNew();
        draft.PackId = "creator-pack";
        draft.Name = "Creator Pack";
        KeepOnlyFirstSet(draft, "alpha-down");
        var source = fixture.WriteSourceWave("My Click!.wav");

        Assert.Null(await fixture.Drafts.AddSampleAsync(draft, draft.SoundSets[0], source));
        var validation = await fixture.Drafts.ValidateAsync(draft);
        var manifest = validation.PackValidation!.Manifest!;

        Assert.True(validation.IsValid);
        var relative = Assert.Single(manifest.SoundSets[0].Files);
        Assert.StartsWith("samples/alpha-down/my-click-", relative);
        Assert.EndsWith(".wav", relative);
        Assert.False(Path.IsPathRooted(relative));
        Assert.DoesNotContain("..", relative);
    }

    [Fact]
    public async Task DraftDuplicateFileNamesAreRenamedDeterministically()
    {
        using var fixture = new CreatorFixture();
        var draft = fixture.Drafts.CreateNew();
        KeepOnlyFirstSet(draft, "alpha-down");
        var first = fixture.WriteSourceWave(Path.Combine("one", "click.wav"));
        var second = fixture.WriteSourceWave(Path.Combine("two", "click.wav"));

        Assert.Null(await fixture.Drafts.AddSampleAsync(draft, draft.SoundSets[0], first));
        Assert.Null(await fixture.Drafts.AddSampleAsync(draft, draft.SoundSets[0], second));
        var validation = await fixture.Drafts.ValidateAsync(draft);

        Assert.True(validation.IsValid);
        Assert.Equal(["samples/alpha-down/click-01.wav", "samples/alpha-down/click-02.wav"], validation.PackValidation!.Manifest!.SoundSets[0].Files);
    }

    [Fact]
    public async Task DraftRejectsUnsupportedDroppedFilesAndEmptySetsBlockSave()
    {
        using var fixture = new CreatorFixture();
        var draft = fixture.Drafts.CreateNew();
        KeepOnlyFirstSet(draft, "alpha-down");
        var bad = fixture.WriteSourceText("not-a-wave.mp3");

        var issue = await fixture.Drafts.AddSampleAsync(draft, draft.SoundSets[0], bad);
        var save = await fixture.Drafts.SaveAsync(draft, false);

        Assert.NotNull(issue);
        Assert.Equal("UnsupportedExtension", issue.Code);
        Assert.False(save.Success);
        Assert.Equal("ValidationFailed", save.ErrorCode);
        Assert.Contains(save.Validation!.Issues, item => item.Code == "EmptySoundSet");
    }

    [Fact]
    public async Task ValidDraftInstallsAsUserPack()
    {
        using var fixture = new CreatorFixture();
        await fixture.Catalog.RefreshAsync();
        var draft = fixture.Drafts.CreateNew();
        draft.PackId = "creator-pack";
        draft.Name = "Creator Pack";
        KeepOnlyFirstSet(draft, "alpha-down");
        Assert.Null(await fixture.Drafts.AddSampleAsync(draft, draft.SoundSets[0], fixture.WriteSourceWave("click.wav")));

        var result = await fixture.Drafts.SaveAsync(draft, false);

        Assert.True(result.Success);
        Assert.True(fixture.Catalog.TryGetPack("creator-pack", out var installed));
        Assert.False(installed.IsBuiltIn);
        Assert.True(File.Exists(Path.Combine(installed.RootDirectory, "manifest.json")));
    }

    [Fact]
    public async Task EditingExistingPackReplacesOnlyAfterValidation()
    {
        using var fixture = new CreatorFixture();
        await fixture.InstallPackAsync("editable-pack", "Old Name");
        Assert.True(fixture.Catalog.TryGetPack("editable-pack", out var installed));
        var draft = await fixture.Drafts.CreateFromInstalledAsync(installed);
        Assert.NotNull(draft);
        draft!.Name = "New Name";

        var result = await fixture.Drafts.SaveAsync(draft, false);
        Assert.True(result.Success);
        Assert.True(fixture.Catalog.TryGetPack("editable-pack", out var replaced));
        Assert.Equal("New Name", replaced.Manifest!.Name);

        var invalid = await fixture.Drafts.CreateFromInstalledAsync(replaced);
        invalid!.SoundSets[0].Files.Clear();
        var failed = await fixture.Drafts.SaveAsync(invalid, false);
        Assert.False(failed.Success);
        Assert.True(fixture.Catalog.TryGetPack("editable-pack", out var stillInstalled));
        Assert.Equal("New Name", stillInstalled.Manifest!.Name);
    }

    [Fact]
    public async Task BuiltInPackCannotBeEdited()
    {
        using var fixture = new CreatorFixture();
        var builtIn = new InstalledSoundPack(new SoundPackManifest { Id = "builtin", Name = "Built-in", SoundSets = [new() { Id = "click", Name = "Click", Files = ["samples/click.wav"] }] },
            Path.Combine(fixture.Paths.SoundPacksDirectory, "builtin"), true, new(true, [], 0, 1));

        Assert.Null(await fixture.Drafts.CreateFromInstalledAsync(builtIn));
    }

    [Fact]
    public async Task RemovingReferencedSoundSetRequiresConfirmation()
    {
        using var fixture = new CreatorFixture();
        await fixture.InstallPackAsync("referenced-pack", "Referenced", ["alpha-down", "space-down"]);
        fixture.Profiles.Add(new SoundProfile { Id = "uses-space", Name = "Uses Space", Groups = { [KeySoundGroup.Spacebar] = new KeyGroupMapping { SoundPackId = "referenced-pack", KeyDownSoundSetId = "space-down" } } });
        Assert.True(fixture.Catalog.TryGetPack("referenced-pack", out var installed));
        var draft = await fixture.Drafts.CreateFromInstalledAsync(installed);
        draft!.SoundSets.RemoveAll(set => set.Id == "space-down");

        var first = await fixture.Drafts.SaveAsync(draft, false);
        var second = await fixture.Drafts.SaveAsync(draft, true);

        Assert.False(first.Success);
        Assert.Equal("ReferenceConfirmationRequired", first.ErrorCode);
        Assert.True(second.Success);
    }

    [Fact]
    public async Task GeneratedProfileMapsCommonKeyGroupsAndKeyUp()
    {
        using var fixture = new CreatorFixture();
        var profile = await fixture.Drafts.CreateProfileForPackAsync("creator-pack", "Creator Profile", ["alpha-down", "space-down", "enter-down", "backspace-down", "modifier-down", "arrow-down", "key-up"]);

        Assert.Equal("creator-pack", profile.Groups[KeySoundGroup.Alpha].SoundPackId);
        Assert.Equal("alpha-down", profile.Groups[KeySoundGroup.Alpha].KeyDownSoundSetId);
        Assert.Equal("space-down", profile.Groups[KeySoundGroup.Spacebar].KeyDownSoundSetId);
        Assert.Equal("enter-down", profile.Groups[KeySoundGroup.Enter].KeyDownSoundSetId);
        Assert.Equal("backspace-down", profile.Groups[KeySoundGroup.Backspace].KeyDownSoundSetId);
        Assert.True(profile.PlayOnKeyUp);
        Assert.All(profile.Groups.Values, mapping => Assert.Equal("key-up", mapping.KeyUpSoundSetId));
    }

    [Fact]
    public async Task CleanupRemovesAbandonedDraftFolders()
    {
        using var fixture = new CreatorFixture();
        _ = fixture.Drafts.CreateNew();

        var cleanup = new StorageCleanupService(fixture.Paths);
        var result = await cleanup.CleanupAsync();

        Assert.True(result.RemovedTempDirectories >= 1);
        Assert.Empty(Directory.EnumerateDirectories(fixture.Paths.SoundPacksDirectory, ".draft-*"));
    }

    private static void KeepOnlyFirstSet(SoundPackDraft draft, string id)
    {
        draft.SoundSets.RemoveRange(1, draft.SoundSets.Count - 1);
        draft.SoundSets[0].Id = id;
        draft.SoundSets[0].Name = id;
    }

    private sealed class CreatorFixture : IDisposable
    {
        public CreatorFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "KeySounds.CreatorTests", Guid.NewGuid().ToString("N"));
            Source = Path.Combine(Root, "source");
            Directory.CreateDirectory(Source);
            Paths = new StoragePaths(Path.Combine(Root, "managed"));
            Directory.CreateDirectory(Paths.SoundPacksDirectory);
            Validator = new();
            Catalog = new(Paths, Validator);
            Importer = new(Paths, Validator, Catalog, () => Profiles);
            Drafts = new(Paths, Validator, Catalog, Importer, () => Profiles);
        }

        public string Root { get; }
        public string Source { get; }
        public StoragePaths Paths { get; }
        public SoundPackValidationService Validator { get; }
        public SoundPackCatalogService Catalog { get; }
        public SoundPackImportService Importer { get; }
        public SoundPackDraftService Drafts { get; }
        public List<SoundProfile> Profiles { get; } = [];

        public string WriteSourceWave(string relative)
        {
            var path = Path.Combine(Source, relative);
            WriteWave(path);
            return path;
        }

        public string WriteSourceText(string relative)
        {
            var path = Path.Combine(Source, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "not audio");
            return path;
        }

        public async Task InstallPackAsync(string id, string name, IReadOnlyList<string>? sets = null)
        {
            var draft = Drafts.CreateNew();
            draft.PackId = id;
            draft.Name = name;
            draft.SoundSets.Clear();
            foreach (var set in sets ?? ["alpha-down"])
            {
                draft.SoundSets.Add(new() { Id = set, Name = set });
                Assert.Null(await Drafts.AddSampleAsync(draft, draft.SoundSets[^1], WriteSourceWave($"{set}.wav")));
            }
            var result = await Drafts.SaveAsync(draft, false);
            Assert.True(result.Success);
        }

        public void Dispose()
        {
            Catalog.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Importer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }

        private static void WriteWave(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var writer = new WaveFileWriter(path, new WaveFormat(48000, 16, 1));
            writer.Write(new byte[4800]);
        }
    }
}
