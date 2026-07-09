using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using KeySounds.Core.Input;
using KeySounds.Platform.Windows.Input;

namespace KeySounds.Application.Keyboards;

public static class KeyboardDeviceIdentityDeriver
{
    public static KeyboardDeviceResolution Derive(KeyboardNativeMetadata? metadata, string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.DevicePath))
        {
            var normalized = metadata.DevicePath.Trim().Replace('/', '\\').ToUpperInvariant();
            var source = $"path|{normalized}|{metadata.VendorId:X4}|{metadata.ProductId:X4}";
            return new(new("kbd-v1-" + Hash(source)), true, false, Display(metadata));
        }
        if (metadata is not null && (metadata.VendorId is not null || metadata.ProductId is not null || !string.IsNullOrWhiteSpace(metadata.ProductName)))
        {
            var source = $"hardware|{metadata.VendorId:X4}|{metadata.ProductId:X4}|{metadata.UsagePage:X4}|{metadata.Usage:X4}|{metadata.ProductName?.Trim().ToUpperInvariant()}";
            return new(new("kbd-v1-" + Hash(source)), true, true, Display(metadata));
        }
        return new(new(sessionId), false, true, "Unknown keyboard", "SessionOnlyIdentity");
    }
    private static string Hash(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..32].ToLowerInvariant();
    private static string Display(KeyboardNativeMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.ProductName)) return string.IsNullOrWhiteSpace(metadata.Manufacturer) ? metadata.ProductName : $"{metadata.Manufacturer} {metadata.ProductName}";
        if (metadata.VendorId is not null || metadata.ProductId is not null) return $"Keyboard {metadata.VendorId:X4}:{metadata.ProductId:X4}";
        return "Keyboard device";
    }
}

public sealed class KeyboardDeviceIdentityService : IKeyboardDeviceIdentityService
{
    private sealed record CacheEntry(KeyboardDeviceResolution Resolution, KeyboardNativeMetadata? Metadata, DateTimeOffset ResolvedUtc);
    private readonly IKeyboardDeviceMetadataProvider _provider;
    private readonly Channel<nint> _requests = Channel.CreateBounded<nint>(new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<nint, TaskCompletionSource<KeyboardDeviceResolution>> _pending = new();
    private IReadOnlyDictionary<nint, CacheEntry> _cache = new Dictionary<nint, CacheEntry>();
    private IReadOnlyList<KeyboardDeviceDescriptor> _devices = [];
    private readonly CancellationTokenSource _lifetime = new(); private readonly Task _worker; private int _sessionCounter, _shutdown;
    public KeyboardDeviceIdentityService(IKeyboardDeviceMetadataProvider provider) { _provider = provider; _worker = ConsumeAsync(_lifetime.Token); }
    public event EventHandler<KeyboardDevicesChangedEventArgs>? DevicesChanged;
    public KeyboardDeviceResolution ResolveCached(nint handle) => Volatile.Read(ref _cache).TryGetValue(handle, out var entry) ? entry.Resolution : new(null, false, true, "Resolving keyboard", "NotResolved");
    public void RequestResolution(nint handle)
    {
        if (handle == 0 || Volatile.Read(ref _shutdown) != 0 || Volatile.Read(ref _cache).ContainsKey(handle)) return;
        var completion = new TaskCompletionSource<KeyboardDeviceResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(handle, completion)) return;
        if (!_requests.Writer.TryWrite(handle)) _pending.TryRemove(handle, out _);
    }
    public async ValueTask<KeyboardDeviceResolution> ResolveAsync(nint handle, CancellationToken token = default)
    {
        var cached = ResolveCached(handle); if (cached.StableId is not null) return cached;
        RequestResolution(handle); if (!_pending.TryGetValue(handle, out var completion)) return ResolveCached(handle);
        return await completion.Task.WaitAsync(token);
    }
    public async Task RefreshAsync(CancellationToken token = default)
    {
        if (Volatile.Read(ref _shutdown) != 0) throw new ObjectDisposedException(nameof(KeyboardDeviceIdentityService));
        var handles = await _provider.EnumerateAsync(token); var connected = handles.ToHashSet();
        var next = Volatile.Read(ref _cache).Where(item => connected.Contains(item.Key)).ToDictionary(); Volatile.Write(ref _cache, next);
        foreach (var handle in handles) RequestResolution(handle);
        foreach (var handle in handles) await ResolveAsync(handle, token);
        PublishDevices();
    }
    public void NotifyDeviceChange(nint handle, bool arrived)
    {
        if (Volatile.Read(ref _shutdown) != 0) return;
        if (arrived) RequestResolution(handle);
        else { var next = Volatile.Read(ref _cache).Where(item => item.Key != handle).ToDictionary(); Volatile.Write(ref _cache, next); _pending.TryRemove(handle, out var pending); pending?.TrySetCanceled(); PublishDevices(); }
    }
    public IReadOnlyList<KeyboardDeviceDescriptor> GetConnectedDevicesSnapshot() => Volatile.Read(ref _devices).ToArray();
    private async Task ConsumeAsync(CancellationToken token)
    {
        try
        {
            await foreach (var handle in _requests.Reader.ReadAllAsync(token))
            {
                if (!_pending.TryGetValue(handle, out var completion)) continue;
                try
                {
                    var metadata = await _provider.QueryAsync(handle, token); var resolution = KeyboardDeviceIdentityDeriver.Derive(metadata, $"session-kbd-{Interlocked.Increment(ref _sessionCounter)}");
                    var next = new Dictionary<nint, CacheEntry>(Volatile.Read(ref _cache));
                    if (next.Count >= 256) next.Remove(next.OrderBy(item => item.Value.ResolvedUtc).First().Key);
                    next[handle] = new(resolution, metadata, DateTimeOffset.UtcNow); Volatile.Write(ref _cache, next); completion.TrySetResult(resolution); PublishDevices();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { completion.TrySetCanceled(token); }
                catch { var resolution = KeyboardDeviceIdentityDeriver.Derive(null, $"session-kbd-{Interlocked.Increment(ref _sessionCounter)}"); var next = new Dictionary<nint, CacheEntry>(Volatile.Read(ref _cache)) { [handle] = new(resolution, null, DateTimeOffset.UtcNow) }; Volatile.Write(ref _cache, next); completion.TrySetResult(resolution); PublishDevices(); }
                finally { _pending.TryRemove(handle, out _); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private void PublishDevices()
    {
        var entries = Volatile.Read(ref _cache).Values.Where(entry => entry.Resolution.StableId is not null).GroupBy(entry => entry.Resolution.StableId!.Value);
        var snapshot = entries.Select(group =>
        {
            var first = group.First(); var meta = first.Metadata; var count = group.Count();
            return new KeyboardDeviceDescriptor(group.Key, first.Resolution.DisplayName, meta?.Manufacturer, meta?.ProductName, meta?.VendorId, meta?.ProductId, true,
                first.Resolution.IsPersistent, first.Resolution.IsAmbiguous || count > 1, count, group.Max(item => item.ResolvedUtc), first.Resolution.ErrorCode);
        }).OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Volatile.Write(ref _devices, snapshot); try { DevicesChanged?.Invoke(this, new(snapshot)); } catch { }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _shutdown, 1) != 0) return; _lifetime.Cancel(); _requests.Writer.TryComplete();
        foreach (var pending in _pending.Values) pending.TrySetCanceled(); await _worker; _lifetime.Dispose();
    }
}
