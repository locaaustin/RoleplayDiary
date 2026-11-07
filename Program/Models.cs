using Microsoft.Data.Sqlite;
using System.Globalization;

namespace RoleplayDiary;

/// <summary>Whether a character is still being played.</summary>
public enum CharacterStatus
{
    Alive = 0,
    Dead = 1,
    Shelved = 2
}

/// <summary>What kind of thing happened, for one entry in a character's story.</summary>
public enum EntryKind
{
    Journal = 0,
    Milestone = 1,
    Relationship = 2,
    Career = 3,
    Crime = 4,
    Injury = 5,
    Death = 6
}

/// <summary>
/// The wording the screen shows for each choice.
///
/// A C# enum is only a named number and cannot carry text of its own, so the labels
/// live here as extension methods. The number is what gets stored; the label is only
/// ever displayed, which means rewording one never disturbs saved data.
/// </summary>
public static class ChoiceLabels
{
    public static string Label(this CharacterStatus status) => status switch
    {
        CharacterStatus.Alive => "Alive",
        CharacterStatus.Dead => "Deceased",
        CharacterStatus.Shelved => "Shelved",
        _ => status.ToString()
    };

    public static string Label(this EntryKind kind) => kind switch
    {
        EntryKind.Journal => "Journal",
        EntryKind.Milestone => "Milestone",
        EntryKind.Relationship => "Relationship",
        EntryKind.Career => "Career",
        EntryKind.Crime => "Crime",
        EntryKind.Injury => "Injury",
        EntryKind.Death => "Death",
        _ => kind.ToString()
    };

    /// <summary>Never throw on an unexpected database value; fall back to the default.</summary>
    public static TEnum Coerce<TEnum>(long value) where TEnum : struct, Enum
    {
        var candidate = (TEnum)Enum.ToObject(typeof(TEnum), value);
        return Enum.IsDefined(candidate) ? candidate : default;
    }
}

/// <summary>
/// Text conversions for the two shapes SQLite stores: full UTC timestamps and plain
/// calendar dates.
///
/// Both the Python and the C# versions of this app write to the same database, so the
/// parser has to read both of their formats. Round-trip ("o") is what this one writes.
/// </summary>
public static class IsoFormat
{
    private const string DateFormat = "yyyy-MM-dd";

    public static DateTime UtcNow => DateTime.UtcNow;

    public static string ToText(DateTime moment) =>
        moment.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    public static DateTime Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return UtcNow;
        }

        // AssumeUniversal covers rows written without an offset; AdjustToUniversal
        // normalises anything that carries one.
        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : UtcNow;
    }

    public static string? DateToText(DateOnly? value) =>
        value?.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateOnly? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var head = text.Trim();
        if (head.Length > 10)
        {
            head = head[..10];
        }

        return DateOnly.TryParseExact(head, DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}

/// <summary>
/// Shared identity and birth time for everything the diary stores.
///
/// Abstract means it cannot be created on its own: it only exists to be inherited by
/// Character, DiaryEntry and CharacterPhoto, which is the whole point of a base class.
/// </summary>
public abstract class Record
{
    public long Id { get; set; }

    public DateTime CreatedUtc { get; set; } = IsoFormat.UtcNow;

    /// <summary>False until a repository has given it a row, and therefore an id.</summary>
    public bool IsSaved => Id > 0;

    /// <summary>
    /// Records that track their own last-modified time override this. Virtual means a
    /// subclass may replace it; the empty body here is what happens when none does.
    /// </summary>
    public virtual void Touch()
    {
    }
}

/// <summary>One character: the identity fields, plus the slow-moving life story.</summary>
public sealed class Character : Record
{
    public const string Columns =
        "Id, Name, Nickname, Age, Server, Occupation, Affiliation, " +
        "Status, Background, PortraitPhotoId, CreatedUtc, UpdatedUtc";

    public string Name { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public int? Age { get; set; }
    public string? Server { get; set; }
    public string? Occupation { get; set; }
    public string? Affiliation { get; set; }
    public CharacterStatus Status { get; set; } = CharacterStatus.Alive;
    public string? Background { get; set; }
    public long? PortraitPhotoId { get; set; }
    public DateTime UpdatedUtc { get; set; } = IsoFormat.UtcNow;

    public static Character FromRow(SqliteDataReader row) => new()
    {
        Id = row.GetInt64(0),
        Name = row.GetString(1),
        Nickname = row.NullableString(2),
        Age = row.NullableInt(3),
        Server = row.NullableString(4),
        Occupation = row.NullableString(5),
        Affiliation = row.NullableString(6),
        Status = ChoiceLabels.Coerce<CharacterStatus>(row.GetInt64(7)),
        Background = row.NullableString(8),
        PortraitPhotoId = row.NullableLong(9),
        CreatedUtc = IsoFormat.Parse(row.GetString(10)),
        UpdatedUtc = IsoFormat.Parse(row.GetString(11))
    };

    public override void Touch() => UpdatedUtc = IsoFormat.UtcNow;

    public string QuotedNickname =>
        string.IsNullOrWhiteSpace(Nickname) ? string.Empty : $"“{Nickname}”";

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Nickname) ? Name : $"{Name} {QuotedNickname}";

    public string AgeLabel => Age is > 0 ? $"{Age} years old" : string.Empty;

    public string ArchivedLabel => $"archived {CreatedUtc.ToLocalTime():d MMMM yyyy}";

    /// <summary>Placeholder shown wherever a portrait is missing.</summary>
    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var letters = string.Concat(words.Take(2).Select(word => word[0])).ToUpperInvariant();
            return letters.Length > 0 ? letters : "?";
        }
    }

    public string FactsLine =>
        string.Join(" · ",
            new[] { AgeLabel, Server, Occupation, Affiliation }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>The one-line summary under the name in the roster.</summary>
    public string MetaLine(int entryCount)
    {
        var parts = new[] { AgeLabel, Server, Occupation }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        parts.Add(entryCount == 1 ? "1 entry" : $"{entryCount} entries");
        return string.Join(" · ", parts);
    }

    public bool Matches(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var needle = search.Trim();
        return Contains(Name) || Contains(Nickname) || Contains(Server);

        bool Contains(string? value) =>
            value is not null &&
            value.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Something that happened. The unit the story is told in.</summary>
public sealed class DiaryEntry : Record
{
    public const string Columns =
        "Id, CharacterId, Title, Body, Kind, OccurredOn, CreatedUtc, UpdatedUtc";

    public long CharacterId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public EntryKind Kind { get; set; } = EntryKind.Journal;
    public DateOnly? OccurredOn { get; set; }
    public DateTime UpdatedUtc { get; set; } = IsoFormat.UtcNow;

    public static DiaryEntry FromRow(SqliteDataReader row) => new()
    {
        Id = row.GetInt64(0),
        CharacterId = row.GetInt64(1),
        Title = row.GetString(2),
        Body = row.NullableString(3),
        Kind = ChoiceLabels.Coerce<EntryKind>(row.GetInt64(4)),
        OccurredOn = IsoFormat.ParseDate(row.NullableString(5)),
        CreatedUtc = IsoFormat.Parse(row.GetString(6)),
        UpdatedUtc = IsoFormat.Parse(row.GetString(7))
    };

    public override void Touch() => UpdatedUtc = IsoFormat.UtcNow;

    public bool IsDated => OccurredOn is not null;

    public string DateLabel =>
        OccurredOn is { } date ? date.ToString("d MMMM yyyy") : "undated";

    public string ShortDateLabel =>
        OccurredOn is { } date ? date.ToString("dd MMM yyyy") : "undated";

    public string KindLabel => Kind.Label();

    /// <summary>
    /// Oldest first. Undated entries fall back to when they were written, so they land
    /// in the story roughly where the player added them.
    /// </summary>
    public (DateOnly Date, long Id) TimelineKey =>
        (OccurredOn ?? DateOnly.FromDateTime(CreatedUtc), Id);
}

/// <summary>
/// A screenshot. The file itself is copied into the diary's own media folder, so
/// moving or deleting the original never breaks the archive.
/// </summary>
public sealed class CharacterPhoto : Record
{
    public const string Columns = "Id, CharacterId, FileName, Caption, TakenOn, CreatedUtc";

    public long CharacterId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public DateOnly? TakenOn { get; set; }

    /// <summary>Filled in by the repository, which is the only thing that knows the folder.</summary>
    public string? FullPath { get; set; }

    public static CharacterPhoto FromRow(SqliteDataReader row) => new()
    {
        Id = row.GetInt64(0),
        CharacterId = row.GetInt64(1),
        FileName = row.GetString(2),
        Caption = row.NullableString(3),
        TakenOn = IsoFormat.ParseDate(row.NullableString(4)),
        CreatedUtc = IsoFormat.Parse(row.GetString(5))
    };

    public string AddedLabel => $"added {CreatedUtc.ToLocalTime():dd MMM yyyy}";
}

/// <summary>A character as the sidebar needs them, with their counts worked out.</summary>
public sealed class RosterItem
{
    public RosterItem(Character character, int entryCount, string? portraitPath)
    {
        Character = character;
        EntryCount = entryCount;
        PortraitPath = portraitPath;
    }

    public Character Character { get; }
    public int EntryCount { get; }
    public string? PortraitPath { get; }

    public long Id => Character.Id;
    public string Name => Character.Name;
    public CharacterStatus Status => Character.Status;
    public string Meta => Character.MetaLine(EntryCount);
}
