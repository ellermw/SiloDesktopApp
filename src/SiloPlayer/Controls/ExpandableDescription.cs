using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Controls;

/// <summary>Three-line detail overview; offers More only when measured text is actually clipped.</summary>
public sealed class ExpandableDescription : StackPanel
{
    private readonly TextBlock _text;
    private readonly Button _toggle;
    private bool _expanded;
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(ExpandableDescription),
        new PropertyMetadata("", (sender, _) => ((ExpandableDescription)sender).ResetText()));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public ExpandableDescription()
    {
        Spacing = 4;
        _text = new TextBlock { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3, FontSize = 15, LineHeight = 24,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
        _toggle = new Button { Content = "More", Padding = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
        _toggle.Click += (_, _) => Toggle();
        _text.IsTextTrimmedChanged += (_, _) => UpdateToggle();
        _text.SizeChanged += (_, _) => UpdateToggle();
        Children.Add(_text); Children.Add(_toggle);
    }

    private void ResetText() { _expanded = false; _text.Text = Text ?? ""; _text.MaxLines = 3; UpdateToggle(); }
    private void Toggle() { _expanded = !_expanded; _text.MaxLines = _expanded ? 0 : 3; UpdateToggle(); }
    private void UpdateToggle()
    {
        _toggle.Content = _expanded ? "Less" : "More";
        _toggle.Visibility = _expanded || _text.IsTextTrimmed ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_toggle, _expanded ? "Show less description" : "Show full description");
    }
}
