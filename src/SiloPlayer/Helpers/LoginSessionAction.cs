using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SiloPlayer.Helpers;

internal static class LoginSessionAction
{
    internal static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    internal static void AllowWrapping(Button button, string text)
    {
        button.Height = double.NaN;
        button.MinWidth = 0;
        button.Padding = new Thickness(12, 4, 12, 4);
        button.Content = Label(text);
    }
}
