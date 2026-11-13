using System.Globalization;

namespace RoleplayDiary.Domain;

/// <summary>
/// Dates people actually type. Anything unrecognised is undated rather than an error.
/// </summary>
public static class FlexibleDate
{
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "d/M/yyyy", "M/d/yyyy", "d-M-yyyy", "d.M.yyyy",
        "d MMMM yyyy", "d MMM yyyy", "MMMM d yyyy", "MMM d yyyy",
        "MMMM d, yyyy", "MMM d, yyyy", "MMMM yyyy", "MMM yyyy", "yyyy"
    };

    private static readonly Dictionary<string, int> Keywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["today"] = 0,
            ["now"] = 0,
            ["yesterday"] = -1,
            ["tomorrow"] = 1
        };

    public static DateOnly? Parse(string? text)
    {
        var cleaned = Collapse(text);
        if (cleaned.Length == 0)
        {
            return null;
        }

        if (Keywords.TryGetValue(cleaned, out var offset))
        {
            return DateOnly.FromDateTime(DateTime.Today.AddDays(offset));
        }

        return DateTime.TryParseExact(
            cleaned,
            Formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? DateOnly.FromDateTime(parsed)
            : null;
    }

    public static (string Message, bool Understood) Describe(string? text)
    {
        if (Collapse(text).Length == 0)
        {
            return ("No date - the entry will sit where you added it.", true);
        }

        return Parse(text) is { } parsed
            ? ($"Reads as {parsed:d MMMM yyyy}.", true)
            : ("Not a date we recognise, so this will be saved undated.", false);
    }

    private static string Collapse(string? text) =>
        string.Join(' ', (text ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
