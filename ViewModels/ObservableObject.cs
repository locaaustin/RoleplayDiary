using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RoleplayDiary.ViewModels;

/// <summary>Notifies WPF when a property value changes so bound controls redraw.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected void Raise(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            Raise(name);
        }
    }

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
