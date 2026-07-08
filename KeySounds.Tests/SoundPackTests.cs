using System.Text.Json;
using KeySounds.Application;
using KeySounds.Application.SoundPacks;
using KeySounds.Audio;
using KeySounds.Core.Audio;
using KeySounds.Core.Profiles;
using KeySounds.Core.SoundPacks;
using KeySounds.Infrastructure.Persistence;
using NAudio.Wave;
using Xunit;

namespace KeySounds.Tests;

public sealed class SoundPackTests
{
    [Fact] public async Task MissingManifestIsRejected() { using var fixture = new PackFixture(false); var result = await fixture.Validator.ValidateAsync(fixture.Source); AssertIssue(result, "ManifestMissing"); }
    [Fact] public async Task MalformedManifestIsRejected() { using var fixture = new PackFixture(false); await File.WriteAllTextAsync(Path.Combine(fixture.Source, "manifest.json"), "{"); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), "ManifestMalformed"); }
    [Fact] public async Task UnsupportedSchemaIsRejected() { using var fixture = new PackFixture(); fixture.WriteManifest(schema: 2); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), "UnsupportedSchema"); }
    [Fact] public async Task InvalidPackIdIsRejected() { using var fixture = new PackFixture(); fixture.WriteManifest(id: "BAD ID"); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), "InvalidPackId"); }
    [Fact] public async Task MissingPackIdIsRejected() { using var fixture = new PackFixture(); fixture.WriteManifest(id: ""); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), "InvalidPackId"); }
    [Fact] public async Task DuplicateSetsAreRejected() { using var fixture = new PackFixture(); fixture.WriteManifest(duplicateSet: true); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), "InvalidSoundSetId"); }
    [Fact] public async Task EmptySetIsRejected() { using var fixture = new PackFixture(); fixture.WriteManifest(emptySet: true); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), "EmptySoundSet"); }
    [Fact] public async Task DuplicateFilesAreRejected() { using var fixture = new PackFixture(); fixture.WriteManifest(duplicateFile: true); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), "DuplicateFile"); }
    [Theory]
    [InlineData("../escape.wav", "PathTraversal")]
    [InlineData("C:/escape.wav", "InvalidPath")]
    [InlineData("samples/a.mp3", "UnsupportedExtension")]
    [InlineData("\\\\server\\share\\a.wav", "InvalidPath")]
    [InlineData("samples\\..\\escape.wav", "PathTraversal")]
    public async Task UnsafePathsAreRejected(string path, string code) { using var fixture = new PackFixture(false); fixture.WriteManifest(file: path); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), code); }
    [Fact] public async Task MissingWaveIsRejected() { using var fixture = new PackFixture(false); fixture.WriteManifest(); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), "SampleMissing"); }
    [Fact] public async Task CorruptWaveIsRejected() { using var fixture = new PackFixture(false); fixture.WriteManifest(); Directory.CreateDirectory(Path.Combine(fixture.Source, "samples")); await File.WriteAllTextAsync(Path.Combine(fixture.Source, "samples", "click.wav"), "bad"); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), "InvalidWave"); }
    [Fact] public async Task DirectoryAsWaveIsRejected() { using var fixture = new PackFixture(false); fixture.WriteManifest(); Directory.CreateDirectory(Path.Combine(fixture.Source, "samples", "click.wav")); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), "DirectoryReferenced"); }
    [Fact] public async Task ZeroLengthWaveIsRejected() { using var fixture = new PackFixture(false); fixture.WriteManifest(); Directory.CreateDirectory(Path.Combine(fixture.Source, "samples")); File.Create(Path.Combine(fixture.Source, "samples", "click.wav")).Dispose(); AssertIssue(await fixture.Validator.ValidateAsync(fixture.Source), "InvalidFileSize"); }
    [Fact] public async Task ValidPackReportsDecodedSize() { using var fixture = new PackFixture(); var result = await fixture.Validator.ValidateAsync(fixture.Source); Assert.True(result.IsValid); Assert.Equal(1, result.SampleCount); Assert.True(result.EstimatedDecodedBytes > 0); }

    [Fact]
    public async Task ImportCopiesOnlyDeclaredFilesAndHonorsConflictPolicy()
    {
        using var fixture = new PackFixture(); await File.WriteAllTextAsync(Path.Combine(fixture.Source, "ignored.exe"), "nope");
        var catalog = fixture.CreateCatalog(); await catalog.RefreshAsync();
        var importer = new SoundPackImportService(fixture.Paths, fixture.Validator, catalog, () => []);
        Assert.True((await importer.ImportFolderAsync(fixture.Source, SoundPackConflictPolicy.Cancel)).Success);
        var installed = Path.Combine(fixture.Paths.SoundPacksDirectory, "test-pack");
        Assert.True(File.Exists(Path.Combine(installed, "samples", "click.wav"))); Assert.False(File.Exists(Path.Combine(installed, "ignored.exe")));
        Assert.Equal("AlreadyInstalled", (await importer.ImportFolderAsync(fixture.Source, SoundPackConflictPolicy.Cancel)).ErrorCode);
        Assert.True((await importer.ImportFolderAsync(fixture.Source, SoundPackConflictPolicy.Replace)).Success);
        await catalog.DisposeAsync();
    }

    [Fact]
    public async Task CacheDecodesOnceAndReleasesLookupWithoutInvalidatingSample()
    {
        using var fixture = new PackFixture(); fixture.InstallSource(); var catalog = fixture.CreateCatalog(); await catalog.RefreshAsync();
        var decoder = new CountingDecoder(); await using var cache = new SoundSampleCache(catalog, decoder, 1024 * 1024);
        SoundSetReference[] required = [new("test-pack", "click")];
        Assert.True((await cache.PrepareAsync(required)).Success); Assert.True((await cache.PrepareAsync(required)).Success); Assert.Equal(1, decoder.Count);
        var id = new SoundId("test-pack", "click", "samples/click.wav"); Assert.True(cache.TryGet(id, out var held));
        await cache.ReleaseUnusedAsync([]); Assert.False(cache.TryGet(id, out _)); Assert.NotEmpty(held.Samples);
        await catalog.DisposeAsync();
    }

    [Fact]
    public async Task CacheBudgetFailureDoesNotPublishPartialSnapshot()
    {
        using var fixture = new PackFixture(); fixture.InstallSource(); var catalog = fixture.CreateCatalog(); await catalog.RefreshAsync();
        await using var cache = new SoundSampleCache(catalog, new CountingDecoder(10_000), 100);
        var result = await cache.PrepareAsync([new("test-pack", "click")]); Assert.False(result.Success); Assert.Equal(0, cache.GetSnapshot().LoadedSampleCount);
        await catalog.DisposeAsync();
    }

    [Fact]
    public async Task RemovalBlocksReferencedAndBuiltInPacks()
    {
        using var fixture = new PackFixture(); fixture.InstallSource(); var catalog = fixture.CreateCatalog(); await catalog.RefreshAsync();
        var profile = Phase2Defaults.CreateProfile(); profile.Groups.Values.First().SoundPackId = "test-pack";
        var importer = new SoundPackImportService(fixture.Paths, fixture.Validator, catalog, () => [profile]);
        Assert.Equal("PackReferenced", (await importer.RemoveAsync("test-pack")).ErrorCode);
        await catalog.DisposeAsync();
    }

    [Fact]
    public async Task UnreferencedPackRemovalRefreshesCatalogAndCache()
    {
        using var fixture = new PackFixture(); fixture.InstallSource(); var catalog = fixture.CreateCatalog(); await catalog.RefreshAsync();
        await using var cache = new SoundSampleCache(catalog, new CountingDecoder()); await cache.PrepareAsync([new("test-pack", "click")]);
        var importer = new SoundPackImportService(fixture.Paths, fixture.Validator, catalog, () => [], cache);
        Assert.True((await importer.RemoveAsync("test-pack")).Success); Assert.False(catalog.TryGetPack("test-pack", out _)); Assert.Equal(0, cache.GetSnapshot().LoadedSampleCount);
        await catalog.DisposeAsync();
    }

    [Fact]
    public async Task PreviewPreparesOnceUsesMixerAndRejectsAfterShutdown()
    {
        using var fixture = new PackFixture(); fixture.InstallSource(); var catalog = fixture.CreateCatalog(); await catalog.RefreshAsync();
        var decoder = new CountingDecoder(); await using var cache = new SoundSampleCache(catalog, decoder); var audio = new PreviewAudio();
        await using var preview = new SoundPreviewService(cache, audio, catalog, new());
        await preview.PreviewSoundSetAsync("test-pack", "click"); await preview.PreviewSoundSetAsync("test-pack", "click");
        Assert.Equal(1, decoder.Count); Assert.Equal(2, audio.Plays);
        preview.Shutdown(); await Assert.ThrowsAsync<ObjectDisposedException>(() => preview.PreviewSoundSetAsync("test-pack", "click"));
        await catalog.DisposeAsync();
    }

    private static void AssertIssue(SoundPackValidationResult result, string code) { Assert.False(result.IsValid); Assert.Contains(result.Issues, issue => issue.Code == code); }

    private sealed class CountingDecoder(int sampleCount = 100) : ISoundDecoder
    {
        public int Count;
        public Task<CachedSound> DecodeAsync(SoundId id, string path, CancellationToken token) { Count++; return Task.FromResult(new CachedSound(id, new float[sampleCount])); }
    }
    private sealed class PreviewAudio : IAudioEngine
    {
        public bool IsInitialized => true; public int Plays;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play(SoundTrigger trigger) => Plays++;
        public void PlayClick() => Plays++;
        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PackFixture : IDisposable
    {
        public PackFixture(bool createValid = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "KeySounds.PackTests", Guid.NewGuid().ToString("N")); Source = Path.Combine(Root, "source"); Directory.CreateDirectory(Source);
            Paths = new StoragePaths(Path.Combine(Root, "managed")); Directory.CreateDirectory(Paths.SoundPacksDirectory); Validator = new();
            if (createValid) { WriteWave(Path.Combine(Source, "samples", "click.wav")); WriteManifest(); }
        }
        public string Root { get; } public string Source { get; } public StoragePaths Paths { get; } public SoundPackValidationService Validator { get; }
        public void WriteManifest(int schema = 1, string id = "test-pack", string file = "samples/click.wav", bool duplicateSet = false, bool emptySet = false, bool duplicateFile = false)
        {
            IReadOnlyList<string> files = emptySet ? [] : duplicateFile ? [file, file] : [file];
            var sets = new List<SoundSetDefinition> { new() { Id = "click", Name = "Click", Files = files } };
            if (duplicateSet) sets.Add(new() { Id = "CLICK", Name = "Duplicate", Files = [file] });
            var manifest = new SoundPackManifest { SchemaVersion = schema, Id = id, Name = "Test Pack", SoundSets = sets };
            File.WriteAllText(Path.Combine(Source, "manifest.json"), JsonSerializer.Serialize(manifest));
        }
        public void InstallSource() => CopyDirectory(Source, Path.Combine(Paths.SoundPacksDirectory, "test-pack"));
        public SoundPackCatalogService CreateCatalog() => new(Paths, Validator);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        private static void WriteWave(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var writer = new WaveFileWriter(path, new WaveFormat(48000, 16, 1)); writer.Write(new byte[4800]);
        }
        private static void CopyDirectory(string source, string target)
        {
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(directory.Replace(source, target));
            Directory.CreateDirectory(target); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, file.Replace(source, target));
        }
    }
}
