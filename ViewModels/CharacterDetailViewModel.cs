using System.Collections.ObjectModel;
using RoleplayDiary.Domain;
using RoleplayDiary.Services;

namespace RoleplayDiary.ViewModels;

/// <summary>The open character: profile, timeline, gallery, and the commands that change them.</summary>
public sealed class CharacterDetailViewModel : ObservableObject
{
    private readonly Diary _diary;
    private readonly IDialogService _dialogs;
    private readonly Action _onChanged;
    private readonly Action _onDeleted;

    /// <summary>Set while the status box is being filled, so filling it does not write a new status.</summary>
    private bool _loadingStatus;

    private CharacterStatus _status;
    private string _background;

    public CharacterDetailViewModel(
        Character character,
        Diary diary,
        IDialogService dialogs,
        Action onChanged,
        Action onDeleted)
    {
        Character = character;
        _diary = diary;
        _dialogs = dialogs;
        _onChanged = onChanged;
        _onDeleted = onDeleted;

        _status = character.Status;
        _background = character.Background ?? string.Empty;

        EditProfileCommand = new RelayCommand(EditProfile);
        DeleteCharacterCommand = new RelayCommand(DeleteCharacter);
        ExportPdfCommand = new RelayCommand(ExportPdf);
        SaveBackgroundCommand = new RelayCommand(SaveBackground, () => HasUnsavedBackground);
        RevertBackgroundCommand = new RelayCommand(
            () => Background = Character.Background ?? string.Empty,
            () => HasUnsavedBackground);

        AddEntryCommand = new RelayCommand(AddEntry);
        EditEntryCommand = new RelayCommand<DiaryEntry>(EditEntry);
        DeleteEntryCommand = new RelayCommand<DiaryEntry>(DeleteEntry);

        AddPhotosCommand = new RelayCommand(AddPhotos);
        SetCoverCommand = new RelayCommand<PhotoViewModel>(SetCover);
        RemovePhotoCommand = new RelayCommand<PhotoViewModel>(RemovePhoto);

        Reload();
    }

    public Character Character { get; }

    public ObservableCollection<DiaryEntry> Entries { get; } = new();

    public ObservableCollection<PhotoViewModel> Photos { get; } = new();

    public IReadOnlyList<CharacterStatus> StatusChoices { get; } = Enum.GetValues<CharacterStatus>();

    public RelayCommand EditProfileCommand { get; }

    public RelayCommand DeleteCharacterCommand { get; }

    public RelayCommand ExportPdfCommand { get; }

    public RelayCommand SaveBackgroundCommand { get; }

    public RelayCommand RevertBackgroundCommand { get; }

    public RelayCommand AddEntryCommand { get; }

    public RelayCommand<DiaryEntry> EditEntryCommand { get; }

    public RelayCommand<DiaryEntry> DeleteEntryCommand { get; }

    public RelayCommand AddPhotosCommand { get; }

    public RelayCommand<PhotoViewModel> SetCoverCommand { get; }

    public RelayCommand<PhotoViewModel> RemovePhotoCommand { get; }

    public string Name => Character.Name;

    public string QuotedNickname => Character.QuotedNickname;

    public string FactsLine => Character.FactsLine;

    public string ArchivedLabel => Character.ArchivedLabel;

    public string Initials => Character.Initials;

    public string? PortraitPath { get; private set; }

    public string StoryHeader => Entries.Count > 0 ? $"Story ({Entries.Count})" : "Story";

    public string GalleryHeader => Photos.Count > 0 ? $"Gallery ({Photos.Count})" : "Gallery";

    public CharacterStatus Status
    {
        get => _status;
        set
        {
            if (!Set(ref _status, value) || _loadingStatus || Character.Status == value)
            {
                return;
            }

            _diary.SetStatus(Character, value);
            _onChanged();
        }
    }

    public string Background
    {
        get => _background;
        set
        {
            if (Set(ref _background, value))
            {
                Raise(nameof(HasUnsavedBackground));
            }
        }
    }

    public bool HasUnsavedBackground =>
        (Character.Background ?? string.Empty).Trim() != Background.Trim();

    private void Reload()
    {
        Entries.Clear();
        foreach (var entry in _diary.Entries(Character.Id))
        {
            Entries.Add(entry);
        }

        Photos.Clear();
        foreach (var photo in _diary.Photos(Character.Id))
        {
            Photos.Add(new PhotoViewModel(photo, _diary, photo.Id == Character.PortraitPhotoId));
        }

        PortraitPath = _diary.PortraitPath(Character);

        _loadingStatus = true;
        Status = Character.Status;
        _loadingStatus = false;

        Raise(nameof(PortraitPath), nameof(StoryHeader), nameof(GalleryHeader));
    }

    private void RaiseProfile() =>
        Raise(
            nameof(Name),
            nameof(QuotedNickname),
            nameof(FactsLine),
            nameof(ArchivedLabel),
            nameof(Initials));

    private void EditProfile()
    {
        var draft = CharacterDraft.From(Character);
        if (!_dialogs.ShowCharacterEditor(draft, PortraitPath, isNew: false))
        {
            return;
        }

        var skipped = _diary.UpdateCharacter(Character, draft);

        Background = Character.Background ?? string.Empty;
        Reload();
        RaiseProfile();
        _onChanged();
        WarnAboutSkipped(skipped);
    }

    private void DeleteCharacter()
    {
        var confirmed = _dialogs.Confirm(
            "Delete character",
            $"Delete {Character.Name} along with every story entry and photo?\n\n"
            + "This cannot be undone.");

        if (!confirmed)
        {
            return;
        }

        _diary.DeleteCharacter(Character.Id);
        _onDeleted();
    }

    private void SaveBackground()
    {
        _diary.SetBackground(Character, Background);
        Raise(nameof(HasUnsavedBackground));
        _onChanged();
    }

    private void AddEntry()
    {
        var draft = new EntryDraft();
        if (!_dialogs.ShowEntryEditor(draft, isNew: true))
        {
            return;
        }

        _diary.AddEntry(Character.Id, draft);
        Reload();
        _onChanged();
    }

    private void EditEntry(DiaryEntry entry)
    {
        var draft = EntryDraft.From(entry);
        if (!_dialogs.ShowEntryEditor(draft, isNew: false))
        {
            return;
        }

        _diary.UpdateEntry(entry, draft);
        Reload();
    }

    private void DeleteEntry(DiaryEntry entry)
    {
        var confirmed = _dialogs.Confirm(
            "Delete entry",
            $"Delete “{entry.Title}” from {Character.Name}'s story?\n\n"
            + "This cannot be undone.");

        if (!confirmed)
        {
            return;
        }

        _diary.DeleteEntry(entry.Id);
        Reload();
        _onChanged();
    }

    private void AddPhotos()
    {
        var chosen = _dialogs.PickImages();
        if (chosen.Count == 0)
        {
            return;
        }

        var skipped = _diary.AddPhotos(Character, chosen);
        Reload();
        _onChanged();
        WarnAboutSkipped(skipped);
    }

    private void SetCover(PhotoViewModel photo)
    {
        _diary.SetCover(Character, photo.Photo);

        foreach (var item in Photos)
        {
            item.IsCover = item.Id == photo.Id;
        }

        PortraitPath = _diary.PortraitPath(Character);
        Raise(nameof(PortraitPath));
        _onChanged();
    }

    private void RemovePhoto(PhotoViewModel photo)
    {
        var confirmed = _dialogs.Confirm(
            "Remove photo",
            "Remove this photo from the diary?\n\nThis cannot be undone.",
            confirmLabel: "Remove");

        if (!confirmed)
        {
            return;
        }

        _diary.RemovePhoto(Character, photo.Photo);
        Reload();
        _onChanged();
    }

    private void WarnAboutSkipped(int skipped)
    {
        if (skipped > 0)
        {
            _dialogs.Inform(
                "Roleplay Diary",
                $"{skipped} file(s) were skipped because Roleplay Diary cannot read them.");
        }
    }

    private void ExportPdf()
    {
        var destination = _dialogs.PickPdfDestination(
            _diary.Paths.Exports, Diary.SuggestedPdfName(Character));

        if (destination is null)
        {
            return;
        }

        string? written;
        try
        {
            written = _diary.ExportPdf(Character.Id, destination);
        }
        catch (IOException error)
        {
            _dialogs.Inform(
                "Export failed",
                $"Could not write the PDF.\n\n{error.Message}\n\n"
                + "If the file is open in a viewer, close it and try again.");
            return;
        }

        if (written is null)
        {
            return;
        }

        if (_dialogs.Offer("Export complete", $"Saved to:\n{written}", "Open it"))
        {
            _dialogs.OpenInShell(written);
        }
    }
}
