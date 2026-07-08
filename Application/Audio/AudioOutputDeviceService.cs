using KeySounds.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace KeySounds.Application.Audio;

public interface IAudioOutputDeviceService : IAsyncDisposable
{
    event EventHandler? DevicesChanged;
    Task<IReadOnlyList<AudioOutputDevice>> GetDevicesAsync(CancellationToken token = default);
    Task<AudioOutputResolution> ResolveAsync(AudioOutputSelection selection, CancellationToken token = default);
}

public sealed class WindowsAudioOutputDeviceService : IAudioOutputDeviceService, IMMNotificationClient
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private bool _disposed;
    public WindowsAudioOutputDeviceService() => _enumerator.RegisterEndpointNotificationCallback(this);
    public event EventHandler? DevicesChanged;

    public Task<IReadOnlyList<AudioOutputDevice>> GetDevicesAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<AudioOutputDevice>>(() =>
    {
        token.ThrowIfCancellationRequested(); string? defaultId = null;
        using var enumerator = new MMDeviceEnumerator();
        try { using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); defaultId = defaultDevice.ID; } catch { }
        var result = new Dictionary<string, AudioOutputDevice>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
            {
                token.ThrowIfCancellationRequested();
                try { if (!string.IsNullOrWhiteSpace(endpoint.ID)) result[endpoint.ID] = new(endpoint.ID, string.IsNullOrWhiteSpace(endpoint.FriendlyName) ? "Unnamed output" : endpoint.FriendlyName, endpoint.ID == defaultId, Map(endpoint.State)); }
                catch { }
                finally { endpoint.Dispose(); }
            }
        }
        catch { }
        return Normalize(result.Values);
    }, token);

    public async Task<AudioOutputResolution> ResolveAsync(AudioOutputSelection selection, CancellationToken token = default)
    {
        if (!selection.IsValid) return new(false, null, null, false, "InvalidSelection", "Output selection is invalid.");
        var devices = await GetDevicesAsync(token);
        if (selection.Kind == AudioOutputSelectionKind.SystemDefault)
        {
            var current = devices.FirstOrDefault(device => device.IsSystemDefault && device.Availability == AudioDeviceAvailability.Active);
            return current is null ? new(false, null, null, false, "NoOutputDevice", "No active system-default output is available.") : new(true, current.Id, current.DisplayName, false);
        }
        var requested = devices.FirstOrDefault(device => device.Id == selection.DeviceId && device.Availability == AudioDeviceAvailability.Active);
        return requested is null ? new(false, null, null, false, "DeviceUnavailable", "The configured output device is unavailable.") : new(true, requested.Id, requested.DisplayName, false);
    }
    private static AudioDeviceAvailability Map(DeviceState state) => state switch { DeviceState.Active => AudioDeviceAvailability.Active, DeviceState.Disabled => AudioDeviceAvailability.Disabled, DeviceState.Unplugged => AudioDeviceAvailability.Unplugged, DeviceState.NotPresent => AudioDeviceAvailability.NotPresent, _ => AudioDeviceAvailability.Unknown };
    internal static IReadOnlyList<AudioOutputDevice> Normalize(IEnumerable<AudioOutputDevice> devices) => devices.Where(device => !string.IsNullOrWhiteSpace(device.Id))
        .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).OrderByDescending(device => device.IsSystemDefault)
        .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    private void Changed() { try { DevicesChanged?.Invoke(this, EventArgs.Empty); } catch { } }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Changed(); public void OnDeviceAdded(string pwstrDeviceId) => Changed(); public void OnDeviceRemoved(string deviceId) => Changed();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { if (flow == DataFlow.Render && role == Role.Multimedia) Changed(); }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => Changed();
    public ValueTask DisposeAsync() { if (_disposed) return ValueTask.CompletedTask; _disposed = true; _enumerator.UnregisterEndpointNotificationCallback(this); _enumerator.Dispose(); return ValueTask.CompletedTask; }
}
