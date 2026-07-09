using Avalonia.Threading;
using KeySounds.Application.Audio;
using KeySounds.Core.Audio;

namespace KeySounds.ViewModels;

public sealed record AudioSelectionOption(string Label, AudioOutputSelection Selection, string Availability)
{
    public override string ToString() => Label;
}

public sealed class AudioSettingsViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AudioDeviceSelectionService _service; private readonly ISelectableAudioEngine _engine; private AudioSelectionOption? _selected; private int _latency = 20; private string? _message; private bool _shutdown;
    public AudioSettingsViewModel(AudioDeviceSelectionService service, ISelectableAudioEngine engine)
    {
        _service = service; _engine = engine; _engine.StatusChanged += OnStatusChanged; _service.Changed += OnChanged;
        RefreshCommand = Command(RefreshAsync); ApplyCommand = Command(ApplyAsync, () => SelectedOption is not null); RetryCommand = Command(RetryAsync);
        UseDefaultCommand = Command(UseDefaultAsync); TestSoundCommand = Command(TestSoundAsync, () => Status.State == AudioEngineState.Running);
    }
    public IReadOnlyList<AudioSelectionOption> Options { get; private set; } = [];
    public AudioSelectionOption? SelectedOption { get => _selected; set { Set(ref _selected, value); ApplyCommand.Refresh(); } }
    public IReadOnlyList<int> Latencies { get; } = [10, 15, 20, 30, 40, 50, 75, 100];
    public int SelectedLatency { get => _latency; set => Set(ref _latency, value); }
    public AudioEngineStatus Status => _engine.Status;
    public string ConfiguredOutput => Status.ConfiguredSelection.Kind == AudioOutputSelectionKind.SystemDefault ? "System Default" : Options.FirstOrDefault(item => item.Selection == Status.ConfiguredSelection)?.Label ?? Status.ConfiguredSelection.DeviceId ?? "Unavailable device";
    public string EffectiveOutput => Status.EffectiveDeviceName ?? "No active output";
    public string FallbackStatus => Status.IsFallbackActive ? "Fallback active — configured preference retained" : "Using configured output";
    public string EngineSummary => $"{Status.State} · {Status.LatencyMilliseconds} ms · {Status.ActiveVoiceCount}/32 voices";
    public string? Message { get => _message ?? Status.ErrorMessage; private set => Set(ref _message, value); }
    public AsyncCommand RefreshCommand { get; } public AsyncCommand ApplyCommand { get; } public AsyncCommand RetryCommand { get; } public AsyncCommand UseDefaultCommand { get; } public AsyncCommand TestSoundCommand { get; }
    private IEnumerable<AsyncCommand> Commands => [RefreshCommand, ApplyCommand, RetryCommand, UseDefaultCommand, TestSoundCommand];
    private AsyncCommand Command(Func<Task> action, Func<bool>? can = null) => new(async () => { if (_shutdown) return; Message = null; await action(); }, can, _ => Message = "Audio operation failed. Refresh devices and try again.");
    public async Task InitializeAsync() { SelectedLatency = Status.LatencyMilliseconds; await RefreshAsync(); SelectConfigured(); }
    private async Task RefreshAsync()
    {
        await _service.RefreshAsync();
        Options = [new("System Default", AudioOutputSelection.SystemDefault, "Follows Windows default"), .. _service.Devices.Select(device => new AudioSelectionOption(device.DisplayName + (device.IsSystemDefault ? " (current default)" : ""), AudioOutputSelection.Specific(device.Id), device.Availability.ToString()))];
        Raise(nameof(Options)); SelectConfigured();
    }
    private async Task ApplyAsync()
    {
        if (SelectedOption is null) return; var result = await _service.ApplyAsync(SelectedOption.Selection, SelectedLatency);
        Message = result.Success ? "Audio output applied." : result.ErrorMessage; RaiseStatus(); if (!result.Success) SelectConfigured();
    }
    private async Task RetryAsync() { var result = await _service.RetryConfiguredAsync(); Message = result.Success ? "Configured output restored." : result.ErrorMessage; RaiseStatus(); }
    private async Task UseDefaultAsync() { SelectedOption = Options.FirstOrDefault(item => item.Selection.Kind == AudioOutputSelectionKind.SystemDefault); await ApplyAsync(); }
    private async Task TestSoundAsync() => Message = await _service.TestSoundAsync() ? "Test sound submitted." : "Audio output is unavailable or busy.";
    private void SelectConfigured() => SelectedOption = Options.FirstOrDefault(item => item.Selection == Status.ConfiguredSelection) ?? Options.FirstOrDefault();
    private void OnStatusChanged(object? sender, AudioEngineStatus status) => Dispatcher.UIThread.Post(RaiseStatus);
    private void OnChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(RaiseStatus);
    private void RaiseStatus() { Raise(nameof(Status)); Raise(nameof(ConfiguredOutput)); Raise(nameof(EffectiveOutput)); Raise(nameof(FallbackStatus)); Raise(nameof(EngineSummary)); Raise(nameof(Message)); foreach (var command in Commands) command.Refresh(); }
    public ValueTask DisposeAsync() { _shutdown = true; _engine.StatusChanged -= OnStatusChanged; _service.Changed -= OnChanged; return ValueTask.CompletedTask; }
}
