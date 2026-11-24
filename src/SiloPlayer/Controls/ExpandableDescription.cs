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

    /// <summary>Lines shown before More: three on a hero, four on the ebook page.</summary>
    public int CollapsedLines
    {
        get => _collapsedLines;
        set
        {
            if (value == _collapsedLines) return;
            _collapsedLines = value;
            if (!_expanded) _text.MaxLines = value;
            UpdateToggle();
        }
    }
    private int _collapsedLines = 3;

    public ExpandableDescription()
    {
        Spacing = 4;
        _text = new TextBlock { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3, FontSize = 15, LineHeight = 24,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
        _toggle = SiloPlayer.Controls.MediaItemCardActions.Localize(new Button { Content = "More", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(-6, -2, -6, -2), CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed }, content: "web.detail.actions.more");
        _toggle.Click += (_, _) => Toggle();
        _text.IsTextTrimmedChanged += (_, _) => UpdateToggle();
        _text.SizeChanged += (_, _) => UpdateToggle();
        Children.Add(_text); Children.Add(_toggle);
    }

    private void ResetText() { _expanded = false; _text.Text = Text ?? ""; _text.MaxLines = _collapsedLines; UpdateToggle(); }
    private void Toggle() { _expanded = !_expanded; _text.MaxLines = _expanded ? 0 : _collapsedLines; UpdateToggle(); }
    private void UpdateToggle()
    {
        SiloPlayer.Controls.MediaItemCardActions.BindText(_toggle, "Content", element => element.Content = _expanded ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.overview.less", fallback: "Less") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.more", fallback: "More"));
        _toggle.Visibility = _expanded || _text.IsTextTrimmed ? Visibility.Visible : Visibility.Collapsed;
        MediaItemCardActions.BindText(_toggle, "Name", element => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, SiloPlayer.Core.Localization.AppStrings.Get(_expanded ? "web.detail.overview.less" : "web.detail.overview.more")));
    }
}
