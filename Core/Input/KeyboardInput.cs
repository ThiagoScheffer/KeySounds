namespace KeySounds.Core.Input;

public enum KeyboardAction { KeyDown, KeyUp }

public readonly record struct KeyboardInputEvent(
    ushort ScanCode, ushort VirtualKey, KeyboardAction Action,
    nint DeviceHandle, bool IsExtended, long Timestamp);

public sealed class InputFailureEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
public sealed class KeyboardDeviceChangeEventArgs(nint deviceHandle, bool arrived) : EventArgs { public nint DeviceHandle { get; } = deviceHandle; public bool Arrived { get; } = arrived; }

public interface IKeyboardInputSource : IAsyncDisposable
{
    event EventHandler<KeyboardInputEvent>? InputReceived;
    event EventHandler<InputFailureEventArgs>? Failure;
    event EventHandler<KeyboardDeviceChangeEventArgs>? DeviceChanged { add { } remove { } }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
