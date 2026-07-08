using KeySounds.Application.SoundPacks;
using KeySounds.Core.Audio;
using KeySounds.Core.SoundPacks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KeySounds.Audio;

public sealed class CachedSound
{
    public CachedSound(SoundId id, float[] samples) { Id = id; Samples = samples; }
    public SoundId Id { get; }
    public float[] Samples { get; }
    public long DecodedBytes => checked((long)Samples.Length * sizeof(float));
}

public sealed record SamplePreparationResult(bool Success, string? ErrorCode = null, string? Message = null, IReadOnlyList<SoundId>? PreparedSamples = null);
public sealed record SampleCacheSnapshot(int LoadedSampleCount, long DecodedBytes, int LoadedPackCount, bool IsPreparing, long MissingSampleCount);

public interface ISoundDecoder { Task<CachedSound> DecodeAsync(SoundId id, string path, CancellationToken token); }

public sealed class NAudioSoundDecoder : ISoundDecoder
{
    public Task<CachedSound> DecodeAsync(SoundId id, string path, CancellationToken token)
    {
        using var reader = new WaveFileReader(path);
        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.SampleRate != 48000) source = new WdlResamplingSampleProvider(source, 48000);
        if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
        if (source.WaveFormat.Channels != 2) throw new NotSupportedException("Only mono and stereo WAV samples are supported.");
        var values = new List<float>(); var buffer = new float[4096]; int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); for (var index = 0; index < read; index++) values.Add(buffer[index]); }
        if (values.Count == 0) throw new InvalidDataException("Decoded sample is empty.");
        return Task.FromResult(new CachedSound(id, values.ToArray()));
    }
}

public interface ISoundSampleCache : IAsyncDisposable
{
    bool TryGet(SoundId id, out CachedSound sample);
    Task<SamplePreparationResult> PrepareAsync(IReadOnlyCollection<SoundSetReference> requiredSets, CancellationToken token = default);
    Task ReleaseUnusedAsync(IReadOnlyCollection<SoundSetReference> requiredSets, CancellationToken token = default);
    Task RemovePackAsync(string packId, CancellationToken token = default);
    SampleCacheSnapshot GetSnapshot();
    void RecordMissingSample();
}

public sealed class SoundSampleCache(SoundPackCatalogService catalog, ISoundDecoder decoder, long maximumDecodedBytes = 256L * 1024 * 1024) : ISoundSampleCache
{
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private IReadOnlyDictionary<SoundId, CachedSound> _snapshot = new Dictionary<SoundId, CachedSound>();
    private int _preparing, _shutdown; private long _missing;
    public bool TryGet(SoundId id, out CachedSound sample) => Volatile.Read(ref _snapshot).TryGetValue(id, out sample!);

    public async Task<SamplePreparationResult> PrepareAsync(IReadOnlyCollection<SoundSetReference> requiredSets, CancellationToken token = default)
    {
        if (Volatile.Read(ref _shutdown) != 0) return new(false, "CacheShutdown", "Sample cache is shutting down.");
        await _mutation.WaitAsync(token); Interlocked.Exchange(ref _preparing, 1);
        try
        {
            if (Volatile.Read(ref _shutdown) != 0) return new(false, "CacheShutdown", "Sample cache is shutting down.");
            var candidate = new Dictionary<SoundId, CachedSound>(Volatile.Read(ref _snapshot));
            var prepared = new List<SoundId>();
            foreach (var reference in requiredSets.Distinct())
            {
                token.ThrowIfCancellationRequested();
                if (!catalog.TryGetPack(reference.PackId, out var pack) || !pack.Validation.IsValid || pack.Manifest is null) return new(false, "UnknownPack", $"Sound pack '{reference.PackId}' is unavailable.");
                var set = pack.Manifest.SoundSets.FirstOrDefault(item => string.Equals(item.Id, reference.SoundSetId, StringComparison.OrdinalIgnoreCase));
                if (set is null || set.Files.Count == 0) return new(false, "UnknownSoundSet", $"Sound set '{reference.SoundSetId}' is unavailable.");
                foreach (var relative in set.Files)
                {
                    var id = new SoundId(pack.Manifest.Id, set.Id, relative.Replace('\\', '/'));
                    if (candidate.ContainsKey(id)) { prepared.Add(id); continue; }
                    SoundPackValidationService.TryResolvePath(Path.GetFullPath(pack.RootDirectory) + Path.DirectorySeparatorChar, relative, out var path, out _);
                    CachedSound decoded;
                    try { decoded = await decoder.DecodeAsync(id, path, token); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { return new(false, "DecodeFailed", ex.Message); }
                    var bytes = checked(candidate.Values.Sum(sample => sample.DecodedBytes) + decoded.DecodedBytes);
                    if (bytes > maximumDecodedBytes) return new(false, "DecodedMemoryLimit", "Preparing samples would exceed the cache memory budget.");
                    candidate[id] = decoded; prepared.Add(id);
                }
            }
            Volatile.Write(ref _snapshot, candidate);
            return new(true, PreparedSamples: prepared.AsReadOnly());
        }
        finally { Interlocked.Exchange(ref _preparing, 0); _mutation.Release(); }
    }

    public async Task ReleaseUnusedAsync(IReadOnlyCollection<SoundSetReference> requiredSets, CancellationToken token = default)
    {
        await _mutation.WaitAsync(token);
        try
        {
            var keep = new HashSet<(string Pack, string Set)>(requiredSets.Select(item => (item.PackId, item.SoundSetId)));
            var next = Volatile.Read(ref _snapshot).Where(item => keep.Contains((item.Key.PackId, item.Key.SoundSetId))).ToDictionary();
            Volatile.Write(ref _snapshot, next);
        }
        finally { _mutation.Release(); }
    }

    public SampleCacheSnapshot GetSnapshot()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return new(snapshot.Count, snapshot.Values.Sum(sample => sample.DecodedBytes), snapshot.Keys.Select(id => id.PackId).Distinct().Count(), Volatile.Read(ref _preparing) != 0, Interlocked.Read(ref _missing));
    }
    public void RecordMissingSample() => Interlocked.Increment(ref _missing);
    public async Task RemovePackAsync(string packId, CancellationToken token = default)
    {
        await _mutation.WaitAsync(token);
        try { Volatile.Write(ref _snapshot, Volatile.Read(ref _snapshot).Where(item => !string.Equals(item.Key.PackId, packId, StringComparison.OrdinalIgnoreCase)).ToDictionary()); }
        finally { _mutation.Release(); }
    }
    public async ValueTask DisposeAsync() { Interlocked.Exchange(ref _shutdown, 1); await _mutation.WaitAsync(); Volatile.Write(ref _snapshot, new Dictionary<SoundId, CachedSound>()); _mutation.Release(); _mutation.Dispose(); }
}
