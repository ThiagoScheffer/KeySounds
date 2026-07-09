using KeySounds.Application.Profiles;
using KeySounds.Application.Routing;
using KeySounds.Application.Settings;
using KeySounds.Audio;
using KeySounds.Core.Input;
using KeySounds.Core.Profiles;
using KeySounds.Core.SoundPacks;

namespace KeySounds.Application.Keyboards;

public sealed record KeyboardAssignmentResult(bool Success, string? ErrorCode = null, string? Message = null);
public sealed record PreparedProfileSnapshot(string ProfileId, CompiledSoundProfile Profile, IReadOnlySet<SoundSetReference> RequiredSoundSets);

public sealed class KeyboardProfileAssignmentService : IDeviceProfileResolver, IAsyncDisposable
{
    private readonly IKeyboardDeviceIdentityService _identity; private readonly Phase2SettingsService _settings; private readonly ActiveProfileService _global; private readonly ISoundSampleCache _cache;
    private readonly SemaphoreSlim _gate = new(1, 1); private IReadOnlyDictionary<KeyboardDeviceId, PreparedProfileSnapshot> _runtime = new Dictionary<KeyboardDeviceId, PreparedProfileSnapshot>(); private bool _shutdown;
    public KeyboardProfileAssignmentService(IKeyboardDeviceIdentityService identity, Phase2SettingsService settings, ActiveProfileService global, ISoundSampleCache cache) { _identity = identity; _settings = settings; _global = global; _cache = cache; _identity.DevicesChanged += OnDevicesChanged; }
    public event EventHandler? Changed;
    public CompiledSoundProfile? Resolve(nint handle)
    {
        var id = _identity.ResolveCached(handle).StableId; return id is not null && Volatile.Read(ref _runtime).TryGetValue(id.Value, out var prepared) ? prepared.Profile : null;
    }
    public string? GetAssignedProfileId(KeyboardDeviceId id) => _settings.Settings.KeyboardProfileAssignments.TryGetValue(id.Value, out var profile) ? profile : null;
    public bool IsRuntimeReady(KeyboardDeviceId id) => Volatile.Read(ref _runtime).ContainsKey(id);
    public async Task<KeyboardAssignmentResult> AssignAsync(KeyboardDeviceId id, string profileId, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_shutdown) return new(false, "Shutdown", "Keyboard assignments are shutting down.");
            var descriptor = _identity.GetConnectedDevicesSnapshot().FirstOrDefault(item => item.Id == id);
            if (descriptor is null || !descriptor.IsPersistent) return new(false, "SessionOnlyIdentity", "This keyboard cannot be assigned persistently.");
            var model = _settings.Profiles.FirstOrDefault(item => item.Id == profileId); if (model is null) return new(false, "ProfileMissing", "The selected profile does not exist.");
            var compiled = ProfileCompiler.Compile(model); var required = ProfileRequirements.GetRequiredSets(compiled); var prepared = await _cache.PrepareAsync(required, token);
            if (!prepared.Success) return new(false, prepared.ErrorCode, prepared.Message);
            var previousRuntime = Volatile.Read(ref _runtime); var nextRuntime = new Dictionary<KeyboardDeviceId, PreparedProfileSnapshot>(previousRuntime) { [id] = new(profileId, compiled, required.ToHashSet()) }; Volatile.Write(ref _runtime, nextRuntime);
            var assignments = new Dictionary<string, string>(_settings.Settings.KeyboardProfileAssignments, StringComparer.Ordinal) { [id.Value] = profileId };
            try { await _settings.SaveKeyboardAssignmentsAsync(assignments, token); }
            catch (Exception ex) { Volatile.Write(ref _runtime, previousRuntime); return new(false, "PersistenceFailed", ex.Message); }
            await ReleaseUnusedAsync(token); Changed?.Invoke(this, EventArgs.Empty); return new(true);
        }
        finally { _gate.Release(); }
    }
    public async Task<KeyboardAssignmentResult> ClearAsync(KeyboardDeviceId id, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_shutdown) return new(false, "Shutdown", "Keyboard assignments are shutting down.");
            var oldRuntime = Volatile.Read(ref _runtime); var next = new Dictionary<KeyboardDeviceId, PreparedProfileSnapshot>(oldRuntime); next.Remove(id); Volatile.Write(ref _runtime, next);
            var assignments = new Dictionary<string, string>(_settings.Settings.KeyboardProfileAssignments, StringComparer.Ordinal); assignments.Remove(id.Value);
            try { await _settings.SaveKeyboardAssignmentsAsync(assignments, token); } catch (Exception ex) { Volatile.Write(ref _runtime, oldRuntime); return new(false, "PersistenceFailed", ex.Message); }
            await ReleaseUnusedAsync(token); Changed?.Invoke(this, EventArgs.Empty); return new(true);
        }
        finally { _gate.Release(); }
    }
    public async Task ReconcileAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_shutdown) return; var next = new Dictionary<KeyboardDeviceId, PreparedProfileSnapshot>();
            foreach (var device in _identity.GetConnectedDevicesSnapshot().Where(item => item.IsPersistent))
            {
                if (!_settings.Settings.KeyboardProfileAssignments.TryGetValue(device.Id.Value, out var profileId)) continue;
                var model = _settings.Profiles.FirstOrDefault(item => item.Id == profileId); if (model is null) continue;
                var compiled = ProfileCompiler.Compile(model); var required = ProfileRequirements.GetRequiredSets(compiled); var preparation = await _cache.PrepareAsync(required, token);
                if (preparation.Success) next[device.Id] = new(profileId, compiled, required.ToHashSet());
            }
            Volatile.Write(ref _runtime, next); await ReleaseUnusedAsync(token); Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }
    private async Task ReleaseUnusedAsync(CancellationToken token)
    {
        var required = ProfileRequirements.GetRequiredSets(_global.Current).Concat(Volatile.Read(ref _runtime).Values.SelectMany(item => item.RequiredSoundSets)).Distinct().ToArray(); await _cache.ReleaseUnusedAsync(required, token);
    }
    private void OnDevicesChanged(object? sender, KeyboardDevicesChangedEventArgs args) { if (!_shutdown) _ = ReconcileSafeAsync(); }
    private async Task ReconcileSafeAsync() { try { await ReconcileAsync(); } catch { } }
    public async ValueTask DisposeAsync() { _shutdown = true; _identity.DevicesChanged -= OnDevicesChanged; await _gate.WaitAsync(); _gate.Release(); _gate.Dispose(); }
}
