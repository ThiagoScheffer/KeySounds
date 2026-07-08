namespace KeySounds.Core.Audio;

public interface IAudioEngine : IAsyncDisposable
{
    bool IsInitialized { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    void Play(SoundTrigger trigger);
    void PlayClick();
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

public interface ISelectableAudioEngine : IAudioEngine
{
    AudioEngineStatus Status { get; }
    event EventHandler<AudioEngineStatus>? StatusChanged;
    void ConfigureStartup(AudioOutputSelection selection, int latencyMilliseconds);
    Task<AudioDeviceSwitchResult> SwitchOutputAsync(AudioOutputSelection selection, int latencyMilliseconds, CancellationToken cancellationToken = default);
}
