using Microsoft.UI.Xaml.Data;

namespace SiloPlayer.Converters;

/// <summary>
/// Converts a string to its first character (uppercase).
/// Returns "?" for null/empty strings.
/// </summary>
public class StringToInitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string s && !string.IsNullOrEmpty(s))
            return s[0].ToString().ToUpperInvariant();
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
