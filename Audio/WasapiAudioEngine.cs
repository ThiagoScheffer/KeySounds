using Avalonia.Platform;
using KeySounds.Application.Audio;
using KeySounds.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KeySounds.Audio;

public interface IAudioOutputHandle : IDisposable { void Stop(); }
public interface IAudioOutputFactory { IAudioOutputHandle Create(string deviceId, int latencyMilliseconds, ISampleProvider mixer, EventHandler<StoppedEventArgs> stopped); }

public sealed class NAudioOutputFactory : IAudioOutputFactory
{
    public IAudioOutputHandle Create(string deviceId, int latencyMilliseconds, ISampleProvider mixer, EventHandler<StoppedEventArgs> stopped)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(deviceId);
        var output = new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMilliseconds);
        try { output.PlaybackStopped += stopped; output.Init(mixer); output.Play(); return new Handle(output, device, stopped); }
        catch { output.PlaybackStopped -= stopped; output.Dispose(); device.Dispose(); throw; }
    }
    private sealed class Handle(WasapiOut output, MMDevice device, EventHandler<StoppedEventArgs> stopped) : IAudioOutputHandle
    {
        private int _disposed;
        public void Stop() { if (Volatile.Read(ref _disposed) == 0) output.Stop(); }
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; output.PlaybackStopped -= stopped; output.Dispose(); device.Dispose(); }
    }
}

public sealed class WasapiAudioEngine : ISelectableAudioEngine
{
    private readonly string _assetUri; private readonly ISoundSampleCache? _sampleCache; private readonly IAudioOutputDeviceService _devices; private readonly IAudioOutputFactory _factory;
    private readonly bool _ownsDeviceService; private readonly object _gate = new(); private readonly SemaphoreSlim _switchGate = new(1, 1);
    private MixingSampleProvider? _mixer; private CachedSound? _fallback; private IAudioOutputHandle? _output;
    private AudioEngineStatus _status = new(AudioEngineState.Uninitialized, AudioOutputSelection.SystemDefault, null, null, false, 20, 0);
    private Task? _shutdownTask, _reconcileTask; private CancellationTokenSource? _reconcileCancellation; private bool _shutdown, _fallbackAttempted; private int _activeVoices; private long _suppressed;

    public WasapiAudioEngine(string assetUri, ISoundSampleCache? sampleCache = null, IAudioOutputDeviceService? devices = null, IAudioOutputFactory? factory = null)
    {
        _assetUri = assetUri; _sampleCache = sampleCache; _devices = devices ?? new WindowsAudioOutputDeviceService(); _ownsDeviceService = devices is null; _factory = factory ?? new NAudioOutputFactory();
        _devices.DevicesChanged += OnDevicesChanged;
    }
    public bool IsInitialized { get { lock (_gate) return _status.State == AudioEngineState.Running; } }
    public AudioEngineStatus Status { get { lock (_gate) return _status with { ActiveVoiceCount = Volatile.Read(ref _activeVoices), SuppressedDuringSwitch = Interlocked.Read(ref _suppressed) }; } }
    public event EventHandler<AudioEngineStatus>? StatusChanged;
    public void ConfigureStartup(AudioOutputSelection selection, int latencyMilliseconds)
    {
        if (!selection.IsValid) selection = AudioOutputSelection.SystemDefault;
        lock (_gate) { if (_status.State != AudioEngineState.Uninitialized) throw new InvalidOperationException("Audio startup is already in progress."); _status = _status with { ConfiguredSelection = selection, LatencyMilliseconds = ValidateLatency(latencyMilliseconds) }; }
    }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        lock (_gate) { if (_status.State != AudioEngineState.Uninitialized) return; PublishLocked(_status with { State = AudioEngineState.Initializing }); }
        InitializeMixerAndFallback(token);
        var configured = Status.ConfiguredSelection; var latency = Status.LatencyMilliseconds;
        var resolution = await _devices.ResolveAsync(configured, token);
        if (resolution.Success) { await SwitchResolvedAsync(configured, resolution, latency, false, token); return; }
        if (configured.Kind == AudioOutputSelectionKind.SpecificDevice)
        {
            var fallback = await _devices.ResolveAsync(AudioOutputSelection.SystemDefault, token);
            if (fallback.Success) { _fallbackAttempted = true; await SwitchResolvedAsync(configured, fallback with { IsFallback = true }, latency, true, token); return; }
        }
        lock (_gate) PublishLocked(_status with { State = AudioEngineState.Unavailable, ErrorCode = resolution.ErrorCode, ErrorMessage = resolution.ErrorMessage });
    }

    public void PlayClick() => Play(new(new("builtin", "click", "samples/key-click.wav"), 1f, 0f));
    public void Play(SoundTrigger trigger)
    {
        lock (_gate)
        {
            if (_status.State != AudioEngineState.Running || _mixer is null || _activeVoices >= 32) { if (_status.State == AudioEngineState.Switching) Interlocked.Increment(ref _suppressed); return; }
            CachedSound? sample;
            if (_sampleCache is not null) { if (!_sampleCache.TryGet(trigger.SoundId, out sample!)) { _sampleCache.RecordMissingSample(); return; } }
            else sample = _fallback;
            if (sample is null) return;
            _activeVoices++;
            try { _mixer.AddMixerInput(new CachedVoice(sample, this, Math.Clamp(trigger.Gain, 0, 1), Math.Clamp(trigger.PitchSemitones, -3, 3))); }
            catch { _activeVoices--; throw; }
        }
    }

    public async Task<AudioDeviceSwitchResult> SwitchOutputAsync(AudioOutputSelection selection, int latencyMilliseconds, CancellationToken token = default)
    {
        if (!selection.IsValid) return Failure(selection, "InvalidSelection", "Output selection is invalid.");
        int latency; try { latency = ValidateLatency(latencyMilliseconds); } catch (ArgumentOutOfRangeException ex) { return Failure(selection, "InvalidLatency", ex.Message); }
        var resolution = await _devices.ResolveAsync(selection, token);
        if (!resolution.Success) return Failure(selection, resolution.ErrorCode!, resolution.ErrorMessage!);
        return await SwitchResolvedAsync(selection, resolution, latency, false, token);
    }

    private async Task<AudioDeviceSwitchResult> SwitchResolvedAsync(AudioOutputSelection configured, AudioOutputResolution resolution, int latency, bool fallback, CancellationToken token)
    {
        await _switchGate.WaitAsync(token);
        try
        {
            if (_shutdown) return Failure(configured, "ShutdownInProgress", "Audio is shutting down.");
            IAudioOutputHandle? previous; AudioEngineStatus previousStatus;
            lock (_gate) { previous = _output; previousStatus = _status; _output = null; PublishLocked(_status with { State = AudioEngineState.Switching, ErrorCode = null, ErrorMessage = null }); }
            StopAndDispose(previous);
            IAudioOutputHandle? candidate = null;
            try
            {
                token.ThrowIfCancellationRequested(); candidate = _factory.Create(resolution.EffectiveDeviceId!, latency, _mixer!, OnPlaybackStopped);
                lock (_gate)
                {
                    _output = candidate; candidate = null; _fallbackAttempted = fallback;
                    PublishLocked(new(AudioEngineState.Running, configured, resolution.EffectiveDeviceId, resolution.EffectiveDisplayName, fallback, latency, _activeVoices));
                }
                return new(true, configured, resolution.EffectiveDeviceId, resolution.EffectiveDisplayName, fallback);
            }
            catch (Exception ex)
            {
                candidate?.Dispose();
                if (previousStatus.EffectiveDeviceId is not null)
                {
                    try
                    {
                        var rollback = _factory.Create(previousStatus.EffectiveDeviceId, previousStatus.LatencyMilliseconds, _mixer!, OnPlaybackStopped);
                        lock (_gate) { _output = rollback; PublishLocked(previousStatus with { State = AudioEngineState.Running, ErrorCode = "InitializationFailed", ErrorMessage = ex.Message }); }
                        return Failure(configured, ex is OperationCanceledException ? "SwitchCancelled" : "InitializationFailed", ex is OperationCanceledException ? "Output switch was cancelled; the previous output was restored." : "Requested output failed; the previous output was restored.", previousStatus);
                    }
                    catch (Exception rollbackError)
                    {
                        lock (_gate) PublishLocked(previousStatus with { State = AudioEngineState.Faulted, EffectiveDeviceId = null, EffectiveDeviceName = null, ErrorCode = "RollbackFailed", ErrorMessage = rollbackError.Message });
                        return Failure(configured, "RollbackFailed", "Requested output and rollback both failed.");
                    }
                }
                lock (_gate) PublishLocked(_status with { State = AudioEngineState.Unavailable, ErrorCode = "InitializationFailed", ErrorMessage = ex.Message });
                return Failure(configured, ex is OperationCanceledException ? "SwitchCancelled" : "InitializationFailed", ex.Message);
            }
        }
        finally { _switchGate.Release(); }
    }

    private void InitializeMixerAndFallback(CancellationToken token)
    {
        float[]? fallbackSamples = null;
        if (_sampleCache is null)
        {
            using var stream = Uri.TryCreate(_assetUri, UriKind.Absolute, out var asset) && asset.Scheme == "avares" ? AssetLoader.Open(asset) : File.OpenRead(_assetUri);
            using var reader = new WaveFileReader(stream); ISampleProvider source = reader.ToSampleProvider();
            if (source.WaveFormat.SampleRate != 48000) source = new WdlResamplingSampleProvider(source, 48000); if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
            var values = new List<float>(); var buffer = new float[4096]; int read; while ((read = source.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); for (var i = 0; i < read; i++) values.Add(buffer[i]); }
            fallbackSamples = values.ToArray();
        }
        lock (_gate) { _mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ReadFully = true }; _fallback = fallbackSamples is null ? null : new(new("builtin", "click", "samples/key-click.wav"), fallbackSamples); }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args) { if (args.Exception is null || _shutdown) return; lock (_gate) PublishLocked(_status with { State = AudioEngineState.Faulted, ErrorCode = "DeviceLost", ErrorMessage = args.Exception.Message }); OnDevicesChanged(this, EventArgs.Empty); }
    private void OnDevicesChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_shutdown) return; _reconcileCancellation?.Cancel(); _reconcileCancellation?.Dispose(); _reconcileCancellation = new();
            _reconcileTask = ReconcileAsync(_reconcileCancellation.Token);
        }
    }
    private async Task ReconcileAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token); var status = Status;
            if (status.ConfiguredSelection.Kind == AudioOutputSelectionKind.SystemDefault) { var resolution = await _devices.ResolveAsync(AudioOutputSelection.SystemDefault, token); if (resolution.Success && resolution.EffectiveDeviceId != status.EffectiveDeviceId) await SwitchResolvedAsync(status.ConfiguredSelection, resolution, status.LatencyMilliseconds, false, token); }
            else
            {
                var configured = await _devices.ResolveAsync(status.ConfiguredSelection, token);
                if (!configured.Success && !_fallbackAttempted) { var fallback = await _devices.ResolveAsync(AudioOutputSelection.SystemDefault, token); if (fallback.Success) { _fallbackAttempted = true; await SwitchResolvedAsync(status.ConfiguredSelection, fallback with { IsFallback = true }, status.LatencyMilliseconds, true, token); } }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { } catch (Exception ex) { lock (_gate) PublishLocked(_status with { ErrorCode = "ReconciliationFailed", ErrorMessage = ex.Message }); }
    }

    public Task ShutdownAsync(CancellationToken token = default) { lock (_gate) return (_shutdownTask ??= ShutdownCoreAsync()).WaitAsync(token); }
    private async Task ShutdownCoreAsync()
    {
        Task? reconciliation; lock (_gate) { _shutdown = true; PublishLocked(_status with { State = AudioEngineState.Stopping }); _reconcileCancellation?.Cancel(); reconciliation = _reconcileTask; }
        if (reconciliation is not null) try { await reconciliation; } catch (OperationCanceledException) { }
        await _switchGate.WaitAsync();
        try { IAudioOutputHandle? output; lock (_gate) { output = _output; _output = null; } StopAndDispose(output); lock (_gate) { _mixer = null; _fallback = null; PublishLocked(_status with { State = AudioEngineState.Stopped, EffectiveDeviceId = null, EffectiveDeviceName = null }); } }
        finally { _switchGate.Release(); }
        _devices.DevicesChanged -= OnDevicesChanged; if (_ownsDeviceService) await _devices.DisposeAsync();
    }
    private static int ValidateLatency(int value) => value is >= 10 and <= 100 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Latency must be between 10 and 100 ms.");
    private static void StopAndDispose(IAudioOutputHandle? output) { if (output is null) return; try { output.Stop(); } catch { } finally { output.Dispose(); } }
    private AudioDeviceSwitchResult Failure(AudioOutputSelection selection, string code, string message, AudioEngineStatus? status = null) { var current = status ?? Status; return new(false, selection, current.EffectiveDeviceId, current.EffectiveDeviceName, current.IsFallbackActive, code, message); }
    private void PublishLocked(AudioEngineStatus value) { _status = value; try { StatusChanged?.Invoke(this, Status); } catch { } }
    private void VoiceCompleted() => Interlocked.Decrement(ref _activeVoices);
    public async ValueTask DisposeAsync() { await ShutdownAsync(); _switchGate.Dispose(); _reconcileCancellation?.Dispose(); }

    private sealed class CachedVoice(CachedSound sample, WasapiAudioEngine owner, float gain, float pitchSemitones) : ISampleProvider
    {
        private readonly float[] _samples = sample.Samples; private readonly double _step = Math.Pow(2, pitchSemitones / 12d); private double _framePosition; private int _completed;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            var requested = count / 2; var frames = _samples.Length / 2; var written = 0;
            while (written < requested && _framePosition < frames - 1) { var frame = (int)_framePosition; var fraction = (float)(_framePosition - frame); var target = offset + written * 2; var source = frame * 2; buffer[target] = (_samples[source] + (_samples[source + 2] - _samples[source]) * fraction) * gain; buffer[target + 1] = (_samples[source + 1] + (_samples[source + 3] - _samples[source + 1]) * fraction) * gain; _framePosition += _step; written++; }
            if (_framePosition >= frames - 1 && Interlocked.Exchange(ref _completed, 1) == 0) owner.VoiceCompleted(); return written * 2;
        }
    }
}
