namespace KeySounds.Core.Input;

public enum KeyboardAction { KeyDown, KeyUp }

public readonly record struct KeyboardInputEvent(
    ushort ScanCode, ushort VirtualKey, KeyboardAction Action,
    nint DeviceHandle, bool IsExtended, long Timestamp);

public sealed class InputFailureEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

public interface IKeyboardInputSource : IAsyncDisposable
{
    event EventHandler<KeyboardInputEvent>? InputReceived;
    event EventHandler<InputFailureEventArgs>? Failure;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
