using KeySounds.Application.Routing;
using KeySounds.Audio;
using KeySounds.Core.Audio;
using KeySounds.Core.Profiles;
using KeySounds.Core.SoundPacks;

namespace KeySounds.Application.SoundPacks;

public sealed class SoundPreviewService(ISoundSampleCache cache, IAudioEngine audio, SoundPackCatalogService catalog, SoundVariationSelector variations) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _shutdown;
    public async Task PreviewSoundSetAsync(string packId, string soundSetId, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            ThrowIfShutdown(); var reference = new SoundSetReference(packId, soundSetId);
            var result = await cache.PrepareAsync([reference], token); if (!result.Success) throw new InvalidOperationException(result.Message);
            if (!catalog.TryGetFiles(packId, soundSetId, out var files)) throw new InvalidOperationException("Sound set is unavailable.");
            var selected = variations.Select($"preview/{packId}/{soundSetId}", files, VariationSelectionMode.AvoidImmediateRepeat);
            await PreviewSampleCoreAsync(new(packId, soundSetId, selected.Replace('\\', '/')), token);
        }
        finally { _gate.Release(); }
    }
    public async Task PreviewSampleAsync(SoundId id, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            ThrowIfShutdown();
            if (!cache.TryGet(id, out _))
            {
                var result = await cache.PrepareAsync([new(id.PackId, id.SoundSetId)], token);
                if (!result.Success) throw new InvalidOperationException(result.Message);
            }
            await PreviewSampleCoreAsync(id, token);
        }
        finally { _gate.Release(); }
    }
    private Task PreviewSampleCoreAsync(SoundId id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (!cache.TryGet(id, out _)) throw new InvalidOperationException("Sample is not prepared.");
        audio.Play(new(id, .65f, 0)); return Task.CompletedTask;
    }
    private void ThrowIfShutdown() { if (_shutdown) throw new ObjectDisposedException(nameof(SoundPreviewService)); }
    public void Shutdown() => _shutdown = true;
    public async ValueTask DisposeAsync() { _shutdown = true; await _gate.WaitAsync(); _gate.Release(); _gate.Dispose(); }
}
