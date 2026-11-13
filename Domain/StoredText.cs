namespace RoleplayDiary.Domain;

/// <summary>Turns blank form input into a database null.</summary>
public static class StoredText
{
    public static string? OrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
