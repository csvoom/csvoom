using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CSVoom.ui.ViewModels;

public class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute == null || canExecute(parameter);

    public void Execute(object? parameter) => execute(parameter);

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null, bool allowConcurrent = false) : ICommand
{
    private int _executionCount;

    public bool CanExecute(object? parameter)
    {
        return (allowConcurrent || _executionCount == 0) && (canExecute == null || canExecute(parameter));
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        try
        {
            Interlocked.Increment(ref _executionCount);
            RaiseCanExecuteChanged();
            await execute(parameter);
        }
        finally
        {
            Interlocked.Decrement(ref _executionCount);
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
