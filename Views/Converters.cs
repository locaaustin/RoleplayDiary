using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoleplayDiary.Domain;

namespace RoleplayDiary.Views;

public abstract class OneWayConverter : IValueConverter
{
    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{GetType().Name} does not convert back.");
}

public sealed class StringToVisibilityConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class NullToVisibilityConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class NotNullToVisibilityConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class CountToVisibilityConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class EmptyCountToVisibilityConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class InverseBoolToVisibilityConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class EnumLabelConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            CharacterStatus status => status.Label(),
            EntryKind kind => kind.Label(),
            _ => value?.ToString() ?? string.Empty
        };
}

public sealed class StatusToBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is CharacterStatus status
            ? status switch
            {
                CharacterStatus.Alive => "AliveBrush",
                CharacterStatus.Dead => "DeadBrush",
                CharacterStatus.Shelved => "ShelvedBrush",
                _ => "TextMutedBrush"
            }
            : "TextMutedBrush";

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}

/// <summary>
/// Loads a file into an image and releases the file handle immediately, so the
/// original can be moved afterwards. A decode width keeps thumbnails small.
/// </summary>
public sealed class PathToImageConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);

            if (parameter is string widthText && int.TryParse(widthText, out var width))
            {
                image.DecodePixelWidth = width;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
