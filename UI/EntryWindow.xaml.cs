using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace RoleplayDiary;

/// <summary>
/// One thing that happened: when, what kind, and the telling of it.
///
/// The date box takes whatever the player types and says underneath how it was read,
/// so a format the app does not recognise is visible before the entry is saved
/// rather than being rejected or quietly misfiled.
/// </summary>
public partial class EntryWindow : Window, INotifyPropertyChanged
{
    public EntryWindow(EntryDraft draft, bool isNew)
    {
        Draft = draft;
        Heading = isNew ? "Add to the story" : "Edit entry";
        SaveLabel = isNew ? "Add entry" : "Save changes";

        DataContext = this;
        InitializeComponent();
        WindowChrome.DarkenWhenReady(this);

        Loaded += (_, _) => TitleBox.Focus();
    }

    public EntryDraft Draft { get; }

    public string Heading { get; }

    public string SaveLabel { get; }

    public IReadOnlyList<EntryKind> KindChoices { get; } = Enum.GetValues<EntryKind>();

    public bool CanSave => Draft.IsValid;

    public string WhenHint => FlexibleDate.Describe(Draft.WhenText).Message;

    /// <summary>Amber when the date was not understood, so it reads as a warning.</summary>
    public Brush WhenHintBrush =>
        (Brush)Application.Current.FindResource(
            FlexibleDate.Describe(Draft.WhenText).Understood ? "TextFaintBrush" : "ShelvedBrush");

    private void OnTitleChanged(object sender, RoutedEventArgs e) => Raise(nameof(CanSave));

    private void OnWhenChanged(object sender, RoutedEventArgs e) =>
        Raise(nameof(WhenHint), nameof(WhenHintBrush));

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Draft.IsValid)
        {
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void Raise(params string[] names)
    {
        foreach (var name in names)
        {
            Raise(name);
        }
    }
}
