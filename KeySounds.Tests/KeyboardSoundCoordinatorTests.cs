using KeySounds.Application;
using KeySounds.Core.Audio;
using KeySounds.Core.Input;
using Xunit;

namespace KeySounds.Tests;

public sealed class KeyboardSoundCoordinatorTests
{
    [Fact]
    public async Task KeyTransitionsSuppressRepeatsAndAllowNextPress()
    {
        var (sut, input, audio) = Create(); await sut.StartAsync();
        input.Send(30, KeyboardAction.KeyDown); input.Send(30, KeyboardAction.KeyDown);
        await Eventually(() => audio.Plays == 1);
        input.Send(30, KeyboardAction.KeyUp); input.Send(30, KeyboardAction.KeyDown);
        await Eventually(() => audio.Plays == 2); await sut.DisposeAsync();
    }

    [Fact]
    public async Task DisabledPreventsPlayback()
    {
        var (sut, input, audio) = Create(); sut.IsEnabled = false; await sut.StartAsync();
        input.Send(30, KeyboardAction.KeyDown); await Task.Delay(50);
        Assert.Equal(0, audio.Plays); await sut.DisposeAsync();
    }

    [Fact]
    public async Task SameScanCodeFromDifferentDevicesIsIndependent()
    {
        var (sut, input, audio) = Create(); await sut.StartAsync();
        input.Send(30, KeyboardAction.KeyDown, 1); input.Send(30, KeyboardAction.KeyDown, 2);
        await Eventually(() => audio.Plays == 2); await sut.DisposeAsync();
    }

    [Fact]
    public async Task ShutdownIsIdempotent()
    {
        var (sut, _, audio) = Create(); await sut.StartAsync(); await sut.StopAsync(); await sut.StopAsync();
        Assert.Equal(1, audio.Shutdowns);
    }

    [Fact]
    public async Task ConcurrentStopsWaitForTheSameCleanup()
    {
        var input = new FakeInput { BlockStop = true }; var audio = new FakeAudio(); var sut = new KeyboardSoundCoordinator(input, audio);
        await sut.StartAsync();
        var first = sut.StopAsync(); var second = sut.StopAsync();
        await input.StopEntered.Task; Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        input.AllowStop.TrySetResult(); await Task.WhenAll(first, second);
        Assert.Equal(1, input.StopCalls); Assert.Equal(1, audio.Shutdowns);
    }

    [Fact]
    public async Task InputStopsBeforeAudio()
    {
        var order = new List<string>(); var input = new FakeInput { Order = order }; var audio = new FakeAudio { Order = order };
        var sut = new KeyboardSoundCoordinator(input, audio); await sut.StartAsync(); await sut.StopAsync();
        Assert.Equal(["input", "audio"], order);
    }

    [Fact]
    public async Task ShutdownAfterPartialStartupFailureIsSafe()
    {
        var input = new FakeInput { StartFailure = new InvalidOperationException("startup failed") }; var audio = new FakeAudio();
        var sut = new KeyboardSoundCoordinator(input, audio); await sut.StartAsync(); await sut.StopAsync();
        Assert.Equal(1, input.StopCalls); Assert.Equal(1, audio.Shutdowns);
    }

    [Fact]
    public async Task ShutdownFailureIsObservableToEveryCaller()
    {
        var input = new FakeInput { StopFailure = new InvalidOperationException("stop failed") }; var audio = new FakeAudio();
        var sut = new KeyboardSoundCoordinator(input, audio); await sut.StartAsync();
        var first = sut.StopAsync(); var second = sut.StopAsync();
        await Assert.ThrowsAsync<AggregateException>(() => first); await Assert.ThrowsAsync<AggregateException>(() => second);
    }

    private static (KeyboardSoundCoordinator, FakeInput, FakeAudio) Create() { var i = new FakeInput(); var a = new FakeAudio(); return (new(i, a), i, a); }
    private static async Task Eventually(Func<bool> predicate) { for (var i = 0; i < 50 && !predicate(); i++) await Task.Delay(10); Assert.True(predicate()); }

    private sealed class FakeInput : IKeyboardInputSource
    {
        public event EventHandler<KeyboardInputEvent>? InputReceived;
        public event EventHandler<InputFailureEventArgs>? Failure { add { } remove { } }
        public bool BlockStop; public int StopCalls; public Exception? StartFailure; public Exception? StopFailure; public List<string>? Order;
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Send(ushort scan, KeyboardAction action, nint device = default) => InputReceived?.Invoke(this, new(scan, 0, action, device, false, 0));
        public Task StartAsync(CancellationToken cancellationToken = default) => StartFailure is null ? Task.CompletedTask : Task.FromException(StartFailure);
        public async Task StopAsync(CancellationToken cancellationToken = default) { StopCalls++; Order?.Add("input"); StopEntered.TrySetResult(); if (BlockStop) await AllowStop.Task; if (StopFailure is not null) throw StopFailure; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeAudio : IAudioEngine
    {
        public bool IsInitialized { get; private set; } public int Plays; public int Shutdowns; public List<string>? Order;
        public Task InitializeAsync(CancellationToken cancellationToken = default) { IsInitialized = true; return Task.CompletedTask; }
        public void Play(SoundTrigger trigger) => Interlocked.Increment(ref Plays);
        public void PlayClick() => Interlocked.Increment(ref Plays);
        public Task ShutdownAsync(CancellationToken cancellationToken = default) { IsInitialized = false; Shutdowns++; Order?.Add("audio"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
