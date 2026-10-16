using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WinOptimizer.Services;

namespace WinOptimizer.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null) : ICommand
{
    private bool _isRunning;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_isRunning && (canExecute?.Invoke(parameter) ?? true);
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isRunning = true;
        NotifyCanExecuteChanged();
        try { await execute(parameter); }
        catch (Exception ex) { AppLog.Write("An interface action could not be completed.", ex); }
        finally
        {
            _isRunning = false;
            NotifyCanExecuteChanged();
        }
    }
}

