using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using KeySounds.Infrastructure.Persistence;

namespace KeySounds.Application.Diagnostics;

public sealed record DiagnosticReport(
    int ReportVersion,
    DateTimeOffset CreatedUtc,
    string AppVersion,
    string OSVersion,
    string ProcessArchitecture,
    long ProcessWorkingSetBytes,
    int ProcessorCount,
    string RuntimeVersion,
    long ExportDurationMilliseconds,
    string SettingsSection,
    bool SettingsFileExists,
    long SettingsFileBytes,
    int? SettingsSchemaVersion,
    bool? SoundsEnabled,
    string? AudioOutputKind,
    int? AudioLatencyMilliseconds,
    bool? StartWithWindows,
    bool? MinimizeToTray,
    bool? CloseToTray,
    bool? ShowWindowOnStartup,
    int KeyboardAssignmentCount,
    string ProfilesSection,
    int ProfileFileCount,
    bool ActiveProfileConfigured,
    string SoundPacksSection,
    int SoundPackDirectoryCount,
    int InvalidSoundPackDirectoryCount,
    string StorageSection,
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
        var settings = ReadSettingsSummary();
        var report = new DiagnosticReport(
            1,
            DateTimeOffset.UtcNow,
            typeof(DiagnosticReportService).Assembly.GetName().Version?.ToString() ?? "development",
            Environment.OSVersion.VersionString,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.WorkingSet,
            Environment.ProcessorCount,
            Environment.Version.ToString(),
            0,
            "Settings summary contains only schema and preference flags; no raw keyboard IDs or assigned profile IDs are exported.",
            File.Exists(paths.SettingsFile),
            File.Exists(paths.SettingsFile) ? new FileInfo(paths.SettingsFile).Length : 0,
            settings.SchemaVersion,
            settings.SoundsEnabled,
            settings.AudioOutputKind,
            settings.AudioLatencyMilliseconds,
            settings.StartWithWindows,
            settings.MinimizeToTray,
            settings.CloseToTray,
            settings.ShowWindowOnStartup,
            settings.KeyboardAssignmentCount,
            "Profile summary contains counts only; active profile ID and per-key overrides are not exported.",
            Directory.Exists(paths.ProfilesDirectory) ? Directory.EnumerateFiles(paths.ProfilesDirectory, "*.json").Count() : 0,
            !string.IsNullOrWhiteSpace(settings.ActiveProfileId),
            "Sound-pack summary contains directory counts only; source import paths are not exported.",
            Directory.Exists(paths.SoundPacksDirectory) ? Directory.EnumerateDirectories(paths.SoundPacksDirectory).Count(path => !Path.GetFileName(path).StartsWith('.')) : 0,
            CountInvalidSoundPackDirectories(),
            "Storage summary contains managed temporary-file counts only.",
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

    private SettingsSummary ReadSettingsSummary()
    {
        if (!File.Exists(paths.SettingsFile)) return new();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(paths.SettingsFile));
            var root = document.RootElement;
            return new SettingsSummary(
                TryGetInt(root, "schemaVersion"),
                TryGetBool(root, "soundsEnabled"),
                TryGetString(root, "audioOutputSelection", "kind"),
                TryGetInt(root, "audioLatencyMilliseconds"),
                TryGetBool(root, "startWithWindows"),
                TryGetBool(root, "minimizeToTray"),
                TryGetBool(root, "closeToTray"),
                TryGetBool(root, "showWindowOnStartup"),
                CountObjectProperties(root, "keyboardProfileAssignments"),
                TryGetString(root, "activeProfileId"));
        }
        catch
        {
            return new();
        }
    }

    private static int? TryGetInt(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;

    private static bool? TryGetBool(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) ? value.GetBoolean() : null;

    private static string? TryGetString(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? TryGetString(JsonElement root, string property, string childProperty)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? TryGetString(value, childProperty) : null;

    private static int CountObjectProperties(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().Count() : 0;

    private sealed record SettingsSummary(
        int? SchemaVersion = null,
        bool? SoundsEnabled = null,
        string? AudioOutputKind = null,
        int? AudioLatencyMilliseconds = null,
        bool? StartWithWindows = null,
        bool? MinimizeToTray = null,
        bool? CloseToTray = null,
        bool? ShowWindowOnStartup = null,
        int KeyboardAssignmentCount = 0,
        string? ActiveProfileId = null);
}
