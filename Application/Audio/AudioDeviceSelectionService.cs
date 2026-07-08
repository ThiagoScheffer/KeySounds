using KeySounds.Application.Settings;
using KeySounds.Audio;
using KeySounds.Core.Audio;
using KeySounds.Core.SoundPacks;

namespace KeySounds.Application.Audio;

public sealed class AudioDeviceSelectionService(IAudioOutputDeviceService devices, ISelectableAudioEngine engine, Phase2SettingsService settings, ISoundSampleCache cache) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1); private bool _shutdown;
    public IReadOnlyList<AudioOutputDevice> Devices { get; private set; } = [];
    public AudioEngineStatus Status => engine.Status;
    public event EventHandler? Changed;
    public async Task RefreshAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token); try { ThrowIfShutdown(); Devices = await devices.GetDevicesAsync(token); Changed?.Invoke(this, EventArgs.Empty); } finally { _gate.Release(); }
    }
    public async Task<AudioDeviceSwitchResult> ApplyAsync(AudioOutputSelection selection, int latency, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            ThrowIfShutdown(); var previous = engine.Status; var result = await engine.SwitchOutputAsync(selection, latency, token);
            if (result.Success)
            {
                try { await settings.SaveAudioConfigurationAsync(selection, latency, token); }
                catch (Exception ex)
                {
                    var rollback = await engine.SwitchOutputAsync(previous.ConfiguredSelection, previous.LatencyMilliseconds, token);
                    result = new(false, selection, engine.Status.EffectiveDeviceId, engine.Status.EffectiveDeviceName, engine.Status.IsFallbackActive,
                        rollback.Success ? "SettingsPersistenceFailed" : "RollbackFailed", rollback.Success ? $"The preference could not be saved; the previous output was restored. {ex.Message}" : "The preference could not be saved and output rollback failed.");
                }
            }
            Changed?.Invoke(this, EventArgs.Empty); return result;
        }
        finally { _gate.Release(); }
    }
    public Task<AudioDeviceSwitchResult> RetryConfiguredAsync(CancellationToken token = default) => ApplyAsync(settings.Settings.AudioOutputSelection, settings.Settings.AudioLatencyMilliseconds, token);
    public async Task<bool> TestSoundAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            ThrowIfShutdown(); if (engine.Status.State != AudioEngineState.Running) return false;
            var reference = new SoundSetReference("builtin", "click"); var prepared = await cache.PrepareAsync([reference], token); if (!prepared.Success) return false;
            engine.Play(new(new("builtin", "click", "samples/key-click.wav"), .65f, 0)); return true;
        }
        finally { _gate.Release(); }
    }
    private void ThrowIfShutdown() { if (_shutdown) throw new ObjectDisposedException(nameof(AudioDeviceSelectionService)); }
    public async ValueTask DisposeAsync() { _shutdown = true; await _gate.WaitAsync(); _gate.Release(); _gate.Dispose(); }
}
