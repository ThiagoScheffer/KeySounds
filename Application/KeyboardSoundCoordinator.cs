using System.Threading.Channels;
using KeySounds.Core.Audio;
using KeySounds.Core.Input;
using KeySounds.Application.Routing;

namespace KeySounds.Application;

public sealed class KeyboardSoundCoordinator : IAsyncDisposable
{
    private readonly IKeyboardInputSource _input;
    private readonly IAudioEngine _audio;
    private readonly Channel<KeyboardInputEvent> _events = Channel.CreateBounded<KeyboardInputEvent>(new BoundedChannelOptions(256)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    private readonly KeyboardSoundRouter _router;
    private readonly IKeyboardDeviceIdentityService? _identity;
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _cts;
    private Task? _consumer;
    private Task? _startTask;
    private Task? _stopTask;
    private bool _terminal;
    private TaskCompletionSource<KeyIdentifier>? _capture;

    public KeyboardSoundCoordinator(IKeyboardInputSource input, IAudioEngine audio, KeyboardSoundRouter? router = null, IKeyboardDeviceIdentityService? identity = null)
    {
        _input = input; _audio = audio;
        _router = router ?? Phase2Defaults.CreateRouter();
        _identity = identity;
    }
    public bool IsEnabled { get; set; } = true;
    public string InputStatus { get; private set; } = "Stopped";
    public string AudioStatus { get; private set; } = "Stopped";
    public string? ErrorMessage { get; private set; }
    private long _clickCount;
    public long ClickCount => Interlocked.Read(ref _clickCount);
    public event EventHandler? StatusChanged;

    public Task StartAsync(CancellationToken token = default)
    {
        lock (_lifecycleGate)
        {
            if (_terminal) throw new ObjectDisposedException(nameof(KeyboardSoundCoordinator));
            return (_startTask ??= StartCoreAsync()).WaitAsync(token);
        }
    }

    private async Task StartCoreAsync()
    {
        _cts = new CancellationTokenSource();
        try { AudioStatus = "Starting"; Changed(); await _audio.InitializeAsync(_cts.Token); AudioStatus = _audio.IsInitialized ? "Active" : "Device error"; if (!_audio.IsInitialized) ErrorMessage = "No usable audio output is currently available."; }
        catch (Exception ex) { AudioStatus = ex is FileNotFoundException ? "Missing sample" : "Device error"; ErrorMessage = ex.Message; }
        try
        {
            _cts.Token.ThrowIfCancellationRequested();
            InputStatus = "Starting"; Changed();
            _input.InputReceived += OnInput;
            _input.Failure += OnInputFailure;
            _input.DeviceChanged += OnDeviceChanged;
            _consumer = ConsumeAsync(_cts.Token);
            await _input.StartAsync(_cts.Token);
            InputStatus = "Active";
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { InputStatus = "Stopped"; }
        catch (Exception ex) { InputStatus = "Error"; ErrorMessage = ex.Message; }
        Changed();
    }

    private void OnInput(object? sender, KeyboardInputEvent value)
    {
        _identity?.RequestResolution(value.DeviceHandle);
        var capture = Volatile.Read(ref _capture);
        if (capture is not null && value.Action == KeyboardAction.KeyDown && capture.TrySetResult(new(value.ScanCode, value.IsExtended)))
        {
            Interlocked.CompareExchange(ref _capture, null, capture);
            return;
        }
        _events.Writer.TryWrite(value);
    }
    private void OnDeviceChanged(object? sender, KeyboardDeviceChangeEventArgs value) { _identity?.NotifyDeviceChange(value.DeviceHandle, value.Arrived); if (!value.Arrived) _router.ClearDeviceState(value.DeviceHandle); }
    public async Task<KeyIdentifier> CaptureNextKeyAsync(CancellationToken token = default)
    {
        lock (_lifecycleGate) if (_terminal) throw new ObjectDisposedException(nameof(KeyboardSoundCoordinator));
        var completion = new TaskCompletionSource<KeyIdentifier>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _capture, completion, null) is not null) throw new InvalidOperationException("A key capture is already active.");
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try { return await completion.Task.ConfigureAwait(false); }
        finally { Interlocked.CompareExchange(ref _capture, null, completion); }
    }
    public void CancelKeyCapture() => Interlocked.Exchange(ref _capture, null)?.TrySetCanceled();
    private void OnInputFailure(object? sender, InputFailureEventArgs value) { InputStatus = "Error"; ErrorMessage = value.Exception.Message; Changed(); }

    private async Task ConsumeAsync(CancellationToken token)
    {
        try
        {
            await foreach (var value in _events.Reader.ReadAllAsync(token))
            {
                var trigger = _router.Route(value);
                if (!IsEnabled || !_audio.IsInitialized || trigger is null) continue;
                _audio.Play(trigger.Value); Interlocked.Increment(ref _clickCount);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public Task StopAsync(CancellationToken token = default)
    {
        lock (_lifecycleGate)
        {
            _terminal = true;
            return (_stopTask ??= StopCoreAsync()).WaitAsync(token);
        }
    }

    private async Task StopCoreAsync()
    {
        List<Exception>? failures = null;
        _input.InputReceived -= OnInput;
        _input.Failure -= OnInputFailure;
        _input.DeviceChanged -= OnDeviceChanged;
        _cts?.Cancel();
        CancelKeyCapture();
        try { await _input.StopAsync().ConfigureAwait(false); } catch (Exception ex) { (failures ??= []).Add(ex); }
        if (_consumer is not null) try { await _consumer.ConfigureAwait(false); } catch (Exception ex) { (failures ??= []).Add(ex); }
        _router.ClearState();
        try { await _audio.ShutdownAsync().ConfigureAwait(false); } catch (Exception ex) { (failures ??= []).Add(ex); }
        InputStatus = AudioStatus = "Stopped"; Changed();
        if (failures is not null) throw new AggregateException("One or more services failed to shut down cleanly.", failures);
    }

    private void Changed() => StatusChanged?.Invoke(this, EventArgs.Empty);
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _input.DisposeAsync().ConfigureAwait(false);
        await _audio.DisposeAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}
