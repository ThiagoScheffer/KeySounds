using System.Text.Json;
using System.Text.RegularExpressions;
using KeySounds.Application.Profiles;
using KeySounds.Core.Input;
using KeySounds.Core.Profiles;
using KeySounds.Core.SoundPacks;
using KeySounds.Infrastructure.Persistence;

namespace KeySounds.Application.SoundPacks;

public sealed class SoundPackDraft
{
    public required string DraftDirectory { get; init; }
    public required string PackId { get; set; }
    public required string Name { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public bool EditingExisting { get; init; }
    public string? OriginalPackId { get; init; }
    public List<SoundPackDraftSet> SoundSets { get; } = [];
}

public sealed class SoundPackDraftSet
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public List<string> Files { get; } = [];
}

public sealed record SoundPackDraftValidation(
    bool IsValid,
    IReadOnlyList<ValidationIssue> Issues,
    SoundPackValidationResult? PackValidation,
    IReadOnlyList<string> ReferencingProfileIds,
    IReadOnlyList<string> RemovedReferencedSoundSetIds)
{
    public bool RequiresReferenceConfirmation => RemovedReferencedSoundSetIds.Count > 0;
}

public sealed record SoundPackDraftSaveResult(bool Success, string? PackId = null, string? ErrorCode = null, string? Message = null, SoundPackDraftValidation? Validation = null);

public sealed class SoundPackDraftService(StoragePaths paths, SoundPackValidationService validator, SoundPackCatalogService catalog, SoundPackImportService importer, Func<IReadOnlyList<SoundProfile>> profiles)
{
    private static readonly Regex SafeId = new("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] DefaultSetIds = ["alpha-down", "space-down", "enter-down", "backspace-down", "modifier-down", "arrow-down", "key-up"];

    public SoundPackDraft CreateNew()
    {
        var draft = new SoundPackDraft { DraftDirectory = CreateDraftDirectory(), PackId = "my-custom-pack", Name = "My Custom Pack" };
        foreach (var id in DefaultSetIds) draft.SoundSets.Add(new() { Id = id, Name = ToDisplayName(id) });
        return draft;
    }

    public async Task<SoundPackDraft?> CreateFromInstalledAsync(InstalledSoundPack pack, CancellationToken token = default)
    {
        if (pack.IsBuiltIn || pack.Manifest is null) return null;
        var draft = new SoundPackDraft
        {
            DraftDirectory = CreateDraftDirectory(),
            PackId = pack.Manifest.Id,
            Name = pack.Manifest.Name,
            Author = pack.Manifest.Author,
            Description = pack.Manifest.Description,
            EditingExisting = true,
            OriginalPackId = pack.Manifest.Id
        };
        foreach (var set in pack.Manifest.SoundSets)
        {
            var draftSet = new SoundPackDraftSet { Id = set.Id, Name = set.Name };
            foreach (var relative in set.Files)
            {
                var source = Path.Combine(pack.RootDirectory, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                var target = Path.Combine(draft.DraftDirectory, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                await CopyFileAsync(source, target, token);
                draftSet.Files.Add(relative.Replace('\\', '/'));
            }
            draft.SoundSets.Add(draftSet);
        }
        return draft;
    }

    public async Task<ValidationIssue?> AddSampleAsync(SoundPackDraft draft, SoundPackDraftSet set, string sourcePath, CancellationToken token = default)
    {
        if (!File.Exists(sourcePath)) return new("SourceMissing", "Dropped sample file does not exist.");
        if (!string.Equals(Path.GetExtension(sourcePath), ".wav", StringComparison.OrdinalIgnoreCase)) return new("UnsupportedExtension", "Only WAV samples can be added.");
        var relative = BuildUniqueRelativePath(draft, set, sourcePath);
        var destination = Path.Combine(draft.DraftDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        await CopyFileAsync(sourcePath, destination, token);
        set.Files.Add(relative);
        return null;
    }

    public async Task<SoundPackDraftValidation> ValidateAsync(SoundPackDraft draft, CancellationToken token = default)
    {
        var issues = ValidateDraftShape(draft).ToList();
        await WriteManifestAsync(draft, token);
        var packValidation = await validator.ValidateAsync(draft.DraftDirectory, token);
        issues.AddRange(packValidation.Issues);
        var removedSets = FindRemovedReferencedSoundSets(draft);
        return new(!issues.Any(issue => issue.Severity == ValidationSeverity.Error), issues, packValidation, FindReferencingProfiles(draft.PackId), removedSets);
    }

    public async Task<SoundPackDraftSaveResult> SaveAsync(SoundPackDraft draft, bool confirmReferenceChanges, CancellationToken token = default)
    {
        var validation = await ValidateAsync(draft, token);
        if (!validation.IsValid) return new(false, draft.PackId, "ValidationFailed", validation.Issues.FirstOrDefault()?.Message ?? "Sound-pack validation failed.", validation);
        if (validation.RequiresReferenceConfirmation && !confirmReferenceChanges)
            return new(false, draft.PackId, "ReferenceConfirmationRequired", "This edit removes sound sets referenced by saved profiles. Confirm and save again to replace the pack.", validation);
        if (string.Equals(draft.PackId, "builtin", StringComparison.OrdinalIgnoreCase)) return new(false, draft.PackId, "BuiltInProtected", "The built-in pack cannot be replaced.", validation);
        var result = await importer.ImportFolderAsync(draft.DraftDirectory, SoundPackConflictPolicy.Replace, token);
        return result.Success ? new(true, result.PackId, Validation: validation) : new(false, result.PackId, result.ErrorCode, result.Message, validation);
    }

    public async Task<SoundProfile> CreateProfileForPackAsync(string packId, string profileName, IReadOnlyList<string> soundSetIds, CancellationToken token = default)
    {
        var setLookup = new HashSet<string>(soundSetIds, StringComparer.OrdinalIgnoreCase);
        var fallback = soundSetIds.FirstOrDefault() ?? "click";
        var profile = new SoundProfile { Id = GenerateProfileId(profileName), Name = string.IsNullOrWhiteSpace(profileName) ? "Custom Sound Pack Profile" : profileName.Trim() };
        foreach (var group in Enum.GetValues<KeySoundGroup>())
        {
            var set = PickSetForGroup(group, setLookup, fallback);
            profile.Groups[group] = new KeyGroupMapping { SoundPackId = packId, KeyDownSoundSetId = set, VariationMode = VariationSelectionMode.AvoidImmediateRepeat };
        }
        if (setLookup.Contains("key-up"))
        {
            profile.PlayOnKeyUp = true;
            foreach (var mapping in profile.Groups.Values) mapping.KeyUpSoundSetId = "key-up";
        }
        await Task.CompletedTask;
        token.ThrowIfCancellationRequested();
        return profile;
    }

    public void DeleteDraft(SoundPackDraft? draft)
    {
        if (draft is null) return;
        try { if (Directory.Exists(draft.DraftDirectory)) Directory.Delete(draft.DraftDirectory, true); } catch { }
    }

    public void CleanupAbandonedDrafts()
    {
        if (!Directory.Exists(paths.SoundPacksDirectory)) return;
        foreach (var directory in Directory.EnumerateDirectories(paths.SoundPacksDirectory, ".draft-*"))
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private string CreateDraftDirectory()
    {
        Directory.CreateDirectory(paths.SoundPacksDirectory);
        var directory = Path.Combine(paths.SoundPacksDirectory, $".draft-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private async Task WriteManifestAsync(SoundPackDraft draft, CancellationToken token)
    {
        var manifest = new SoundPackManifest
        {
            Id = draft.PackId.Trim(),
            Name = draft.Name.Trim(),
            Author = string.IsNullOrWhiteSpace(draft.Author) ? null : draft.Author.Trim(),
            Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description.Trim(),
            SoundSets = draft.SoundSets.Select(set => new SoundSetDefinition { Id = set.Id.Trim(), Name = set.Name.Trim(), Files = set.Files.ToArray() }).ToArray()
        };
        await using var stream = new FileStream(Path.Combine(draft.DraftDirectory, "manifest.json"), FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, token);
        await stream.FlushAsync(token);
    }

    private IEnumerable<ValidationIssue> ValidateDraftShape(SoundPackDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.PackId) || !SafeId.IsMatch(draft.PackId.Trim())) yield return new("InvalidPackId", "Pack ID must use lowercase letters, numbers, hyphens, or underscores.");
        if (string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > 128) yield return new("InvalidPackName", "Pack name is required and must not exceed 128 characters.");
        if (draft.SoundSets.Count == 0) yield return new("NoSoundSets", "At least one sound set is required.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in draft.SoundSets)
        {
            if (string.IsNullOrWhiteSpace(set.Id) || !SafeId.IsMatch(set.Id.Trim()) || !ids.Add(set.Id.Trim())) yield return new("InvalidSoundSetId", "Sound-set IDs must be safe, nonempty, and unique.");
            if (string.IsNullOrWhiteSpace(set.Name)) yield return new("InvalidSoundSetName", "Sound-set name is required.");
            if (set.Files.Count == 0) yield return new("EmptySoundSet", $"Sound set '{set.Id}' contains no files.");
        }
    }

    private IReadOnlyList<string> FindReferencingProfiles(string packId)
        => profiles().Where(profile => profile.Groups.Values.Any(group => group.SoundPackId == packId) || profile.KeyOverrides.Values.Any(item => item.SoundPackId == packId)).Select(profile => profile.Id).Distinct().ToArray();

    private IReadOnlyList<string> FindRemovedReferencedSoundSets(SoundPackDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.OriginalPackId) || !string.Equals(draft.OriginalPackId, draft.PackId, StringComparison.OrdinalIgnoreCase)) return [];
        if (!catalog.TryGetPack(draft.OriginalPackId, out var installed) || installed.Manifest is null) return [];
        var newSets = draft.SoundSets.Select(set => set.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = installed.Manifest.SoundSets.Select(set => set.Id).Where(id => !newSets.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (removed.Count == 0) return [];
        var referenced = profiles()
            .SelectMany(profile => profile.Groups.Values.SelectMany(group => new[] { group.KeyDownSoundSetId, group.KeyUpSoundSetId }).Concat(profile.KeyOverrides.Values.SelectMany(item => new[] { item.KeyDownSoundSetId, item.KeyUpSoundSetId })))
            .Where(id => id is not null && removed.Contains(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return referenced;
    }

    private static string BuildUniqueRelativePath(SoundPackDraft draft, SoundPackDraftSet set, string sourcePath)
    {
        var stem = SafeFileStem(Path.GetFileNameWithoutExtension(sourcePath));
        var folder = SafeFileStem(set.Id);
        var index = 1;
        string relative;
        do { relative = $"samples/{folder}/{stem}-{index++:00}.wav"; }
        while (draft.SoundSets.SelectMany(item => item.Files).Contains(relative, StringComparer.OrdinalIgnoreCase) || File.Exists(Path.Combine(draft.DraftDirectory, relative.Replace('/', Path.DirectorySeparatorChar))));
        return relative;
    }

    private static string SafeFileStem(string value)
    {
        var chars = value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var normalized = new string(chars).Trim('-');
        while (normalized.Contains("--", StringComparison.Ordinal)) normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(normalized) ? "sample" : normalized[..Math.Min(normalized.Length, 48)];
    }

    private static string ToDisplayName(string id) => string.Join(' ', id.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private string GenerateProfileId(string name)
    {
        var stem = SafeFileStem(name);
        var id = stem;
        var suffix = 2;
        while (profiles().Any(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))) id = $"{stem}-{suffix++}";
        return id;
    }

    private static string PickSetForGroup(KeySoundGroup group, IReadOnlySet<string> sets, string fallback)
    {
        var preferred = group switch
        {
            KeySoundGroup.Alpha => "alpha-down",
            KeySoundGroup.Spacebar => "space-down",
            KeySoundGroup.Enter => "enter-down",
            KeySoundGroup.Backspace => "backspace-down",
            KeySoundGroup.Modifier => "modifier-down",
            KeySoundGroup.Arrow => "arrow-down",
            _ => "alpha-down"
        };
        return sets.Contains(preferred) ? preferred : fallback;
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, token);
        await output.FlushAsync(token);
        output.Flush(true);
    }
}
