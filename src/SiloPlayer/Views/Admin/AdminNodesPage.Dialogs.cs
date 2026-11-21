using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Windows.ApplicationModel.DataTransfer;
using U = SiloPlayer.Controls.AdminSettingsUi;
using N = SiloPlayer.Core.Services.Admin.AdminPlaybackNodePresentation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminNodesPage
{
    /// <summary>
    /// The destructive filled button of this page: the official --destructive-fill (#dc2626) under white text, darker on hover and pressed. In high contrast it uses the system button and highlight colors.
    /// Page-local on purpose: the shared destructive style and DarkTheme.xaml stay as they are.
    /// </summary>
    internal static Style DestructiveStyle()
    {
        var highContrast = false;
        try { highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast; } catch { }
        var (rest, hover, pressed, foreground, border, hoverForeground) = highContrast
            ? ("{ThemeResource SystemColorButtonFaceColorBrush}", "{ThemeResource SystemColorHighlightColorBrush}", "{ThemeResource SystemColorHighlightColorBrush}", "{ThemeResource SystemColorButtonTextColorBrush}", "{ThemeResource SystemColorButtonTextColorBrush}", "{ThemeResource SystemColorHighlightTextColorBrush}")
            : ("#DC2626", "#B91C1C", "#991B1B", "#FFFFFF", "#00000000", "#FFFFFF");
        var template = (ControlTemplate)XamlReader.Load($$"""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Border x:Name="NodesDestructiveRoot" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="{TemplateBinding CornerRadius}" Padding="{TemplateBinding Padding}">
                <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CommonStates">
                  <VisualState x:Name="Normal"/>
                  <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="NodesDestructiveRoot.Background" Value="{{hover}}"/><Setter Target="NodesDestructiveContent.Foreground" Value="{{hoverForeground}}"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="NodesDestructiveRoot.Background" Value="{{pressed}}"/><Setter Target="NodesDestructiveContent.Foreground" Value="{{hoverForeground}}"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="NodesDestructiveRoot.Opacity" Value="0.5"/></VisualState.Setters></VisualState>
                </VisualStateGroup></VisualStateManager.VisualStateGroups>
                <ContentPresenter x:Name="NodesDestructiveContent" Content="{TemplateBinding Content}" Foreground="{TemplateBinding Foreground}" FontSize="{TemplateBinding FontSize}" FontWeight="{TemplateBinding FontWeight}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
            </ControlTemplate>
            """);
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        Brush System(string key, Brush fallback) => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : fallback;
        style.Setters.Add(new Setter(Control.BackgroundProperty, highContrast ? System("SystemColorButtonFaceColorBrush", new SolidColorBrush(Microsoft.UI.Colors.Black)) : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xDC, 0x26, 0x26))));
        style.Setters.Add(new Setter(Control.ForegroundProperty, highContrast ? System("SystemColorButtonTextColorBrush", new SolidColorBrush(Microsoft.UI.Colors.White)) : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))));
        if (highContrast) style.Setters.Add(new Setter(Control.BorderBrushProperty, System("SystemColorButtonTextColorBrush", new SolidColorBrush(Microsoft.UI.Colors.White))));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 8, 16, 8)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(10)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, Microsoft.UI.Text.FontWeights.Medium));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36d));
        style.Setters.Add(new Setter(Control.UseSystemFocusVisualsProperty, true));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(highContrast ? 1 : 0)));
        return style;
    }
    private async Task EditNode(string type, AdminPlaybackNode? target)
    {
        var controller = _controller; var owner = controller?.State.Owner;
        if (_dialog != null || controller == null || owner?.IsCurrent != true || controller.State.Busy || controller.State.ReviewRequired) return;
        var editor = new AdminPlaybackNodeEditor(type, target, _group ?? "");
        string? clusterHwAccel = null;
        try { clusterHwAccel = (await App.Services.GetRequiredService<AdminServerSettingsStore>().GetEffectiveAsync(owner)).Values.GetValueOrDefault("playback.hw_accel"); } catch { }
        if (!owner.IsCurrent || _dialog != null) return;
        var content = new StackPanel { Spacing = 16 }; var error = U.Text("", 13); error.Foreground = new SolidColorBrush(Danger); AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        var fields = new Dictionary<string, Control>(); var locked = new HashSet<Control>(); var picker = new StackPanel { Spacing = 8 }; var syncing = false; var pending = false; var saved = false;
        // Each field is its label, the control, then the help; a field the node declares from its environment is read-only and says so, as on the official form.
        StackPanel Row(string label, string wire, Control control, params string?[] help)
        {
            var row = new StackPanel { Spacing = 8 }; row.Children.Add(U.Text(label)); row.Children.Add(control);
            if (editor.IsLocked(wire)) { control.IsEnabled = false; locked.Add(control); row.Children.Add(U.Text(T("web.admin.nodes.form.envManaged"), 13, true)); }
            foreach (var text in help) if (!string.IsNullOrEmpty(text)) row.Children.Add(U.Text(text, 13, true));
            content.Children.Add(row); return row;
        }
        TextBox Input(string key, string wire, string value, Action<string> set, string help = "", bool number = false, string placeholder = "", bool ltr = false, bool ltrWhenFilled = false)
        {
            var box = U.Input(T("web.admin.nodes.form." + key), "Node_" + key, placeholder, number: number); box.Text = value; box.Width = double.NaN; box.HorizontalAlignment = HorizontalAlignment.Stretch; box.MaxLength = key is "url" or "publicUrl" ? 8192 : 1024;
            // URLs and paths read left to right; a field whose placeholder is a translated sentence takes that direction only while it holds a value.
            void Direction() => box.FlowDirection = ltr || ltrWhenFilled && box.Text.Length > 0 ? FlowDirection.LeftToRight : FlowDirection;
            Direction(); box.TextChanged += (_, _) => { Direction(); if (!syncing) set(box.Text); };
            Row(T("web.admin.nodes.form." + key), wire, box, help.Length > 0 ? T("web.admin.nodes.form." + help) : null); fields[key] = box; return box;
        }
        ComboBox Select(string key, string wire, string label, string value, IEnumerable<(string Value, string Label)> options, Action<string> set, string help, string? extra = null)
        {
            var box = U.Select(label, "Node_" + key, options); box.Width = double.NaN; box.HorizontalAlignment = HorizontalAlignment.Stretch;
            box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == value);
            box.SelectionChanged += (_, _) => { if (!syncing && box.SelectedItem is ComboBoxItem { Tag: string selected }) set(selected); };
            Row(label, wire, box, help, extra); fields[key] = box; return box;
        }
        var proxy = type == "proxy";
        Input("name", "name", editor.Name, value => editor.Name = value, placeholder: T(proxy ? "web.admin.nodes.form.namePlaceholderProxy" : "web.admin.nodes.form.namePlaceholderTranscode"));
        var typeRow = new StackPanel { Spacing = 8 }; typeRow.Children.Add(U.Text(T("web.admin.nodes.form.type")));
        typeRow.Children.Add(new Border { Child = U.Text(T(proxy ? "web.admin.nodes.form.typeProxy" : "web.admin.nodes.form.typeTranscode"), 14), Padding = new(10, 2, 10, 2), CornerRadius = new(10), Background = U.Brush("SecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Left }); content.Children.Add(typeRow);
        Input("url", "url", editor.Url, value => editor.Url = value, proxy ? "urlHelpProxy" : "urlHelpTranscode", placeholder: proxy ? "https://proxy1.example.com" : "http://10.0.0.5:8082", ltr: true);
        if (proxy) Input("publicUrl", "public_url", editor.PublicUrl, value => editor.PublicUrl = value, "publicUrlHelp", placeholder: T("web.admin.nodes.form.publicUrlPlaceholder"), ltrWhenFilled: true);
        Input("group", "group", editor.Group, value => editor.Group = value, "groupHelp", placeholder: T("web.admin.nodes.form.groupPlaceholder"));
        Input(proxy ? "maxStreams" : "maxTranscodes", "max_jobs", editor.MaxJobs, value => editor.MaxJobs = value, "maxJobsHelp", true, T("web.admin.nodes.form.unlimited"));
        if (proxy) Input("maxEgress", "max_bandwidth_kbps", editor.MaxBandwidthMbps, value => editor.MaxBandwidthMbps = value, "maxEgressHelp", true, T("web.admin.nodes.form.unlimited"));
        var canSource = !proxy && controller.State.Capabilities is { Available: true, SourceAccess: true };
        if (canSource) Select("source_access", "source_access", T("web.admin.nodes.form.sourceAccess"),editor.SourceAccess,
            [("local", T("web.admin.nodes.form.sourceAccessLocal")), ("core", T("web.admin.nodes.form.sourceAccessCore"))], value => editor.SourceAccess = value, T("web.admin.nodes.form.sourceAccessHelp"));
        if (target != null && !proxy)
        {
            Select("hw_accel_override", "hw_accel_override", T("web.admin.nodes.form.hardwareAcceleration"), editor.HwAccelOverride,
                NodeHardwareOptions(),
                value =>
                {
                    // A render path is never silently reinterpreted as a CUDA identity (or the reverse). A device the node declares is saved as stored, so it stays.
                    var crosses = N.DeviceSyntaxChanges(target, editor.HwAccelOverride, value, clusterHwAccel);
                    editor.HwAccelOverride = value;
                    if (crosses && !editor.IsLocked("hw_device_override")) editor.HwDeviceOverride = "";
                    RenderDevices();
                }, T("web.admin.nodes.form.hardwareAccelerationHelp"), N.EffectiveAcceleration(target));
            var devices = new StackPanel { Spacing = 8 }; devices.Children.Add(picker);
            if (editor.IsLocked("hw_device_override")) devices.Children.Add(U.Text(T("web.admin.nodes.form.envManaged"), 13, true));
            content.Children.Add(devices); RenderDevices();
        }
        content.Children.Add(error);
        var scroll = new ScrollViewer { Content = content, MaxHeight = Math.Max(240, (XamlRoot?.Size.Height ?? 900) - 220), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = T(target != null ? "web.admin.nodes.formDialog.editTitle" : proxy ? "web.admin.nodes.formDialog.addProxyTitle" : "web.admin.nodes.formDialog.addTranscodeTitle"),
            Content = scroll, PrimaryButtonText = T("common.save"), CloseButtonText = T("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 512d;
        dialog.Closing += (_, args) => { if (pending && owner.IsCurrent) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; if (pending || !owner.IsCurrent) return;
            IReadOnlyDictionary<string, object?> body;
            try { body = editor.Capture(canSource); } catch (ArgumentException failure) { error.Text = failure.Message; return; }
            // Every field the node declares is read-only: nothing is left to save, so nothing is sent.
            if (body.Count == 0) { dialog.Hide(); return; }
            pending = true; dialog.IsPrimaryButtonEnabled = false; dialog.PrimaryButtonText = T("web.admin.nodes.form.saving"); foreach (var control in fields.Values) control.IsEnabled = false; picker.IsHitTestVisible = false;
            try
            {
                saved = target == null ? await controller.CreateAsync(body) : await controller.UpdateAsync(target, body);
                // A refused write keeps the draft on screen with the server's reason; a saved one closes below.
                error.Text = saved ? "" : controller.State.Error ?? "";
            }
            finally { pending = false; dialog.PrimaryButtonText = T("common.save"); dialog.IsPrimaryButtonEnabled = !controller.State.ReviewRequired; foreach (var (_, control) in fields) control.IsEnabled = !locked.Contains(control); picker.IsHitTestVisible = true; }
            if (saved || !owner.IsCurrent) dialog.Hide();
        };
        await ShowDialog(dialog, owner);
        syncing = true; foreach (var box in fields.Values.OfType<TextBox>()) box.Text = ""; picker.Children.Clear();
        editor.Name = editor.Url = editor.PublicUrl = editor.Group = editor.HwDeviceOverride = "";
        if (saved && target == null && !proxy && controller.State.Capabilities is { Available: true, NodeKeys: true } && controller.State.CreatedNode is { } created && owner.IsCurrent)
        {
            // Let the saved row's captured config validator own the follow-up, after the editor fully closes.
            var current = controller.State.Window.Items.FirstOrDefault(node => node.Id == created.Id); if (current != null) await ShowKey(current);
        }
        void RenderDevices()
        {
            picker.Children.Clear(); var gpuLabel = T("web.admin.nodes.form.gpuDevices"); picker.Children.Add(U.Text(gpuLabel));
            var cuda = N.UsesCudaDevices(target, editor.HwAccelOverride, clusterHwAccel); var disabled = editor.IsLocked("hw_device_override");
            if (!cuda && N.HasDeviceInventory(target))
            {
                var rows = N.DeviceRows(target, editor.HwDeviceOverride); var paths = N.DevicePaths(target);
                foreach (var row in rows)
                {
                    var line = new Grid { ColumnSpacing = 12 }; line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    var text = new StackPanel(); var description = U.Text(row.Description, 14, !row.Reported); description.TextTrimming = TextTrimming.CharacterEllipsis; description.TextWrapping = TextWrapping.NoWrap; text.Children.Add(description);
                    text.Children.Add(Mono(row.Path)); ToolTipService.SetToolTip(text, row.Title); line.Children.Add(text);
                    var toggle = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0, IsOn = row.Selected, IsEnabled = !disabled }; Grid.SetColumn(toggle, 1);
                    AutomationProperties.SetName(toggle, T("web.admin.nodes.devicePicker.toggle", new { path = P(row.Path) }));
                    // The toggle reports its starting state once it is realized: only a change the admin made may stage a draft.
                    var path = row.Path; toggle.Toggled += (_, _) => { if (!toggle.IsLoaded || syncing || disabled) return; var wanted = toggle.IsOn; if (N.DeviceRows(target, editor.HwDeviceOverride).FirstOrDefault(candidate => candidate.Path == path)?.Selected == wanted) return; editor.HwDeviceOverride = AdminPlaybackSettingsRules.ToggleDevice(editor.HwDeviceOverride, path, paths); RenderDevices(); };
                    line.Children.Add(toggle); picker.Children.Add(line);
                }
                var selected = rows.Count(row => row.Selected); var footer = new Grid { ColumnSpacing = 8 }; footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                footer.Children.Add(U.Text(T(selected == 0 ? "web.admin.nodes.devicePicker.inheriting" : selected == 1 ? "web.admin.nodes.devicePicker.singleDevice" : "web.admin.nodes.devicePicker.balanced"), 13, true));
                if (selected > 0 && !disabled) { var inherit = U.Action(T("web.admin.nodes.devicePicker.useClusterDefault"), "NodeDevicesInherit"); inherit.Click += (_, _) => { editor.HwDeviceOverride = ""; RenderDevices(); }; Grid.SetColumn(inherit, 1); footer.Children.Add(inherit); }
                picker.Children.Add(footer);
            }
            else
            {
                var box = U.Input(gpuLabel, "Node_hw_device_override", T("web.admin.nodes.form.gpuDevicesPlaceholder")); box.Width = double.NaN; box.HorizontalAlignment = HorizontalAlignment.Stretch; box.Text = editor.HwDeviceOverride; box.MaxLength = 8192; box.IsEnabled = !disabled;
                void Direction() => box.FlowDirection = box.Text.Length > 0 ? FlowDirection.LeftToRight : FlowDirection;
                Direction(); box.TextChanged += (_, _) => { Direction(); if (!syncing && !disabled) editor.HwDeviceOverride = box.Text; }; picker.Children.Add(box);
                picker.Children.Add(U.Text(T(cuda ? "web.admin.nodes.form.cudaDeviceHelp" : "web.admin.nodes.form.renderDeviceHelp", new { index = "0", uuid = "GPU-a1b2c3d4", path = "/dev/dri", example = "/dev/dri/renderD128,/dev/dri/renderD129" }), 13, true));
            }
        }
    }
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
        // The node was captured when the admin pressed Delete: this confirmation, and the one request it sends, act on exactly that node.
        var body = new StackPanel { Spacing = 12 }; body.Children.Add(U.Text(T("web.admin.nodes.deleteDialog.description", new { name = P(target.Name) }))); var error = U.Text("", 13); error.Foreground = new SolidColorBrush(Danger); body.Children.Add(error); var pending = false;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = T("web.admin.nodes.deleteDialog.title"), Content = body, PrimaryButtonText = T("web.admin.nodes.deleteDialog.confirm"), CloseButtonText = T("common.cancel"), DefaultButton = ContentDialogButton.Close, PrimaryButtonStyle = DestructiveStyle() };
        dialog.Closing += (_, args) => { if (pending && owner.IsCurrent) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; if (pending) return; pending = true; dialog.IsPrimaryButtonEnabled = false;
            var success = await controller.DeleteAsync(target); pending = false; error.Text = success ? "" : controller.State.Error ?? "";
            dialog.IsPrimaryButtonEnabled = !controller.State.ReviewRequired; if (success || !owner.IsCurrent) dialog.Hide();
        };
        await ShowDialog(dialog, owner);
    }
    private async Task ShowKey(AdminPlaybackNode target)
    {
        var controller = _controller; var owner = controller?.State.Owner;
        if (_dialog != null || controller == null || owner?.IsCurrent != true || target.IsDeploymentManaged || controller.State.Capabilities is not { Available: true, NodeKeys: true }) return;
        // The key lives in this dialog only: it is never put in state, a property, a tooltip or a log, and is erased when the dialog closes.
        var rotate = target.KeyIssued == true; var name = P(target.Name);
        var body = new StackPanel { Spacing = 12 }; var pending = false; string? issued = null;
        var description = U.Text(T(rotate ? "web.admin.nodes.key.rotateDescription" : "web.admin.nodes.key.offer", new { name })); body.Children.Add(description);
        if (rotate) body.Children.Add(U.Text(T("web.admin.nodes.key.rotateDrainHint"), 14));
        var error = U.Text("", 13); error.Foreground = new SolidColorBrush(Danger); AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite); body.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = T(rotate ? "web.admin.nodes.key.rotateTitle" : "web.admin.nodes.key.dialogTitle", new { name }), Content = body, PrimaryButtonText = T(rotate ? "web.admin.nodes.key.rotate" : "web.admin.nodes.key.issue"), CloseButtonText = T(rotate ? "common.cancel" : "web.admin.nodes.key.later"), DefaultButton = ContentDialogButton.Close };
        if (rotate) dialog.PrimaryButtonStyle = DestructiveStyle();
        PasswordBox? keyBox = null; TextBox? settings = null; TextBox? coreUrl = null;
        dialog.Closing += (_, args) => { if (pending && owner.IsCurrent) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; if (pending || issued != null) return;
            pending = true; dialog.IsPrimaryButtonEnabled = false; description.Text = T("web.admin.nodes.key.issuing");
            // Sent once: nothing here retries, and an unknown outcome is never repeated until the page has read the nodes again.
            var success = await controller.IssueKeyAsync(target, key => issued = key);
            pending = false;
            if (!owner.IsCurrent) { issued = null; dialog.Hide(); return; }
            if (!success || issued == null)
            {
                // A refusal says why (nothing was issued); a lost answer or a server fault may have rotated the key anyway.
                var state = controller.State;
                var reason = U.Text(state.ReviewRequired || state.Error == null ? T("web.admin.nodes.key.unconfirmed", new { name }) : state.Error, 14); reason.Foreground = new SolidColorBrush(Danger);
                AutomationProperties.SetLiveSetting(reason, AutomationLiveSetting.Assertive); body.Children.Clear(); body.Children.Add(reason);
                dialog.PrimaryButtonText = ""; dialog.CloseButtonText = T("web.admin.nodes.key.close"); return;
            }
            dialog.Title = T("web.admin.nodes.key.dialogTitle", new { name });
            body.Children.Clear(); var warning = U.Text(T("web.admin.nodes.key.notShownAgain"), 14); warning.Foreground = new SolidColorBrush(Warning); warning.FontWeight = Microsoft.UI.Text.FontWeights.Medium; body.Children.Add(warning);
            keyBox = U.Secret(T("web.admin.nodes.key.keyLabel"), "NodeIssuedKey"); keyBox.Password = issued; keyBox.Width = double.NaN; keyBox.FlowDirection = FlowDirection.LeftToRight; body.Children.Add(U.Text(T("web.admin.nodes.key.keyLabel"))); body.Children.Add(keyBox);
            var copyKey = U.Action(T("web.admin.nodes.key.copyKey"), "NodeCopyKey"); copyKey.Click += (_, _) => Copy(issued); body.Children.Add(copyKey);
            coreUrl = U.Input(T("web.admin.nodes.key.coreUrlLabel"), "NodeCoreUrl", "http://10.0.0.2:8080"); coreUrl.Width = double.NaN; coreUrl.HorizontalAlignment = HorizontalAlignment.Stretch; coreUrl.FlowDirection = FlowDirection.LeftToRight;
            try
            {
                var publicUrl = (await App.Services.GetRequiredService<AdminServerSettingsStore>().GetEffectiveAsync(owner)).Values.GetValueOrDefault("server.public_url", "");
                if (AdminPlaybackNodeEditor.ValidUrl(publicUrl) && !AdminPlaybackNodeText.HasInvisible(publicUrl)) coreUrl.Text = publicUrl;
            }
            catch { }
            if (!owner.IsCurrent) { issued = null; dialog.Hide(); return; }
            body.Children.Add(U.Text(T("web.admin.nodes.key.coreUrlLabel"))); body.Children.Add(coreUrl); body.Children.Add(U.Text(T("web.admin.nodes.key.coreUrlHelp"), 13, true));
            settings = U.Input(T("web.admin.nodes.key.settingsLabel"), "NodeStartSettings"); settings.Width = double.NaN; settings.HorizontalAlignment = HorizontalAlignment.Stretch; settings.IsReadOnly = true; settings.AcceptsReturn = true; settings.Height = 90; settings.FlowDirection = FlowDirection.LeftToRight;
            // The key is shown masked and joins the copied settings only on an explicit Copy settings action, never in page state.
            var copySettings = U.Action(T("web.admin.nodes.key.copySettings"), "NodeCopySettings"); var settingsHelp = U.Text("", 13, true);
            void UpdateSettings()
            {
                var empty = coreUrl.Text.Trim().TrimEnd('/').Length == 0;
                settings.Text = "SILO_CORE_URL=" + coreUrl.Text.Trim().TrimEnd('/') + "\nSILO_NODE_KEY=••••••••";
                copySettings.IsEnabled = AdminPlaybackNodeText.KeySettings(coreUrl.Text, "x") != null; settingsHelp.Text = T(empty ? "web.admin.nodes.key.coreUrlMissing" : "web.admin.nodes.key.settingsHelp");
            }
            UpdateSettings(); coreUrl.TextChanged += (_, _) => UpdateSettings();
            copySettings.Click += (_, _) => Copy(AdminPlaybackNodeText.KeySettings(coreUrl.Text, issued ?? ""));
            body.Children.Add(U.Text(T("web.admin.nodes.key.settingsLabel"))); body.Children.Add(settings); body.Children.Add(settingsHelp); body.Children.Add(copySettings); body.Children.Add(error);
            dialog.PrimaryButtonText = ""; dialog.CloseButtonText = T("web.admin.nodes.key.saved");
        };
        try { await ShowDialog(dialog, owner); }
        finally { issued = null; if (keyBox != null) keyBox.Password = ""; if (settings != null) settings.Text = ""; if (coreUrl != null) coreUrl.Text = ""; body.Children.Clear(); }
        void Copy(string? value)
        {
            if (!owner.IsCurrent || string.IsNullOrEmpty(value)) return;
            // The key authenticates a transcode node: a copy stays out of clipboard history and cloud sync, which would outlive this dialog and reach other devices. It is exactly the value, with no invisible characters.
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
        try
        {
            // A dialog that follows another one (the key dialog after the editor that created the node) is refused while the first is still closing, so it is offered again until it opens.
            for (var attempt = 0; ; attempt++)
            {
                try { await dialog.ShowAsync(); break; }
                catch (System.Runtime.InteropServices.COMException) when (attempt < 40 && owner.IsCurrent && !dialog.IsLoaded) { await Task.Delay(50); }
                catch (System.Runtime.InteropServices.COMException) { break; }
            }
        }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
}
