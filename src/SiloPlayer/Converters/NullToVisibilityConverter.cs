using Microsoft.UI.Xaml.Data;

namespace SiloPlayer.Converters;

/// <summary>
/// Converts a value to Visibility based on whether it is null/empty.
/// Non-null/non-empty = Visible, null/empty = Collapsed.
/// Set Invert=true to reverse the logic.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasValue = value switch
        {
            string s => !string.IsNullOrEmpty(s),
            null => false,
            _ => true
        };

        if (Invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
