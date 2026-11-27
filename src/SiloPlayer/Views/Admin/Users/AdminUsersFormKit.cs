using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Localization;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The pieces every Users form is made of, after the official shadcn form parts: a labelled field, a bordered switch row, two columns that become one when
/// narrow, an edit row (label and help at the start, the control at the end) and the Default / Custom segment of the Access cards. The Create user form,
/// the Access card editors, the profile and invitation forms and the confirm dialog use these and no second set.
/// </summary>
internal static class AdminUsersFormKit
{
    internal static ToggleSwitch Switch(bool on = false) => new() { IsOn = on, OnContent = "", OffContent = "", MinWidth = 0, Width = 44, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

    internal static string Count(int count) => count.ToString("N0", AppStrings.FormatCulture);

    // ── Form parts shared by the profile, invitation, invite-code and identity forms ─────────────────────

    /// <summary>A form field's problem line: red, hidden until there is something to say, announced when it appears.</summary>
    internal static TextBlock Problem()
    {
        var block = Text("", 12); block.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive); block.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(block, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
        return block;
    }

    internal static void Say(TextBlock block, string? message) { block.Text = message ?? ""; block.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible; }

    /// <summary>A titled box of a form (official <c>section</c>: a border, a heading and a line under it).</summary>
    internal static Border Section(string title, string? hint, UIElement body)
    {
        var stack = new StackPanel { Spacing = 16 };
        var heading = new StackPanel { Spacing = 4 };
        var name = Text(title, 14); name.FontWeight = FontWeights.SemiBold; heading.Children.Add(name);
        if (!string.IsNullOrEmpty(hint)) heading.Children.Add(Text(hint, 14, true));
        stack.Children.Add(heading); stack.Children.Add(body);
        return new Border { Child = stack, Padding = new(16), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Brush("BorderBrush") };
    }

    /// <summary>A bordered yes/no row: the label and its help, the switch at the end.</summary>
    internal static Border SwitchRow(string label, string? help, ToggleSwitch toggle)
    {
        AutomationProperties.SetName(toggle, label);
        var texts = new StackPanel { Spacing = 2 };
        texts.Children.Add(Text(label, 14));
        if (!string.IsNullOrEmpty(help)) texts.Children.Add(Text(help, 12, true));
        return Card(texts, toggle);
    }

    /// <summary>One option of a radio group shown as a card (official <c>ChoiceOption</c>).</summary>
    internal static Border ChoiceCard(RadioButton radio, string title, string body, bool enabled = true)
    {
        var texts = new StackPanel { Spacing = 2 };
        var name = Text(title, 14); name.FontWeight = FontWeights.Medium; texts.Children.Add(name); texts.Children.Add(Text(body, 12, true));
        radio.Content = texts; radio.IsEnabled = enabled; radio.MinWidth = 0; radio.HorizontalAlignment = HorizontalAlignment.Stretch;
        var card = new Border { Child = radio, Padding = new(12, 8, 12, 8), CornerRadius = new(12), BorderThickness = new(1), BorderBrush = Tint("BorderBrush", 0.7), Opacity = enabled ? 1 : 0.6 };
        return card;
    }

    /// <summary>The border of a choice card follows whether its radio button is picked.</summary>
    internal static void PaintChoice(Border card, bool picked)
    {
        card.BorderBrush = picked ? Tint("AccentBrush", 0.5) : Tint("BorderBrush", 0.7);
        card.Background = picked ? Tint("AccentBrush", 0.06) : null;
    }


    internal static StackPanel Field(string label, UIElement control, params UIElement[] extra)
    {
        var field = new StackPanel { Spacing = 6 };
        field.Children.Add(Text(label, 14));
        field.Children.Add(control);
        foreach (var element in extra) field.Children.Add(element);
        return field;
    }

    /// <summary>A bordered row: text at the start, a control at the end. A drop-down moves under the text when the row is too narrow for both.</summary>
    internal static Border Card(UIElement left, UIElement right, bool stackWhenNarrow = false)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var control = (FrameworkElement)right;
        grid.Children.Add(left); grid.Children.Add(control);
        Grid.SetColumn(control, 1);
        if (stackWhenNarrow)
        {
            var stacked = false;
            grid.SizeChanged += (_, _) =>
            {
                var narrow = grid.ActualWidth > 0 && grid.ActualWidth < 360;
                if (narrow == stacked) return;
                stacked = narrow;
                grid.RowDefinitions.Clear();
                if (narrow)
                {
                    grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    Grid.SetColumnSpan((FrameworkElement)left, 2); Grid.SetRow(control, 1); Grid.SetColumn(control, 0); Grid.SetColumnSpan(control, 2);
                    control.HorizontalAlignment = HorizontalAlignment.Stretch; control.MaxWidth = double.PositiveInfinity;
                }
                else
                {
                    Grid.SetColumnSpan((FrameworkElement)left, 1); Grid.SetRow(control, 0); Grid.SetColumn(control, 1); Grid.SetColumnSpan(control, 1);
                    control.HorizontalAlignment = HorizontalAlignment.Right; control.MaxWidth = 260;
                }
            };
        }
        return new Border { Child = grid, Padding = new(12, 8, 12, 8), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Brush("BorderBrush") };
    }

    /// <summary>Two columns from 520 px, one below: the official <c>sm:grid-cols-2</c>.</summary>
    internal static void AdaptiveColumns(Grid grid)
    {
        var applied = -1;
        void Apply()
        {
            var columns = grid.ActualWidth > 0 && grid.ActualWidth < 520 ? 1 : 2;
            if (columns == applied) return;
            applied = columns;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (var row = 0; row < (grid.Children.Count + columns - 1) / columns; row++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var index = 0; index < grid.Children.Count; index++)
            {
                var child = (FrameworkElement)grid.Children[index];
                Grid.SetRow(child, index / columns); Grid.SetColumn(child, index % columns);
            }
        }
        grid.SizeChanged += (_, _) => Apply();
        Apply();
    }

    /// <summary>
    /// An edit row of an Access card (official <c>KeyValueRow</c> in edit mode): the label and its help at the start and the control at the end; the control
    /// drops below when they cannot share a line. A changed row is tinted amber.
    /// </summary>
    internal static Border EditRow(string label, string? description, UIElement control, bool changed = false, bool first = false, string? id = null)
    {
        var start = new StackPanel { Spacing = 0 };
        var name = Text(label, 14, true);
        start.Children.Add(name);
        if (!string.IsNullOrEmpty(description)) { var hint = Text(description, 12, true); hint.Opacity = 0.8; hint.LineHeight = 19.5; start.Children.Add(hint); }
        if (control is FrameworkElement element) element.HorizontalAlignment = HorizontalAlignment.Right;
        var row = new Border
        {
            Child = new SplitRow(start, control) { MinStart = 112, Gap = 16, RowGap = 8, CenterVertically = true },
            Padding = new(0, 12, 0, 12), BorderThickness = new(0, first ? 0 : 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.5),
        };
        SetChanged(row, changed);
        if (id != null) AutomationProperties.SetAutomationId(row, id);
        return row;
    }

    internal static void SetChanged(Border row, bool changed) =>
        row.Background = changed ? AdminMonitorUi.Solid(AdminMonitorUi.Amber500, 0.06) : null;

    /// <summary>A stack of edit rows with a hairline between them.</summary>
    internal static StackPanel Rows(params Border[] rows)
    {
        var stack = new StackPanel();
        var first = true;
        foreach (var row in rows)
        {
            if (first) row.BorderThickness = new(0);
            first = false;
            stack.Children.Add(row);
        }
        return stack;
    }

    /// <summary>
    /// "Default · {value}" or "Custom" for one row of an Access card (official <c>DefaultCustomSegment</c>). The control for the custom value (if any) shows beside
    /// the segment while Custom is picked.
    /// </summary>
    internal sealed class DefaultCustomSegment
    {
        private readonly Button _default, _custom;
        private readonly Border _frame;
        private readonly UIElement? _customControl;
        private readonly Action<bool> _changed;
        private bool _isCustom;

        internal FrameworkElement Content { get; }
        internal bool Custom => _isCustom;

        internal DefaultCustomSegment(string label, string id, string? defaultText, bool custom, Action<bool> changed, UIElement? customControl = null, bool enabled = true)
        {
            _changed = changed; _customControl = customControl; _isCustom = custom;
            _default = Option(T("web.admin.users.detail.access.segment.default"), id + "Default"); _custom = Option(T("web.admin.users.detail.access.segment.custom"), id + "Custom");
            var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
            pair.Children.Add(_default); pair.Children.Add(_custom);
            _frame = new Border { Child = pair, BorderThickness = new(1), BorderBrush = Tint("BorderBrush", 0.7), CornerRadius = new(8), Padding = new(2), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(_frame, label); AutomationProperties.SetAutomationId(_frame, id);
            var wrap = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
            wrap.Children.Add(_frame);
            if (customControl != null) wrap.Children.Add(customControl);
            Content = wrap;
            _default.Click += (_, _) => Pick(false); _custom.Click += (_, _) => Pick(true);
            _default.IsEnabled = _custom.IsEnabled = enabled;
            SetDefaultText(defaultText);
            Paint();
        }

        private static Button Option(string text, string id)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.NoWrap },
                Padding = new(10, 4, 10, 4), MinHeight = 28, MinWidth = 0, CornerRadius = new(6), Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            };
            AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, text);
            return button;
        }

        /// <summary>"Default · {value}", or just "Default" while what Default resolves to is unknown.</summary>
        internal void SetDefaultText(string? value)
        {
            var text = string.IsNullOrEmpty(value) ? T("web.admin.users.detail.access.segment.default") : T("web.admin.users.detail.access.segment.defaultWithValue", ("value", value));
            ((TextBlock)_default.Content).Text = text; AutomationProperties.SetName(_default, text);
        }

        internal void Set(bool custom) { _isCustom = custom; Paint(); }

        private void Pick(bool custom)
        {
            if (custom == _isCustom) return;
            _isCustom = custom; Paint(); _changed(custom);
        }

        private void Paint()
        {
            void Style(Button button, bool pressed, bool amber)
            {
                button.Background = pressed ? (amber ? AdminMonitorUi.Solid(AdminMonitorUi.Amber500, 0.15) : Brush("AccentBackgroundBrush")) : null;
                if (button.Content is TextBlock block)
                    block.Foreground = pressed ? (amber ? AdminMonitorUi.Solid(AdminMonitorUi.Amber300) : Brush("PrimaryTextBrush")) : Brush("SecondaryTextBrush");
                AutomationProperties.SetItemStatus(button, pressed ? "selected" : "");
            }
            Style(_default, !_isCustom, false); Style(_custom, _isCustom, true);
            if (_customControl is FrameworkElement control) control.Visibility = _isCustom ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
