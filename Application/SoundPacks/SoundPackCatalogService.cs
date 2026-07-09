using System.Text.Json;
using Avalonia.Platform;
using KeySounds.Application.Routing;
using KeySounds.Core.SoundPacks;
using KeySounds.Infrastructure.Persistence;

namespace KeySounds.Application.SoundPacks;

public sealed class SoundPackCatalogService(StoragePaths paths, SoundPackValidationService validator) : ISoundSetCatalog, IAsyncDisposable
{
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private IReadOnlyDictionary<string, InstalledSoundPack> _snapshot = new Dictionary<string, InstalledSoundPack>(StringComparer.OrdinalIgnoreCase);
    private bool _shutdown;
    public IReadOnlyCollection<InstalledSoundPack> Packs => Volatile.Read(ref _snapshot).Values.ToArray();

    public async Task InitializeAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(paths.SoundPacksDirectory);
        foreach (var stale in Directory.EnumerateDirectories(paths.SoundPacksDirectory, ".import-*")) Directory.Delete(stale, true);
        foreach (var stale in Directory.EnumerateDirectories(paths.SoundPacksDirectory, ".trash-*")) Directory.Delete(stale, true);
        foreach (var stale in Directory.EnumerateDirectories(paths.SoundPacksDirectory, ".replace-*"))
        {
            var validation = await validator.ValidateAsync(stale, token);
            var destination = validation.Manifest is null ? null : Path.Combine(paths.SoundPacksDirectory, validation.Manifest.Id);
            if (validation.IsValid && destination is not null && !Directory.Exists(destination)) Directory.Move(stale, destination);
            else Directory.Delete(stale, true);
        }
        await EnsureFallbackAsync(token);
        await RefreshAsync(token);
    }

    private async Task EnsureFallbackAsync(CancellationToken token)
    {
        var root = Path.Combine(paths.SoundPacksDirectory, "builtin");
        var sample = Path.Combine(root, "samples", "key-click.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(sample)!);
        if (!File.Exists(sample))
        {
            await using (var source = AssetLoader.Open(new Uri("avares://KeySounds/Assets/Audio/key-click.wav")))
            await using (var destination = new FileStream(sample, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            { await source.CopyToAsync(destination, token); await destination.FlushAsync(token); destination.Flush(true); }
        }
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            var manifest = new SoundPackManifest { Id = "builtin", Name = "Built-in Click", Author = "KeySounds", SoundSets = [new() { Id = "click", Name = "Click", Files = ["samples/key-click.wav"] }] };
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), token);
        }
    }

    public async Task RefreshAsync(CancellationToken token = default)
    {
        await _mutation.WaitAsync(token);
        try
        {
            ThrowIfShutdown();
            var packs = new Dictionary<string, InstalledSoundPack>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.EnumerateDirectories(paths.SoundPacksDirectory).Where(path => !Path.GetFileName(path).StartsWith('.')))
            {
                var validation = await validator.ValidateAsync(directory, token);
                var key = validation.Manifest?.Id ?? $"invalid:{Path.GetFileName(directory)}";
                if (!packs.ContainsKey(key)) packs[key] = new(validation.Manifest, directory, string.Equals(validation.Manifest?.Id, "builtin", StringComparison.OrdinalIgnoreCase), validation);
                else
                {
                    var duplicate = new SoundPackValidationResult(false, validation.Issues.Concat([new ValidationIssue("DuplicatePackId", $"Pack ID '{key}' is already installed.")]).ToArray(), validation.EstimatedDecodedBytes, validation.SampleCount, validation.Manifest);
                    packs[$"duplicate:{Path.GetFileName(directory)}"] = new(validation.Manifest, directory, false, duplicate);
                }
            }
            Volatile.Write(ref _snapshot, packs);
        }
        finally { _mutation.Release(); }
    }

    public bool TryGetFiles(string packId, string setId, out IReadOnlyList<string> files)
    {
        files = [];
        if (!Volatile.Read(ref _snapshot).TryGetValue(packId, out var pack) || !pack.Validation.IsValid || pack.Manifest is null) return false;
        var set = pack.Manifest.SoundSets.FirstOrDefault(item => string.Equals(item.Id, setId, StringComparison.OrdinalIgnoreCase));
        if (set is null) return false;
        files = set.Files.ToArray(); return true;
    }

    public bool TryGetPack(string packId, out InstalledSoundPack pack) => Volatile.Read(ref _snapshot).TryGetValue(packId, out pack!);
    private void ThrowIfShutdown() { if (_shutdown) throw new ObjectDisposedException(nameof(SoundPackCatalogService)); }
    public async ValueTask DisposeAsync() { _shutdown = true; await _mutation.WaitAsync(); _mutation.Release(); _mutation.Dispose(); }
}
