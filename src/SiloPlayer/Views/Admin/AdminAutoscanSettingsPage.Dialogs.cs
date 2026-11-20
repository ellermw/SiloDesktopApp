using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using T = SiloPlayer.Core.Services.Admin.AdminAutoscanSettingsText;

namespace SiloPlayer.Views.Admin;

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
        var mode = Select(T.T("dialog.type"), "AutoscanConnectionType", [("reuse", T.T("dialog.reuse")), ("own", T.T("dialog.own"))]);
        var modeField = Field(T.T("dialog.type"), mode); modeField.Visibility = initial.Id == null ? Visibility.Visible : Visibility.Collapsed; content.Children.Add(modeField);
        var reuse = new StackPanel { Spacing = 8 };
        var servers = Select(T.T("dialog.requestServer"), "AutoscanRequestServer", captured.Servers.Select(server => (server.Id, server.Name + " (" + Kind(server.Kind) + ")")).ToArray());
        servers.PlaceholderText = T.T("dialog.selectServer"); reuse.Children.Add(Field(T.T("dialog.requestServer"), servers));
        var serverState = U.Text("", 13, true); reuse.Children.Add(serverState);
        var noServers = new StackPanel { Spacing = 4 };
        noServers.Children.Add(U.Text(T.T("dialog.noServers"), 13, true));
        var setup = new HyperlinkButton { Content = T.T("dialog.requests"), Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(setup, "AutoscanRequestsSetup"); noServers.Children.Add(setup);
        noServers.Children.Add(U.Text(T.T("dialog.noServersEnd"), 13, true)); reuse.Children.Add(noServers); content.Children.Add(reuse);
        var own = new StackPanel { Spacing = 12 };
        var name = U.Input(T.T("name"), "AutoscanConnectionName", T.T("name.placeholder")); name.MaxLength = 512;
        var kind = Select(T.T("kind"), "AutoscanConnectionKind", [("sonarr", "Sonarr"), ("radarr", "Radarr")]);
        var url = U.Input(T.T("baseUrl"), "AutoscanConnectionUrl", "http://localhost:8989"); url.MaxLength = 4096;
        var key = U.Secret(T.T("key"), "AutoscanConnectionKey"); key.MaxLength = 8192; _key = key;
        key.PlaceholderText = initial.HasApiKey ? "••••••••" : T.T("key.placeholder");
        foreach (var input in new FrameworkElement[] { name, kind, url, key }) { input.Width = double.NaN; input.HorizontalAlignment = HorizontalAlignment.Stretch; }
        own.Children.Add(Field(T.T("name"), name)); own.Children.Add(Field(T.T("kind"), kind)); own.Children.Add(Field(T.T("baseUrl"), url));
        own.Children.Add(Field(T.T("key") + (initial.HasApiKey ? " " + T.T("key.keep") : ""), key)); content.Children.Add(own);
        var result = U.Text("", 13); AutomationProperties.SetAutomationId(result, "AutoscanConnectionResult"); AutomationProperties.SetLiveSetting(result, AutomationLiveSetting.Polite); content.Children.Add(result);
        var errors = U.Text("", 13); AutomationProperties.SetLiveSetting(errors, AutomationLiveSetting.Polite); content.Children.Add(errors);
        var test = U.Action(T.T("test"), "AutoscanConnectionTest"); content.Children.Add(test);
        var reconcile = U.Action("Reload and review", "AutoscanConnectionReconcile"); content.Children.Add(reconcile);
        var scroll = new ScrollViewer { Content = content, MaxHeight = Math.Max(200, (XamlRoot?.Size.Height ?? 800) - 240), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = T.T(initial.Id == null ? "connections.add" : "edit"), Content = scroll,
            PrimaryButtonText = T.T(initial.Id == null ? "connections.add" : "dialog.save"), CloseButtonText = AdminText.T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        _dialog = dialog; var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _dialogClosed = closed;
        var syncing = false;
        void Change(Func<AdminAutoscanConnectionEditor, AdminAutoscanConnectionEditor> change)
        { if (!syncing && ReferenceEquals(controller, _controller) && ReferenceEquals(owner, controller.State.Owner) && controller.State.Editor is { } draft) controller.EditConnection(change(draft)); }
        mode.SelectionChanged += (_, _) => Change(draft => draft with { Reuse = Value(mode) == "reuse" });
        servers.SelectionChanged += (_, _) => Change(draft => draft with { IntegrationId = Value(servers) });
        name.TextChanging += (_, _) => Change(draft => draft with { Name = name.Text });
        kind.SelectionChanged += (_, _) => Change(draft => draft with { Kind = Value(kind) });
        url.TextChanging += (_, _) => Change(draft => draft with { BaseUrl = url.Text });
        key.PasswordChanged += (_, _) => Change(draft => draft with { ApiKey = key.Password });
        void CaptureFields() => Change(draft => draft with { Name = name.Text, Kind = Value(kind), BaseUrl = url.Text, ApiKey = key.Password });
        test.Click += async (_, _) => { CaptureFields(); await controller.TestConnectionAsync(); };
        reconcile.Click += async (_, _) => { dialog.Hide(); await closed.Task; if (_active && ReferenceEquals(controller, _controller)) await ReconcileAsync(); };
        setup.Click += async (_, _) =>
        {
            dialog.Hide(); await closed.Task;
            if (!_active || !ReferenceEquals(owner, controller.State.Owner) || !owner.IsCurrent) return;
            if (AdminShellPage.TryOpenHref("/admin/settings/requests") is not { Kind: AdminRouteResolutionKind.Route })
            { U.UpdateLiveText(_status, "Settings › Requests is not available in this build."); _alerts.Visibility = Visibility.Visible; }
        };
        _renderDialog = state =>
        {
            if (!ReferenceEquals(owner, state.Owner) || !owner.IsCurrent || state.Editor is not { } draft) { key.Password = ""; dialog.Hide(); return; }
            syncing = true;
            Pick(mode, draft.Reuse ? "reuse" : "own"); Pick(servers, draft.IntegrationId); Pick(kind, draft.Kind);
            if (name.Text != draft.Name) name.Text = draft.Name; if (url.Text != draft.BaseUrl) url.Text = draft.BaseUrl;
            if (key.Password != draft.ApiKey) key.Password = draft.ApiKey;
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
            U.UpdateLiveText(result, state.TestError ?? (checkedResult == null ? "" : checkedResult.Ok ? T.Connected(checkedResult.Version) : T.T("failed")));
            result.Visibility = result.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        catch (Exception) { if (owner.IsCurrent && _active) { U.UpdateLiveText(_status, "Connection editor could not be opened."); _alerts.Visibility = Visibility.Visible; } }
        finally
        {
            syncing = true; key.Password = "";
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
        var error = U.Text("", 13); body.Children.Add(error);
        var reload = U.Action("Reload and review", "AutoscanDeleteReconcile"); body.Children.Add(reload);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = T.T("delete.title"), Content = body, PrimaryButtonText = "Delete", CloseButtonText = AdminText.T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        _dialog = dialog; var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _dialogClosed = closed;
        _renderDialog = state =>
        {
            if (!ReferenceEquals(owner, state.Owner) || !owner.IsCurrent || state.DeleteTarget == null) { dialog.Hide(); return; }
            U.UpdateLiveText(error, state.Error ?? ""); reload.Visibility = state.NeedsReview ? Visibility.Visible : Visibility.Collapsed;
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
        catch (Exception) { if (owner.IsCurrent && _active) { U.UpdateLiveText(_status, "Delete confirmation could not be opened."); _alerts.Visibility = Visibility.Visible; } }
        finally
        {
            if (ReferenceEquals(_dialog, dialog)) { _dialog = null; _renderDialog = null; }
            if (ReferenceEquals(owner, controller.State.Owner)) controller.CloseEditor(); closed.TrySetResult();
        }
    }

    private static StackPanel Field(string label, FrameworkElement control)
    { var field = new StackPanel { Spacing = 6 }; field.Children.Add(U.Text(label, 13)); field.Children.Add(control); return field; }
    private static ComboBox Select(string label, string id, IReadOnlyList<(string Value, string Label)> items)
    {
        var select = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        AutomationProperties.SetName(select, label); AutomationProperties.SetAutomationId(select, id);
        foreach (var item in items) select.Items.Add(new ComboBoxItem { Content = item.Label, Tag = item.Value });
        return select;
    }
    private static string Value(ComboBox select) => (select.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    private static void Pick(ComboBox select, string value) => select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == value);
}

