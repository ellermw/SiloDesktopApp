using Microsoft.UI.Xaml.Data;

namespace SiloPlayer.Converters;

public sealed class IntroSkipModeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        "never" => "Never",
        "ask" => "Ask to skip",
        "always" => "Skip automatically",
        _ => value,
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
