using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace SiloPlayer.Controls;

public sealed class MetadataTagsInput : StackPanel
{
    private readonly WrapPanel _chips = new() { HorizontalSpacing = 6, VerticalSpacing = 6 };
    private readonly TextBox _input = new() { PlaceholderText = "Add…", MinWidth = 100 };
    private readonly List<string> _values;
    public event EventHandler? Changed;
    public string[] Values => _values.ToArray();
    public MetadataTagsInput(IEnumerable<string> values)
    {
        _values = values.ToList(); Spacing = 6; Children.Add(_chips); Children.Add(_input); Render();
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
            var chip = new Button { Content = value + "  ×", Padding = new Thickness(8, 2, 8, 2), FontSize = 12, CornerRadius = new CornerRadius(6) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, "Remove " + value);
            chip.Click += (_, _) => { _values.Remove(value); Render(); Changed?.Invoke(this, EventArgs.Empty); };
            _chips.Children.Add(chip);
        }
    }
}
