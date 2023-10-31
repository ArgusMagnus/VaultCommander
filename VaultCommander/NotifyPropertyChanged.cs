using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace VaultCommander;

abstract class NotifyPropertyChanged : INotifyPropertyChanged
{
    string[]? _propertyNames;

    PropertyChangedEventHandler? _propertyChangedHandler;
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _propertyChangedHandler += value;
        remove => _propertyChangedHandler += value;
    }

    protected void RaisePropertyChanged([CallerMemberName] string propertyName = null!) => OnPropertyChanged(new PropertyChangedEventArgs(propertyName));

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        _propertyChangedHandler?.Invoke(this, args);
    }

    protected void RaisePropertyChangedForAll(Predicate<string>? filter = null)
    {
        if (_propertyNames == null)
            _propertyNames = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => x.Name).ToArray();
        foreach (var prop in _propertyNames)
        {
            if (filter?.Invoke(prop) ?? true)
                RaisePropertyChanged(prop);
        }
    }

    /// <summary>
    /// All deriving classes should set their properties using this method.
    /// This ensures that the <see cref="INotifyPropertyChanged.PropertyChanged"/> event
    /// is properly raised and data binding works.
    /// </summary>
    protected void SetProperty<T>(ref T backingField, T value, Action<T, T>? propertyChangedHandler = null, [CallerMemberName] string propertyName = null!)
        => SetProperty(ref backingField, value, RaisePropertyChanged, propertyChangedHandler, propertyName);


    public static bool SetProperty<T>(ref T backingField, T value, Action<string> propertyChangedEvent, Action<T, T>? propertyChangedHandler = null, [CallerMemberName] string propertyName = null!)
    {
        var equatable = backingField as IEquatable<T>;
        bool isEqual;
        if (equatable != null)
            isEqual = equatable?.Equals(value) ?? object.ReferenceEquals(backingField, value);
        else
            isEqual = backingField?.Equals(value) ?? object.ReferenceEquals(backingField, value);
        var oldValue = backingField;
        backingField = value;
        if (!isEqual)
        {
            propertyChangedEvent?.Invoke(propertyName);
            propertyChangedHandler?.Invoke(oldValue, value);
        }
        return !isEqual;
    }
}