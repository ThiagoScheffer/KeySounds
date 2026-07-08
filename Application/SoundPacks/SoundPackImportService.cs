using KeySounds.Core.Profiles;
using KeySounds.Core.SoundPacks;
using KeySounds.Infrastructure.Persistence;
using KeySounds.Audio;

namespace KeySounds.Application.SoundPacks;

public sealed class SoundPackImportService(StoragePaths paths, SoundPackValidationService validator, SoundPackCatalogService catalog, Func<IReadOnlyList<SoundProfile>> profiles, ISoundSampleCache? cache = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _shutdown;

    public async Task<SoundPackImportResult> ImportFolderAsync(string source, SoundPackConflictPolicy policy, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        string? temporary = null, backup = null;
        try
        {
            ThrowIfShutdown();
            var validation = await validator.ValidateAsync(source, token);
            if (!validation.IsValid || validation.Manifest is null) return new(false, ErrorCode: validation.Issues.FirstOrDefault()?.Code, Message: validation.Issues.FirstOrDefault()?.Message);
            var manifest = validation.Manifest;
            if (manifest.Id == "builtin") return new(false, manifest.Id, "BuiltInProtected", "The built-in pack cannot be imported or replaced.");
            var destination = Path.Combine(paths.SoundPacksDirectory, manifest.Id);
            if (Directory.Exists(destination) && policy == SoundPackConflictPolicy.Cancel) return new(false, manifest.Id, "AlreadyInstalled", "A pack with this ID is already installed.");
            temporary = Path.Combine(paths.SoundPacksDirectory, $".import-{Guid.NewGuid():N}"); Directory.CreateDirectory(temporary);
            await CopyFileAsync(Path.Combine(source, "manifest.json"), Path.Combine(temporary, "manifest.json"), token);
            var sourceRoot = EnsureSeparator(Path.GetFullPath(source));
            foreach (var relative in manifest.SoundSets.SelectMany(set => set.Files).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                SoundPackValidationService.TryResolvePath(sourceRoot, relative, out var sourceFile, out _);
                var target = Path.Combine(temporary, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                await CopyFileAsync(sourceFile, target, token);
            }
            var copiedValidation = await validator.ValidateAsync(temporary, token);
            if (!copiedValidation.IsValid) return new(false, manifest.Id, "CopiedPackInvalid", copiedValidation.Issues.First().Message);
            if (Directory.Exists(destination)) { backup = Path.Combine(paths.SoundPacksDirectory, $".replace-{Guid.NewGuid():N}"); Directory.Move(destination, backup); }
            try { Directory.Move(temporary, destination); temporary = null; }
            catch { if (backup is not null && Directory.Exists(backup) && !Directory.Exists(destination)) Directory.Move(backup, destination); throw; }
            if (backup is not null && Directory.Exists(backup)) { try { Directory.Delete(backup, true); } catch (IOException) { } }
            await catalog.RefreshAsync(token);
            return new(true, manifest.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(false, ErrorCode: "ImportFailed", Message: ex.Message); }
        finally
        {
            if (temporary is not null && Directory.Exists(temporary)) Directory.Delete(temporary, true);
            _gate.Release();
        }
    }

    public async Task<SoundPackRemovalResult> RemoveAsync(string packId, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            ThrowIfShutdown();
            if (!catalog.TryGetPack(packId, out var pack)) return new(false, "NotFound", "Sound pack is not installed.", []);
            if (pack.IsBuiltIn) return new(false, "BuiltInProtected", "The built-in pack cannot be removed.", []);
            var references = profiles().Where(profile => profile.Groups.Values.Any(group => group.SoundPackId == packId) || profile.KeyOverrides.Values.Any(item => item.SoundPackId == packId)).Select(profile => profile.Id).Distinct().ToArray();
            if (references.Length > 0) return new(false, "PackReferenced", "The pack is referenced by saved profiles.", references);
            var trash = Path.Combine(paths.SoundPacksDirectory, $".trash-{Guid.NewGuid():N}"); Directory.Move(pack.RootDirectory, trash);
            try { Directory.Delete(trash, true); } catch { if (!Directory.Exists(pack.RootDirectory)) Directory.Move(trash, pack.RootDirectory); throw; }
            if (cache is not null) await cache.RemovePackAsync(packId, token);
            await catalog.RefreshAsync(token); return new(true, null, null, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(false, "RemovalFailed", ex.Message, []); }
        finally { _gate.Release(); }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, token); await output.FlushAsync(token); output.Flush(true);
    }
    private static string EnsureSeparator(string path) => Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
    private void ThrowIfShutdown() { if (_shutdown) throw new ObjectDisposedException(nameof(SoundPackImportService)); }
    public void Shutdown() => _shutdown = true;
    public async ValueTask DisposeAsync() { _shutdown = true; await _gate.WaitAsync(); _gate.Release(); _gate.Dispose(); }
}
