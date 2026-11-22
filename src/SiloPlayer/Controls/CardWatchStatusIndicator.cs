using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

/// <summary>A passive poster status corner; it does not own watched actions.</summary>
public sealed class CardWatchStatusIndicator : UserControl
{
    private readonly Border _circle = new();
    private (string? Name, CardOverlayGeometry Geometry, double BorderInset)? _shown;
    public double ReservedWidth { get; private set; }
    /// <summary>
    /// The watched check is black on the emerald circle: white on that green is 2.5:1, below the 3:1 a graphic needs, and black is 8.5:1
    /// (official CardWatchStatus, #5080). The remaining-episodes count stays white on its dark circle.
    /// </summary>
    internal static readonly Windows.UI.Color PlayedForeground = Microsoft.UI.Colors.Black;
    internal static readonly Windows.UI.Color CountForeground = Microsoft.UI.Colors.White;

    private readonly TextBlock _text = new()
    {
        Foreground = new SolidColorBrush(CountForeground),
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public CardWatchStatusIndicator()
    {
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Visibility = Visibility.Collapsed;
        _circle.Child = _text;
        Content = _circle;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Content);
    }

    public void Update(UserState? state, CardOverlayGeometry geometry, double borderInset = 1)
    {
        var name = CardWatchStatus.AccessibleName(state);
        var presentation = (name, geometry, borderInset);
        if (_shown == presentation) return;
        _shown = presentation;
        Visibility = name is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(this, name ?? "");
        var scale = geometry.Scale;
        var edge = borderInset + geometry.EdgeInset;
        Margin = new Thickness(edge);
        MinWidth = Height = geometry.WatchStatusSize;
        _circle.CornerRadius = new CornerRadius(11 * scale);
        _circle.Padding = new Thickness(4.4 * scale, 0, 4.4 * scale, 0);
        _circle.BorderThickness = new Thickness(scale);
        _circle.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(state?.Played == true ? (byte)89 : (byte)51, 255, 255, 255));
        _circle.Background = new SolidColorBrush(state?.Played == true
            ? Microsoft.UI.ColorHelper.FromArgb(255, 16, 185, 129)
            : Microsoft.UI.ColorHelper.FromArgb(166, 0, 0, 0));
        _text.Foreground = new SolidColorBrush(state?.Played == true ? PlayedForeground : CountForeground);
        _text.FontSize = (state?.Played == true ? 13 : 11) * scale;
        _text.Text = state?.Played == true ? "\u2713" : CardWatchStatus.RemainingEpisodes(state)?.ToString() ?? "";
        _text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, Height));
        ReservedWidth = Math.Max(MinWidth, _text.DesiredSize.Width + _circle.Padding.Left + _circle.Padding.Right + 2 * scale);
    }
}
