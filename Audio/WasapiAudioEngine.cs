using Avalonia.Platform;
using KeySounds.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KeySounds.Audio;

public sealed class WasapiAudioEngine(string assetUri, ISoundSampleCache? sampleCache = null) : IAudioEngine
{
    private enum AudioState { Uninitialized, Initializing, Initialized, Stopping, Stopped, Faulted }
    private readonly object _gate = new();
    private WasapiOut? _output;
    private MixingSampleProvider? _mixer;
    private CachedSound? _fallback;
    private long _missingSampleCount;
    private AudioState _state;
    private Task? _shutdownTask;
    private int _activeVoices;
    private EventHandler<StoppedEventArgs>? _playbackStoppedHandler;

    public bool IsInitialized { get { lock (_gate) return _state == AudioState.Initialized; } }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_state == AudioState.Initialized) return Task.CompletedTask;
            if (_state is AudioState.Initializing or AudioState.Stopping) throw new InvalidOperationException("The audio engine is changing state.");
            if (_state == AudioState.Stopped) throw new ObjectDisposedException(nameof(WasapiAudioEngine));
            _state = AudioState.Initializing;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                float[]? fallbackSamples = null;
                if (sampleCache is null)
                {
                    using var stream = AssetLoader.Open(new Uri(assetUri));
                    using var reader = new WaveFileReader(stream);
                    ISampleProvider source = reader.ToSampleProvider();
                    if (source.WaveFormat.SampleRate != 48000) source = new WdlResamplingSampleProvider(source, 48000);
                    if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
                    if (source.WaveFormat.Channels != 2) throw new NotSupportedException("The click WAV must be mono or stereo.");
                    var values = new List<float>(); var buffer = new float[4096]; int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0) for (var index = 0; index < read; index++) values.Add(buffer[index]);
                    fallbackSamples = values.ToArray();
                }

                var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ReadFully = true };
                var output = new WasapiOut(AudioClientShareMode.Shared, true, 20);
                _playbackStoppedHandler = OnPlaybackStopped;
                output.PlaybackStopped += _playbackStoppedHandler;
                try { output.Init(mixer); output.Play(); }
                catch { output.PlaybackStopped -= _playbackStoppedHandler; output.Dispose(); throw; }
                _fallback = fallbackSamples is null ? null : new CachedSound(new("builtin", "click", "samples/key-click.wav"), fallbackSamples);
                _mixer = mixer;
                _output = output;
                _state = AudioState.Initialized;
                return Task.CompletedTask;
            }
            catch { _state = AudioState.Faulted; throw; }
        }
    }

    public void PlayClick()
        => Play(new SoundTrigger(new SoundId("builtin", "click", "samples/key-click.wav"), 1f, 0f));

    public void Play(SoundTrigger trigger)
    {
        lock (_gate)
        {
            if (_state != AudioState.Initialized || _mixer is null || _activeVoices >= 32) return;
            CachedSound? sample;
            if (sampleCache is not null)
            {
                if (!sampleCache.TryGet(trigger.SoundId, out sample!)) { sampleCache.RecordMissingSample(); Interlocked.Increment(ref _missingSampleCount); return; }
            }
            else sample = _fallback;
            if (sample is null) return;
            _activeVoices++;
            try { _mixer.AddMixerInput(new CachedVoice(sample, this, Math.Clamp(trigger.Gain, 0, 1), Math.Clamp(trigger.PitchSemitones, -3, 3))); }
            catch { _activeVoices--; throw; }
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return (_shutdownTask ??= ShutdownCoreAsync()).WaitAsync(cancellationToken);
    }

    private async Task ShutdownCoreAsync()
    {
        await Task.Yield();
        WasapiOut? output;
        EventHandler<StoppedEventArgs>? handler;
        lock (_gate)
        {
            if (_state == AudioState.Stopped) return;
            _state = AudioState.Stopping;
            output = _output;
            handler = _playbackStoppedHandler;
            _output = null;
            _mixer = null;
            _fallback = null;
            _playbackStoppedHandler = null;
        }
        if (output is not null)
        {
            if (handler is not null) output.PlaybackStopped -= handler;
            try { output.Stop(); }
            finally { output.Dispose(); }
        }
        lock (_gate) { _activeVoices = 0; _state = AudioState.Stopped; }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is null) return;
        lock (_gate) { if (_state == AudioState.Initialized) _state = AudioState.Faulted; }
    }

    private void VoiceCompleted() => Interlocked.Decrement(ref _activeVoices);
    public async ValueTask DisposeAsync() => await ShutdownAsync().ConfigureAwait(false);

    private sealed class CachedVoice(CachedSound sample, WasapiAudioEngine owner, float gain, float pitchSemitones) : ISampleProvider
    {
        private readonly float[] _samples = sample.Samples;
        private readonly double _step = Math.Pow(2, pitchSemitones / 12d);
        private double _framePosition;
        private int _completed;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            var requestedFrames = count / 2;
            var sourceFrames = _samples.Length / 2;
            var writtenFrames = 0;
            while (writtenFrames < requestedFrames && _framePosition < sourceFrames - 1)
            {
                var frame = (int)_framePosition;
                var fraction = (float)(_framePosition - frame);
                var target = offset + writtenFrames * 2;
                var source = frame * 2;
                buffer[target] = (_samples[source] + (_samples[source + 2] - _samples[source]) * fraction) * gain;
                buffer[target + 1] = (_samples[source + 1] + (_samples[source + 3] - _samples[source + 1]) * fraction) * gain;
                _framePosition += _step;
                writtenFrames++;
            }
            if (_framePosition >= sourceFrames - 1 && Interlocked.Exchange(ref _completed, 1) == 0) owner.VoiceCompleted();
            return writtenFrames * 2;
        }
    }
}
