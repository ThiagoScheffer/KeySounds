using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using KeySounds.Core.Input;

namespace KeySounds.Platform.Windows.Input;

public sealed class WindowsRawInputSource : IKeyboardInputSource
{
    private enum InputLifecycleState { Stopped, Starting, Running, Stopping, Faulted }
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private const uint MaximumRawInputSize = 64 * 1024;
    private readonly object _gate = new();
    private InputLifecycleState _state;
    private Thread? _thread;
    private CancellationTokenSource? _lifetime;
    private TaskCompletionSource<nint>? _windowReady;
    private TaskCompletionSource<object?>? _startup;
    private TaskCompletionSource<object?>? _threadExited;
    private Task? _stopTask;
    private nint _window;
    private NativeMethods.WindowProc? _windowProc;
    private int _processingFaultReported;

    public event EventHandler<KeyboardInputEvent>? InputReceived;
    public event EventHandler<InputFailureEventArgs>? Failure;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task startup;
        lock (_gate)
        {
            if (_state is InputLifecycleState.Starting or InputLifecycleState.Running) return _startup!.Task.WaitAsync(cancellationToken);
            if (_state is InputLifecycleState.Stopping) throw new InvalidOperationException("Raw Input is stopping.");
            _state = InputLifecycleState.Starting;
            _lifetime = new CancellationTokenSource();
            _windowReady = NewCompletion<nint>();
            _startup = NewCompletion<object?>();
            _threadExited = NewCompletion<object?>();
            _stopTask = null;
            _processingFaultReported = 0;
            _thread = new Thread(MessageLoop) { IsBackground = true, Name = "KeySounds Raw Input" };
            _thread.Start();
            startup = _startup.Task;
        }
        return startup.WaitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_state == InputLifecycleState.Stopped) return Task.CompletedTask;
            return (_stopTask ??= StopCoreAsync()).WaitAsync(cancellationToken);
        }
    }

    private async Task StopCoreAsync()
    {
        Task<nint>? windowReady;
        Task? threadExited;
        lock (_gate)
        {
            _state = InputLifecycleState.Stopping;
            _lifetime?.Cancel();
            windowReady = _windowReady?.Task;
            threadExited = _threadExited?.Task;
        }

        if (threadExited is null) return;
        if (!threadExited.IsCompleted && windowReady is not null)
        {
            var first = await Task.WhenAny(windowReady, threadExited).ConfigureAwait(false);
            if (first == windowReady && windowReady.IsCompletedSuccessfully && windowReady.Result != 0)
                NativeMethods.PostMessage(windowReady.Result, NativeMethods.WmClose, 0, 0);
        }

        try { await threadExited.WaitAsync(ShutdownTimeout).ConfigureAwait(false); }
        catch (TimeoutException ex) { throw new TimeoutException("The Raw Input thread did not terminate within five seconds.", ex); }

        lock (_gate)
        {
            _state = InputLifecycleState.Stopped;
            _thread = null;
            _lifetime?.Dispose();
            _lifetime = null;
        }
    }

    private void MessageLoop()
    {
        nint buffer = 0;
        uint bufferCapacity = 0;
        var instance = NativeMethods.GetModuleHandle(null);
        var className = $"KeySounds.RawInput.{Environment.ProcessId}.{Guid.NewGuid():N}";
        var classRegistered = false;
        var rawInputRegistered = false;
        nint classNamePointer = 0;
        Exception? failure = null;
        try
        {
            _windowProc = WindowProcedure;
            classNamePointer = Marshal.StringToHGlobalUni(className);
            var wc = new NativeMethods.WndClass { instance = instance, className = classNamePointer, wndProc = Marshal.GetFunctionPointerForDelegate(_windowProc) };
            if (NativeMethods.RegisterClass(ref wc) == 0) Throw("RegisterClassW");
            classRegistered = true;
            _window = NativeMethods.CreateWindowEx(0, className, string.Empty, 0, 0, 0, 0, 0, 0, 0, instance, 0);
            if (_window == 0) Throw("CreateWindowExW");
            _windowReady!.TrySetResult(_window);

            if (_lifetime!.IsCancellationRequested) return;
            var devices = new[] { new NativeMethods.RawInputDevice { usagePage = 0x01, usage = 0x06, flags = NativeMethods.RidevInputSink, target = _window } };
            if (!NativeMethods.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<NativeMethods.RawInputDevice>())) Throw("RegisterRawInputDevices");
            rawInputRegistered = true;
            if (_lifetime.IsCancellationRequested) return;
            lock (_gate) { if (_state == InputLifecycleState.Starting) _state = InputLifecycleState.Running; }
            _startup!.TrySetResult(null);

            while (true)
            {
                var result = NativeMethods.GetMessage(out var message, 0, 0, 0);
                if (result > 0) { NativeMethods.TranslateMessage(ref message); NativeMethods.DispatchMessage(ref message); continue; }
                if (result == 0) break;
                Throw("GetMessageW");
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            _startup?.TrySetException(ex);
            ReportFailure(ex);
        }
        finally
        {
            Exception? cleanupFailure = null;
            if (rawInputRegistered)
            {
                var remove = new[] { new NativeMethods.RawInputDevice { usagePage = 0x01, usage = 0x06, flags = NativeMethods.RidevRemove, target = 0 } };
                if (!NativeMethods.RegisterRawInputDevices(remove, 1, (uint)Marshal.SizeOf<NativeMethods.RawInputDevice>())) cleanupFailure = Error("RegisterRawInputDevices(RIDEV_REMOVE)");
            }
            if (_window != 0 && !NativeMethods.DestroyWindow(_window)) cleanupFailure ??= Error("DestroyWindow");
            _window = 0;
            if (buffer != 0) Marshal.FreeHGlobal(buffer);
            if (classRegistered && !NativeMethods.UnregisterClass(className, instance)) cleanupFailure ??= Error("UnregisterClassW");
            if (classNamePointer != 0) Marshal.FreeHGlobal(classNamePointer);
            _windowProc = null;
            _windowReady?.TrySetResult(0);
            if (!_startup!.Task.IsCompleted)
            {
                if (_lifetime?.IsCancellationRequested == true) _startup.TrySetCanceled(_lifetime.Token);
                else _startup.TrySetException(failure ?? cleanupFailure ?? new InvalidOperationException("Raw Input stopped during startup."));
            }
            if (cleanupFailure is not null) ReportFailure(cleanupFailure);
            lock (_gate) { if (_state != InputLifecycleState.Stopping) _state = failure is null && cleanupFailure is null ? InputLifecycleState.Stopped : InputLifecycleState.Faulted; }
            _threadExited!.TrySetResult(null);
        }

        nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
        {
            try
            {
                if (message == NativeMethods.WmInput) ReadInput(lParam, ref buffer, ref bufferCapacity);
                else if (message == NativeMethods.WmClose)
                {
                    if (!NativeMethods.DestroyWindow(hwnd)) Throw("DestroyWindow");
                    return 0;
                }
                else if (message == NativeMethods.WmDestroy)
                {
                    _window = 0;
                    NativeMethods.PostQuitMessage(0);
                    return 0;
                }
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _processingFaultReported, 1) == 0) ReportFailure(ex);
                NativeMethods.PostMessage(hwnd, NativeMethods.WmClose, 0, 0);
            }
            try { return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam); }
            catch { return 0; }
        }
    }

    private unsafe void ReadInput(nint handle, ref nint buffer, ref uint capacity)
    {
        uint required = 0;
        var headerSize = (uint)Marshal.SizeOf<NativeMethods.RawInputHeader>();
        var query = NativeMethods.GetRawInputData(handle, NativeMethods.RidInput, 0, ref required, headerSize);
        if (query == uint.MaxValue) Throw("GetRawInputData(size)");
        if (required == 0 || required > MaximumRawInputSize) throw new InvalidDataException("Raw Input reported an invalid buffer size.");
        if (required > capacity) { buffer = Marshal.ReAllocHGlobal(buffer, checked((nint)required)); capacity = required; }
        var returnedSize = required;
        var returned = NativeMethods.GetRawInputData(handle, NativeMethods.RidInput, buffer, ref returnedSize, headerSize);
        if (returned == uint.MaxValue) Throw("GetRawInputData(data)");
        var available = Math.Min(returned, returnedSize);
        if (available < headerSize) throw new InvalidDataException("Raw Input header was truncated.");
        var bytes = new ReadOnlySpan<byte>((void*)buffer, checked((int)available));
        var header = MemoryMarshal.Read<NativeMethods.RawInputHeader>(bytes);
        if (header.type != NativeMethods.RimTypeKeyboard) return;
        if (!RawInputParser.TryParse(bytes, out var input)) throw new InvalidDataException("Raw keyboard payload was truncated.");
        InputReceived?.Invoke(this, input);
    }

    private void ReportFailure(Exception exception)
    {
        try { Failure?.Invoke(this, new InputFailureEventArgs(exception)); }
        catch { /* Subscriber failures must never cross the unmanaged callback boundary. */ }
    }
    private static TaskCompletionSource<T> NewCompletion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Win32Exception Error(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, $"{operation} failed with Win32 error {error}.");
    }
    private static void Throw(string operation) => throw Error(operation);
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
