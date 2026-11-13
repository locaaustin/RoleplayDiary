namespace RoleplayDiary.Domain;

/// <summary>Image types the diary is willing to copy in.</summary>
public static class ImageFiles
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
        };

    public static bool HasSupportedExtension(string path) =>
        Extensions.Contains(Path.GetExtension(path));

    public static string FileDialogFilter()
    {
        var patterns = string.Join(";", Extensions.Order().Select(extension => "*" + extension));
        return $"Images ({patterns})|{patterns}|All files (*.*)|*.*";
    }
}
