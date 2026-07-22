using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SiloPlayer.Converters;

/// <summary>
/// Converts an absolute artwork URL into the ImageSource type expected by
/// WinUI. Compiled x:Bind cannot safely coerce a nullable string directly to
/// Image.Source and throws while a recycled template is being realized.
/// </summary>
public sealed class UrlToImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string url ||
            string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null!;
        }

        return new BitmapImage(uri) { DecodePixelWidth = 342 };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
