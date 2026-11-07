using System.Windows;

namespace RoleplayDiary;

/// <summary>
/// The shell window.
///
/// There is deliberately almost nothing here. Everything the window does lives in
/// <see cref="MainViewModel"/>, which the markup binds to; this file exists only to
/// construct the window and make its title bar dark.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowChrome.DarkenWhenReady(this);
    }
}
