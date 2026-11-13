namespace RoleplayDiary.Domain;

/// <summary>
/// A screenshot copied into the diary's own media folder. The row stores the file
/// name; <see cref="FullPath"/> is filled in when the row is loaded.
/// </summary>
public sealed class CharacterPhoto : StoredEntity
{
    public long CharacterId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? Caption { get; set; }

    public DateOnly? TakenOn { get; set; }

    public string? FullPath { get; set; }

    public string AddedLabel => $"added {CreatedUtc.ToLocalTime():dd MMM yyyy}";
}
