using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace RoleplayDiary.Views;

/// <summary>A dialog window that can notify its own bindings.</summary>
public class BindableWindow : Window, INotifyPropertyChanged
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
}
