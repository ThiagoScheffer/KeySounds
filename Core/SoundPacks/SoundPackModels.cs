namespace KeySounds.Core.SoundPacks;

public sealed class SoundPackManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<SoundSetDefinition> SoundSets { get; init; } = [];
}

public sealed class SoundSetDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Files { get; init; } = [];
}

public enum ValidationSeverity { Warning, Error }
public sealed record ValidationIssue(string Code, string Message, ValidationSeverity Severity = ValidationSeverity.Error);
public sealed record SoundPackValidationResult(bool IsValid, IReadOnlyList<ValidationIssue> Issues, long EstimatedDecodedBytes, int SampleCount, SoundPackManifest? Manifest = null);
public sealed record InstalledSoundPack(SoundPackManifest? Manifest, string RootDirectory, bool IsBuiltIn, SoundPackValidationResult Validation);
public sealed record SoundSetReference(string PackId, string SoundSetId);
public sealed record RuntimeSoundSet(string PackId, string SoundSetId, IReadOnlyList<Audio.SoundId> Samples);

public enum SoundPackConflictPolicy { Cancel, Replace }
public sealed record SoundPackImportResult(bool Success, string? PackId = null, string? ErrorCode = null, string? Message = null);
public sealed record SoundPackRemovalResult(bool Success, string? ErrorCode, string? Message, IReadOnlyList<string> ReferencingProfileIds);
