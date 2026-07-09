using System.Text.Json;
using System.Text.RegularExpressions;
using KeySounds.Core.SoundPacks;
using NAudio.Wave;

namespace KeySounds.Application.SoundPacks;

public sealed record SoundPackValidationOptions(int MaximumSamplesPerPack = 512, int MaximumSamplesPerSet = 128, long MaximumFileBytes = 10 * 1024 * 1024,
    TimeSpan? MaximumDuration = null, TimeSpan? MinimumDuration = null, long MaximumDecodedBytes = 256L * 1024 * 1024)
{
    public TimeSpan MaxDuration => MaximumDuration ?? TimeSpan.FromSeconds(5);
    public TimeSpan MinDuration => MinimumDuration ?? TimeSpan.FromMilliseconds(5);
}

public sealed class SoundPackValidationService(SoundPackValidationOptions? options = null)
{
    private static readonly Regex SafeId = new("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SoundPackValidationOptions _options = options ?? new();

    public async Task<SoundPackValidationResult> ValidateAsync(string root, CancellationToken token = default)
    {
        var issues = new List<ValidationIssue>(); var samples = new List<SoundSampleValidationMetadata>(); long decodedBytes = 0; var sampleCount = 0;
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!Directory.Exists(root)) return Invalid("SourceMissing", "Sound-pack directory does not exist.");
        if (!File.Exists(manifestPath)) return Invalid("ManifestMissing", "manifest.json is missing.");
        SoundPackManifest? manifest;
        try { await using var stream = File.OpenRead(manifestPath); manifest = await JsonSerializer.DeserializeAsync<SoundPackManifest>(stream, JsonOptions, token); }
        catch (Exception ex) when (ex is JsonException or IOException) { return Invalid("ManifestMalformed", "manifest.json is not readable JSON."); }
        if (manifest is null) return Invalid("ManifestMalformed", "manifest.json is empty.");
        if (manifest.SchemaVersion != 1) issues.Add(new("UnsupportedSchema", "Only sound-pack schema version 1 is supported."));
        if (string.IsNullOrWhiteSpace(manifest.Id) || !SafeId.IsMatch(manifest.Id)) issues.Add(new("InvalidPackId", "Pack ID must use lowercase letters, numbers, hyphens, or underscores."));
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 128) issues.Add(new("InvalidPackName", "Pack name is required and must not exceed 128 characters."));
        if (manifest.SoundSets.Count == 0) issues.Add(new("NoSoundSets", "At least one sound set is required."));
        var setIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
        foreach (var set in manifest.SoundSets)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(set.Id) || !setIds.Add(set.Id)) issues.Add(new("InvalidSoundSetId", "Sound-set IDs must be nonempty and unique."));
            if (set.Files.Count == 0) issues.Add(new("EmptySoundSet", $"Sound set '{set.Id}' contains no files."));
            if (set.Files.Count > _options.MaximumSamplesPerSet) issues.Add(new("TooManySetSamples", $"Sound set '{set.Id}' exceeds the sample limit."));
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var relative in set.Files)
            {
                sampleCount++;
                if (!files.Add(relative ?? string.Empty)) { issues.Add(new("DuplicateFile", $"Sound set '{set.Id}' contains a duplicate file reference.")); continue; }
                if (!TryResolvePath(rootFull, relative, out var fullPath, out var pathIssue)) { issues.Add(pathIssue!); continue; }
                if (Directory.Exists(fullPath)) { issues.Add(new("DirectoryReferenced", $"Referenced WAV '{relative}' is a directory.")); continue; }
                if (!File.Exists(fullPath)) { issues.Add(new("SampleMissing", $"Referenced WAV '{relative}' is missing.")); continue; }
                if (ContainsReparsePoint(rootFull, fullPath)) { issues.Add(new("ReparsePointRejected", $"Referenced WAV '{relative}' uses a reparse point.")); continue; }
                try
                {
                    var info = new FileInfo(fullPath);
                    if (info.Length == 0 || info.Length > _options.MaximumFileBytes) { issues.Add(new("InvalidFileSize", $"WAV '{relative}' has an unsupported file size.")); continue; }
                    using var reader = new WaveFileReader(fullPath);
                    if (reader.WaveFormat.Channels is < 1 or > 2 || reader.WaveFormat.SampleRate is < 8000 or > 192000) { issues.Add(new("UnsupportedWaveFormat", $"WAV '{relative}' has unsupported channels or sample rate.")); continue; }
                    if (reader.TotalTime < _options.MinDuration || reader.TotalTime > _options.MaxDuration) { issues.Add(new("InvalidDuration", $"WAV '{relative}' duration is outside the supported range.")); continue; }
                    var sampleDecodedBytes = checked((long)Math.Ceiling(reader.TotalTime.TotalSeconds * 48000) * 2 * sizeof(float));
                    decodedBytes = checked(decodedBytes + sampleDecodedBytes);
                    samples.Add(new(set.Id, relative!.Replace('\\', '/'), reader.TotalTime, sampleDecodedBytes));
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { issues.Add(new("InvalidWave", $"WAV '{relative}' could not be decoded.")); }
            }
        }
        if (sampleCount > _options.MaximumSamplesPerPack) issues.Add(new("TooManySamples", "Pack exceeds the total sample limit."));
        if (decodedBytes > _options.MaximumDecodedBytes) issues.Add(new("DecodedMemoryLimit", "Pack exceeds the decoded-memory limit."));
        return new(!issues.Any(issue => issue.Severity == ValidationSeverity.Error), issues.AsReadOnly(), decodedBytes, sampleCount, manifest, samples.AsReadOnly());

        SoundPackValidationResult Invalid(string code, string message) => new(false, [new(code, message)], 0, 0);
    }

    internal static bool TryResolvePath(string rootFull, string? relative, out string fullPath, out ValidationIssue? issue)
    {
        fullPath = string.Empty; issue = null;
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.StartsWith("\\\\", StringComparison.Ordinal) || relative.Contains(':')) { issue = new("InvalidPath", "Sample path must be relative."); return false; }
        var normalized = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Split(Path.DirectorySeparatorChar).Any(part => part == "..")) { issue = new("PathTraversal", "Sample path escapes the pack directory."); return false; }
        if (!string.Equals(Path.GetExtension(normalized), ".wav", StringComparison.OrdinalIgnoreCase)) { issue = new("UnsupportedExtension", "Only WAV samples are supported."); return false; }
        try { fullPath = Path.GetFullPath(Path.Combine(rootFull, normalized)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { issue = new("InvalidPath", "Sample path is invalid."); return false; }
        if (!fullPath.StartsWith(rootFull, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) { issue = new("PathTraversal", "Sample path escapes the pack directory."); return false; }
        return true;
    }
    private static string EnsureTrailingSeparator(string path) => Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
    private static bool ContainsReparsePoint(string root, string file)
    {
        var current = Path.GetDirectoryName(file);
        while (current is not null && current.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            current = Path.GetDirectoryName(current);
        }
        return (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0;
    }
}
