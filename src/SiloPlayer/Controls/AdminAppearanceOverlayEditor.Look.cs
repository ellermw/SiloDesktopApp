using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using T = SiloPlayer.Core.Services.Admin.AdminAppearanceOverlayText;

namespace SiloPlayer.Controls;

internal sealed partial class AdminAppearanceOverlayEditor
{
    private readonly StackPanel _advancedLook = new() { Spacing = 12 };
    private readonly Dictionary<string, Button> _presets = new(StringComparer.Ordinal);
    private readonly List<Action> _lookBindings = [];
    private readonly List<Control> _lookControls = [];
    private readonly Button _resetLook = new() { HorizontalAlignment = HorizontalAlignment.Left };

    private void BuildLook()
    {
        var choices = new LogsWrapPanel { Spacing = 8 };
        foreach (var id in new[] { "minimal", "classic", "vibrant", "pill", "square", "plate", "band" })
        {
            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(new Border
            {
                Height = 40, Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 38, 49, 67)), CornerRadius = new(6),
                Child = OverlayBadgeRenderer.Build("4K", "resolution", new(true, OverlayPosition.TopLeft, id == "vibrant" ? "#f5c518" : null), id, 1),
            });
            stack.Children.Add(new TextBlock { Text = T.T("web.settings.overlays.preset." + id + ".label"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            var button = new Button { Content = stack, Width = 106, Margin = new(0, 0, 8, 8), Padding = new(8) };
            AutomationProperties.SetAutomationId(button, "AppearanceOverlayPreset-" + id);
            AutomationProperties.SetName(button, T.T("web.settings.overlays.preset." + id + ".label"));
            ToolTipService.SetToolTip(button, T.T("web.settings.overlays.preset." + id + ".description"));
            button.Click += (_, _) => { if (id is not ("plate" or "band") || Advanced) Stage(_prefs with { Preset = id }); };
            _presets.Add(id, button); choices.Children.Add(button);
        }
        _defaults.Children.Add(choices);
        _defaults.Children.Add(_advancedLook);
        var sizeLabel = new TextBlock { Text = "100%", VerticalAlignment = VerticalAlignment.Center };
        var size = new Slider { Minimum = .75, Maximum = 2, StepFrequency = .05, Value = 1, Width = 240 };
        AutomationProperties.SetName(size, T.T("web.settings.overlays.look.sizeLabel"));
        AutomationProperties.SetAutomationId(size, "AppearanceOverlaySize");
        size.ValueChanged += (_, args) => SetStyle(style => style with { Size = Math.Abs(args.NewValue - 1) < .001 ? null : Math.Round(args.NewValue, 2) });
        var sizeControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        sizeControls.Children.Add(size); sizeControls.Children.Add(sizeLabel);
        _advancedLook.Children.Add(Field(Heading(T.T("web.settings.overlays.look.size")), sizeControls));
        _lookControls.Add(size);
        _lookBindings.Add(() => { var value = _prefs.Style?.Size is >= .75 and <= 2 ? _prefs.Style.Size.Value : 1; if (Math.Abs(size.Value - value) > .001) size.Value = value; sizeLabel.Text = Math.Round(value * 100) + "%"; });
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
        _resetLook.Click += (_, _) => { if (Advanced) Stage(_prefs with { Style = null }); };
        _advancedLook.Children.Add(_resetLook);
        _lookControls.Add(_resetLook);
    }

    private void SetStyle(Func<OverlayStyle, OverlayStyle> update)
    {
        if (!Advanced || _binding || !CanEdit) return;
        Stage(_prefs with { Style = update(_prefs.Style ?? new()) });
    }

    private void Choice(string field, string[] options, Func<OverlayStyle, string?> selected, Func<OverlayStyle, string?, OverlayStyle> update)
    {
        var label = T.T("web.settings.overlays.look." + field);
        var box = new ComboBox { MinWidth = 180 };
        box.Items.Add(new ComboBoxItem { Tag = "", Content = T.T("web.settings.overlays.look.styleDefault") });
        foreach (var option in options) box.Items.Add(new ComboBoxItem { Tag = option, Content = T.T("web.settings.overlays.look." + field + "." + option) });
        AutomationProperties.SetName(box, label);
        AutomationProperties.SetAutomationId(box, "AppearanceOverlayLook-" + field);
        box.SelectionChanged += (_, _) => SetStyle(style => update(style, (box.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } value ? value : null));
        _advancedLook.Children.Add(Field(Heading(label), box));
        _lookControls.Add(box);
        _lookBindings.Add(() => box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == (selected(_prefs.Style ?? new()) ?? "")) ?? box.Items[0]);
    }

    private void Swatches(string field, (string Value, string Label)[] swatches, Func<OverlayStyle, string?> selected, Func<OverlayStyle, string?, OverlayStyle> update)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var buttons = new List<(string? Value, Button Button)>();
        var inherit = new Button { Content = T.T("web.settings.overlays.look.styleDefault"), Padding = new(8, 4, 8, 4) };
        inherit.Click += (_, _) => SetStyle(style => update(style, null));
        buttons.Add((null, inherit)); stack.Children.Add(inherit);
        _lookControls.Add(inherit);
        foreach (var (value, label) in swatches)
        {
            var color = OverlayColor.Parse(value)!.Value;
            var button = new Button { Width = 28, Height = 28, Padding = new(2), Background = OverlayBadgeRenderer.Brush(color), CornerRadius = new(14) };
            AutomationProperties.SetName(button, T.T("web.settings.overlays.look." + (field == "text" ? "textColor" : field) + "." + label));
            AutomationProperties.SetAutomationId(button, "AppearanceOverlaySwatch-" + field + "-" + label);
            ToolTipService.SetToolTip(button, AutomationProperties.GetName(button));
            button.Click += (_, _) => SetStyle(style => update(style, value));
            buttons.Add((value, button)); stack.Children.Add(button);
            _lookControls.Add(button);
        }
        _advancedLook.Children.Add(Field(Heading(T.T("web.settings.overlays.look." + field)), stack));
        _lookBindings.Add(() =>
        {
            foreach (var (value, button) in buttons)
            {
                var active = string.Equals(value, selected(_prefs.Style ?? new()), StringComparison.OrdinalIgnoreCase);
                button.BorderThickness = new(active ? 2 : 1);
                button.BorderBrush = new SolidColorBrush(active ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Gray);
                AutomationProperties.SetItemStatus(button, active ? "Selected" : "");
            }
        });
    }

    private void SynchronizeLook()
    {
        _advancedLook.Visibility = Advanced ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (id, button) in _presets)
        {
            button.Visibility = Advanced || id is not ("plate" or "band") ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = CanEdit;
            var selected = id == _prefs.Preset;
            button.BorderThickness = new(selected ? 2 : 1);
            button.BorderBrush = new SolidColorBrush(selected ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Gray);
            AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
        }
        foreach (var bind in _lookBindings) bind();
        foreach (var control in _lookControls) control.IsEnabled = CanEdit && Advanced;
        _resetLook.Visibility = _prefs.Style != null ? Visibility.Visible : Visibility.Collapsed;
    }
}
