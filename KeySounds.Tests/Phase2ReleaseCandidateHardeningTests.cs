using KeySounds.Application.Diagnostics;
using KeySounds.Application.SoundPacks;
using KeySounds.Core.SoundPacks;
using KeySounds.Infrastructure.Persistence;
using Xunit;

namespace KeySounds.Tests;

public sealed class Phase2ReleaseCandidateHardeningTests
{
    [Fact]
    public async Task DiagnosticExportIsSanitizedAndContainsAggregateCountsOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "KeySounds.Diagnostics", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StoragePaths(root);
            Directory.CreateDirectory(paths.ProfilesDirectory);
            Directory.CreateDirectory(Path.Combine(paths.SoundPacksDirectory, "builtin"));
            await File.WriteAllTextAsync(paths.SettingsFile, """{"KeyboardProfileAssignments":{"kbd-v1-secret":"default"},"DeviceHandle":"should-not-be-copied"}""");
            await File.WriteAllTextAsync(Path.Combine(paths.ProfilesDirectory, "default.json"), "{}");
            var reportPath = Path.Combine(root, "diagnostics.json");

            await new DiagnosticReportService(paths).ExportAsync(reportPath);

            var json = await File.ReadAllTextAsync(reportPath);
            Assert.Contains("ProfileFileCount", json);
            Assert.Contains("SoundPackDirectoryCount", json);
            Assert.DoesNotContain("kbd-v1-secret", json);
            Assert.DoesNotContain("DeviceHandle", json);
            Assert.DoesNotContain("ScanCode", json);
            Assert.DoesNotContain("VirtualKey", json);
            Assert.DoesNotContain("RawInput", json);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SoundPackPickerCancellationIsHarmless()
    {
        var picker = new BlockingSoundPackFolderPicker();
        using var cancellation = new CancellationTokenSource();
        var pending = picker.PickImportFolderAsync(cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task OwnerlessDuplicateConflictFailsClosedToCancel()
    {
        var resolver = new AvaloniaSoundPackConflictResolver(() => null);
        var result = await resolver.ResolveDuplicateAsync(new(null, "root", false, new(false, [], 0, 0)));

        Assert.Equal(SoundPackConflictPolicy.Cancel, result);
    }

    private sealed class BlockingSoundPackFolderPicker : ISoundPackFolderPicker
    {
        public async Task<string?> PickImportFolderAsync(CancellationToken token = default)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), token);
            return null;
        }

        public Task<IReadOnlyList<string>> PickSampleFilesAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
