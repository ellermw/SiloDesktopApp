using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.System;

namespace SiloPlayer.Controls;

public sealed class MetadataTagsInput : UserControl
{
    private readonly WrapPanel _chips = new() { HorizontalSpacing = 6, VerticalSpacing = 6 };
    private readonly TextBox _input = new() { PlaceholderText = "Add...", MinWidth = 80, Width = 80, Height = 22, MinHeight = 0,
        Padding = new Thickness(0), FontSize = 14, BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly List<string> _values;
    public event EventHandler? Changed;
    public string[] Values => _values.ToArray();
    public MetadataTagsInput(IEnumerable<string> values)
    {
        _values = values.ToList(); MinHeight = 36;
        Content = new Border { Child = _chips, Padding = new Thickness(12, 6, 12, 6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), MinHeight = 36,
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] };
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            _input.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            _input.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Tapped += (_, _) => _input.Focus(FocusState.Programmatic);
        Render();
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { Commit(); e.Handled = true; }
            if (e.Key == VirtualKey.Back && _input.Text.Length == 0 && _values.Count > 0)
            { _values.RemoveAt(_values.Count - 1); Render(); Changed?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        };
        _input.LostFocus += (_, _) => Commit();
        _input.TextChanged += (_, _) => { if (_input.Text.Contains(',')) Commit(); };
    }
    public void CommitPending() => Commit();
    private void Commit()
    {
        var additions = _input.Text.Split(',').Select(value => value.Trim()).Where(value => value.Length > 0).ToArray();
        _input.Text = "";
        foreach (var value in additions) if (!_values.Contains(value)) _values.Add(value);
        if (additions.Length > 0) { Render(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    private void Render()
    {
        _chips.Children.Clear();
        foreach (var value in _values)
        {
            var label = new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
            var remove = new Button { Content = WebUiIcon.Create("x", 12, (Brush)Application.Current.Resources["PrimaryTextBrush"]),
                Width = 16, Height = 16, MinWidth = 0, MinHeight = 0, Padding = new Thickness(2), CornerRadius = new CornerRadius(8),
                Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, "Remove " + value);
            remove.Click += (_, _) => { _values.Remove(value); Render(); Changed?.Invoke(this, EventArgs.Empty); };
            var contents = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { label, remove } };
            var accent = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            var chip = new Border { Child = contents, Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(accent.Color) { Opacity = .15 } };
            _chips.Children.Add(chip);
        }
        _input.PlaceholderText = _values.Count == 0 ? "Add..." : "";
        _chips.Children.Add(_input);
    }
}
