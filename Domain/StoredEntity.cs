namespace RoleplayDiary.Domain;

/// <summary>Identity shared by every row the diary stores.</summary>
public abstract class StoredEntity
{
    public long Id { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
