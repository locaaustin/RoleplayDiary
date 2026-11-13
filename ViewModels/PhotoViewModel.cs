using RoleplayDiary.Domain;
using RoleplayDiary.Services;

namespace RoleplayDiary.ViewModels;

/// <summary>One gallery photo. The caption saves itself when the player leaves the field.</summary>
public sealed class PhotoViewModel : ObservableObject
{
    private readonly Diary _diary;
    private string _caption;
    private bool _isCover;

    public PhotoViewModel(CharacterPhoto photo, Diary diary, bool isCover)
    {
        Photo = photo;
        _diary = diary;
        _isCover = isCover;
        _caption = photo.Caption ?? string.Empty;
    }

    public CharacterPhoto Photo { get; }

    public long Id => Photo.Id;

    public string? FullPath => Photo.FullPath;

    public string AddedLabel => Photo.AddedLabel;

    public string Caption
    {
        get => _caption;
        set
        {
            if (Set(ref _caption, value))
            {
                _diary.SetCaption(Photo, value);
            }
        }
    }

    public bool IsCover
    {
        get => _isCover;
        set => Set(ref _isCover, value);
    }
}
