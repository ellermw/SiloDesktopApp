using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using R = SiloPlayer.Core.Services.Admin.AdminPlaybackSettingsRules;

namespace SiloPlayer.Views.Admin;

/// <summary>Shared native row/group chrome for the separate playback and routing forms, each with its own save bar.</summary>
internal sealed partial class AdminPlaybackSettingsPanel : StackPanel, IDisposable
{
    private readonly AdminPlaybackSettingsController _controller;
    private readonly FrameworkElement _host;
    private readonly bool _routing;
    private readonly LoggingDisclosurePreferences _preferences;
    private readonly Dictionary<string, bool> _forcedAdvanced = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _advancedOverrides = new(StringComparer.Ordinal);
    private readonly StackPanel _fields = new() { Spacing = 20, MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<string, FrameworkElement> _editors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdminSettingsRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _errors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _resets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdminSettingsGroup> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Expander> _advanced = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StackPanel> _advancedFields = new(StringComparer.Ordinal);
    private readonly TextBlock _message = U.Text("", 13);
    private readonly TextBlock _restart = U.Text("", 13, true);
    private readonly Button _reload = U.Action(T("common.retry"), "PlaybackReload");
    private readonly StackPanel _devices = new() { Spacing = 6 };
    private readonly TextBlock _hardwareStatus = U.Text("", 12, true);
    private readonly TextBlock _paths = U.Text("", 12, true);
    private readonly TextBlock _routingWarning = U.Text("", 12, true);
    private readonly Dictionary<string, Button> _presets = new(StringComparer.Ordinal);
    private readonly TextBlock _customPreset = U.Text(T("web.admin.settings.nodeRouting.presets.custom"), 12, true);
    private readonly StackPanel _tasks = new() { Spacing = 14 };
    private void NameTasks() => AutomationProperties.SetAutomationId(_tasks, "PlaybackMarkerTasks");
    private readonly HashSet<string> _uncertainTasks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingTasks = new(StringComparer.Ordinal);
    private readonly Button _supportReload = U.Action(AdminText.T("web.admin.activity.page.refresh"), "PlaybackSupportReload");
    private AdminPlaybackSupport? _support;
    private AdminPlaybackNodeWindow? _nodeWindow;
    private ContentDialog? _dialog;
    private bool _syncing, _disposed, _confirming;
    private string _deviceSignature = "";
    internal AdminSettingsSaveBar SaveBar { get; }
    internal bool IsDialogOpen => _dialog != null;
    internal event Func<string, Task>? RunTask;
    internal event Func<Task>? RefreshSupport;
    private static string T(string key, object? args = null) => AdminPlaybackAdministrationText.T(key, args);
    private static string P(string suffix) => T("web.admin.settings.playback." + suffix);

    internal AdminPlaybackSettingsPanel(AdminPlaybackSettingsController controller, bool routing, FrameworkElement host)
    {
        _controller = controller; _routing = routing; _host = host; Spacing = 12; NameTasks();
        _preferences = App.Services.GetService<LoggingDisclosurePreferences>() ?? new LoggingDisclosurePreferences(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "monitor-ui"));
        SaveBar = new(routing ? "NodeRouting" : "Playback");
        Children.Add(_message); Children.Add(_reload); Children.Add(_restart); Children.Add(_fields);
        Children.Add(new Border { Height = 112 });
        _reload.Click += async (_, _) => await _controller.ReloadAsync(true);
        SaveBar.Save.Click += async (_, _) => await Save(); SaveBar.Discard.Click += (_, _) => _controller.Discard();
        _controller.Changed += QueueRender;
        if (routing) BuildRouting(); else BuildPlayback(); Render();
        if (!routing) { Children.Insert(0, _supportReload); _supportReload.Click += async (_, _) => { if (RefreshSupport != null) { _supportReload.IsEnabled = false; await RefreshSupport(); if (!_disposed) _supportReload.IsEnabled = true; } }; }
        SizeChanged += (_, _) => { _fields.Width = Math.Min(1120, ActualWidth); SaveBar.SetAvailableWidth(_host.ActualWidth); };
    }
    private void BuildPlayback()
    {
        foreach (var groupKey in new[] { "watch", "transcoding", "speedTests", "previews", "markers" })
        {
            var description = groupKey is "previews" or "markers" ? P(groupKey + ".groupDescription") : "";
            var group = new AdminSettingsGroup(P(groupKey + ".group"), description); _groups[groupKey] = group; _fields.Children.Add(group);
            if (groupKey is "previews" or "markers")
            {
                var link = U.Action(P(groupKey == "previews" ? "previews.librariesLink" : "markers.providersLink"), "PlaybackLink_" + groupKey);
                link.HorizontalAlignment = HorizontalAlignment.Left;
                Link(link, groupKey == "previews" ? "/admin/libraries" : "/admin/settings/providers"); group.Add(link);
            }
            foreach (var field in R.Fields.Where(field => field.Group == groupKey && !field.Advanced)) AddField(group, null, field);
            var advanced = R.Fields.Where(field => field.Group == groupKey && field.Advanced).ToArray();
            if (advanced.Length > 0)
            {
                var rows = new StackPanel(); var disclosure = new Expander { Header = T("web.settings.advancedSection.title"), Content = rows, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                _advanced[groupKey] = disclosure; _advancedFields[groupKey] = rows; group.Add(disclosure);
                ConfigureAdvanced(groupKey, disclosure);
                foreach (var field in advanced)
                {
                    if (field.Key == "playback.chapter_thumbnail_hdr_policy") { rows.Children.Add(U.Text(P("previews.hdrHeading"), 14)); rows.Children.Add(U.Text(P("previews.hdrCaption"), 12, true)); }
                    AddField(group, rows, field);
                }
            }
            if (groupKey == "markers") group.Add(_tasks);
        }
        _rows["playback.hw_accel"].AddStatus(_hardwareStatus);
    }
    private void AddField(AdminSettingsGroup group, StackPanel? advanced, AdminPlaybackField field)
    {
        var label = P(field.Label); FrameworkElement editor;
        if (field.Kind == "toggle")
        {
            var toggle = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 };
            toggle.Toggled += (_, _) =>
            {
                if (_syncing || _confirming || _disposed) return;
                Set(field.Key, toggle.IsOn ? "true" : "false");
                // A refused change (busy, retired or env-managed) must not leave the switch showing a value that was never staged.
                var staged = R.Get(_controller.State.Values, field.Key) == "true";
                if (toggle.IsOn != staged) { _syncing = true; try { toggle.IsOn = staged; } finally { _syncing = false; } }
            }; editor = toggle;
        }
        else if (field.Options != null)
        {
            var select = U.Select(label, "Playback_" + field.Key, field.Options.Select(value => (value, Option(field.Key, value))));
            select.SelectionChanged += (_, _) => { if (select.SelectedItem is ComboBoxItem { Tag: string value }) Set(field.Key, value); }; editor = select;
        }
        else if (field.Kind == "devices") editor = _devices;
        else
        {
            var box = U.Input(label, "Playback_" + field.Key, field.Kind == "path" ? PathDefault(field.Key) : "", field.Kind == "number"); box.MaxLength = field.Kind == "path" ? 8192 : 20;
            box.TextChanging += (_, _) => Set(field.Key, box.Text); editor = box;
        }
        _editors[field.Key] = editor; AutomationProperties.SetAutomationId(editor, "Playback_" + field.Key); AutomationProperties.SetName(editor, label);
        var description = field.Description.Length > 0 ? T("web.admin.settings.playback." + field.Description, new { path = PathDefault(field.Key) }) : "";
        string? unit = field.Key is "playback.watched_threshold" or "playback.min_resume_threshold" ? T("web.admin.common.units.percent")
            : field.Key == "playback.preview_image_width" ? T("web.admin.common.units.pixels") : field.Key == "playback.bandwidth_probe_max_age_days" ? P("unitDays")
            : field.Key is "transcode_throttle_seconds" or "playback.segment_retention_seconds" or "playback.trickplay_interval_seconds" ? P("unitSeconds") : null;
        var row = new AdminSettingsRow(label, description, editor, unit); _rows[field.Key] = row;
        AutomationProperties.SetAutomationId(row, "PlaybackRow_" + field.Key);
        var error = U.Text("", 12); _errors[field.Key] = error; row.AddStatus(error);
        if (field.Kind == "path")
        {
            var reset = U.Action(AdminText.T("web.settings.pathSettingField.reset"), "PlaybackReset_" + field.Key); reset.HorizontalAlignment = HorizontalAlignment.Left; reset.Click += (_, _) => Set(field.Key, ""); row.AddStatus(reset);
            _resets[field.Key] = reset;
        }
        if (advanced == null) group.Add(row); else advanced.Children.Add(row);
    }
    private void Set(string key, string value)
    {
        if (_syncing || _confirming || _disposed) return;
        var old = _controller.State.Values.GetValueOrDefault(key, ""); _controller.Set(key, value);
        if (key == "playback.hw_accel" && old == "none" && value != "none" && RefreshSupport != null) _ = RefreshSupport();
    }
    private static string DisclosureKey(string group) => group == "routing" ? "nodes.routing" : group == "previews" ? "playback.preview_images" : "playback.transcoding";
    private void ConfigureAdvanced(string group, Expander expander)
    {
        expander.IsExpanded = _preferences.Read(DisclosureKey(group));
        expander.RegisterPropertyChangedCallback(Expander.IsExpandedProperty, (_, _) =>
        {
            if (_syncing || _disposed) return;
            _advancedOverrides[group] = expander.IsExpanded; _preferences.Write(DisclosureKey(group), expander.IsExpanded);
        });
    }
    private void ForceAdvanced(string group, bool reason)
    {
        if (_forcedAdvanced.GetValueOrDefault(group) != reason) _advancedOverrides.Remove(group);
        _forcedAdvanced[group] = reason;
        _advanced[group].IsExpanded = _advancedOverrides.TryGetValue(group, out var choice) ? choice : reason || _preferences.Read(DisclosureKey(group));
    }
    private static string PathDefault(string key) => key == "playback.ffmpeg_path" ? "/usr/lib/jellyfin-ffmpeg/ffmpeg" : "/tmp/silo-transcode";
    private static void Link(Button button, string href)
    {
        button.IsEnabled = AdminRouteCatalog.TryResolve(href, out var resolved) && resolved.Request != null && AdminShellPage.CanOpen(resolved.Request);
        if (!button.IsEnabled) ToolTipService.SetToolTip(button, "This view is not included in this build.");
        button.Click += (_, _) => AdminShellPage.TryOpenHref(href);
    }
    internal void SetNodeWindow(AdminPlaybackNodeWindow? window) { _nodeWindow = window; Render(); }
    internal void SetSupport(AdminPlaybackSupport support) { _support = support; _nodeWindow = support.Nodes; RenderTasks(); Render(); }
    internal void SetTasks(IReadOnlyList<AdminPlaybackTask> tasks) { if (_support != null) { _support = _support with { Tasks = tasks }; RenderTasks(); } }
    internal void ReviewTaskOutcomes() { _uncertainTasks.Clear(); RenderTasks(); }
    internal void MarkTaskUncertain(string key) { _uncertainTasks.Add(key); RenderTasks(); }
    private void QueueRender() => DispatcherQueue.TryEnqueue(() => { if (!_disposed) Render(); });
    private void Render()
    {
        if (_disposed) return; var state = _controller.State; var current = state.Owner?.IsCurrent == true;
        U.UpdateLiveText(_message, current ? state.Error ?? (state.Loading ? T("web.admin.settings.fields.loadingSettings") : "") : "");
        _reload.Visibility = current && state.Error != null ? Visibility.Visible : Visibility.Collapsed; _reload.IsEnabled = !state.Saving && !state.Loading;
        _restart.Text = state.RestartRequired ? "These settings were saved. A server restart is required." : "";
        _fields.Visibility = current && state.Loaded ? Visibility.Visible : Visibility.Collapsed;
        _syncing = true;
        foreach (var (key, editor) in _editors)
        {
            var raw = current ? state.Values.GetValueOrDefault(key, "") : "";
            var value = current ? R.Get(state.Values, key) : "";
            if (editor is TextBox box && box.Text != raw) box.Text = raw;
            else if (editor is ToggleSwitch toggle) toggle.IsOn = value == "true";
            else if (editor is ComboBox combo) combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == value);
            var visible = _routing || R.Visible(key, state.Values, _support?.Trickplay == true, _support?.DetectionKindSettings == true);
            _rows[key].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            var enabled = current && state.Loaded && !state.Loading && !state.Saving && !_confirming && !state.ManagedByEnv.Contains(key);
            if (key == "playback.chapter_thumbnail_software_tone_map_enabled" && R.Get(state.Values, "playback.chapter_thumbnail_hdr_policy") == "disabled") enabled = false;
            editor.IsHitTestVisible = enabled; if (editor is Control control) control.IsEnabled = enabled;
            _rows[key].State(state.DirtyKeys.Contains(key), state.RestartKeys.Has(key));
            _errors[key].Text = state.Errors.GetValueOrDefault(key, "") + (state.ManagedByEnv.Contains(key) ? " Set by the server environment." : "");
            if (_resets.TryGetValue(key, out var reset)) { reset.Visibility = raw.Length > 0 && raw != PathDefault(key) ? Visibility.Visible : Visibility.Collapsed; reset.IsEnabled = enabled; }
        }
        if (_routing) RenderRouting(state); else RenderPlayback(state);
        _syncing = false;
        foreach (var group in _groups.Values) group.RefreshRules();
        SaveBar.State(current ? state.DirtyKeys.Count : 0, state.Saving, current && state.Loaded && !state.Loading && !state.Conflict && !state.OutcomeUnknown && state.Errors.Count == 0 && !_confirming);
        SaveBar.SetAvailableWidth(_host.ActualWidth);
        _supportReload.IsEnabled = current && state.Loaded && !state.Saving && !state.Loading;
        if (!current) { _support = null; _nodeWindow = null; _tasks.Children.Clear(); _devices.Children.Clear(); _uncertainTasks.Clear(); _deviceSignature = ""; _dialog?.Hide(); }
    }
    private async Task Save()
    {
        var state = _controller.State; var owner = state.Owner;
        if (_disposed || _dialog != null || owner?.IsCurrent != true) return;
        bool Changed(string key, string defaultValue) => state.DirtyKeys.Contains(key) && (state.Values.GetValueOrDefault(key, "") is { Length: > 0 } value ? value : defaultValue) != (state.PersistedValues.GetValueOrDefault(key, "") is { Length: > 0 } saved ? saved : defaultValue);
        var width = !_routing && Changed("playback.preview_image_width", "300");
        var interval = !_routing && Changed("playback.trickplay_interval_seconds", "10") && (_support?.PreviewsToRemake is null or > 0);
        if (width || interval)
        {
            _confirming = true; Render();
            var count = _support?.PreviewsToRemake;
            var suffix = width ? count == null ? "widthExisting" : count > 0 ? "widthCount" : "widthNoPreviews" : count == null ? "intervalExisting" : "intervalCount";
            var dialog = new ContentDialog { XamlRoot = _host.XamlRoot, Title = P(width ? "remake.widthTitle" : "remake.intervalTitle"), Content = T("web.admin.settings.playback.remake." + suffix, new { count = count ?? 0, count_text = (count ?? 0).ToString("N0") }), PrimaryButtonText = T("common.save"), CloseButtonText = T("common.cancel"), DefaultButton = ContentDialogButton.Close };
            _dialog = dialog; using var retired = owner.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            var result = ContentDialogResult.None;
            try { result = await dialog.ShowAsync(); } catch (System.Runtime.InteropServices.COMException) { }
            finally { _dialog = null; _confirming = false; Render(); }
            if (result != ContentDialogResult.Primary || !owner.IsCurrent) return;
        }
        await _controller.SaveAsync();
    }
    private static string Option(string key, string value) => key switch
    {
        "playback.hw_accel" => AdminNodesPage.HardwareOptions().First(option => option.Value == value).Label,
        "markers.mode" => P("markers.mode" + char.ToUpperInvariant(value[0]) + value[1..]),
        "markers.online_storage" => P(value == "stored" ? "markers.storageStored" : "markers.storageOnDemand"),
        "playback.chapter_thumbnail_hdr_policy" => P(value == "best_effort" ? "previews.hdrBestEffort" : "previews.hdrDisabled"),
        _ => P(value == "local" ? "execution.thisServer" : value == "prefer_transcode_nodes" ? "execution.nodesWhenAvailable" : "execution.nodesOnly"),
    };
    public void Dispose() { if (_disposed) return; _disposed = true; _controller.Changed -= QueueRender; _dialog?.Hide(); _dialog = null; _support = null; _nodeWindow = null; foreach (var box in _editors.Values.OfType<TextBox>()) box.Text = ""; _devices.Children.Clear(); _tasks.Children.Clear(); RunTask = null; RefreshSupport = null; }
}
