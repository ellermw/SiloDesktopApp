using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Windows.UI;

namespace SiloPlayer.Controls;

/// <summary>Native counterparts of the official settings row/group primitives. No entrance motion.</summary>
internal static class AdminSettingsUi
{
    internal static void UpdateLiveText(TextBlock element, string value)
    {
        if (element.Text == value) return;
        element.Text = value;
        AutomationProperties.SetName(element, value);
        if (element.IsLoaded && value.Length > 0)
            (FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element))
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    // SettingField and SecretField explicitly override the global --input token.
    private static Brush FieldBorder => AdminMonitorUi.Solid(AdminMonitorUi.Muted, .25);
    internal static Brush Accent => new SolidColorBrush(Color.FromArgb(255, 167, 139, 250));
    internal static Brush Brush(string name) => AdminMonitorUi.Brush(name);
    internal static TextBlock Text(string value, double size = 14, bool muted = false) => new()
    {
        Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Foreground = Brush(muted ? "SecondaryTextBrush" : "PrimaryTextBrush"),
        LineHeight = size <= 12 ? 16 : size <= 13 ? 21.125 : 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        VerticalAlignment = VerticalAlignment.Center,
    };

    internal static TextBlock Title(string title)
    {
        var text = Text(title, 32);
        text.FontWeight = FontWeights.Bold;
        text.CharacterSpacing = -35;
        text.LineHeight = 36.8;
        AutomationProperties.SetHeadingLevel(text, AutomationHeadingLevel.Level1);
        text.Loaded += (_, _) => Resize();
        text.SizeChanged += (_, _) => Resize();
        void Resize() { text.FontSize = (text.XamlRoot?.Size.Width ?? 1000) < 640 ? 28 : 32; text.LineHeight = text.FontSize * 1.15; }
        return text;
    }

    internal static Button Action(string label, string id, bool primary = false, bool pill = false, bool secondary = false)
    {
        // Use a local template: app-wide Ghost/Accent styles intentionally use pill chrome elsewhere.
        // The radius is bound in every state, including hover and pressed.
        var secondaryColor = AdminMonitorUi.ColorOf("SecondaryBrush", Color.FromArgb(255, 28, 28, 32));
        var hover = primary ? "#B899FF" : secondary ? $"#CC{secondaryColor.R:X2}{secondaryColor.G:X2}{secondaryColor.B:X2}" : "{ThemeResource MutedBrush}";
        var template = (ControlTemplate)XamlReader.Load($$"""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Border x:Name="SettingsButtonRoot" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="{TemplateBinding CornerRadius}" Padding="{TemplateBinding Padding}">
                <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CommonStates">
                  <VisualState x:Name="Normal"/>
                  <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="SettingsButtonRoot.Background" Value="{{hover}}"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="SettingsButtonRoot.Background" Value="{{hover}}"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="SettingsButtonRoot.Opacity" Value="0.5"/></VisualState.Setters></VisualState>
                </VisualStateGroup></VisualStateManager.VisualStateGroups>
                <ContentPresenter Content="{TemplateBinding Content}" Foreground="{TemplateBinding Foreground}" FontSize="{TemplateBinding FontSize}"
                  FontWeight="{TemplateBinding FontWeight}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
            </ControlTemplate>
            """);
        var button = new Button
        {
            Content = label, Template = template, MinWidth = 0, MinHeight = 32, Height = 32,
            Padding = new(12, 0, 12, 0), CornerRadius = new(pill ? 16 : 10), FontSize = 14,
            FontWeight = FontWeights.Medium, UseSystemFocusVisuals = true,
            Background = primary ? Accent : secondary ? Brush("SecondaryBrush") : new SolidColorBrush(Colors.Transparent),
            Foreground = primary ? new SolidColorBrush(Color.FromArgb(255, 21, 21, 26)) : Brush("PrimaryTextBrush"),
            BorderBrush = primary ? Accent : Brush("BorderBrush"), BorderThickness = new(primary || secondary ? 0 : 1),
            FocusVisualPrimaryBrush = AdminMonitorUi.Solid(AdminMonitorUi.ColorOf("FocusVisualPrimaryBrush", Colors.White), .5),
            FocusVisualPrimaryThickness = new(3), FocusVisualSecondaryBrush = new SolidColorBrush(Colors.Transparent),
        };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    internal static TextBox Input(string label, string id, string hint = "", bool number = false)
    {
        var input = new TextBox
        {
            Width = number ? 160 : 240, MinWidth = 0, MinHeight = 36, Height = 36,
            FontSize = 14, PlaceholderText = hint, Padding = new(12, 4, 12, 4), CornerRadius = new(10),
            Background = Brush("AppBackgroundBrush"), BorderBrush = FieldBorder,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        input.Resources["TextControlBorderBrushPointerOver"] = FieldBorder;
        if (number) input.InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } };
        AutomationProperties.SetName(input, label);
        AutomationProperties.SetAutomationId(input, id);
        return input;
    }

    internal static PasswordBox Secret(string label, string id)
    {
        var input = new PasswordBox
        {
            Width = 240, MinWidth = 0, MinHeight = 36, Height = 36, FontSize = 14,
            Padding = new(12, 4, 12, 4), CornerRadius = new(10), PasswordRevealMode = PasswordRevealMode.Hidden,
            Background = Brush("AppBackgroundBrush"), BorderBrush = FieldBorder,
        };
        input.Resources["TextControlBorderBrushPointerOver"] = FieldBorder;
        AutomationProperties.SetName(input, label);
        AutomationProperties.SetAutomationId(input, id);
        return input;
    }

    internal static ComboBox Select(string label, string id, IEnumerable<(string Value, string Label)> options)
    {
        var select = new ComboBox
        {
            Width = 240, MinWidth = 0, MinHeight = 36, Height = 36, FontSize = 14, Padding = new(12, 4, 12, 4), CornerRadius = new(10),
            Background = Brush("AppBackgroundBrush"), BorderBrush = FieldBorder, HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, UseSystemFocusVisuals = true,
        };
        select.Resources["ComboBoxBorderBrushPointerOver"] = FieldBorder;
        select.Resources["ComboBoxBorderBrushPressed"] = FieldBorder;
        select.Resources["ComboBoxBorderBrushFocused"] = FieldBorder;
        select.Resources["ComboBoxDropDownCornerRadius"] = new CornerRadius(10);
        foreach (var (value, text) in options)
        {
            var item = new ComboBoxItem { Content = text, Tag = value, CornerRadius = new(6), MinHeight = 32 };
            AutomationProperties.SetName(item, text);
            select.Items.Add(item);
        }
        AutomationProperties.SetName(select, label);
        AutomationProperties.SetAutomationId(select, id);
        return select;
    }

    internal static Border Skeleton(double height, double width = double.NaN) => new()
    {
        Height = height, Width = width, CornerRadius = new(10), Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .12),
        HorizontalAlignment = double.IsNaN(width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
    };
    internal static Border Alert(string message, string? description = null) => Alert(Text(message, description == null ? 13 : 14), description);
    internal static Border Alert(TextBlock text, string? description = null)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var icon = AdminMonitorUi.Icon("alert-triangle", 16, AdminMonitorUi.Red500); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new(0, 2, 0, 0); row.Children.Add(icon);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Assertive);
        var content = new StackPanel { Spacing = 4 }; content.Children.Add(text);
        if (description != null) { text.FontWeight = FontWeights.Medium; content.Children.Add(Text(description, 13, true)); }
        else text.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
        Grid.SetColumn(content, 1); row.Children.Add(content);
        var alert = new Border { Child = row, Padding = new(16), CornerRadius = new(16), BorderThickness = new(1), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Red500, .2), Background = AdminMonitorUi.Solid(AdminMonitorUi.Red500, .05) };
        AutomationProperties.SetLiveSetting(alert, AutomationLiveSetting.Assertive);
        return alert;
    }

    /// <summary>
    /// The settings pages' dirty-departure dialog. It owns its content-sized Cancel/Discard
    /// actions, starts on Cancel, treats Escape as Cancel and closes when the owner retires.
    /// Returns null when no dialog could be shown.
    /// </summary>
    internal static async Task<bool?> ConfirmDiscardAsync(FrameworkElement host, MonitorAuthorityLease owner,
        Func<ContentDialog?> current, Action<ContentDialog?> track)
    {
        if (current() != null || !owner.IsCurrent) return null;
        var confirmed = false;
        var content = new StackPanel { Spacing = 24 };
        content.Children.Add(Text(AdminText.T("web.shell.unsavedChanges.description")));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        Button DialogAction(string key, string style) => new()
        {
            Content = AdminText.T(key), Style = (Style)Application.Current.Resources[style],
            MinWidth = 0, Height = 36, Padding = new(12, 0, 12, 0), CornerRadius = new(10), UseSystemFocusVisuals = true,
        };
        var cancel = DialogAction("common.cancel", "OutlineButtonStyle");
        var discard = DialogAction("web.shell.unsavedChanges.discard", "DestructiveButtonStyle");
        AutomationProperties.SetName(cancel, AdminText.T("common.cancel"));
        AutomationProperties.SetName(discard, AdminText.T("web.shell.unsavedChanges.discard"));
        actions.Children.Add(cancel); actions.Children.Add(discard); content.Children.Add(actions);
        var dialog = new ContentDialog { XamlRoot = host.XamlRoot, Title = AdminText.T("web.shell.unsavedChanges.title"), Content = content };
        // Own the two actions so their order and size do not depend on native command
        // template internals. Escape/light dismissal never confirms a discard.
        dialog.Resources["ContentDialogMaxWidth"] = 512d;
        cancel.Click += (_, _) => dialog.Hide();
        discard.Click += (_, _) => { confirmed = true; dialog.Hide(); };
        var queue = host.DispatcherQueue;
        dialog.Opened += (_, _) => queue.TryEnqueue(() => { if (ReferenceEquals(current(), dialog)) cancel.Focus(FocusState.Keyboard); });
        track(dialog);
        using var cancelled = owner.Token.Register(() => queue.TryEnqueue(() => { if (ReferenceEquals(current(), dialog)) dialog.Hide(); }));
        try { await dialog.ShowAsync(); return confirmed; }
        catch (System.Runtime.InteropServices.COMException) { return null; }
        finally { if (ReferenceEquals(current(), dialog)) track(null); }
    }

    internal static Border Restart(string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(AdminMonitorUi.Icon("refresh-cw", 10, AdminMonitorUi.Amber400));
        row.Children.Add(Text(label, 12));
        var badge = new Border
        {
            Child = row, CornerRadius = new(10), Padding = new(6, 2, 6, 2),
            Background = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, .1),
            BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, .25), BorderThickness = new(1),
            HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetName(badge, label);
        return badge;
    }
}

internal sealed class AdminSettingsSaveBar : Grid
{
    private readonly TextBlock _count = AdminSettingsUi.Text("", 13);
    private readonly Grid _layout = new() { ColumnSpacing = 8 };
    internal Button Save { get; }
    internal Button Discard { get; }

    /// <param name="automationPrefix">Page prefix for the Save/Discard automation ids, e.g. General or Email.</param>
    internal AdminSettingsSaveBar(string automationPrefix = "General")
    {
        Save = AdminSettingsUi.Action(AdminText.T("common.save"), automationPrefix + "Save", primary: true, pill: true);
        Discard = AdminSettingsUi.Action(AdminText.T("web.shell.unsavedChanges.discard"), automationPrefix + "Discard", pill: true);
        CornerRadius = new(25);
        Background = AdminSettingsUi.Brush("SurfaceBrush");
        BorderBrush = AdminSettingsUi.Brush("BorderBrush"); BorderThickness = new(1);
        Padding = new(18, 8, 8, 8); Margin = new(16, 0, 16, 24);
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Bottom;
        Visibility = Visibility.Collapsed;
        _count.FontWeight = FontWeights.Medium; _count.TextWrapping = TextWrapping.NoWrap;
        _count.TextTrimming = TextTrimming.CharacterEllipsis; _count.Margin = new(0, 0, 8, 0);
        _layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _layout.Children.Add(_count); Grid.SetColumn(Discard, 1); _layout.Children.Add(Discard);
        Grid.SetColumn(Save, 2); _layout.Children.Add(Save); Children.Add(_layout);
        Discard.BorderThickness = new(0);
        AutomationProperties.SetName(this, AdminText.T("web.shell.saveBar.regionLabel"));
        AutomationProperties.SetLiveSetting(_count, AutomationLiveSetting.Polite);
    }

    internal void SetAvailableWidth(double available)
    {
        MaxWidth = Math.Max(0, available - 32);
        _count.MaxWidth = Math.Max(0, Math.Min(240, available - 220));
    }

    internal void State(int dirty, bool saving, bool canSave)
    {
        Visibility = dirty > 0 ? Visibility.Visible : Visibility.Collapsed;
        AdminSettingsUi.UpdateLiveText(_count, AdminText.T("web.shell.saveBar.count", new { count = dirty, shown = dirty }));
        Save.Content = AdminText.T(saving ? "web.ui.saving" : "common.save");
        AutomationProperties.SetName(Save, Save.Content.ToString());
        Save.IsEnabled = canSave && !saving; Discard.IsEnabled = !saving;
    }
}

internal sealed class AdminSettingsRow : Grid
{
    private readonly Grid _layout = new() { RowSpacing = 12 };
    private readonly StackPanel _labels = new() { Spacing = 4, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _dirty = new() { Width = 6, Height = 6, CornerRadius = new(3), Background = AdminSettingsUi.Accent, Visibility = Visibility.Collapsed };
    private readonly Border _restart;
    private readonly FrameworkElement _editor;
    private readonly double _editorWidth;
    internal TextBlock Description { get; }
    internal Control? Input => _editor as Control;
    /// <summary>Raised after each reflow with whether the row uses its wide label/editor columns.</summary>
    internal event Action<bool>? LayoutChanged;

    internal AdminSettingsRow(string label, string description, FrameworkElement editor)
    {
        _editor = editor;
        _editorWidth = editor.Width;
        Padding = new(0, 14, 0, 14);
        BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6);
        BorderThickness = new(0, 0, 0, 1);
        _layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _layout.ColumnDefinitions.Add(new() { Width = new(64) });
        _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var text = AdminSettingsUi.Text(label);
        text.FontWeight = FontWeights.Medium;
        AutomationProperties.SetName(_dirty, AdminText.T("web.admin.settings.fields.unsavedChange"));
        ToolTipService.SetToolTip(_dirty, AdminText.T("web.admin.settings.fields.unsavedChange"));
        _restart = AdminSettingsUi.Restart(AdminText.T("web.settings.restartBadge.label"));
        ToolTipService.SetToolTip(_restart, AdminText.T("web.settings.restartBadge.title"));
        _labels.Children.Add(new AdminSettingsLabel(text, _dirty, _restart));
        Description = AdminSettingsUi.Text(description, 13, true);
        Description.Visibility = description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _labels.Children.Add(Description);
        _layout.Children.Add(_labels);
        Grid.SetColumn(editor, 1); _layout.Children.Add(editor);
        Children.Add(_layout);
        AutomationProperties.SetName(editor, label);
        if (description.Length > 0) AutomationProperties.SetHelpText(editor, description);
        AutomationProperties.SetLabeledBy(editor, text);
        SizeChanged += (_, _) => Reflow();
        Loaded += (_, _) => Reflow();
    }

    internal void State(bool dirty, bool restart)
    {
        _dirty.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        _restart.Visibility = restart ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Reflow()
    {
        var wide = (XamlRoot?.Size.Width ?? ActualWidth) >= 640 && ActualWidth >= 520;
        Grid.SetColumn(_editor, wide ? 1 : 0);
        Grid.SetRow(_editor, wide ? 0 : 1);
        Grid.SetColumnSpan(_editor, wide ? 1 : 3);
        Grid.SetColumnSpan(_labels, wide ? 1 : 3);
        _layout.ColumnDefinitions[2].Width = wide ? new(64) : new(0);
        _editor.Width = wide && double.IsFinite(_editorWidth) ? _editorWidth : double.NaN;
        _editor.HorizontalAlignment = wide ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        _editor.VerticalAlignment = VerticalAlignment.Top;
        _editor.Margin = wide ? new(24, 0, 8, 0) : new(0);
        LayoutChanged?.Invoke(wide);
    }
}

/// <summary>Hugs the dirty dot to a wrapping label instead of stretching it across the label column.</summary>
internal sealed class AdminSettingsLabel : Panel
{
    private readonly TextBlock _label;
    private readonly Border _dot;
    private readonly Border _restart;
    private bool _wrap;
    internal AdminSettingsLabel(TextBlock label, Border dot, Border restart) { _label = label; _dot = dot; _restart = restart; Children.Add(dot); Children.Add(label); Children.Add(restart); }
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
    {
        var gap = _dot.Visibility == Visibility.Visible ? 14 : 0;
        _label.Measure(new(Math.Max(0, available.Width - gap), double.PositiveInfinity)); _dot.Measure(new(6, 6)); _restart.Measure(available);
        var restartWidth = _restart.Visibility == Visibility.Visible ? _restart.DesiredSize.Width + 8 : 0;
        _wrap = gap + _label.DesiredSize.Width + restartWidth > available.Width;
        return new(Math.Min(available.Width, gap + _label.DesiredSize.Width + restartWidth),
            _wrap ? _label.DesiredSize.Height + 8 + _restart.DesiredSize.Height : Math.Max(_label.DesiredSize.Height, _restart.DesiredSize.Height));
    }
    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
    {
        var gap = _dot.Visibility == Visibility.Visible ? 14 : 0;
        _label.Arrange(new(gap, 0, Math.Min(_label.DesiredSize.Width, Math.Max(0, final.Width - gap)), _label.DesiredSize.Height));
        _dot.Arrange(new(0, Math.Max(0, (_label.DesiredSize.Height - 6) / 2), 6, 6));
        _restart.Arrange(new(_wrap ? 0 : gap + _label.DesiredSize.Width + 8, _wrap ? _label.DesiredSize.Height + 8 : 0,
            _restart.DesiredSize.Width, _restart.DesiredSize.Height));
        return final;
    }
}

internal sealed class AdminSettingsGroup : Grid
{
    private readonly StackPanel _heading = new() { Spacing = 6 };
    private readonly TextBlock _description;
    private readonly Border _dirty = new() { Width = 6, Height = 6, CornerRadius = new(3), Background = AdminSettingsUi.Accent, Visibility = Visibility.Collapsed };
    internal StackPanel Fields { get; } = new();

    internal AdminSettingsGroup(string title, string description = "")
    {
        ColumnSpacing = 48;
        BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .65); BorderThickness = new(0, 1, 0, 0);
        ColumnDefinitions.Add(new() { Width = new(256) });
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        var titleRow = new Grid { ColumnSpacing = 8 };
        titleRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = AdminSettingsUi.Text(title, 15); label.FontWeight = FontWeights.SemiBold; label.CharacterSpacing = -25;
        AutomationProperties.SetHeadingLevel(label, AutomationHeadingLevel.Level2);
        titleRow.Children.Add(label); Grid.SetColumn(_dirty, 1); titleRow.Children.Add(_dirty);
        AutomationProperties.SetName(_dirty, AdminText.T("web.admin.settings.fields.groupUnsavedChanges"));
        ToolTipService.SetToolTip(_dirty, AdminText.T("web.admin.settings.fields.groupUnsavedChanges"));
        _heading.Children.Add(titleRow);
        AutomationProperties.SetName(this, title);
        _description = AdminSettingsUi.Text(description, 13, true);
        _description.Visibility = description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _heading.Children.Add(_description);
        Children.Add(_heading); Grid.SetColumn(Fields, 1); Children.Add(Fields);
        SizeChanged += (_, _) => Reflow(); Loaded += (_, _) => Reflow();
    }

    internal void Add(UIElement row)
    {
        Fields.Children.Add(row);
        RefreshRules();
    }

    internal void RefreshRules()
    {
        var visible = Fields.Children.Where(row => row.Visibility == Visibility.Visible).ToArray();
        for (var index = 0; index < visible.Length; index++)
        {
            var line = new Thickness(0, 0, 0, index == visible.Length - 1 ? 0 : 1);
            if (visible[index] is Grid grid) grid.BorderThickness = line;
            else if (visible[index] is Border border) border.BorderThickness = line;
        }
    }

    internal void State(bool dirty, string description)
    {
        _dirty.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        _description.Text = description;
        _description.Visibility = description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Reflow()
    {
        var wide = (XamlRoot?.Size.Width ?? ActualWidth) >= 1024 && ActualWidth >= 832;
        ColumnDefinitions[0].Width = wide ? new(256) : new(1, GridUnitType.Star);
        RowSpacing = wide ? 0 : 16;
        Grid.SetColumn(Fields, wide ? 1 : 0); Grid.SetRow(Fields, wide ? 0 : 1);
        Grid.SetColumnSpan(_heading, wide ? 1 : 2); Grid.SetColumnSpan(Fields, wide ? 1 : 2);
        Fields.Margin = wide ? new(0, -14, 0, 0) : new(0);
        var vertical = (XamlRoot?.Size.Width ?? ActualWidth) >= 1024 ? 28 : 24;
        Padding = new(0, vertical, 0, vertical);
    }
}

/// <summary>Connection action and result share a row while space permits and wrap on compact layouts.</summary>
internal sealed class AdminSettingsActionContent : Panel
{
    private readonly FrameworkElement _button, _result;
    private bool _wrap;
    internal AdminSettingsActionContent(FrameworkElement button, FrameworkElement result) { _button = button; _result = result; Children.Add(button); Children.Add(result); }
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
    {
        _button.Measure(available); _result.Measure(available);
        _wrap = _result.DesiredSize.Width > 0 && _button.DesiredSize.Width + 12 + _result.DesiredSize.Width > available.Width;
        return new(available.Width, _wrap ? _button.DesiredSize.Height + 12 + _result.DesiredSize.Height : Math.Max(_button.DesiredSize.Height, _result.DesiredSize.Height));
    }
    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
    {
        _button.Arrange(new(0, 0, _button.DesiredSize.Width, _button.DesiredSize.Height));
        _result.Arrange(new(_wrap ? 0 : _button.DesiredSize.Width + 12, _wrap ? _button.DesiredSize.Height + 12 : Math.Max(0, (_button.DesiredSize.Height - _result.DesiredSize.Height) / 2),
            Math.Max(0, _wrap ? final.Width : final.Width - _button.DesiredSize.Width - 12), _result.DesiredSize.Height));
        return final;
    }
}
