using Microsoft.UI.Xaml.Data;

namespace SiloPlayer.Converters;

/// <summary>
/// Inverts a boolean value. Useful for IsEnabled bindings (e.g., disable while loading).
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b ? !b : true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is bool b ? !b : false;
    }
}
