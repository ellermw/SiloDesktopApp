using Microsoft.UI.Xaml.Data;

namespace SiloPlayer.Converters;

/// <summary>
/// Converts a string to its first character as a reader sees it, upper-cased for the interface language.
/// Returns "?" for null/empty strings.
/// </summary>
public class StringToInitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return SiloPlayer.Core.Localization.AvatarInitials.One(value as string);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
