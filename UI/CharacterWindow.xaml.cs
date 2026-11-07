using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace RoleplayDiary;

/// <summary>
/// The new/edit character dialog.
///
/// Photos picked here are <i>staged</i> on the draft, not imported: a brand new
/// character has no id to file them under yet, and cancelling must leave nothing
/// behind. The diary imports them once the save succeeds.
///
/// Small dialogs like this are their own view model, which is why the window
/// implements <see cref="INotifyPropertyChanged"/> and sets itself as the data
/// context. A separate class would be ceremony for no gain at this size.
/// </summary>
public partial class CharacterWindow : Window, INotifyPropertyChanged
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

    public IReadOnlyList<CharacterStatus> StatusChoices { get; } =
        Enum.GetValues<CharacterStatus>();

    public bool CanSave => Draft.IsValid;

    /// <summary>What the portrait tile shows: the first staged photo, else the current cover.</summary>
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

    /// <summary>Shown on the tile while there is no photo; follows what is being typed.</summary>
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

    // ------------------------------------------------------------------ events

    private void OnNameChanged(object sender, RoutedEventArgs e) =>
        Raise(nameof(CanSave), nameof(Initials));

    /// <summary>Keeps letters out of the age box as they are typed.</summary>
    private void OnDigitsOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = NonDigit.IsMatch(e.Text);

    private void OnAddPhotos(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add photos",
            Multiselect = true,
            Filter = PhotoStorage.FileDialogFilter()
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

        RaisePhotoState();
    }

    private void OnClearPhotos(object sender, RoutedEventArgs e)
    {
        Draft.Photos.Clear();
        RaisePhotoState();
    }

    private void RaisePhotoState() =>
        Raise(nameof(PreviewPath), nameof(HasPendingPhotos), nameof(PhotoSummary));

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Guards the keyboard path too: Enter must not create a nameless character.
        if (Draft.IsValid)
        {
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    // ----------------------------------------------------------- notification

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
