namespace RoleplayDiary.Persistence;

/// <summary>
/// Every file the diary owns lives under one folder, so a backup is a single directory.
/// The layout matches earlier versions, so an existing diary is picked up in place.
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

    public string Root { get; }

    public string Database => Path.Combine(Root, DatabaseName);

    public string Media => Path.Combine(Root, "media");

    public string Exports => Path.Combine(Root, "exports");

    public string CrashLog => Path.Combine(Root, "crash.log");

    public string MediaFor(long characterId)
    {
        var folder = Path.Combine(Media, characterId.ToString());
        Directory.CreateDirectory(folder);
        return folder;
    }

    public string PhotoPath(long characterId, string fileName) =>
        Path.Combine(Media, characterId.ToString(), fileName);

    private static string DefaultRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            FolderName);
}
