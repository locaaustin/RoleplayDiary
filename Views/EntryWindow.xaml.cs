using System.Windows;
using System.Windows.Media;
using RoleplayDiary.Domain;

namespace RoleplayDiary.Views;

/// <summary>The add and edit story-entry dialog. The date hint shows how the typed date was read.</summary>
public partial class EntryWindow : BindableWindow
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
}
