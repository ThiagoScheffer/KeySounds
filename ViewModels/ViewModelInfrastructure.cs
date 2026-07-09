using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace KeySounds.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Raise(name); return true; }
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null) : ObservableObject, ICommand
{
    private bool _running;
    public bool IsRunning { get => _running; private set { if (Set(ref _running, value)) CanExecuteChanged?.Invoke(this, EventArgs.Empty); } }
    public bool CanExecute(object? parameter) => !IsRunning && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter) { if (!CanExecute(parameter)) return; IsRunning = true; try { await execute(); } catch (OperationCanceledException) { } catch (Exception ex) { onError?.Invoke(ex); } finally { IsRunning = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
