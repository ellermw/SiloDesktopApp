using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Converters;

public class ThumbhashToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string base64Hash || string.IsNullOrEmpty(base64Hash))
            return null;

        try
        {
            var decoded = ThumbhashDecoder.Decode(base64Hash);
            var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);

            // WriteableBitmap expects BGRA, ThumbhashDecoder returns RGBA
            var bgra = new byte[decoded.Rgba.Length];
            for (int i = 0; i < decoded.Rgba.Length; i += 4)
            {
                bgra[i] = decoded.Rgba[i + 2];     // B
                bgra[i + 1] = decoded.Rgba[i + 1]; // G
                bgra[i + 2] = decoded.Rgba[i];     // R
                bgra[i + 3] = decoded.Rgba[i + 3]; // A
            }

            bgra.CopyTo(bitmap.PixelBuffer);
            bitmap.Invalidate();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
