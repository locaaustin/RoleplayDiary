using System.Globalization;

namespace RoleplayDiary.Persistence;

/// <summary>
/// UTC timestamps and calendar dates as SQLite text. Readers accept both the
/// round-trip format this app writes and the looser text older versions stored.
/// </summary>
public static class IsoFormat
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string ToText(DateTime moment) =>
        moment.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    public static DateTime Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DateTime.UtcNow;
        }

        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : DateTime.UtcNow;
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

        return DateOnly.TryParseExact(
            head,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }
}
