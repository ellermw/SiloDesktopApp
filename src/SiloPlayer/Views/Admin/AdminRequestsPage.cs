using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using J = SiloPlayer.Core.Services.Admin.AdminRequestJson;

namespace SiloPlayer.Views.Admin;

/// <summary>The complete three-route Manage › Requests topic. All callbacks belong to the page authority that rendered them.</summary>
public sealed partial class AdminRequestsPage : Page, IAdminGutterScrollPage, IAdminPageSuspension, IAdminNavigationGuard
{
    private readonly AdminRequestsApi _api = App.Services.GetRequiredService<AdminRequestsApi>();
    private readonly AdminMonitorAuthority _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
    private readonly AdminRequestCountStore _counts = App.Services.GetRequiredService<AdminRequestCountStore>();
    private readonly StackPanel _body = new() { Spacing = 20, Padding = new(0, 0, 0, 32) };
    private readonly StackPanel _content = new() { Spacing = 20 };
    private readonly TextBlock _notice = U.Text("", 13);
    private readonly Button _writeReview = U.Action("Reload and review current values", "AdminRequestsWriteReview");
    private Func<Task>? _pendingWriteReview;
    private readonly List<PasswordBox> _secrets = [];
    private readonly ScrollViewer _scroll;
    private readonly Border _saveBar = new() { Visibility = Visibility.Collapsed, Padding = new(16), VerticalAlignment = VerticalAlignment.Bottom,
        Background = U.Brush("SurfaceBrush"), BorderBrush = U.Brush("BorderBrush"), BorderThickness = new(0, 1, 0, 0) };
    private AdminAuthorityLifetime? _lifetime;
    private MonitorAuthorityLease? Owner => _lifetime?.Owner;
    private AdminRouteRequest _request = new(AdminRoute.RequestQueue);
    private JsonObject _capabilities = new();
    private ContentDialog? _dialog;
    private Task? _dialogClosed;
    private CancellationTokenSource? _dialogReads;
    private bool _busy;
    private bool _dirtyValue;
    private bool _dirty
    {
        get => _dirtyValue;
        set { _dirtyValue = value; _saveBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed; _body.Padding = new(0, 0, 0, value ? 96 : 32); }
    }
    private long _generation;
    public FrameworkElement ScrollContent => _body;
    public bool RequiresLeaveConfirmation => Owner is { IsCurrent: true } && _dirty;

    public AdminRequestsPage()
    {
        InitializeComponent();
        _notice.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_notice, "AdminRequestsNotice");
        AutomationProperties.SetLiveSetting(_notice, AutomationLiveSetting.Polite);
        _writeReview.Visibility = Visibility.Collapsed;
        _writeReview.Click += async (_, _) =>
        {
            var owner = Owner; var review = _pendingWriteReview;
            if (_busy || !Current(owner) || review == null) return;
            _busy = true; _writeReview.IsEnabled = false;
            try { Notice(""); await ReconcileWriteAsync(owner!, review); }
            finally { if (Current(owner)) { _busy = false; _writeReview.IsEnabled = true; } }
        };
        _body.Children.Add(_notice); _body.Children.Add(_writeReview); _body.Children.Add(_content);
        _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var root = new Grid(); root.Children.Add(_scroll); root.Children.Add(_saveBar); Content = root;
        Loaded += async (_, _) => await OpenAsync();
        Unloaded += (_, _) => SuspendAdminPage();
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); if (e.Parameter is AdminRouteRequest request) _request = request; }
    private async Task OpenAsync()
    {
        if (!IsLoaded) return;
        _lifetime?.Dispose();
        _lifetime = new(_authority); _lifetime.Retired += Retired;
        try { _lifetime.Open(); await ReloadAsync(); }
        catch (OperationCanceledException) { Clear(); Notice("Permission to manage requests is required."); }
    }
    private void Retired() => DispatcherQueue.TryEnqueue(async () =>
    {
        if (!IsLoaded) return;
        Clear();
        if (_authority.IsAvailable) await OpenAsync(); else Notice("Permission to manage requests is required. Reload after access is restored.");
    });
    private void Clear()
    {
        _generation++; _dialog?.Hide(); foreach (var secret in _secrets) secret.Password = ""; _secrets.Clear();
        _dialogReads?.Cancel(); _queueCancellation?.Cancel(); StopQueuePolling();
        _dirty = false; _busy = false; SetWriteReview(null); _selected.Clear(); _queueRows.Clear(); _content.Children.Clear();
        _general = null; _generalDraft = null; _layout = null; _routing = null; _routes = null; _servers = []; _plugins = new(); _users = new();
        _queueRead++; _readingQueue = false; _queueError = null; _queueList = null; _bulkBar = null; _bulkProgress = null; _viewButtons.Clear();
    }
    public void SuspendAdminPage() { _lifetime?.Retire(); Clear(); _lifetime?.Dispose(); _lifetime = null; }
    private bool Current(MonitorAuthorityLease? owner) => IsLoaded && owner is { IsCurrent: true } && ReferenceEquals(owner, Owner);

    private async Task ReloadAsync()
    {
        var owner = Owner; if (!Current(owner)) return;
        StopQueuePolling();
        if (_pendingWriteReview == null) Notice("");
        var generation = ++_generation;
        _content.Children.Clear(); _content.Children.Add(U.Text("Loading…", 14, true));
        try
        {
            var capabilities = await _api.GetCapabilitiesAsync(owner!);
            if (!Current(owner) || generation != _generation) return;
            _capabilities = capabilities; _content.Children.Clear();
            if (!J.Flag(capabilities, "available")) { Notice(S("web.admin.requests.page.unavailable.detail")); return; }
            switch (_request.Route)
            {
                case AdminRoute.RequestQueue: await BuildQueueAsync(owner!, generation); break;
                case AdminRoute.RequestDiscover:
                    if (!J.Flag(capabilities, "discover_layout")) { Notice(S("web.admin.requests.discoverPage.unavailable.detail")); return; }
                    await BuildDiscoverAsync(owner!, generation); break;
                case AdminRoute.RequestSettings:
                    if (!J.Flag(capabilities, "guarded_configuration")) { Notice(S("web.admin.settings.requests.page.unavailable")); return; }
                    await BuildSettingsAsync(owner!, generation); break;
            }
        }
        catch (OperationCanceledException) when (!Current(owner) || generation != _generation) { }
        catch (Exception error) { if (Current(owner) && generation == _generation) { _content.Children.Clear(); Notice(AdminRequestRules.Error(error)); _content.Children.Add(Button("Try again", "AdminRequestsRetry", ReloadAsync)); } if (_pendingWriteReview != null) throw; }
    }
    private bool _readNotice;
    /// <summary>A read notice reports a failed read, so the next button action, refresh or successful read replaces it. Every other notice stays until a load, review or successful write.</summary>
    private void Notice(string message, bool read = false) { _readNotice = read && message.Length > 0; _notice.Text = message; _notice.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible; }
    private void ClearReadNotice() { if (_readNotice) Notice(""); }
    private static string S(string key, object? args = null) => AdminRequestsText.T(key, args);
    private static object Count(int count) => new { count, count_text = AdminText.FormatNumber(count) };
    private Button Button(string label, string id, Func<Task> action, bool primary = false)
    {
        var owner = Owner; var button = U.Action(label, id, primary);
        button.Click += async (_, _) =>
        {
            if (!Current(owner) || _busy) return;
            // An earlier failed read's notice ends here, so a retry that succeeds leaves none and one that fails reports its own.
            ClearReadNotice();
            button.IsEnabled = false;
            try { await action(); }
            catch (OperationCanceledException) { }
            // Writes report their own failures, so what reaches a button is a failed read.
            catch (Exception error) { if (Current(owner)) Notice(AdminRequestRules.Error(error), true); }
            finally { if (Current(owner)) button.IsEnabled = true; }
        };
        return button;
    }
    private static StackPanel Stack(params UIElement[] elements) { var panel = new StackPanel { Spacing = 10 }; foreach (var element in elements) panel.Children.Add(element); return panel; }
    private static StackPanel Row(params UIElement[] elements) { var panel = Stack(elements); panel.Orientation = Orientation.Horizontal; panel.Spacing = 8; return panel; }
    private static Border Card(UIElement content) => new() { Child = content, Padding = new(16), CornerRadius = new(10), BorderThickness = new(1), BorderBrush = U.Brush("BorderBrush"), Background = U.Brush("SurfaceBrush") };
    private static FrameworkElement Field(string label, FrameworkElement control, string? help = null)
    {
        control.HorizontalAlignment = HorizontalAlignment.Stretch; if (control is Control c) c.MinWidth = 0;
        var panel = Stack(U.Text(label, 13), control); if (!string.IsNullOrWhiteSpace(help)) panel.Children.Add(U.Text(help, 12, true)); return panel;
    }
    private static TextBox Input(string value, string id, string placeholder = "")
    {
        var input = new TextBox { Text = value, PlaceholderText = placeholder, MinWidth = 0, MaxLength = 4096 };
        AutomationProperties.SetAutomationId(input, id); AutomationProperties.SetName(input, placeholder.Length == 0 ? id : placeholder); return input;
    }
    private static ComboBox Select(string id, IEnumerable<(string Value, string Label)> options, string current)
    {
        var select = U.Select(id, id, options); select.Width = double.NaN; select.HorizontalAlignment = HorizontalAlignment.Stretch;
        select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, current)); return select;
    }
    private static string Selected(ComboBox select) => (select.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
    private static ToggleSwitch Toggle(string label, string id, bool value)
    {
        var toggle = new ToggleSwitch { Header = label, IsOn = value, OnContent = "", OffContent = "", MinWidth = 0 };
        AutomationProperties.SetAutomationId(toggle, id); AutomationProperties.SetName(toggle, label); return toggle;
    }
    private void Header(string title, string? subtitle = null, params UIElement[] actions)
    {
        var heading = Stack(U.Title(title)); if (subtitle != null) heading.Children.Add(U.Text(subtitle, 14, true));
        if (actions.Length > 0) heading.Children.Add(Row(actions)); _content.Children.Add(heading);
    }
    private Task NavigateAsync(AdminRoute route) { AdminShellPage.Open(new AdminRouteRequest(route)); return Task.CompletedTask; }
    private void ReplaceLocation(string key, string? value)
    {
        _request = _request.WithQuery(key, value);
        for (DependencyObject? node = this; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is AdminShellPage shell) { shell.ReplaceRequest(this, _request); break; }
    }

    private async Task<bool> ConfirmAsync(string title, string description, string confirm)
    {
        if (_dialog != null || _lifetime == null) return false;
        using var action = _lifetime.CaptureAction();
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = U.Text(description), PrimaryButtonText = confirm, CloseButtonText = S("common.cancel"), DefaultButton = ContentDialogButton.Close };
        _dialog = dialog;
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(action.Token); _dialogReads = reads;
        using var cancellation = action.Token.Register(() => DispatcherQueue.TryEnqueue(() => { if (ReferenceEquals(_dialog, dialog) && dialog.IsLoaded) dialog.Hide(); }));
        try { var task = ShowAsync(dialog); _dialogClosed = task; return await task == ContentDialogResult.Primary && action.IsCurrent; }
        finally { reads.Cancel(); if (ReferenceEquals(_dialogReads, reads)) _dialogReads = null; if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    /// <summary>Saves inside the modal; a conflict or unknown result locks Save until an explicit canonical reopen.</summary>
    private async Task EditAsync(string title, StackPanel fields, Func<MonitorAuthorityLease, Task> save, string? saveLabel = null, Func<Task>? reload = null, bool mutates = true)
    {
        if (_dialog != null || _lifetime == null) return;
        if (_pendingWriteReview != null) { Notice("Reload and review the previous change before making another change."); return; }
        using var action = _lifetime.CaptureAction(); var pageOwner = Owner;
        var errorText = U.Text("", 13); errorText.Visibility = Visibility.Collapsed;
        var fieldHost = new ContentControl { Content = fields, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var editor = Stack(fieldHost, errorText); var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title,
            Content = new ScrollViewer { Content = editor, MaxHeight = Math.Max(240, Math.Min(620, XamlRoot.Size.Height - 230)), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = saveLabel ?? S("common.save"), CloseButtonText = S("common.cancel"), DefaultButton = ContentDialogButton.None };
        var sending = false; var completed = false; var reopen = false;
        var reloadButton = Button("Reload and review current values", "AdminRequestEditorReload", () => { reopen = true; dialog.Hide(); return Task.CompletedTask; });
        reloadButton.Visibility = Visibility.Collapsed; editor.Children.Add(reloadButton);
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; if (sending || _busy || !action.IsCurrent || !Current(pageOwner) || mutates && _pendingWriteReview != null) return;
            var deferral = args.GetDeferral(); sending = true; fieldHost.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
            if (mutates) { _busy = true; SetWriteReview(ReconcileCurrentAsync); }
            try { await save(action); action.ThrowIfRetired(); if (Current(pageOwner)) { completed = true; if (mutates) Notice(""); dialog.Hide(); } }
            catch (Exception error)
            {
                if (action.IsCurrent && Current(pageOwner))
                {
                    errorText.Text = AdminRequestRules.Error(error, true); errorText.Visibility = Visibility.Visible;
                    if (error is ApiException { StatusCode: 422 } validation) MarkValidationErrors(fields, validation);
                    if (error is ArgumentException || error is ApiException { StatusCode: 422 }) { if (mutates) SetWriteReview(null); fieldHost.IsEnabled = true; dialog.IsPrimaryButtonEnabled = true; }
                    else reloadButton.Visibility = reload == null ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            finally { sending = false; if (mutates && Current(pageOwner)) { _busy = false; _writeReview.IsEnabled = true; } deferral.Complete(); }
        };
        dialog.Closing += (_, args) => { if (sending && !completed && action.IsCurrent) args.Cancel = true; };
        _dialog = dialog;
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(action.Token); _dialogReads = reads;
        using var cancellation = action.Token.Register(() => DispatcherQueue.TryEnqueue(() => { if (ReferenceEquals(_dialog, dialog) && dialog.IsLoaded) dialog.Hide(); }));
        try { var task = ShowAsync(dialog); _dialogClosed = task; await task; }
        finally { reads.Cancel(); if (ReferenceEquals(_dialogReads, reads)) _dialogReads = null; if (ReferenceEquals(_dialog, dialog)) _dialog = null; foreach (var secret in _secrets) secret.Password = ""; _secrets.Clear(); }
        if (mutates && Current(pageOwner) && _pendingWriteReview is { } review)
        {
            _busy = true; _writeReview.IsEnabled = false;
            try { await ReconcileWriteAsync(pageOwner!, review); }
            finally { if (Current(pageOwner)) { _busy = false; _writeReview.IsEnabled = true; } }
        }
        if (reopen && Current(pageOwner) && _pendingWriteReview == null && reload != null) await reload();
    }

    private static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog) => await dialog.ShowAsync();

    private static void MarkValidationErrors(FrameworkElement content, ApiException error)
    {
        foreach (var control in Descendants(content).OfType<Control>())
        {
            if (control.Tag is not string key || !error.FieldErrors.Any(field => field.Location == "body." + key || field.Location?.EndsWith("." + key, StringComparison.Ordinal) == true)) continue;
            control.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
            AutomationProperties.SetHelpText(control, "The server rejected this field. Review its value.");
            ToolTipService.SetToolTip(control, "The server rejected this field. Review its value.");
        }
    }

    /// <summary>True only when the operation itself completed. Inline controls redraw from the canonical document otherwise.</summary>
    private async Task<bool> WriteAsync(Func<MonitorAuthorityLease, Task> operation, Func<Task>? reconcile = null)
    {
        if (_busy || _lifetime == null) return false;
        if (_pendingWriteReview != null) { Notice("Reload and review the previous change before making another change."); return false; }
        var pageOwner = Owner; var written = false;
        try
        {
            using var action = _lifetime.CaptureAction(); _busy = true; SetWriteReview(reconcile ?? ReconcileCurrentAsync);
            await operation(action); action.ThrowIfRetired(); written = true; if (Current(pageOwner)) Notice("");
        }
        catch (OperationCanceledException) when (!Current(pageOwner)) { }
        catch (Exception error)
        {
            if (Current(pageOwner))
            {
                Notice(AdminRequestRules.Error(error, true));
                if (error is ArgumentException or ApiException { StatusCode: 422 }) SetWriteReview(null);
            }
        }
        finally
        {
            if (Current(pageOwner))
            {
                if (_pendingWriteReview is { } review) await ReconcileWriteAsync(pageOwner!, review);
                if (Current(pageOwner)) { _busy = false; _writeReview.IsEnabled = true; }
            }
        }
        return written;
    }
    private void SetWriteReview(Func<Task>? review)
    {
        _pendingWriteReview = review; _writeReview.Visibility = review == null ? Visibility.Collapsed : Visibility.Visible;
        _writeReview.IsEnabled = !_busy;
    }
    private async Task ReconcileWriteAsync(MonitorAuthorityLease owner, Func<Task> review)
    {
        var generation = _generation;
        try
        {
            await review();
            if (Current(owner) && generation == _generation && ReferenceEquals(_pendingWriteReview, review)) SetWriteReview(null);
        }
        catch (Exception error) { if (Current(owner)) Notice(AdminRequestRules.Error(error) + " Reload and review before making another change."); }
    }
    private async Task ReconcileCurrentAsync()
    {
        if (_request.Route == AdminRoute.RequestSettings) await RefreshConfigurationAsync();
        else if (_request.Route == AdminRoute.RequestQueue) await RefreshQueueAsync(true);
        else if (Owner is { } owner) await BuildDiscoverAsync(owner, _generation);
    }
    public async Task<bool> CanLeaveAsync()
    {
        if (!RequiresLeaveConfirmation) return true;
        if (_dialog != null) return false;
        var owner = Owner;
        if (!await ConfirmAsync("Discard unsaved changes?", "Your changes have not been saved.", "Discard")) return !Current(owner);
        _dirty = false; return true;
    }
}
