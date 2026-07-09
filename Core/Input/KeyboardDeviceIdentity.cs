namespace KeySounds.Core.Input;

public readonly record struct KeyboardDeviceId
{
    public KeyboardDeviceId(string value) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Keyboard device ID is required."); Value = value; }
    public string Value { get; }
    public override string ToString() => Value;
}
public sealed record KeyboardDeviceDescriptor(KeyboardDeviceId Id, string DisplayName, string? Manufacturer, string? ProductName, ushort? VendorId, ushort? ProductId,
    bool IsConnected, bool IsPersistent, bool IsIdentityAmbiguous, int ConnectedSessionCount, DateTimeOffset LastSeenUtc, string? ErrorCode = null);
public sealed record KeyboardDeviceResolution(KeyboardDeviceId? StableId, bool IsPersistent, bool IsAmbiguous, string DisplayName, string? ErrorCode = null);
public sealed class KeyboardDevicesChangedEventArgs(IReadOnlyList<KeyboardDeviceDescriptor> devices) : EventArgs { public IReadOnlyList<KeyboardDeviceDescriptor> Devices { get; } = devices; }
public interface IKeyboardDeviceIdentityService : IAsyncDisposable
{
    KeyboardDeviceResolution ResolveCached(nint deviceHandle);
    ValueTask<KeyboardDeviceResolution> ResolveAsync(nint deviceHandle, CancellationToken token = default);
    void RequestResolution(nint deviceHandle);
    void NotifyDeviceChange(nint deviceHandle, bool arrived);
    Task RefreshAsync(CancellationToken token = default);
    IReadOnlyList<KeyboardDeviceDescriptor> GetConnectedDevicesSnapshot();
    event EventHandler<KeyboardDevicesChangedEventArgs>? DevicesChanged;
}
