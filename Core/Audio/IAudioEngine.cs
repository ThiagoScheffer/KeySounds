namespace KeySounds.Core.Audio;

public interface IAudioEngine : IAsyncDisposable
{
    bool IsInitialized { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    void PlayClick();
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
