namespace RoleplayDiary;

/// <summary>
/// Every file the diary owns lives under one folder in the user's roaming profile,
/// so backing up the whole archive means copying a single directory.
///
/// The layout is deliberately the one earlier versions used, so an existing diary is
/// picked up rather than orphaned.
/// </summary>
public sealed class AppPaths
{
    public const string FolderName = "RoleplayDiary";
    public const string DatabaseName = "roleplaydiary.db";

    public AppPaths(string? root = null)
    {
        Root = root ?? DefaultRoot();

        foreach (var folder in new[] { Root, Media, Exports })
        {
            Directory.CreateDirectory(folder);
        }
    }

    /// <summary>%AppData%\RoleplayDiary.</summary>
    private static string DefaultRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            FolderName);

    public string Root { get; }

    public string Database => Path.Combine(Root, DatabaseName);

    public string Media => Path.Combine(Root, "media");

    public string Exports => Path.Combine(Root, "exports");

    public string CrashLog => Path.Combine(Root, "crash.log");

    /// <summary>One folder per character, created on demand.</summary>
    public string MediaFor(long characterId)
    {
        var folder = Path.Combine(Media, characterId.ToString());
        Directory.CreateDirectory(folder);
        return folder;
    }

    public string PhotoPath(long characterId, string fileName) =>
        Path.Combine(Media, characterId.ToString(), fileName);

    public override string ToString() => $"AppPaths({Root})";
}
