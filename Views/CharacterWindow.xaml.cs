using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using RoleplayDiary.Domain;

namespace RoleplayDiary.Views;

/// <summary>
/// The new and edit character dialog. Photos picked here stay on the draft until save.
/// </summary>
public partial class CharacterWindow : BindableWindow
{
    private static readonly Regex NonDigit = new("[^0-9]", RegexOptions.Compiled);

    private readonly string? _existingPortrait;

    public CharacterWindow(CharacterDraft draft, string? portraitPath, bool isNew)
    {
        Draft = draft;
        _existingPortrait = portraitPath;
        Heading = isNew ? "New character" : "Edit character";
        SaveLabel = isNew ? "Create character" : "Save changes";

        DataContext = this;
        InitializeComponent();
        WindowChrome.DarkenWhenReady(this);

        Loaded += (_, _) => NameBox.Focus();
    }

    public CharacterDraft Draft { get; }

    public string Heading { get; }

    public string SaveLabel { get; }

    public IReadOnlyList<CharacterStatus> StatusChoices { get; } = Enum.GetValues<CharacterStatus>();

    public bool CanSave => Draft.IsValid;

    public string? PreviewPath =>
        Draft.Photos.Count > 0 ? Draft.Photos[0] : _existingPortrait;

    public bool HasPendingPhotos => Draft.Photos.Count > 0;

    public string PhotoSummary => Draft.Photos.Count switch
    {
        0 when _existingPortrait is not null => "Current cover",
        0 => "Optional - you can add photos later",
        1 => "1 photo ready",
        var count => $"{count} photos ready, first is the cover"
    };

    public string Initials
    {
        get
        {
            var words = (Draft.Name ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var letters = string.Concat(words.Take(2).Select(word => word[0])).ToUpperInvariant();
            return letters.Length > 0 ? letters : "?";
        }
    }

    private void OnNameChanged(object sender, RoutedEventArgs e) =>
        Raise(nameof(CanSave), nameof(Initials));

    private void OnDigitsOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = NonDigit.IsMatch(e.Text);

    private void OnAddPhotos(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add photos",
            Multiselect = true,
            Filter = ImageFiles.FileDialogFilter()
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            if (!Draft.Photos.Contains(file))
            {
                Draft.Photos.Add(file);
            }
        }

        Raise(nameof(PreviewPath), nameof(HasPendingPhotos), nameof(PhotoSummary));
    }

    private void OnClearPhotos(object sender, RoutedEventArgs e)
    {
        Draft.Photos.Clear();
        Raise(nameof(PreviewPath), nameof(HasPendingPhotos), nameof(PhotoSummary));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Draft.IsValid)
        {
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
