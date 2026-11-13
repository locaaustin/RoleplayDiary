using System.Windows;

namespace RoleplayDiary.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowChrome.DarkenWhenReady(this);
    }
}
