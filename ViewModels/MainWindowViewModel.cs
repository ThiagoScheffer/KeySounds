using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using KeySounds.Application;

namespace KeySounds.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly KeyboardSoundCoordinator _coordinator;
    public MainWindowViewModel(KeyboardSoundCoordinator coordinator) { _coordinator = coordinator; coordinator.StatusChanged += OnChanged; }
    public bool IsEnabled { get => _coordinator.IsEnabled; set { _coordinator.IsEnabled = value; Raise(); } }
    public string InputStatus => _coordinator.InputStatus;
    public string AudioStatus => _coordinator.AudioStatus;
    public long ClickCount => _coordinator.ClickCount;
    public string? ErrorMessage => _shutdownError ?? _coordinator.ErrorMessage;
    public Task StartAsync() => _coordinator.StartAsync();
    public Task StopAsync() => _coordinator.StopAsync();
    public void ReportShutdownError(Exception exception) { _shutdownError = exception.Message; Raise(nameof(ErrorMessage)); }
    private void OnChanged(object? s, EventArgs e) => Dispatcher.UIThread.Post(() => { Raise(nameof(InputStatus)); Raise(nameof(AudioStatus)); Raise(nameof(ClickCount)); Raise(nameof(ErrorMessage)); });
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private string? _shutdownError;
}
