using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CSVoom.ui.ViewModels;

/// <summary>
///     Base class for view models, providing property change notification.
/// </summary>
public class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    ///     Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    ///     Raises the <see cref="PropertyChanged" /> event.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    ///     Sets a field and raises the <see cref="PropertyChanged" /> event if the value changed.
    /// </summary>
    /// <typeparam name="T">The type of the field.</typeparam>
    /// <param name="field">A reference to the field to set.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">The name of the property (automatically populated).</param>
    /// <returns>True if the value changed, false otherwise.</returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
