using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

public static class RequestDownloadProgress
{
    public static FrameworkElement Build(RequestDownload download, double maxWidth = double.PositiveInfinity)
    {
        var panel = new StackPanel { Spacing = 4, MaxWidth = maxWidth, HorizontalAlignment = HorizontalAlignment.Left };
        if (double.IsFinite(maxWidth)) panel.Width = maxWidth;
        var percent = RequestViewerPolicy.DownloadPercent(download);
        var label = new TextBlock { Text = RequestViewerPolicy.DownloadLabel(download), FontSize = 12, LineHeight = 16.5,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap };
        if (percent.HasValue)
            panel.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = percent.Value,
                Style = (Style)Application.Current.Resources["RequestDownloadProgressBarStyle"],
                Foreground = (Brush)Application.Current.Resources["AccentBrush"], Background = (Brush)Application.Current.Resources["SurfaceBrush"] });
        panel.Children.Add(label); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(panel, label.Text);
        return panel;
    }
}
