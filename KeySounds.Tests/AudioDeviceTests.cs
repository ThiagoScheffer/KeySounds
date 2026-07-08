using KeySounds.Application.Audio;
using KeySounds.Application.Settings;
using KeySounds.Audio;
using KeySounds.Core.Audio;
using KeySounds.Core.Settings;
using KeySounds.Core.SoundPacks;
using NAudio.Wave;
using Xunit;

namespace KeySounds.Tests;

public sealed class AudioDeviceTests
{
    [Fact]
    public void SelectionValidationEqualityAndMigrationWork()
    {
        Assert.True(AudioOutputSelection.SystemDefault.IsValid); Assert.Throws<ArgumentException>(() => AudioOutputSelection.Specific(""));
        Assert.Equal(AudioOutputSelection.Specific("usb"), AudioOutputSelection.Specific("usb"));
        var settings = new ApplicationSettings { SchemaVersion = 1, AudioOutputDeviceId = "usb", AudioLatencyMilliseconds = 0 };
        Assert.True(ApplicationSettingsMigration.Apply(settings)); Assert.Equal(AudioOutputSelection.Specific("usb"), settings.AudioOutputSelection); Assert.Equal(20, settings.AudioLatencyMilliseconds); Assert.False(ApplicationSettingsMigration.Apply(settings));
    }

    [Fact]
    public void EndpointNormalizationDeduplicatesAndOrdersDefaultFirst()
    {
        var devices = WindowsAudioOutputDeviceService.Normalize([new("b", "Zeta", false, AudioDeviceAvailability.Active), new("a", "Alpha", true, AudioDeviceAvailability.Active), new("B", "Duplicate", false, AudioDeviceAvailability.Active)]);
        Assert.Equal(["a", "b"], devices.Select(device => device.Id));
    }

    [Fact]
    public async Task ConfiguredSpecificStartsAndSuccessfulSwitchDisposesOldOutput()
    {
        var devices = FakeDevices.Create(); var factory = new FakeFactory(); await using var engine = Create(devices, factory);
        engine.ConfigureStartup(AudioOutputSelection.Specific("usb"), 20); await engine.InitializeAsync();
        Assert.Equal("usb", engine.Status.EffectiveDeviceId); var result = await engine.SwitchOutputAsync(AudioOutputSelection.Specific("speakers"), 30);
        Assert.True(result.Success); Assert.Equal("speakers", engine.Status.EffectiveDeviceId); Assert.Equal(30, engine.Status.LatencyMilliseconds); Assert.Equal(1, factory.Handles.Single(handle => handle.Device == "usb").DisposeCount);
    }

    [Fact]
    public async Task MissingSpecificFallsBackWithoutOverwritingPreference()
    {
        var devices = FakeDevices.Create(); devices.Available.Remove("usb"); await using var engine = Create(devices, new());
        engine.ConfigureStartup(AudioOutputSelection.Specific("usb"), 20); await engine.InitializeAsync();
        Assert.True(engine.IsInitialized); Assert.True(engine.Status.IsFallbackActive); Assert.Equal(AudioOutputSelection.Specific("usb"), engine.Status.ConfiguredSelection); Assert.Equal("speakers", engine.Status.EffectiveDeviceId);
    }

    [Fact]
    public async Task FailedSwitchRollsBackAndPreservesConfiguredAndEffectiveDevice()
    {
        var devices = FakeDevices.Create(); var factory = new FakeFactory(); await using var engine = Create(devices, factory); await engine.InitializeAsync();
        factory.FailDevices.Add("usb"); var result = await engine.SwitchOutputAsync(AudioOutputSelection.Specific("usb"), 20);
        Assert.False(result.Success); Assert.Equal("InitializationFailed", result.ErrorCode); Assert.Equal("speakers", engine.Status.EffectiveDeviceId); Assert.Equal(AudioOutputSelection.SystemDefault, engine.Status.ConfiguredSelection);
        Assert.Equal(2, factory.Handles.Count(handle => handle.Device == "speakers"));
    }

    [Fact]
    public async Task NoDeviceIsNonfatalAndPlaybackIsSuppressed()
    {
        var devices = new FakeDevices(); await using var engine = Create(devices, new()); await engine.InitializeAsync();
        Assert.False(engine.IsInitialized); Assert.Equal(AudioEngineState.Unavailable, engine.Status.State); engine.PlayClick();
    }

    [Fact]
    public async Task InvalidLatencyDoesNotReplaceOutput()
    {
        var devices = FakeDevices.Create(); await using var engine = Create(devices, new()); await engine.InitializeAsync();
        var result = await engine.SwitchOutputAsync(AudioOutputSelection.Specific("usb"), 2); Assert.False(result.Success); Assert.Equal("InvalidLatency", result.ErrorCode); Assert.Equal("speakers", engine.Status.EffectiveDeviceId);
    }

    [Fact]
    public async Task SpecificDeviceLossFallsBackOnlyOnceAndRetainsPreference()
    {
        var devices = FakeDevices.Create(); var factory = new FakeFactory(); await using var engine = Create(devices, factory);
        engine.ConfigureStartup(AudioOutputSelection.Specific("usb"), 20); await engine.InitializeAsync(); devices.Available.Remove("usb"); devices.RaiseChanged();
        await Eventually(() => engine.Status.IsFallbackActive); var creations = factory.Handles.Count; devices.RaiseChanged(); await Task.Delay(400);
        Assert.Equal(creations, factory.Handles.Count); Assert.Equal(AudioOutputSelection.Specific("usb"), engine.Status.ConfiguredSelection);
    }

    [Fact]
    public async Task SystemDefaultChangeFollowsNewEndpointWithoutChangingPreference()
    {
        var devices = FakeDevices.Create(); var factory = new FakeFactory(); await using var engine = Create(devices, factory); await engine.InitializeAsync();
        devices.DefaultId = "usb"; devices.RaiseChanged(); await Eventually(() => engine.Status.EffectiveDeviceId == "usb"); Assert.Equal(AudioOutputSelection.SystemDefault, engine.Status.ConfiguredSelection);
    }

    [Fact]
    public async Task ShutdownDisposesPublishedOutputExactlyOnce()
    {
        var devices = FakeDevices.Create(); var factory = new FakeFactory(); var engine = Create(devices, factory); await engine.InitializeAsync(); await engine.ShutdownAsync(); await engine.ShutdownAsync();
        Assert.Equal(1, factory.Handles.Single().DisposeCount); await engine.DisposeAsync();
    }

    private static WasapiAudioEngine Create(FakeDevices devices, FakeFactory factory) => new("unused", new FakeCache(), devices, factory);
    private static async Task Eventually(Func<bool> condition) { for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20); Assert.True(condition()); }

    private sealed class FakeDevices : IAudioOutputDeviceService
    {
        public HashSet<string> Available { get; } = []; public string? DefaultId = "speakers";
        public static FakeDevices Create() { var value = new FakeDevices(); value.Available.UnionWith(["speakers", "usb"]); return value; }
        public event EventHandler? DevicesChanged;
        public Task<IReadOnlyList<AudioOutputDevice>> GetDevicesAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<AudioOutputDevice>>(Available.Select(id => new AudioOutputDevice(id, id, id == DefaultId, AudioDeviceAvailability.Active)).ToArray());
        public Task<AudioOutputResolution> ResolveAsync(AudioOutputSelection selection, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); var id = selection.Kind == AudioOutputSelectionKind.SystemDefault ? DefaultId : selection.DeviceId;
            return Task.FromResult(id is not null && Available.Contains(id) ? new AudioOutputResolution(true, id, id, false) : new(false, null, null, false, "DeviceUnavailable", "Unavailable"));
        }
        public void RaiseChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty); public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeFactory : IAudioOutputFactory
    {
        public List<FakeHandle> Handles { get; } = []; public HashSet<string> FailDevices { get; } = [];
        public IAudioOutputHandle Create(string deviceId, int latencyMilliseconds, ISampleProvider mixer, EventHandler<StoppedEventArgs> stopped)
        { if (FailDevices.Contains(deviceId)) throw new InvalidOperationException("candidate failed"); var handle = new FakeHandle(deviceId); Handles.Add(handle); return handle; }
    }
    private sealed class FakeHandle(string device) : IAudioOutputHandle { public string Device { get; } = device; public int DisposeCount; public void Stop() { } public void Dispose() => DisposeCount++; }
    private sealed class FakeCache : ISoundSampleCache
    {
        private readonly CachedSound _sound = new(new("builtin", "click", "samples/key-click.wav"), new float[100]);
        public bool TryGet(SoundId id, out CachedSound sample) { sample = _sound; return true; }
        public Task<SamplePreparationResult> PrepareAsync(IReadOnlyCollection<SoundSetReference> requiredSets, CancellationToken token = default) => Task.FromResult(new SamplePreparationResult(true));
        public Task ReleaseUnusedAsync(IReadOnlyCollection<SoundSetReference> requiredSets, CancellationToken token = default) => Task.CompletedTask;
        public Task RemovePackAsync(string packId, CancellationToken token = default) => Task.CompletedTask;
        public SampleCacheSnapshot GetSnapshot() => new(1, 400, 1, false, 0); public void RecordMissingSample() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
