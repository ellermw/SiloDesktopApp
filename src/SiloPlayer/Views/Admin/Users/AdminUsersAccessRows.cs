using SiloPlayer.Controls;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersFormKit;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// One Default / Custom row of an Access card editor (official <c>ChoicePolicyEdit</c>, the streams, profile, video, bitrate rows): a label with its help, the
/// segment that says what Default resolves to, and the control for a custom value. A row is valid while Default or while its custom value can be stored.
/// </summary>
internal abstract class AccessRow
{
    internal Border Row { get; set; } = null!;
    internal string[] Fields { get; init; } = [];
    internal abstract bool Valid { get; }
    /// <summary>The override of each field in <see cref="Fields"/>: null is Default. Only meaningful while <see cref="Valid"/>.</summary>
    internal abstract JsonNode?[] Values();
    protected Action Raise { get; init; } = () => { };
    protected AdminUsersFormKit.DefaultCustomSegment Segment { get; set; } = null!;

    internal bool Custom => Segment.Custom;
    internal void SetDefaultText(string? text) => Segment.SetDefaultText(text);
}

/// <summary>A choice between a few named values (Allowed / Not allowed, Yes / No, the quality ceilings).</summary>
internal sealed class ChoiceAccessRow : AccessRow
{
    private readonly IReadOnlyList<(string Tag, string Label)> _options;
    private readonly Func<string, JsonNode?> _toNode;
    private readonly ComboBox _select = new() { MinWidth = 144, HorizontalAlignment = HorizontalAlignment.Right };
    private string? _inherited;
    private string? _tag;
    private bool _syncing;

    internal ChoiceAccessRow(string label, string? description, string id, string field, IReadOnlyList<(string Tag, string Label)> options, JsonNode? start,
        Func<JsonNode?, string?> toTag, Func<string, JsonNode?> toNode, string? inheritedTag, Action raise, bool enabled = true)
    {
        Fields = [field]; Raise = raise; _options = options; _toNode = toNode; _inherited = inheritedTag;
        foreach (var option in options) _select.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Tag });
        _select.PlaceholderText = T("web.admin.users.detail.access.choose");
        AutomationProperties.SetName(_select, label); AutomationProperties.SetAutomationId(_select, id + "Value");
        _tag = start == null ? null : toTag(start);
        Show(_tag);
        _select.IsEnabled = enabled;
        _select.SelectionChanged += (_, _) =>
        {
            if (_syncing || _select.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
            _tag = tag; Raise();
        };
        Segment = new(label, id, Label(inheritedTag), start != null, custom =>
        {
            _tag = custom ? _inherited : null; Show(_tag); Raise();
        }, _select, enabled);
        Row = EditRow(label, description, Segment.Content, id: id + "Row");
    }

    private string? Label(string? tag) => tag == null ? null : _options.FirstOrDefault(option => option.Tag == tag).Label;

    private void Show(string? tag)
    {
        _syncing = true;
        _select.SelectedItem = tag == null ? null : _select.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == tag);
        _syncing = false;
    }

    /// <summary>What Default resolves to changed (a role or group change): the segment says so, and a custom row that has no value yet starts from it.</summary>
    internal void SetInherited(string? tag)
    {
        _inherited = tag; SetDefaultText(Label(tag));
    }

    internal override bool Valid => !Segment.Custom || _tag != null;
    internal override JsonNode?[] Values() => [Segment.Custom && _tag != null ? _toNode(_tag) : null];
}

/// <summary>A whole number (simultaneous streams, profiles allowed) with the official hints under the box.</summary>
internal sealed class NumberAccessRow : AccessRow
{
    private readonly long _min;
    private readonly TextBox _input = new() { Width = 96, InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } } };
    private readonly TextBlock _hint = Text("", 12, true);
    private readonly string _validHint, _invalidHint;
    private long? _inherited;

    internal NumberAccessRow(string label, string? description, string id, string field, long min, JsonNode? start, long? inherited, Func<long, string> format,
        string validHint, string invalidHint, Action raise, bool enabled = true)
    {
        Fields = [field]; Raise = raise; _min = min; _inherited = inherited; _validHint = validHint; _invalidHint = invalidHint;
        _input.Text = start?.ToString() ?? "";
        AutomationProperties.SetName(_input, label); AutomationProperties.SetAutomationId(_input, id + "Value");
        var box = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        _hint.TextAlignment = TextAlignment.Right; _hint.HorizontalAlignment = HorizontalAlignment.Right;
        box.Children.Add(_input); box.Children.Add(_hint);
        _input.TextChanged += (_, _) => { Describe(); Raise(); };
        _input.IsEnabled = enabled;
        Segment = new(label, id, inherited == null ? null : format(inherited.Value), start != null, custom =>
        {
            // Custom starts from what the account inherits; with that unknown the box starts empty and the row keeps its default.
            _input.Text = custom && _inherited != null ? _inherited.Value.ToString(CultureInfo.InvariantCulture) : "";
            Describe(); Raise();
        }, box, enabled);
        _format = format;
        Describe();
        Row = EditRow(label, description, Segment.Content, id: id + "Row");
    }

    private readonly Func<long, string> _format;

    private void Describe()
    {
        var invalid = Segment != null && Segment.Custom && AdminUserCreatePolicy.ParseLimit(_input.Text, _min) == null;
        _hint.Text = invalid ? _invalidHint : _validHint;
        _hint.Visibility = _hint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_hint.Text.Length > 0) _hint.Foreground = invalid ? AdminMonitorUi.Solid(AdminMonitorUi.Destructive) : Brush("SecondaryTextBrush");
    }

    internal void SetInherited(long? value)
    {
        _inherited = value; SetDefaultText(value == null ? null : _format(value.Value));
    }

    internal void Focus() => _input.Focus(FocusState.Programmatic);
    internal override bool Valid => !Segment.Custom || AdminUserCreatePolicy.ParseLimit(_input.Text, _min) != null;
    internal override JsonNode?[] Values() => [Segment.Custom && AdminUserCreatePolicy.ParseLimit(_input.Text, _min) is { } value ? JsonValue.Create(value) : null];
}

/// <summary>Video transcoding: Off, Unlimited, or Up to a number at a time.</summary>
internal sealed class VideoAccessRow : AccessRow
{
    private readonly ComboBox _mode = new() { Width = 128 };
    private readonly TextBox _max = new() { Width = 80, Visibility = Visibility.Collapsed, InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } } };
    private AdminUserVideoTranscoding? _inherited;
    private string? _tag;

    internal VideoAccessRow(string label, string id, AdminUserVideoTranscoding? start, AdminUserVideoTranscoding? inherited, Action raise, bool enabled = true)
    {
        Fields = ["transcode_allowed", "max_transcodes"]; Raise = raise; _inherited = inherited;
        _mode.PlaceholderText = T("web.admin.users.detail.access.choose");
        _mode.Items.Add(new ComboBoxItem { Content = T("web.admin.users.detail.access.value.off"), Tag = AdminUserVideoTranscoding.Off });
        _mode.Items.Add(new ComboBoxItem { Content = T("web.admin.users.detail.access.value.unlimited"), Tag = AdminUserVideoTranscoding.Unlimited });
        _mode.Items.Add(new ComboBoxItem { Content = T("web.admin.users.detail.access.playback.upTo"), Tag = AdminUserVideoTranscoding.Limit });
        AutomationProperties.SetName(_mode, label); AutomationProperties.SetAutomationId(_mode, id + "Value");
        AutomationProperties.SetName(_max, T("web.admin.users.detail.access.playback.videoMaxAria")); AutomationProperties.SetAutomationId(_max, id + "Max");
        Show(start);
        _mode.SelectionChanged += (_, _) =>
        {
            if (_mode.SelectedItem is not ComboBoxItem { Tag: string tag } || _tag == tag) return;
            _tag = tag;
            if (tag == AdminUserVideoTranscoding.Limit && _max.Text.Length == 0) _max.Text = "1";
            _max.Visibility = tag == AdminUserVideoTranscoding.Limit ? Visibility.Visible : Visibility.Collapsed;
            Raise();
        };
        _max.TextChanged += (_, _) => Raise();
        var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        pair.Children.Add(_mode); pair.Children.Add(_max);
        _mode.IsEnabled = _max.IsEnabled = enabled;
        Segment = new(label, id, inherited == null ? null : Format(inherited), start != null, custom =>
        {
            Show(custom ? _inherited : null); Raise();
        }, pair, enabled);
        Row = EditRow(label, null, Segment.Content, id: id + "Row");
    }

    internal static string Format(AdminUserVideoTranscoding value) => value.Mode switch
    {
        AdminUserVideoTranscoding.Off => T("web.admin.users.detail.access.value.off"),
        AdminUserVideoTranscoding.Unlimited => T("web.admin.users.detail.access.value.unlimited"),
        _ => T("web.admin.users.detail.access.value.upToAtATime", ("max", value.Max)),
    };

    private void Show(AdminUserVideoTranscoding? value)
    {
        _tag = value?.Mode;
        _mode.SelectedItem = value == null ? null : _mode.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == value.Mode);
        _max.Text = value?.Mode == AdminUserVideoTranscoding.Limit ? value.Max.ToString(CultureInfo.InvariantCulture) : "";
        _max.Visibility = value?.Mode == AdminUserVideoTranscoding.Limit ? Visibility.Visible : Visibility.Collapsed;
    }

    private AdminUserVideoTranscoding? Current()
    {
        if (_tag == null) return null;
        if (_tag != AdminUserVideoTranscoding.Limit) return new(_tag);
        return AdminUserCreatePolicy.ParseLimit(_max.Text, 1) is { } max ? new(AdminUserVideoTranscoding.Limit, max) : null;
    }

    internal override bool Valid => !Segment.Custom || Current() != null;
    internal override JsonNode?[] Values()
    {
        var (allowed, max) = Segment.Custom && Current() is { } value ? AdminUserVideoTranscoding.ToFields(value) : (null, null);
        return [allowed == null ? null : JsonValue.Create(allowed.Value), max == null ? null : JsonValue.Create(max.Value)];
    }
}

/// <summary>A stream bitrate cap: Unlimited, a preset or a custom number of Mbps.</summary>
internal sealed class BitrateAccessRow : AccessRow
{
    /// <summary>Where a Custom cap starts when the default is no cap (Unlimited would change nothing).</summary>
    private const long CustomStartKbps = 20000;
    private readonly AdminUsersBitrateControl _control;
    private long? _inherited;

    internal BitrateAccessRow(string label, string? description, string id, string field, JsonNode? start, long? inherited, Action raise, bool enabled = true)
    {
        Fields = [field]; Raise = raise; _inherited = inherited;
        var initial = start is JsonValue value && UserJson.TryInteger(value, out var kbps) ? kbps : (long?)null;
        _control = new AdminUsersBitrateControl(label, id + "Value", initial);
        _control.Content.MinWidth = 176;
        _control.Changed += () => Raise();
        Segment = new(label, id, inherited == null ? null : Cap(inherited.Value), start != null, custom =>
        {
            if (custom) _control.Set(_inherited is > 0 ? _inherited : CustomStartKbps); else _control.Reset();
            Raise();
        }, _control.Content, enabled);
        Row = EditRow(label, description, Segment.Content, id: id + "Row");
    }

    internal static string Cap(long kbps) => kbps == 0 ? T("web.admin.users.detail.access.value.noCap") : AdminUsersBitrateControl.Format(kbps);

    internal void SetInherited(long? value) { _inherited = value; SetDefaultText(value == null ? null : Cap(value.Value)); }
    internal override bool Valid => !Segment.Custom || _control.IsValid && _control.Value != null;
    internal override JsonNode?[] Values() => [Segment.Custom && _control.IsValid && _control.Value is { } kbps ? JsonValue.Create(kbps) : null];
}
