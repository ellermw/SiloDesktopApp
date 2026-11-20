using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using T = SiloPlayer.Core.Services.Admin.AdminAppearanceOverlayText;

namespace SiloPlayer.Controls;

/// <summary>OverlayLookControls and BadgeStylePicker (official 66b836f82).</summary>
internal sealed partial class AdminAppearanceOverlayEditor
{
    private static readonly string[] PresetIds = ["minimal", "classic", "vibrant", "pill", "square", "plate", "band"];
    // The presets an older server (below settings revision 25) can store.
    private static readonly string[] LegacyPresetIds = ["minimal", "classic", "vibrant", "pill", "square"];
    private readonly StackPanel _advancedLook = new() { Spacing = 20 };
    private readonly Dictionary<string, ToggleButton> _presets = new(StringComparer.Ordinal);
    private readonly List<Action> _lookBindings = [];
    private readonly List<Control> _lookControls = [];
    private readonly Button _resetLook = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Slider _size = new() { Minimum = .75, Maximum = 2, StepFrequency = .05, Value = 1, Width = 260 };
    private readonly TextBlock _sizeLabel = new() { Text = "100%", VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Opacity = .7 };

    private void BuildLook()
    {
        var choices = new LogsWrapPanel { Spacing = 8 };
        AutomationProperties.SetName(choices, T.T("web.settings.overlays.look.style"));
        foreach (var id in PresetIds)
        {
            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(new Border
            {
                Height = 40, Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 38, 49, 67)), CornerRadius = new(6),
                Child = OverlayBadgeRenderer.Build("4K", "resolution", new(true, OverlayPosition.TopLeft, id == "vibrant" ? "#f5c518" : null), id, 1),
            });
            stack.Children.Add(new TextBlock { Text = T.T("web.settings.overlays.preset." + id + ".label"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            var button = new ToggleButton { Content = stack, Width = 106, Margin = new(0, 0, 0, 8), Padding = new(8), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(button, "AppearanceOverlayPreset-" + id);
            TintWhenChecked(button);
            AutomationProperties.SetName(button, T.T("web.settings.overlays.preset." + id + ".label"));
            ToolTipService.SetToolTip(button, T.T("web.settings.overlays.preset." + id + ".description"));
            OnToggle(button, () =>
            {
                if (CanEdit && (SupportsStyles || LegacyPresetIds.Contains(id)) && _prefs.Preset != id) Stage(_prefs with { Preset = id });
            });
            _presets.Add(id, button); choices.Children.Add(button);
        }
        _look.Children.Add(choices);
        _look.Children.Add(_advancedLook);

        var sizeHeader = new Grid();
        sizeHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        sizeHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        sizeHeader.Children.Add(Heading(T.T("web.settings.overlays.look.size")));
        Grid.SetColumn(_sizeLabel, 1); sizeHeader.Children.Add(_sizeLabel);
        var sizeBlock = new StackPanel { Spacing = 8, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
        sizeBlock.Children.Add(sizeHeader); sizeBlock.Children.Add(_size);
        AutomationProperties.SetName(_size, T.T("web.settings.overlays.look.sizeLabel"));
        AutomationProperties.SetAutomationId(_size, "AppearanceOverlaySize");
        // Staging is local to the page, so each step stages; nothing is written until Save.
        _size.ValueChanged += (_, args) => { if (!_binding) { _sizeLabel.Text = Math.Round(args.NewValue * 100) + "%"; CommitSize(); } };
        _advancedLook.Children.Add(sizeBlock);
        _lookControls.Add(_size);
        _lookBindings.Add(() =>
        {
            var value = _prefs.Style?.Size is >= .75 and <= 2 ? _prefs.Style.Size.Value : 1;
            if (Math.Abs(_size.Value - value) > .001) _size.Value = value;
            _sizeLabel.Text = Math.Round(value * 100) + "%";
        });
        Choice("shape", ["pill", "rounded", "square"], style => style.Shape, (style, value) => style with { Shape = value });
        Choice("weight", ["regular", "semibold", "extrabold"], style => style.FontWeight, (style, value) => style with { FontWeight = value });
        Choice("letters", ["capitals", "asWritten"], style => style.Uppercase switch { true => "capitals", false => "asWritten", _ => null },
            (style, value) => style with { Uppercase = value is null ? null : value == "capitals" });
        Choice("shadow", ["on", "off"], style => style.Shadow switch { true => "on", false => "off", _ => null },
            (style, value) => style with { Shadow = value is null ? null : value == "on" });
        Swatches("fill", [("#000000B3", "dark"), ("#0F172ACC", "slate"), ("#000000FF", "black"), ("#FFFFFFE6", "light"), ("#00000000", "none")],
            style => style.Background, (style, value) => style with { Background = value });
        Swatches("text", [("#FFFFFF", "white"), ("#000000", "black"), ("#F5C518", "gold"), ("#CBD5E1", "silver")],
            style => style.TextColor, (style, value) => style with { TextColor = value });
        Choice("border", ["none", "subtle", "strong"], style => style.BorderColor == null && style.BorderWidth == null ? null :
                style.BorderWidth == 0 ? "none" : style.BorderWidth >= 2 ? "strong" : "subtle",
            (style, value) => value switch
            {
                "none" => style with { BorderColor = null, BorderWidth = 0 },
                "subtle" => style with { BorderColor = "#FFFFFF26", BorderWidth = 1 },
                "strong" => style with { BorderColor = "#FFFFFFB3", BorderWidth = 2 },
                _ => style with { BorderColor = null, BorderWidth = null },
            });
        _resetLook.Content = T.T("web.settings.overlays.look.reset");
        AutomationProperties.SetAutomationId(_resetLook, "AppearanceOverlayResetLook");
        _resetLook.Click += (_, _) => { if (!_binding && SupportsStyles && CanEdit) Stage(_prefs with { Style = null }); };
        _advancedLook.Children.Add(_resetLook);
        _lookControls.Add(_resetLook);
    }

    private void CommitSize()
    {
        if (_binding || !SupportsStyles || !CanEdit) return;
        var next = _size.Value;
        var current = _prefs.Style?.Size is >= .75 and <= 2 ? _prefs.Style.Size.Value : 1;
        if (Math.Abs(next - current) < .001) return;
        SetStyle(style => style with { Size = Math.Abs(next - 1) < .001 ? null : Math.Round(next, 2) });
    }

    private void SetStyle(Func<OverlayStyle, OverlayStyle> update)
    {
        if (!SupportsStyles || _binding || !CanEdit) return;
        var next = update(_prefs.Style ?? new());
        // An emptied style leaves the document entirely (official withStyle).
        Stage(_prefs with { Style = next == new OverlayStyle() ? null : next });
    }

    private void Choice(string field, string[] options, Func<OverlayStyle, string?> selected, Func<OverlayStyle, string?, OverlayStyle> update)
    {
        var label = T.T("web.settings.overlays.look." + field);
        var block = new StackPanel { Spacing = 8 };
        block.Children.Add(Heading(label));
        var row = new LogsWrapPanel { Spacing = 8 };
        AutomationProperties.SetName(row, label);
        var buttons = new List<(string? Value, ToggleButton Button)>();
        foreach (var option in new string?[] { null }.Concat(options))
        {
            var text = option == null ? T.T("web.settings.overlays.look.styleDefault") : T.T("web.settings.overlays.look." + field + "." + option);
            var button = new ToggleButton { Content = text, Padding = new(10, 4, 10, 4), FontSize = 13, Margin = new(0, 0, 0, 8) };
            AutomationProperties.SetAutomationId(button, "AppearanceOverlayLook-" + field + "-" + (option ?? "preset"));
            OnToggle(button, () =>
            {
                if (selected(_prefs.Style ?? new()) != option) SetStyle(style => update(style, option));
            });
            buttons.Add((option, button)); row.Children.Add(button);
            _lookControls.Add(button);
        }
        block.Children.Add(row);
        _advancedLook.Children.Add(block);
        _lookBindings.Add(() =>
        {
            var current = selected(_prefs.Style ?? new());
            foreach (var (value, button) in buttons) button.IsChecked = value == current;
        });
    }

    private void Swatches(string field, (string Value, string Label)[] swatches, Func<OverlayStyle, string?> selected, Func<OverlayStyle, string?, OverlayStyle> update)
    {
        var label = T.T("web.settings.overlays.look." + field);
        var block = new StackPanel { Spacing = 8 };
        block.Children.Add(Heading(label));
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        AutomationProperties.SetName(stack, label);
        var buttons = new List<(string? Value, ToggleButton Button)>();
        var inherit = new ToggleButton { Content = T.T("web.settings.overlays.look.styleDefault"), Padding = new(10, 2, 10, 2), FontSize = 12, CornerRadius = new(14), Height = 28 };
        AutomationProperties.SetAutomationId(inherit, "AppearanceOverlaySwatch-" + field + "-preset");
        buttons.Add((null, inherit)); stack.Children.Add(inherit);
        foreach (var (value, name) in swatches)
        {
            var color = OverlayColor.Parse(value)!.Value;
            var button = new ToggleButton { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(2), CornerRadius = new(14), Content = new Border { CornerRadius = new(12), Width = 22, Height = 22, Background = OverlayBadgeRenderer.Brush(color) } };
            AutomationProperties.SetName(button, T.T("web.settings.overlays.look." + (field == "text" ? "textColor" : field) + "." + name));
            AutomationProperties.SetAutomationId(button, "AppearanceOverlaySwatch-" + field + "-" + name);
            ToolTipService.SetToolTip(button, AutomationProperties.GetName(button));
            buttons.Add((value, button)); stack.Children.Add(button);
        }
        foreach (var (value, button) in buttons)
        {
            OnToggle(button, () =>
            {
                if (!string.Equals(selected(_prefs.Style ?? new()), value, StringComparison.OrdinalIgnoreCase)) SetStyle(style => update(style, value));
            });
            _lookControls.Add(button);
        }
        block.Children.Add(stack);
        _advancedLook.Children.Add(block);
        _lookBindings.Add(() =>
        {
            var current = selected(_prefs.Style ?? new());
            foreach (var (value, button) in buttons)
                button.IsChecked = value == null ? current == null : string.Equals(value, current, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void SynchronizeLook()
    {
        var wasBinding = _binding;
        _binding = true;
        try
        {
            _advancedLook.Visibility = SupportsStyles ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (id, button) in _presets)
            {
                button.Visibility = SupportsStyles || LegacyPresetIds.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
                button.IsEnabled = CanEdit;
                button.IsChecked = id == _prefs.Preset;
            }
            foreach (var bind in _lookBindings) bind();
            foreach (var control in _lookControls) control.IsEnabled = CanEdit && SupportsStyles;
            _resetLook.Visibility = _prefs.Style != null ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _binding = wasBinding; }
    }
}
