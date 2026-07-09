using System.Diagnostics;
using System.Text.Json;
using KeySounds.Infrastructure.Persistence;

namespace KeySounds.Application.Diagnostics;

public sealed record DiagnosticReport(
    DateTimeOffset CreatedUtc,
    long ProcessWorkingSetBytes,
    int ProcessorCount,
    string RuntimeVersion,
    long ExportDurationMilliseconds,
    bool SettingsFileExists,
    long SettingsFileBytes,
    int ProfileFileCount,
    int SoundPackDirectoryCount,
    int InvalidSoundPackDirectoryCount,
    int ManagedTempDirectoryCount,
    int ManagedTempFileCount,
    string PrivacyNote = "Sanitized diagnostic report: no typed text, key sequences, scan-code history, virtual-key history, transient Raw Input handles, raw HID paths, or source import paths are included.");

public sealed class DiagnosticReportService(StoragePaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public DiagnosticReport CreateReport()
    {
        var watch = Stopwatch.StartNew();
        Directory.CreateDirectory(paths.Root);
        var report = new DiagnosticReport(
            DateTimeOffset.UtcNow,
            Environment.WorkingSet,
            Environment.ProcessorCount,
            Environment.Version.ToString(),
            0,
            File.Exists(paths.SettingsFile),
            File.Exists(paths.SettingsFile) ? new FileInfo(paths.SettingsFile).Length : 0,
            Directory.Exists(paths.ProfilesDirectory) ? Directory.EnumerateFiles(paths.ProfilesDirectory, "*.json").Count() : 0,
            Directory.Exists(paths.SoundPacksDirectory) ? Directory.EnumerateDirectories(paths.SoundPacksDirectory).Count(path => !Path.GetFileName(path).StartsWith('.')) : 0,
            CountInvalidSoundPackDirectories(),
            CountManagedTempDirectories(),
            CountManagedTempFiles());
        watch.Stop();
        return report with { ExportDurationMilliseconds = watch.ElapsedMilliseconds };
    }

    public async Task ExportAsync(string path, CancellationToken token = default)
    {
        var report = CreateReport();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions, token);
        await stream.FlushAsync(token);
    }

    private int CountInvalidSoundPackDirectories()
    {
        if (!Directory.Exists(paths.SoundPacksDirectory)) return 0;
        var count = 0;
        foreach (var directory in Directory.EnumerateDirectories(paths.SoundPacksDirectory).Where(path => !Path.GetFileName(path).StartsWith('.')))
            if (!File.Exists(Path.Combine(directory, "manifest.json"))) count++;
        return count;
    }

    private int CountManagedTempDirectories()
    {
        if (!Directory.Exists(paths.Root)) return 0;
        return Directory.EnumerateDirectories(paths.Root, "*", SearchOption.AllDirectories).Count(path => IsManagedTempDirectory(Path.GetFileName(path)));
    }

    private int CountManagedTempFiles()
    {
        if (!Directory.Exists(paths.Root)) return 0;
        return Directory.EnumerateFiles(paths.Root, "*.tmp", SearchOption.AllDirectories).Count();
    }

    private static bool IsManagedTempDirectory(string name)
        => name.StartsWith(".import-", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith(".replace-", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith(".trash-", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith(".cache-", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith(".atomic-", StringComparison.OrdinalIgnoreCase);
}
