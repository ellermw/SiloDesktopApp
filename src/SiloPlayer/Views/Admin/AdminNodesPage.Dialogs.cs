using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Windows.ApplicationModel.DataTransfer;
using U = SiloPlayer.Controls.AdminSettingsUi;
using R = SiloPlayer.Core.Services.Admin.AdminPlaybackSettingsRules;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminNodesPage
{
    private async Task EditNode(string type, AdminPlaybackNode? target)
    {
        var controller = _controller; var owner = controller?.State.Owner;
        if (_dialog != null || controller == null || owner?.IsCurrent != true || controller.State.Busy || controller.State.ReviewRequired) return;
        var editor = new AdminPlaybackNodeEditor(type, target, _group ?? "");
        string? clusterHwAccel = null;
        try { clusterHwAccel = (await App.Services.GetRequiredService<AdminServerSettingsStore>().GetEffectiveAsync(owner)).Values.GetValueOrDefault("playback.hw_accel"); } catch { }
        if (!owner.IsCurrent || _dialog != null) return;
        var content = new StackPanel { Spacing = 14 }; var error = U.Text("", 13);
        var fields = new Dictionary<string, Control>(); var picker = new StackPanel { Spacing = 8 }; var syncing = false; var pending = false; var saved = false;
        TextBox Input(string key, string value, Action<string> set, string help = "", bool number = false)
        {
            var box = U.Input(T("web.admin.nodes.form." + key), "Node_" + key, number: number); box.Text = value; box.Width = double.NaN; box.HorizontalAlignment = HorizontalAlignment.Stretch; box.MaxLength = key is "url" or "publicUrl" ? 8192 : 1024;
            box.TextChanged += (_, _) => { if (!syncing) set(box.Text); };
            var row = new StackPanel { Spacing = 5 }; row.Children.Add(U.Text(T("web.admin.nodes.form." + key))); row.Children.Add(box);
            if (help.Length > 0) row.Children.Add(U.Text(T("web.admin.nodes.form." + help), 12, true)); content.Children.Add(row); fields[key] = box; return box;
        }
        ComboBox Select(string key, string label, string value, IEnumerable<(string Value, string Label)> options, Action<string> set, string help)
        {
            var box = U.Select(label, "Node_" + key, options); box.Width = double.NaN; box.HorizontalAlignment = HorizontalAlignment.Stretch;
            box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == value);
            box.SelectionChanged += (_, _) => { if (!syncing && box.SelectedItem is ComboBoxItem { Tag: string selected }) set(selected); };
            var row = new StackPanel { Spacing = 5 }; row.Children.Add(U.Text(label)); row.Children.Add(box); row.Children.Add(U.Text(help, 12, true)); content.Children.Add(row); fields[key] = box; return box;
        }
        Input("name", editor.Name, value => editor.Name = value);
        content.Children.Add(U.Text(T("web.admin.nodes.form.type") + " · " + T("web.admin.nodes.form.type" + (type == "proxy" ? "Proxy" : "Transcode")), 13, true));
        Input("url", editor.Url, value => editor.Url = value, type == "proxy" ? "urlHelpProxy" : "urlHelpTranscode");
        if (type == "proxy") Input("publicUrl", editor.PublicUrl, value => editor.PublicUrl = value, "publicUrlHelp");
        Input("group", editor.Group, value => editor.Group = value, "groupHelp");
        Input(type == "proxy" ? "maxStreams" : "maxTranscodes", editor.MaxJobs, value => editor.MaxJobs = value, "maxJobsHelp", true);
        if (type == "proxy") Input("maxEgress", editor.MaxBandwidthMbps, value => editor.MaxBandwidthMbps = value, "maxEgressHelp", true);
        var canSource = type == "transcode" && controller.State.Capabilities is { Available: true, SourceAccess: true };
        if (canSource) Select("source_access", T("web.admin.nodes.form.sourceAccess"), editor.SourceAccess,
            [("local", T("web.admin.nodes.form.sourceAccessLocal")), ("core", T("web.admin.nodes.form.sourceAccessCore"))], value => editor.SourceAccess = value, T("web.admin.nodes.form.sourceAccessHelp"));
        if (target != null && type == "transcode")
        {
            Select("hw_accel_override", T("web.admin.nodes.form.hardwareAcceleration"), editor.HwAccelOverride,
                NodeHardwareOptions(),
                value =>
                {
                    var wasCuda = UsesCuda(editor.HwAccelOverride, target, clusterHwAccel); var isCuda = UsesCuda(value, target, clusterHwAccel);
                    editor.HwAccelOverride = value;
                    // A DRM path is never silently reinterpreted as a CUDA identity (or vice versa).
                    if (wasCuda != isCuda) editor.HwDeviceOverride = "";
                    RenderDevices();
                }, T("web.admin.nodes.form.hardwareAccelerationHelp"));
            content.Children.Add(picker); RenderDevices();
        }
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = T(target != null ? "web.admin.nodes.formDialog.editTitle" : type == "proxy" ? "web.admin.nodes.formDialog.addProxyTitle" : "web.admin.nodes.formDialog.addTranscodeTitle"),
            Content = new ScrollViewer { Content = content, MaxHeight = Math.Max(240, (XamlRoot?.Size.Height ?? 900) - 220), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = T("common.save"), CloseButtonText = T("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 640d;
        dialog.Closing += (_, args) => { if (pending && owner.IsCurrent) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; if (pending || !owner.IsCurrent) return;
            IReadOnlyDictionary<string, object?> body;
            try { body = editor.Capture(canSource); } catch (ArgumentException failure) { error.Text = failure.Message; return; }
            pending = true; dialog.IsPrimaryButtonEnabled = false; foreach (var control in fields.Values) control.IsEnabled = false; picker.IsHitTestVisible = false;
            try
            {
                saved = target == null ? await controller.CreateAsync(body) : await controller.UpdateAsync(target, body);
                error.Text = controller.State.Error ?? "";
            }
            finally { pending = false; dialog.IsPrimaryButtonEnabled = !controller.State.ReviewRequired; foreach (var control in fields.Values) control.IsEnabled = true; picker.IsHitTestVisible = true; }
            if (saved || !owner.IsCurrent) dialog.Hide();
        };
        await ShowDialog(dialog, owner);
        syncing = true; foreach (var box in fields.Values.OfType<TextBox>()) box.Text = ""; picker.Children.Clear();
        editor.Name = editor.Url = editor.PublicUrl = editor.Group = editor.HwDeviceOverride = "";
        if (saved && target == null && type == "transcode" && controller.State.Capabilities is { Available: true, NodeKeys: true } && controller.State.CreatedNode is { } created && owner.IsCurrent)
        {
            // Let the saved row's captured config validator own the follow-up, after the editor fully closes.
            var current = controller.State.Window.Items.FirstOrDefault(node => node.Id == created.Id); if (current != null) await ShowKey(current);
        }
        void RenderDevices()
        {
            picker.Children.Clear(); picker.Children.Add(U.Text(T("web.admin.nodes.form.gpuDevices")));
            var cuda = UsesCuda(editor.HwAccelOverride, target!, clusterHwAccel);
            var detected = (target?.Capabilities?.RenderDeviceDetails ?? []).Select(device => device.Path).Concat(target?.Capabilities?.RenderDevices ?? []).Distinct().ToArray();
            if (!cuda && detected.Length > 0)
            {
                var inherit = U.Action(T("web.admin.nodes.devicePicker.useClusterDefault"), "NodeDevicesInherit"); inherit.Click += (_, _) => { editor.HwDeviceOverride = ""; RenderDevices(); }; picker.Children.Add(inherit);
                foreach (var path in detected.Concat(R.ParseDevices(editor.HwDeviceOverride)).Distinct().Take(64))
                {
                    var toggle = new ToggleSwitch { Header = path, OffContent = "", OnContent = "", IsOn = R.ParseDevices(editor.HwDeviceOverride).Contains(path) };
                    AutomationProperties.SetName(toggle, path); toggle.Toggled += (_, _) => editor.HwDeviceOverride = R.ToggleDevice(editor.HwDeviceOverride, path, detected); picker.Children.Add(toggle);
                }
            }
            else
            {
                var box = U.Input(T("web.admin.nodes.form.gpuDevices"), "Node_hw_device_override"); box.Width = double.NaN; box.Text = editor.HwDeviceOverride; box.MaxLength = 8192;
                box.TextChanged += (_, _) => editor.HwDeviceOverride = box.Text; picker.Children.Add(box);
                picker.Children.Add(U.Text(T(cuda ? "web.admin.nodes.form.cudaDeviceHelp" : "web.admin.nodes.form.renderDeviceHelp", new { index = "0", uuid = "GPU-a1b2c3d4", path = "/dev/dri", example = "/dev/dri/renderD128,/dev/dri/renderD129" }), 12, true));
            }
        }
    }
    private static bool UsesCuda(string acceleration, AdminPlaybackNode target, string? cluster)
    {
        if (acceleration == "nvenc") return true;
        if (acceleration is not ("inherit" or "auto" or "")) return false;
        if (acceleration is "inherit" or "" && !string.IsNullOrEmpty(cluster) && cluster != "auto") return cluster == "nvenc";
        return target.Capabilities?.Resolved == "nvenc";
    }
    /// <summary>Official hwAccelOverrideOptions(): the server's per-node override enum, which has no VideoToolbox (the cluster-wide setting does).</summary>
    internal static IEnumerable<(string Value, string Label)> NodeHardwareOptions() => AdminPlaybackNodeEditor.HwAccelOverrideValues.Select(value => (value, value switch
    {
        "inherit" => T("web.admin.nodes.hwAccel.clusterDefault"), "auto" => T("web.admin.nodes.hwAccel.auto"), "none" => T("web.admin.nodes.hwAccel.software"),
        _ => HardwareOptions().First(option => option.Value == value).Label,
    }));
    internal static IEnumerable<(string Value, string Label)> HardwareOptions() => new[]
    {
        ("auto", T("web.admin.settings.playback.hwAccel.auto")), ("qsv", "Intel Quick Sync (QSV)"), ("vaapi", "VA-API"), ("nvenc", "NVIDIA NVENC"), ("videotoolbox", "VideoToolbox (macOS)"), ("none", T("web.admin.settings.playback.hwAccel.software")),
    };
    private async Task DeleteNode(AdminPlaybackNode target)
    {
        var controller = _controller; var owner = controller?.State.Owner;
        if (_dialog != null || controller == null || owner?.IsCurrent != true) return;
        var body = new StackPanel { Spacing = 12 }; body.Children.Add(U.Text(T("web.admin.nodes.deleteDialog.description", new { name = target.Name }))); var error = U.Text("", 13); body.Children.Add(error); var pending = false;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = T("web.admin.nodes.deleteDialog.title"), Content = body, PrimaryButtonText = T("web.admin.nodes.deleteDialog.confirm"), CloseButtonText = T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        dialog.Closing += (_, args) => { if (pending && owner.IsCurrent) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; if (pending) return; pending = true; dialog.IsPrimaryButtonEnabled = false;
            var success = await controller.DeleteAsync(target); pending = false; error.Text = controller.State.Error ?? "";
            dialog.IsPrimaryButtonEnabled = !controller.State.ReviewRequired; if (success || !owner.IsCurrent) dialog.Hide();
        };
        await ShowDialog(dialog, owner);
    }
    private async Task ShowKey(AdminPlaybackNode target)
    {
        var controller = _controller; var owner = controller?.State.Owner;
        if (_dialog != null || controller == null || owner?.IsCurrent != true || controller.State.Capabilities is not { Available: true, NodeKeys: true }) return;
        var body = new StackPanel { Spacing = 12 }; var pending = false; string? issued = null;
        body.Children.Add(U.Text(T(target.KeyIssued == true ? "web.admin.nodes.key.rotateDescription" : "web.admin.nodes.key.offer", new { name = target.Name })));
        if (target.KeyIssued == true) body.Children.Add(U.Text(T("web.admin.nodes.key.rotateDrainHint"), 13, true));
        var error = U.Text("", 13); body.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = T(target.KeyIssued == true ? "web.admin.nodes.key.rotateTitle" : "web.admin.nodes.key.dialogTitle", new { name = target.Name }), Content = body, PrimaryButtonText = T(target.KeyIssued == true ? "web.admin.nodes.key.rotate" : "web.admin.nodes.key.issue"), CloseButtonText = T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        PasswordBox? keyBox = null; TextBox? settings = null; TextBox? coreUrl = null;
        dialog.Closing += (_, args) => { if (pending && owner.IsCurrent) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; if (pending || issued != null) return;
            pending = true; dialog.IsPrimaryButtonEnabled = false;
            var success = await controller.IssueKeyAsync(target, key => issued = key);
            pending = false;
            if (!owner.IsCurrent) { issued = null; dialog.Hide(); return; }
            if (!success || issued == null) { error.Text = T("web.admin.nodes.key.unconfirmed", new { name = target.Name }); return; }
            body.Children.Clear(); body.Children.Add(U.Text(T("web.admin.nodes.key.notShownAgain"), 14));
            keyBox = U.Secret(T("web.admin.nodes.key.keyLabel"), "NodeIssuedKey"); keyBox.Password = issued; keyBox.Width = double.NaN; body.Children.Add(keyBox);
            var copyKey = U.Action(T("web.admin.nodes.key.copyKey"), "NodeCopyKey"); copyKey.Click += (_, _) => Copy(issued); body.Children.Add(copyKey);
            coreUrl = U.Input(T("web.admin.nodes.key.coreUrlLabel"), "NodeCoreUrl", "http://10.0.0.2:8080"); coreUrl.Width = double.NaN;
            try
            {
                var publicUrl = (await App.Services.GetRequiredService<AdminServerSettingsStore>().GetEffectiveAsync(owner)).Values.GetValueOrDefault("server.public_url", "");
                if (AdminPlaybackNodeEditor.ValidUrl(publicUrl)) coreUrl.Text = publicUrl;
            }
            catch { }
            if (!owner.IsCurrent) { issued = null; dialog.Hide(); return; }
            body.Children.Add(U.Text(T("web.admin.nodes.key.coreUrlLabel"))); body.Children.Add(coreUrl); body.Children.Add(U.Text(T("web.admin.nodes.key.coreUrlHelp"), 12, true));
            settings = U.Input(T("web.admin.nodes.key.settingsLabel"), "NodeStartSettings"); settings.Width = double.NaN; settings.IsReadOnly = true; settings.AcceptsReturn = true; settings.Height = 90;
            // Settings include the one-time key only on an explicit Copy settings action, never in page state.
            settings.Text = "SILO_CORE_URL=" + coreUrl.Text.Trim().TrimEnd('/') + "\nSILO_NODE_KEY=••••••••";
            var copySettings = U.Action(T("web.admin.nodes.key.copySettings"), "NodeCopySettings"); copySettings.IsEnabled = AdminPlaybackNodeEditor.ValidUrl(coreUrl.Text);
            coreUrl.TextChanged += (_, _) => copySettings.IsEnabled = AdminPlaybackNodeEditor.ValidUrl(coreUrl.Text);
            copySettings.Click += (_, _) => { if (AdminPlaybackNodeEditor.ValidUrl(coreUrl.Text)) Copy("SILO_CORE_URL=" + coreUrl.Text.Trim().TrimEnd('/') + "\nSILO_NODE_KEY=" + issued); };
            body.Children.Add(settings); body.Children.Add(U.Text(T("web.admin.nodes.key.settingsHelp"), 12, true)); body.Children.Add(copySettings); body.Children.Add(error);
            dialog.PrimaryButtonText = ""; dialog.CloseButtonText = T("web.admin.nodes.key.saved");
        };
        try { await ShowDialog(dialog, owner); }
        finally { issued = null; if (keyBox != null) keyBox.Password = ""; if (settings != null) settings.Text = ""; if (coreUrl != null) coreUrl.Text = ""; body.Children.Clear(); }
        void Copy(string? value)
        {
            if (!owner.IsCurrent || string.IsNullOrEmpty(value)) return;
            // The key authenticates a transcode node: a copy stays out of clipboard history and cloud sync, which would outlive this dialog and reach other devices.
            try
            {
                var data = new DataPackage(); data.SetText(value);
                var copied = Clipboard.SetContentWithOptions(data, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false });
                error.Text = T(copied ? "web.admin.nodes.key.copied" : "web.admin.nodes.key.copyFailed");
            }
            catch { error.Text = T("web.admin.nodes.key.copyFailed"); }
        }
    }
    private async Task ShowDialog(ContentDialog dialog, MonitorAuthorityLease owner)
    {
        if (_dialog != null || !owner.IsCurrent) return;
        _dialog = dialog; using var retired = owner.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try { await dialog.ShowAsync(); }
        catch (System.Runtime.InteropServices.COMException) { }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
}
