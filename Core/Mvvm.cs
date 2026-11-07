using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace RoleplayDiary;

/// <summary>
/// The base class every view model inherits from.
///
/// WPF only redraws when it is told something changed, and the way it is told is the
/// <see cref="INotifyPropertyChanged"/> event below. Libraries exist that generate
/// this for you; it is written out here so you can see what they would be hiding.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Announce that a property changed. <c>[CallerMemberName]</c> makes the compiler
    /// fill in the name of whatever property called this, so it is never mistyped.
    /// </summary>
    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected void Raise(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            Raise(name);
        }
    }

    /// <summary>
    /// Store a new value and announce it, but only if it is actually different.
    /// Returns true when something changed, which callers use to do extra work.
    /// </summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }
}

/// <summary>
/// A button press, as an object.
///
/// WPF buttons bind to an <see cref="ICommand"/> rather than calling a method, which
/// is what lets the window stay ignorant of what the button actually does.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>
    /// WPF re-asks every command whether it can run when this fires. Routing it to
    /// the global requery event means buttons grey themselves out without any
    /// bookkeeping on our side.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}

/// <summary>The same thing, for buttons that pass the item they belong to.</summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) =>
        parameter is T typed ? _canExecute?.Invoke(typed) ?? true : parameter is null;

    public void Execute(object? parameter)
    {
        if (parameter is T typed)
        {
            _execute(typed);
        }
    }
}
