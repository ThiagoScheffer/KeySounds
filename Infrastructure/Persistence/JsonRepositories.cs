using System.Text.Json;
using KeySounds.Core.Profiles;
using KeySounds.Core.Settings;

namespace KeySounds.Infrastructure.Persistence;

public sealed class JsonApplicationSettingsRepository(StoragePaths paths, AtomicFileWriter writer)
{
    public async Task<(ApplicationSettings Settings, string? RecoveryMessage)> LoadAsync(CancellationToken token = default)
    {
        writer.CleanTemporaryFiles(paths.Root);
        var main = await TryReadAsync<ApplicationSettings>(paths.SettingsFile, token);
        if (main is not null) return (main, null);
        var backup = await TryReadAsync<ApplicationSettings>(paths.SettingsFile + ".backup", token);
        return backup is not null ? (backup, "Settings were recovered from the last known-good backup.") : (new ApplicationSettings(), File.Exists(paths.SettingsFile) ? "Settings were corrupt; safe defaults are active." : null);
    }
    public Task SaveAsync(ApplicationSettings settings, CancellationToken token = default) => writer.WriteJsonAsync(paths.SettingsFile, settings, token);
    private static async Task<T?> TryReadAsync<T>(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return default;
        try { await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: token); }
        catch (JsonException) { return default; } catch (IOException) { return default; }
    }
}

public sealed class JsonProfileRepository(StoragePaths paths, AtomicFileWriter writer)
{
    public async Task<IReadOnlyList<SoundProfile>> ListAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(paths.ProfilesDirectory); writer.CleanTemporaryFiles(paths.ProfilesDirectory);
        var profiles = new List<SoundProfile>();
        foreach (var file in Directory.EnumerateFiles(paths.ProfilesDirectory, "*.json"))
        {
            var profile = await TryReadAsync(file, token);
            if (profile is not null) profiles.Add(profile);
        }
        return profiles;
    }
    public Task SaveAsync(SoundProfile profile, CancellationToken token = default)
    {
        if (!IsSafeId(profile.Id)) throw new ArgumentException("Profile ID contains unsupported characters.");
        return writer.WriteJsonAsync(Path.Combine(paths.ProfilesDirectory, profile.Id + ".json"), profile, token);
    }
    private static bool IsSafeId(string id) => !string.IsNullOrWhiteSpace(id) && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static async Task<SoundProfile?> TryReadAsync(string path, CancellationToken token)
    {
        try { await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<SoundProfile>(stream, cancellationToken: token); }
        catch (JsonException) { return null; } catch (IOException) { return null; }
    }
}
