using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CSVoom.ui.ViewModels;

/// <summary>
///     A command that executes a synchronous action.
/// </summary>
/// <param name="execute">The action to execute.</param>
/// <param name="canExecute">A predicate to determine if the command can be executed.</param>
public class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    /// <inheritdoc />
    public bool CanExecute(object? parameter) => canExecute == null || canExecute(parameter);

    /// <inheritdoc />
    public void Execute(object? parameter) => execute(parameter);

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;
}

/// <summary>
///     A command that executes an asynchronous function.
/// </summary>
/// <param name="execute">The function to execute.</param>
/// <param name="canExecute">A predicate to determine if the command can be executed.</param>
/// <param name="allowConcurrent">True to allow multiple concurrent executions, false otherwise.</param>
public class AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null, bool allowConcurrent = false) : ICommand
{
    private int _executionCount;

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        return (allowConcurrent || _executionCount == 0) && (canExecute == null || canExecute(parameter));
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    ///     Raises the <see cref="CanExecuteChanged" /> event.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
