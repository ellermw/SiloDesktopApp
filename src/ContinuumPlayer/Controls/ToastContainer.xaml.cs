using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace ContinuumPlayer.Controls;

/// <summary>
/// F2: Host container for transient toast notifications. Instantiated once
/// inside MainWindow and registered with <see cref="Services.ToastService"/>.
/// Individual toasts are <see cref="Border"/> elements dynamically appended
/// to <c>ToastStack</c>, with a slide-in + fade-out animation and a 4 s
/// auto-dismiss timer.
/// </summary>
public sealed partial class ToastContainer : UserControl
{
    public ToastContainer()
    {
        this.InitializeComponent();
    }

    public enum ToastKind { Success, Error, Info, Warning }

    /// <summary>
    /// Show a toast with the given message + kind. Returns immediately; the
    /// toast animates in, auto-dismisses after <paramref name="durationMs"/>,
    /// then fades out and is removed from the visual tree.
    /// </summary>
    public void Show(string message, ToastKind kind, int durationMs = 4000)
    {
        var toast = BuildToast(message, kind);
        ToastStack.Children.Insert(0, toast);

        // Slide-in from the right + fade-in.
        var transform = new TranslateTransform { X = 48 };
        toast.RenderTransform = transform;
        toast.Opacity = 0;

        var sbIn = new Storyboard();
        var slide = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, "X");
        sbIn.Children.Add(slide);

        var fade = new DoubleAnimation
        {
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
        };
        Storyboard.SetTarget(fade, toast);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sbIn.Children.Add(fade);

        sbIn.Begin();

        // Auto-dismiss timer.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DismissToast(toast);
        };
        timer.Start();
    }

    private void DismissToast(Border toast)
    {
        var fade = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(fade, toast);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(fade);
        sb.Completed += (_, _) =>
        {
            if (ToastStack.Children.Contains(toast))
                ToastStack.Children.Remove(toast);
        };
        sb.Begin();
    }

    private Border BuildToast(string message, ToastKind kind)
    {
        var (iconGlyph, accent) = kind switch
        {
            ToastKind.Success => ("\uE73E", Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)),
            ToastKind.Error => ("\uEA39", Color.FromArgb(0xFF, 0xEF, 0x6B, 0x73)),
            ToastKind.Warning => ("\uE7BA", Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)),
            _ => ("\uE946", (Color)Application.Current.Resources["AccentColor"]),
        };

        var root = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(1, 1, 1, 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            MinWidth = 260,
            MaxWidth = 360,
            IsHitTestVisible = true,
        };

        // Accent stripe on the left edge.
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = iconGlyph,
            FontSize = 16,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new TextBlock
        {
            Text = message,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        // Dismiss button.
        var dismissBtn = new Button
        {
            Content = new FontIcon
            {
                Glyph = "\uE711",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            },
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4),
            MinWidth = 24,
            MinHeight = 24,
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            VerticalAlignment = VerticalAlignment.Center,
        };
        dismissBtn.Click += (_, _) => DismissToast(root);
        Grid.SetColumn(dismissBtn, 2);
        grid.Children.Add(dismissBtn);

        root.Child = grid;
        return root;
    }
}
