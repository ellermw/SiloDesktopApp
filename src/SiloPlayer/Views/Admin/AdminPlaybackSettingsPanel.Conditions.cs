using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using R = SiloPlayer.Core.Services.Admin.AdminPlaybackSettingsRules;
using Status = SiloPlayer.Core.Services.Admin.AdminPlaybackMarkerTaskStatus;

namespace SiloPlayer.Views.Admin;

internal sealed partial class AdminPlaybackSettingsPanel
{
    private void BuildRouting()
    {
        var group = new AdminSettingsGroup(T("web.admin.settings.nodeRouting.group"), T("web.admin.settings.nodeRouting.description")); _groups["routing"] = group; _fields.Children.Add(group);
        var presets = new LogsWrapPanel { Spacing = 8 };
        foreach (var preset in new[] { "standard", "gpu", "central" })
        {
            var button = U.Action(T("web.admin.settings.nodeRouting.presets." + preset), "NodeRoutingPreset_" + preset);
            button.Click += (_, _) => _controller.ApplyPreset(preset); presets.Children.Add(button); _presets[preset] = button;
        }
        presets.Children.Add(_customPreset);
        group.Add(new AdminSettingsRow(T("web.admin.settings.nodeRouting.presets.label"), T("web.admin.settings.nodeRouting.presets.description"), presets));
        var advanced = new StackPanel(); var disclosure = new Expander { Header = T("web.settings.advancedSection.title"), Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        _advanced["routing"] = disclosure; _advancedFields["routing"] = advanced; group.Add(disclosure);
        ConfigureAdvanced("routing", disclosure);
        string[] suffixes = ["directPlayEgress", "remuxExecution", "remuxEgress", "videoTranscodeExecution", "videoTranscodeEgress"];
        for (var index = 0; index < R.RoutingKeys.Count; index++)
        {
            var key = R.RoutingKeys[index]; var suffix = suffixes[index]; var egress = key.EndsWith("_egress");
            var label = T("web.admin.settings.nodeRouting.fields." + suffix + ".label");
            var descriptionKey = "web.admin.settings.nodeRouting.fields." + suffix + ".description";
            var select = U.Select(label, "NodeRouting_" + key, (egress ? R.EgressOptions : R.ExecutionOptions).Select(value => (value, RoutingOption(value))));
            select.SelectionChanged += (_, _) => { if (select.SelectedItem is ComboBoxItem { Tag: string value }) Set(key, value); };
            var row = new AdminSettingsRow(label, AdminPlaybackAdministrationText.Pattern(descriptionKey) == null ? "" : T(descriptionKey), select);
            _rows[key] = row; _editors[key] = select; var error = U.Text("", 12); _errors[key] = error; row.AddStatus(error); advanced.Children.Add(row);
        }
        group.Add(new AdminSettingsRow(T("web.admin.settings.nodeRouting.paths.label"), T("web.admin.settings.nodeRouting.paths.description"), _paths));
        group.Add(_routingWarning);
    }
    private static string RoutingOption(string value) => T("web.admin.settings.nodeRouting." + (value switch
    {
        "prefer_proxy" => "egress.preferProxy", "proxy_only" => "egress.proxyOnly", "prefer_worker" => "execution.preferWorker", "prefer_transcode" => "execution.preferTranscode", "worker_only" => "execution.workerOnly", "prefer_api" => "option.preferApi", _ => "option.apiOnly",
    }));
    private void RenderRouting(AdminPlaybackSettingsState state)
    {
        string V(string key) => R.Get(state.Values, "playback.routing." + key);
        foreach (var (name, button) in _presets)
        {
            var active = R.Preset(name).All(pair => state.Values.GetValueOrDefault(pair.Key, "") == pair.Value);
            button.Opacity = active ? 1 : .8; AutomationProperties.SetHelpText(button, active ? "Selected" : ""); button.IsEnabled = state.Loaded && !state.Saving && !state.Loading;
        }
        var dirty = state.DirtyKeys.Count > 0; ForceAdvanced("routing", dirty);
        _customPreset.Visibility = _presets.Keys.Any(name => R.Preset(name).All(pair => state.Values.GetValueOrDefault(pair.Key, "") == pair.Value)) ? Visibility.Collapsed : Visibility.Visible;
        _paths.Text = T("web.admin.settings.nodeRouting.paths.directPlay") + " · " + Egress(V("direct_play_egress")) + "\n"
            + T("web.admin.settings.nodeRouting.paths.remux") + " · " + Execution(V("remux_execution"), false) + " · " + Egress(V("remux_egress")) + "\n"
            + T("web.admin.settings.nodeRouting.paths.videoTranscode") + " · " + Execution(V("video_transcode_execution"), true) + " · " + Egress(V("video_transcode_egress"));
        var proxyOnly = R.RoutingKeys.Any(key => state.Values.GetValueOrDefault(key, "") == "proxy_only");
        var known = _nodeWindow is { HasMore: false }; var proxy = _nodeWindow?.Items.Any(node => node.Type == "proxy" && node.Enabled && node.Healthy) == true; var transcode = UsableTranscode();
        var stranded = known && (proxyOnly && !proxy || V("remux_execution") == "worker_only" && !proxy && !transcode || V("video_transcode_execution") == "worker_only" && !transcode);
        _routingWarning.Text = (stranded ? T("web.admin.settings.nodeRouting.warnings.stranded") : "") + (proxyOnly ? "\n" + T("web.admin.settings.nodeRouting.warnings.proxyOnlyClients") : "");
        _groups["routing"].State(dirty, T("web.admin.settings.nodeRouting.description"));
        static string Egress(string value) => T("web.admin.settings.nodeRouting.preview." + (value switch { "api_only" => "apiOnly", "proxy_only" => "egress.proxyOnly", "prefer_api" => "egress.preferApi", _ => "egress.preferProxy" }));
        static string Execution(string value, bool video) => T("web.admin.settings.nodeRouting.preview." + (value == "api_only" ? "apiOnly" : (video ? "videoTranscode." : "remux.") + (value switch { "worker_only" => "workerOnly", "prefer_api" => "preferApi", "prefer_transcode" => "preferTranscode", _ => video ? "preferTranscode" : "preferWorker" })));
    }
    private string[] DetectedDevicePaths() => (_support?.Hardware?.RenderDeviceDetails ?? []).Select(device => device.Path).Concat(_support?.Hardware?.RenderDevices ?? []).Distinct().ToArray();
    /// <summary>
    /// A switch reports its state again when it is realized or set in code; only a state that differs from the staged list is the admin's choice, so a late
    /// echo can never stage a device list.
    /// </summary>
    private void StageDevice(ToggleSwitch toggle, string path)
    {
        if (_syncing || _disposed || !toggle.IsEnabled) return;
        var state = _controller.State;
        var draft = state.Values.GetValueOrDefault("playback.hw_device", ""); var saved = state.PersistedValues.GetValueOrDefault("playback.hw_device", "");
        var acceleration = R.Get(state.Values, "playback.hw_accel");
        var applies = R.DevicePickerApplies(acceleration, acceleration == "nvenc" || acceleration == "auto" && _support?.Hardware?.Resolved == "nvenc");
        if (toggle.IsOn == R.ParseDevices(draft).Contains(path) || toggle.IsOn && R.DeviceSwitchLocked(false, applies, path, draft, saved)) return;
        var order = R.DeviceOrder(saved, R.DeviceRows(DetectedDevicePaths(), saved, draft));
        Set("playback.hw_device", R.NextDeviceDraft(draft, saved, path, order));
        // A refused change (busy, retired or env-managed) must not leave the switch showing a device that was never staged.
        var staged = R.ParseDevices(_controller.State.Values.GetValueOrDefault("playback.hw_device", "")).Contains(path);
        if (toggle.IsOn != staged) { _syncing = true; try { toggle.IsOn = staged; } finally { _syncing = false; } }
    }
    private bool UsableTranscode() => _nodeWindow?.Items.Any(node => node.Type == "transcode" && node.Enabled && node.Healthy) == true;
    private bool UsableTrickplay() => _nodeWindow?.Items.Any(node => node.Type == "transcode" && node.Enabled && node.Healthy && node.Capabilities?.TransportFeatures?.Contains("trickplay_extract_v2") == true
        && (node.AdvertisedCapabilitiesHash == null || node.AdvertisedCapabilitiesHash.Length > 0 && node.AdvertisedCapabilitiesHash == node.CapabilitiesHash)) == true;
    private void RenderPlayback(AdminPlaybackSettingsState state)
    {
        var hardware = _support?.Hardware; var acceleration = R.Get(state.Values, "playback.hw_accel");
        var cuda = acceleration == "nvenc" || acceleration == "auto" && hardware?.Resolved == "nvenc";
        var draftDevices = state.Values.GetValueOrDefault("playback.hw_device", ""); var savedDevices = state.PersistedValues.GetValueOrDefault("playback.hw_device", "");
        var selected = R.ParseDevices(draftDevices);
        var paths = DetectedDevicePaths();
        // Saved paths keep their rows when the draft drops them; a staged list keeps the picker in view under a mode it does not apply to (official #4696).
        var allDevices = R.DeviceRows(paths, savedDevices, draftDevices);
        var applies = R.DevicePickerApplies(acceleration, cuda);
        var devicesDirty = state.DirtyKeys.Contains("playback.hw_device");
        var orderOnly = R.OrderOnlyDraft(devicesDirty, allDevices, draftDevices, savedDevices);
        _rows["playback.hw_device"].Visibility = R.ShowsDevicePicker(allDevices.Count, applies, devicesDirty) ? Visibility.Visible : Visibility.Collapsed;
        _deviceLocks.Clear();
        foreach (var path in allDevices) if (R.DeviceSwitchLocked(false, applies, path, draftDevices, savedDevices)) _deviceLocks.Add(path);
        var signature = string.Join("|", allDevices) + ";" + string.Join(",", selected) + ";" + applies + ";" + orderOnly + ";" + savedDevices;
        if (signature != _deviceSignature)
        {
            _deviceSignature = signature; _devices.Children.Clear(); _deviceMarks.Clear();
            _devices.Children.Add(U.Text(P(selected.Count == 0 ? "devices.captionAuto" : selected.Count == 1 ? "devices.captionOne" : "devices.captionMany"), 12, true));
            foreach (var path in allDevices)
            {
                var description = hardware?.RenderDeviceDetails?.FirstOrDefault(device => device.Path == path)?.Description ?? (paths.Contains(path) ? P("devices.gpu") : P("devices.notDetected"));
                // The unsaved-change dot sits before the device's name, as the official row's does; DeviceKeptForEdit decides which devices carry it.
                // A Grid with an Auto column: the dot takes no room while collapsed, and the name wraps in the rest.
                var mark = new Microsoft.UI.Xaml.Controls.Border { Width = 6, Height = 6, CornerRadius = new(3), Background = U.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0), Visibility = Visibility.Collapsed };
                AutomationProperties.SetName(mark, AdminText.T("web.admin.settings.fields.unsavedChange"));
                var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                var name = U.Text(description); Grid.SetColumn(name, 1); header.Children.Add(mark); header.Children.Add(name);
                _deviceMarks[path] = mark;
                var toggle = new ToggleSwitch { Header = header, OffContent = "", OnContent = "", IsOn = selected.Contains(path), MinWidth = 0, Tag = path };
                AutomationProperties.SetName(toggle, description + " · " + path); AutomationProperties.SetAutomationId(toggle, "PlaybackDevice_" + path);
                toggle.Toggled += (_, _) => StageDevice(toggle, path);
                _devices.Children.Add(toggle); _devices.Children.Add(U.Text(path, 12, true));
                var missing = (hardware?.Nodes ?? []).Where(node => string.IsNullOrEmpty(node.Error) && !(node.RenderDevices ?? []).Contains(path)).Select(node => node.NodeName.Length > 0 ? node.NodeName : "Transcode node").ToArray();
                if (missing.Length > 0) _devices.Children.Add(U.Text(T("web.admin.settings.playback.devices.missingOnNodes", new { nodes = string.Join(", ", missing) }), 12, true));
            }
            var inventories = (hardware?.Nodes ?? []).Where(node => string.IsNullOrEmpty(node.Error)).Select(node => string.Join(",", (node.RenderDevices ?? []).Order())).Distinct().Count();
            if (inventories > 1)
            {
                _devices.Children.Add(U.Text("Device inventories differ across transcode nodes. Set a node-specific device override when paths differ.", 12, true));
                var nodes = U.Action(T("web.admin.nodes.page.title"), "PlaybackDevicesNodes"); nodes.Click += (_, _) => AdminShellPage.TryOpenHref("/admin/nodes"); _devices.Children.Add(nodes);
            }
        }
        SyncDevices();
        foreach (var (path, mark) in _deviceMarks) mark.Visibility = R.DeviceKeptForEdit(applies, path, draftDevices, savedDevices, orderOnly) ? Visibility.Visible : Visibility.Collapsed;
        var backend = hardware?.DetectedBackends?.FirstOrDefault(probe => probe.Backend.Trim().ToLowerInvariant() == hardware.Resolved);
        _hardwareStatus.Text = acceleration == "none" ? "" : hardware == null ? "Hardware detection could not be read. No verified acceleration is assumed." : Detection(hardware, backend);
        if (cuda && selected.Count > 1) _hardwareStatus.Text += "\n" + T("web.admin.settings.playback.hwAccel.nvencFirstDevice", new { device = selected[0] });
        _rows["playback.trickplay_node_capacity"].Visibility = _support?.Trickplay == true && (R.Get(state.Values, "playback.trickplay_execution") != "local" || state.DirtyKeys.Contains("playback.trickplay_node_capacity")) ? Visibility.Visible : Visibility.Collapsed;
        // Where images run is essential once a healthy enabled transcode node could take the work; otherwise Advanced.
        var strandedPreviews = false;
        foreach (var key in new[] { "playback.chapter_thumbnail_execution", "playback.trickplay_execution" })
        {
            var row = _rows[key]; var essential = UsableTranscode();
            var parent = essential ? _groups["previews"].Fields : _advancedFields["previews"];
            if (!parent.Children.Contains(row))
            {
                if (_groups["previews"].Fields.Children.Contains(row)) _groups["previews"].Fields.Children.Remove(row); else _advancedFields["previews"].Children.Remove(row);
                if (essential) parent.Children.Insert(Math.Max(0, parent.Children.IndexOf(_advanced["previews"])), row); else parent.Children.Insert(0, row);
            }
            var current = R.Get(state.Values, key); var available = _nodeWindow == null || _nodeWindow.HasMore || (key == "playback.trickplay_execution" ? UsableTrickplay() : UsableTranscode());
            var select = (ComboBox)_editors[key]; foreach (var option in select.Items.OfType<ComboBoxItem>()) option.IsEnabled = available || (string)option.Tag == "local" || (string)option.Tag == current;
            _errors[key].Text = !available ? P(key == "playback.trickplay_execution" ? "previews.noTrickplayNode" : "previews.noTranscodeNodes") : "";
            if (!available && current == "transcode_nodes_only") strandedPreviews = true;
        }
        var markerMode = R.Get(state.Values, "markers.mode"); var storage = R.Get(state.Values, "markers.online_storage");
        _rows["markers.online_storage"].Description.Text = P(storage == "stored" ? "markers.onlineStorageStored" : "markers.onlineStorageOnDemand");
        _rows["markers.lazy_playback"].Description.Text = P(markerMode == "local" ? "markers.lazyPlaybackLocal" : markerMode == "both" ? "markers.lazyPlaybackBoth" : "markers.lazyPlaybackOnline");
        _groups["previews"].State(R.Fields.Where(field => field.Group == "previews").Any(field => state.DirtyKeys.Contains(field.Key)), P(_support?.Trickplay == true ? "previews.groupDescription" : "previews.groupDescriptionNoTrickplay"));
        ForceAdvanced("transcoding", R.Fields.Any(field => field.Group == "transcoding" && field.Advanced && state.DirtyKeys.Contains(field.Key)));
        ForceAdvanced("previews", strandedPreviews || R.Fields.Any(field => field.Group == "previews" && (field.Advanced || !UsableTranscode() && field.Key.EndsWith("_execution")) && state.DirtyKeys.Contains(field.Key)));
        foreach (var group in _groups.Where(pair => pair.Key != "previews")) group.Value.State(R.Fields.Any(field => field.Group == group.Key && state.DirtyKeys.Contains(field.Key)), group.Key == "markers" ? P("markers.groupDescription") : "");
    }
    private static string Detection(AdminPlaybackHardware hardware, AdminPlaybackBackend? backend)
    {
        if (hardware.Resolved == "none") return P("hwAccel.noneFound");
        var label = AdminNodesPage.HardwareOptions().FirstOrDefault(option => option.Value == hardware.Resolved).Label ?? hardware.Resolved;
        var node = hardware.Source == "transcode_node" ? "Node" : "";
        var device = backend?.Device ?? hardware.RenderDevices?.FirstOrDefault();
        var suffix = backend?.Verified == true ? device == null ? "detected" : "detectedOnDevice" : backend?.Skipped == true ? "notAccessible" : backend != null ? "checkFailed" : hardware.Resolved == "videotoolbox" ? "needsNativeMac" : "notFound";
        return T("web.admin.settings.playback.hwAccel." + suffix + node, new { backend = label, device = device ?? "" });
    }
    /// <summary>Official TaskStatusBadge for the last marker run: destructive for failures, outline for cancelled, the server's error as tooltip.</summary>
    private static FrameworkElement StatusBadge(AdminPlaybackTask task, string label)
    {
        var tone = Status.Tone(task);
        var destructive = tone == AdminPlaybackMarkerTaskStatus.BadgeTone.Destructive;
        var badge = AdminMonitorUi.Badge(label, destructive ? AdminMonitorUi.Red500 : AdminMonitorUi.Muted, fontSize: 12,
            backgroundAlpha: tone == AdminPlaybackMarkerTaskStatus.BadgeTone.Outline ? 0 : .12, borderAlpha: .3,
            foreground: destructive ? AdminMonitorUi.Red500 : AdminMonitorUi.Foreground, radius: 8);
        AutomationProperties.SetAutomationId(badge, "PlaybackTaskStatus_" + task.Key); AutomationProperties.SetName(badge, label);
        if (Status.ErrorText(task) is { } error) { ToolTipService.SetToolTip(badge, error); AutomationProperties.SetHelpText(badge, error); }
        return badge;
    }
    private void RenderTasks()
    {
        _tasks.Children.Clear();
        foreach (var key in SiloPlayer.Core.Api.AdminPlaybackAdministrationApi.MarkerTasks)
        {
            var task = _support?.Tasks.FirstOrDefault(task => task.Key == key); var suffix = key == "sync_markers" ? "sync" : key == "detect_intro_markers" ? "detect" : "contribute";
            var row = new StackPanel { Spacing = 6 };
            var heading = new LogsWrapPanel { Spacing = 8 };
            heading.Children.Add(U.Text(task?.Name is { Length: > 0 } name ? name : T("web.admin.settings.markers.tasks." + suffix + "Name"), 14));
            if (task != null && Status.StatusLabel(task) is { } statusLabel) heading.Children.Add(StatusBadge(task, statusLabel));
            row.Children.Add(heading);
            row.Children.Add(U.Text(task?.Description is { Length: > 0 } description ? description : T("web.admin.settings.markers.tasks." + suffix + "Description"), 12, true));
            var result = U.Text(Status.ResultText(task), 12, true); AutomationProperties.SetAutomationId(result, "PlaybackTaskResult_" + key); row.Children.Add(result);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var history = U.Action(T("web.admin.settings.markers.tasks.history"), "PlaybackTaskHistory_" + key); Link(history, "/admin/tasks/" + key); actions.Children.Add(history);
            var running = task?.State is "running" or "cancelling";
            var run = U.Action(T(running ? "web.admin.settings.markers.tasks.running" : "web.admin.settings.markers.tasks.runNow"), "PlaybackTaskRun_" + key); run.IsEnabled = task != null && !running;
            run.IsEnabled &= !_uncertainTasks.Contains(key) && !_pendingTasks.Contains(key);
            if (_uncertainTasks.Contains(key)) row.Children.Add(U.Text("Outcome unconfirmed. Review task history, then refresh before running again.", 12, true));
            run.Click += async (_, _) =>
            {
                if (RunTask == null || !_pendingTasks.Add(key)) return;
                run.IsEnabled = false;
                try { await RunTask(key); }
                finally { _pendingTasks.Remove(key); if (!_disposed) RenderTasks(); }
            }; actions.Children.Add(run);
            row.Children.Add(actions); _tasks.Children.Add(row);
        }
    }
}
