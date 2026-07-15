using System.Text.Json;
using KeySounds.Application.Settings;
using KeySounds.Core.Profiles;
using KeySounds.Infrastructure.Persistence;

namespace KeySounds.Application.Profiles;

public enum ProfileImportConflictPolicy { Cancel, ImportAsCopy, Replace }
public sealed record ProfileOperationResult(bool Success, string? ErrorCode = null, string? Message = null, SoundProfile? Profile = null);

public sealed class ProfileManagementService(JsonProfileRepository repository, Phase2SettingsService settings, ProfileValidationService validator, AtomicFileWriter writer) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _shutdown;
    public IReadOnlyList<SoundProfile> Profiles => settings.Profiles;

    public async Task<ProfileOperationResult> CreateAsync(string name, SoundProfile? template = null, CancellationToken token = default)
        => await MutateAsync(async () =>
        {
            var id = GenerateId(name); var source = template ?? Phase2Defaults.CreateProfile(); var profile = SoundProfileCloner.Clone(source, id, string.IsNullOrWhiteSpace(name) ? "New Profile" : name.Trim());
            await repository.SaveAsync(profile, token); await settings.ReloadProfilesAsync(token); return new(true, Profile: profile);
        }, token);

    public async Task<ProfileOperationResult> SaveGeneratedAsync(SoundProfile profile, bool activate, CancellationToken token = default)
        => await MutateAsync(async () =>
        {
            var issues = validator.Validate(profile);
            if (issues.Any(issue => issue.Severity == ProfileValidationSeverity.Error)) return Invalid(issues);
            await repository.SaveAsync(profile, token);
            await settings.ReloadProfilesAsync(token);
            if (activate) await settings.ActivateAsync(profile.Id, token);
            return new(true, Profile: profile);
        }, token);

    public Task<ProfileOperationResult> DuplicateAsync(SoundProfile source, CancellationToken token = default) => CreateAsync(source.Name + " Copy", source, token);

    public async Task<ProfileOperationResult> RenameAsync(SoundProfile source, string name, CancellationToken token = default)
        => await MutateAsync(async () =>
        {
            var copy = SoundProfileCloner.Clone(source); copy.Name = name.Trim(); var issues = validator.Validate(copy);
            if (issues.Any(issue => issue.Severity == ProfileValidationSeverity.Error)) return Invalid(issues);
            await repository.SaveAsync(copy, token); await settings.ReloadProfilesAsync(token); return new(true, Profile: copy);
        }, token);

    public async Task<ProfileOperationResult> SaveAsync(EditableProfileSession session, bool activate, CancellationToken token = default)
        => await MutateAsync(async () =>
        {
            var issues = validator.Validate(session.WorkingCopy); if (issues.Any(issue => issue.Severity == ProfileValidationSeverity.Error)) return Invalid(issues);
            var saved = SoundProfileCloner.Clone(session.WorkingCopy); await repository.SaveAsync(saved, token); await settings.ReloadProfilesAsync(token);
            if (activate) await settings.ActivateAsync(saved.Id, token);
            session.AcceptSaved(saved); return new(true, Profile: saved);
        }, token);

    public async Task<ProfileOperationResult> DeleteAsync(SoundProfile profile, CancellationToken token = default)
        => await MutateAsync(async () =>
        {
            if (profile.Id == "default") return new(false, "FallbackProtected", "The fallback profile cannot be deleted.");
            if (settings.Settings.ActiveProfileId == profile.Id) return new(false, "ActiveProfile", "Activate another profile before deleting this one.");
            var keyboardReferences = settings.Settings.KeyboardProfileAssignments.Where(item => item.Value == profile.Id).Select(item => item.Key).ToArray();
            if (keyboardReferences.Length > 0) return new(false, "KeyboardAssignments", $"Clear {keyboardReferences.Length} keyboard assignment(s) before deleting this profile.");
            await repository.DeleteAsync(profile.Id, token); await settings.ReloadProfilesAsync(token); return new(true);
        }, token);

    public async Task<ProfileOperationResult> ImportAsync(string path, ProfileImportConflictPolicy policy, CancellationToken token = default)
        => await MutateAsync(async () =>
        {
            SoundProfile? profile;
            try { await using var stream = File.OpenRead(path); profile = await JsonSerializer.DeserializeAsync<SoundProfile>(stream, JsonOptions, token); }
            catch (Exception ex) when (ex is JsonException or IOException) { return new(false, "MalformedProfile", "Profile JSON is malformed or unreadable."); }
            if (profile is null || profile.SchemaVersion != 1) return new(false, "UnsupportedSchema", "Only profile schema version 1 is supported.");
            var existing = settings.Profiles.FirstOrDefault(item => item.Id == profile.Id);
            if (existing is not null)
            {
                if (policy == ProfileImportConflictPolicy.Cancel) return new(false, "DuplicateId", "A profile with this ID already exists.");
                if (existing.Id == "default" && policy == ProfileImportConflictPolicy.Replace) return new(false, "FallbackProtected", "The fallback profile cannot be replaced.");
                if (policy == ProfileImportConflictPolicy.ImportAsCopy) profile = SoundProfileCloner.Clone(profile, GenerateId(profile.Name + " Copy"), profile.Name + " Copy");
            }
            var issues = validator.Validate(profile); if (issues.Any(issue => issue.Severity == ProfileValidationSeverity.Error)) return Invalid(issues);
            await repository.SaveAsync(profile, token); await settings.ReloadProfilesAsync(token); return new(true, Profile: profile);
        }, token);

    public async Task<ProfileOperationResult> ExportAsync(SoundProfile profile, string path, CancellationToken token = default)
        => await MutateAsync(async () => { await writer.WriteJsonAsync(path, profile, token); return new(true, Profile: profile); }, token);

    private async Task<ProfileOperationResult> MutateAsync(Func<Task<ProfileOperationResult>> operation, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { if (_shutdown) return new(false, "Shutdown", "Profile operations are shutting down."); return await operation(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(false, "OperationFailed", "The profile operation could not be completed."); }
        finally { _gate.Release(); }
    }
    private string GenerateId(string name)
    {
        var stem = new string(name.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
        if (stem.Length == 0) stem = "profile"; if (stem.Length > 48) stem = stem[..48];
        var id = stem; var suffix = 2; while (settings.Profiles.Any(profile => profile.Id == id)) id = $"{stem}-{suffix++}"; return id;
    }
    private static ProfileOperationResult Invalid(IReadOnlyList<ProfileValidationIssue> issues) => new(false, "ValidationFailed", string.Join(Environment.NewLine, issues.Select(issue => issue.Message)));
    public async ValueTask DisposeAsync() { _shutdown = true; await _gate.WaitAsync(); _gate.Release(); _gate.Dispose(); }
}
