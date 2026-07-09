using KeySounds.Infrastructure.Persistence;

namespace KeySounds.Application.SoundPacks;

public sealed record StorageCleanupResult(int RemovedTempDirectories, int RemovedTempFiles, int SkippedUnsafePaths)
{
    public override string ToString() => $"Removed temp directories: {RemovedTempDirectories}; removed temp files: {RemovedTempFiles}; skipped unsafe paths: {SkippedUnsafePaths}.";
}

public sealed class StorageCleanupService(StoragePaths paths)
{
    public Task<StorageCleanupResult> CleanupAsync(CancellationToken token = default)
    {
        var root = Path.GetFullPath(paths.Root);
        var removedDirectories = 0;
        var removedFiles = 0;
        var skipped = 0;
        Directory.CreateDirectory(paths.Root);

        foreach (var parent in new[] { paths.SoundPacksDirectory, paths.ProfilesDirectory, paths.Root }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(parent)) continue;
            foreach (var directory in Directory.EnumerateDirectories(parent))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(directory);
                if (!IsManagedTempDirectory(name)) continue;
                if (!IsUnderRoot(root, directory)) { skipped++; continue; }
                try { Directory.Delete(directory, true); removedDirectories++; }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
            foreach (var file in Directory.EnumerateFiles(parent, "*.tmp"))
            {
                token.ThrowIfCancellationRequested();
                if (!IsUnderRoot(root, file)) { skipped++; continue; }
                try { File.Delete(file); removedFiles++; }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
        }

        return Task.FromResult(new StorageCleanupResult(removedDirectories, removedFiles, skipped));
    }

    private static bool IsManagedTempDirectory(string name)
        => name.StartsWith(".import-", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith(".replace-", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith(".trash-", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith(".cache-", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith(".atomic-", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderRoot(string root, string candidate)
    {
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(candidate);
        return full.StartsWith(rootWithSeparator, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
