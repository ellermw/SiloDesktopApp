using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using M = SiloPlayer.Controls.AdminMonitorUi;
using U = SiloPlayer.Controls.AdminSettingsUi;
using T = SiloPlayer.Core.Services.Admin.AdminAutoscanSettingsText;

namespace SiloPlayer.Views.Admin;

// Add/edit (ConnectionsPanel Dialog) and delete (AlertDialog) of the official connections panel, native ContentDialogs.
public sealed partial class AdminAutoscanSettingsPage
{
    private TaskCompletionSource? _dialogClosed;
    public Task DialogClosed => _dialogClosed?.Task ?? Task.CompletedTask;

    private async Task ShowEditorAsync()
    {
        var controller = _controller;
        if (!_active || _dialog != null || controller?.State is not { Editor: { } initial, Owner.IsCurrent: true } captured) return;
        var owner = captured.Owner!;
        var content = new StackPanel { Spacing = 16, MinWidth = 0 };
        content.Children.Add(U.Text(T.T("dialog.description"), 13, true));

        // Connection type: only while creating; "reuse" is unavailable until a Requests Sonarr/Radarr server exists.
        var mode = Select(T.T("dialog.type"), "AutoscanConnectionType", [("reuse", T.T("dialog.reuse")), ("own", T.T("dialog.own"))]);
        var modeField = Field(T.T("dialog.type"), mode); modeField.Visibility = initial.Id == null ? Visibility.Visible : Visibility.Collapsed; content.Children.Add(modeField);

        var reuse = new StackPanel { Spacing = 8 };
        var servers = Select(T.T("dialog.requestServer"), "AutoscanRequestServer", []);
        servers.PlaceholderText = T.T("dialog.selectServer"); reuse.Children.Add(Field(T.T("dialog.requestServer"), servers));
        var serverState = U.Text("", 13, true); reuse.Children.Add(serverState);
        var noServers = new StackPanel { Spacing = 4 };
        noServers.Children.Add(U.Text(T.T("dialog.noServers"), 13, true));
        var setup = new HyperlinkButton { Content = T.T("dialog.requests"), Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(setup, "AutoscanRequestsSetup"); noServers.Children.Add(setup);
        noServers.Children.Add(U.Text(T.T("dialog.noServersEnd"), 13, true)); reuse.Children.Add(noServers); content.Children.Add(reuse);

        var own = new StackPanel { Spacing = 12 };
        var name = U.Input(T.T("name"), "AutoscanConnectionName", T.T("name.placeholder")); name.MaxLength = AdminAutoscanLimits.Name;
        // A stored kind other than Sonarr/Radarr stays selectable so an unrelated edit does not rewrite it.
        var kinds = new List<(string Value, string Label)> { ("sonarr", "Sonarr"), ("radarr", "Radarr") };
        if (initial.Kind.Length > 0 && kinds.All(item => item.Value != initial.Kind)) kinds.Add((initial.Kind, T.KindLabel(initial.Kind)));
        var kind = Select(T.T("kind"), "AutoscanConnectionKind", kinds);
        var url = U.Input(T.T("baseUrl"), "AutoscanConnectionUrl", "http://localhost:8989"); url.MaxLength = AdminAutoscanLimits.BaseUrl;
        var key = U.Secret(T.T("key"), "AutoscanConnectionKey"); key.MaxLength = AdminAutoscanLimits.ApiKey; _key = key;
        key.PlaceholderText = initial.HasApiKey ? "••••••••" : T.T("key.placeholder");
        foreach (var input in new FrameworkElement[] { name, kind, url, key }) { input.Width = double.NaN; input.HorizontalAlignment = HorizontalAlignment.Stretch; }
        own.Children.Add(Field(T.T("name"), name)); own.Children.Add(Field(T.T("kind"), kind)); own.Children.Add(Field(T.T("baseUrl"), url));
        own.Children.Add(Field(T.T("key") + (initial.HasApiKey ? " " + T.T("key.keep") : ""), key)); content.Children.Add(own);

        // Advisory check result (official inline role=status box): success or failure, never blocking Save.
        var result = U.Text("", 13); AutomationProperties.SetAutomationId(result, "AutoscanConnectionResult");
        var resultIcon = new ContentPresenter { VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 2, 0, 0) };
        var resultLine = new Grid { ColumnSpacing = 8 };
        resultLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); resultLine.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        resultLine.Children.Add(resultIcon); Grid.SetColumn(result, 1); resultLine.Children.Add(result);
        var resultBox = new Border { Child = resultLine, Padding = new(10), CornerRadius = new(6), BorderThickness = new(1), Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(result, AutomationLiveSetting.Polite); content.Children.Add(resultBox);
        var errors = U.Text("", 13); AutomationProperties.SetAutomationId(errors, "AutoscanConnectionError");
        errors.Foreground = M.Solid(M.Destructive);
        AutomationProperties.SetLiveSetting(errors, AutomationLiveSetting.Polite); content.Children.Add(errors);
        var test = U.Action(T.T("test"), "AutoscanConnectionTest"); content.Children.Add(test);
        var reconcile = U.Action(T.T("reloadReview"), "AutoscanConnectionReconcile"); content.Children.Add(reconcile);

        var scroll = new ScrollViewer { Content = content, MaxHeight = Math.Max(200, (XamlRoot?.Size.Height ?? 800) - 240), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = T.T(initial.Id == null ? "connections.add" : "edit"), Content = scroll,
            PrimaryButtonText = T.T(initial.Id == null ? "connections.add" : "dialog.save"), CloseButtonText = AdminText.T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        _dialog = dialog; var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _dialogClosed = closed;
        var syncing = false;
        IReadOnlyList<AdminAutoscanRequestServer>? shownServers = null;
        void Change(Func<AdminAutoscanConnectionEditor, AdminAutoscanConnectionEditor> change)
        { if (!syncing && ReferenceEquals(controller, _controller) && ReferenceEquals(owner, controller.State.Owner) && controller.State.Editor is { } draft) controller.EditConnection(change(draft)); }
        mode.SelectionChanged += (_, _) => { if (Value(mode) is { Length: > 0 } selected) Change(draft => draft with { Reuse = selected == "reuse" }); };
        servers.SelectionChanged += (_, _) => { if (Value(servers) is { Length: > 0 } selected) Change(draft => draft with { IntegrationId = selected }); };
        name.TextChanging += (_, _) => Change(draft => draft with { Name = name.Text });
        kind.SelectionChanged += (_, _) => { if (Value(kind) is { Length: > 0 } selected) Change(draft => draft with { Kind = selected }); };
        url.TextChanging += (_, _) => Change(draft => draft with { BaseUrl = url.Text });
        // PasswordChanged arrives after the box changed; a render in between must not overwrite typing the controller has not seen yet.
        var keySynced = "";
        key.PasswordChanged += (_, _) => { keySynced = key.Password; Change(draft => draft with { ApiKey = key.Password }); };
        void CaptureFields() => Change(draft => draft with { Name = name.Text, Kind = Value(kind) is { Length: > 0 } selected ? selected : draft.Kind, BaseUrl = url.Text, ApiKey = key.Password });
        test.Click += async (_, _) => { CaptureFields(); await controller.TestConnectionAsync(); };
        reconcile.Click += async (_, _) => { dialog.Hide(); await closed.Task; if (_active && ReferenceEquals(controller, _controller)) await ReconcileAsync(); };
        setup.Click += async (_, _) =>
        {
            dialog.Hide(); await closed.Task;
            if (!_active || !ReferenceEquals(owner, controller.State.Owner) || !owner.IsCurrent) return;
            // Settings › Requests is a real route; it stays disabled with its reason only when that page is not in this build.
            if (AdminShellPage.TryOpenHref("/admin/settings/requests") is not { Kind: AdminRouteResolutionKind.Route })
            { U.UpdateLiveText(_errorText, T.T("requestsUnavailable")); _alerts.Visibility = Visibility.Visible; }
        };
        _renderDialog = state =>
        {
            if (!ReferenceEquals(owner, state.Owner) || !owner.IsCurrent || state.Editor is not { } draft) { key.Password = ""; dialog.Hide(); return; }
            syncing = true;
            if (!ReferenceEquals(shownServers, state.Servers))
            {
                // Request servers can arrive after the dialog opened; rebuild the option list from the newest read.
                shownServers = state.Servers; servers.Items.Clear();
                foreach (var server in state.Servers) servers.Items.Add(Item(T.RequestServerOption(server.Name, server.Kind), server.Id));
            }
            if (mode.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == "reuse") is { } reuseItem) reuseItem.IsEnabled = state.Servers.Count > 0;
            Pick(mode, draft.Reuse ? "reuse" : "own"); Pick(servers, draft.IntegrationId); Pick(kind, draft.Kind);
            if (name.Text != draft.Name) name.Text = draft.Name; if (url.Text != draft.BaseUrl) url.Text = draft.BaseUrl;
            if (key.Password != draft.ApiKey && key.Password == keySynced) { key.Password = draft.ApiKey; keySynced = draft.ApiKey; }
            syncing = false;
            reuse.Visibility = draft.Reuse ? Visibility.Visible : Visibility.Collapsed; own.Visibility = draft.Reuse ? Visibility.Collapsed : Visibility.Visible;
            var ready = !state.Busy && !state.NeedsReview;
            mode.IsEnabled = servers.IsEnabled = name.IsEnabled = kind.IsEnabled = url.IsEnabled = key.IsEnabled = ready;
            servers.Visibility = state.ServersLoaded && state.Servers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            noServers.Visibility = state.ServersLoaded && state.Servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            serverState.Text = state.ServersError ?? (!state.ServersLoaded ? T.T("dialog.loadingServers") : "");
            serverState.Visibility = serverState.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = state.CanWrite && draft.CanSave && (!draft.Reuse || state.ServersLoaded);
            dialog.PrimaryButtonText = state.Busy ? T.T("dialog.saving") : T.T(initial.Id == null ? "connections.add" : "dialog.save");
            test.IsEnabled = state.CanWrite && draft.CanTest; test.Content = T.T(state.Testing ? "testing" : "test");
            var checkedResult = state.TestResult;
            var success = state.TestError == null && checkedResult is { Ok: true };
            var text = state.TestError ?? (checkedResult == null ? "" : checkedResult.Ok ? T.Connected(checkedResult.Version) : checkedResult.Error is { Length: > 0 } reason ? reason : T.T("failed"));
            U.UpdateLiveText(result, text);
            resultBox.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var tone = success ? M.Emerald400 : M.Destructive;
            resultBox.Background = M.Solid(tone, .1); resultBox.BorderBrush = M.Solid(tone, .3);
            result.Foreground = M.Solid(tone);
            resultIcon.Content = AdminLibrariesUi.Icon(success ? "circle-check" : "x", 16, M.Solid(tone));
            U.UpdateLiveText(errors, state.Error ?? ""); errors.Visibility = errors.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            reconcile.Visibility = state.NeedsReview || state.ServersError != null ? Visibility.Visible : Visibility.Collapsed;
            reconcile.IsEnabled = !state.Busy && !state.Testing;
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (!owner.IsCurrent || !ReferenceEquals(controller.State.Owner, owner)) return;
            CaptureFields();
            await controller.SaveConnectionAsync();
            if (controller.State.Editor == null && ReferenceEquals(_dialog, dialog)) dialog.Hide();
        };
        dialog.Closing += (_, args) => { if (owner.IsCurrent && controller.State.Busy) args.Cancel = true; };
        _renderDialog(captured);
        try { await dialog.ShowAsync(); }
        catch (Exception) { if (owner.IsCurrent && _active) { U.UpdateLiveText(_errorText, T.T("editorUnavailable")); _alerts.Visibility = Visibility.Visible; } }
        finally
        {
            syncing = true; key.Password = ""; keySynced = "";
            if (ReferenceEquals(_dialog, dialog)) { _dialog = null; _key = null; _renderDialog = null; }
            if (ReferenceEquals(owner, controller.State.Owner)) controller.CloseEditor();
            closed.TrySetResult();
        }
    }

    private async Task ShowDeleteAsync()
    {
        var controller = _controller;
        if (!_active || _dialog != null || controller?.State is not { DeleteTarget: { } target, Owner.IsCurrent: true } captured) return;
        var owner = captured.Owner!;
        var body = new StackPanel { Spacing = 12 }; body.Children.Add(U.Text(T.DeleteDescription(target.Name)));
        var error = U.Text("", 13); error.Foreground = M.Solid(M.Destructive);
        AutomationProperties.SetAutomationId(error, "AutoscanDeleteError"); AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite); body.Children.Add(error);
        var reload = U.Action(T.T("reloadReview"), "AutoscanDeleteReconcile"); body.Children.Add(reload);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = T.T("delete.title"), Content = body, PrimaryButtonText = T.T("delete.confirm"), CloseButtonText = AdminText.T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        if (Application.Current.Resources.TryGetValue("DestructiveButtonStyle", out var destructive) && destructive is Style style) dialog.PrimaryButtonStyle = style;
        _dialog = dialog; var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _dialogClosed = closed;
        _renderDialog = state =>
        {
            if (!ReferenceEquals(owner, state.Owner) || !owner.IsCurrent || state.DeleteTarget == null) { dialog.Hide(); return; }
            U.UpdateLiveText(error, state.Error ?? ""); error.Visibility = error.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            reload.Visibility = state.NeedsReview ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = state.CanWrite; reload.IsEnabled = !state.Busy;
        };
        reload.Click += async (_, _) => { dialog.Hide(); await closed.Task; if (_active && ReferenceEquals(controller, _controller)) await ReconcileAsync(); };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (!owner.IsCurrent || !ReferenceEquals(owner, controller.State.Owner)) return;
            await controller.DeleteConnectionAsync();
            if (controller.State.DeleteTarget == null && ReferenceEquals(_dialog, dialog)) dialog.Hide();
        };
        dialog.Closing += (_, args) => { if (owner.IsCurrent && controller.State.Busy) args.Cancel = true; };
        _renderDialog(captured);
        try { await dialog.ShowAsync(); }
        catch (Exception) { if (owner.IsCurrent && _active) { U.UpdateLiveText(_errorText, T.T("deleteUnavailable")); _alerts.Visibility = Visibility.Visible; } }
        finally
        {
            if (ReferenceEquals(_dialog, dialog)) { _dialog = null; _renderDialog = null; }
            if (ReferenceEquals(owner, controller.State.Owner)) controller.CloseEditor();
            closed.TrySetResult();
        }
    }

    private static StackPanel Field(string label, FrameworkElement control)
    { var field = new StackPanel { Spacing = 6 }; field.Children.Add(U.Text(label, 13)); field.Children.Add(control); return field; }
    private static ComboBoxItem Item(string label, string value) => new() { Content = label, Tag = value };
    private static ComboBox Select(string label, string id, IEnumerable<(string Value, string Label)> items)
    {
        var select = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        AutomationProperties.SetName(select, label); AutomationProperties.SetAutomationId(select, id);
        foreach (var item in items) select.Items.Add(Item(item.Label, item.Value));
        return select;
    }
    private static string Value(ComboBox select) => (select.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    private static void Pick(ComboBox select, string value) => select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == value);
}
