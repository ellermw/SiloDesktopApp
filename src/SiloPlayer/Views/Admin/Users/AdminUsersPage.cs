using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage : Page, IAdminNavigationGuard, IAdminPageSuspension, IAdminGutterScrollPage
{
    private readonly AdminUsersApi _api;
    private AdminUsersWorkspace _workspace;
    private bool _workspaceDisposed;
    private bool _activePage;
    private readonly StackPanel _body = new() { Spacing = 24, Padding = new Thickness(0, 0, 0, 24), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _notice = AdminUsersUi.Text("", 14);
    /// <summary>The shell pads the scrolling content with its gutters and max width on this element, as it does for the other Admin pages.</summary>
    FrameworkElement IAdminGutterScrollPage.ScrollContent => _body;
    private UsersData _listData = new();
    private UsersData _detailData = new();
    private UsersData _data = new();
    private Dictionary<string, JsonObject> _documents => _data.Documents;
    private Dictionary<string, UserPageRows> _rows = new(StringComparer.Ordinal);
    private string? _detailAccount;
    private long? _dataEpoch;
    private string? _listIdentity;
    private double _listOffset;
    private bool _backToUsers;
    private long _pendingRender;
    private long _failedRender;
    private long _reconcileGeneration;
    private double? _initialOffset;
    private AdminRouteRequest _location = new(AdminRoute.Users);
    private AdminUserReview? _review;
    private long _generation;
    private long _loadRevision;
    private long _actionRevision;
    private bool _loading;
    private bool _loadingLocation;
    private bool _actionBusy;
    /// <summary>WebUI <c>useLibrariesTableLayout</c>: tables from 1024 px (Tailwind lg), compact rows below; phone is below 640 px.</summary>
    private bool _wide;
    private bool _phone;
    /// <summary>Table cells are tighter (<c>px-2</c>) while the page is narrower than 1200 px.</summary>
    private bool _tight;
    private readonly Dictionary<string, string> _loadErrors = new(StringComparer.Ordinal);
    private bool _dialogDirty;
    private ContentDialog? _dialog;
    private int _pageSize = 25;
    private int _displayPage;
    private string _search = "";
    private string _exactDraft = "";
    private readonly Dictionary<string, Control> _filters = new();
    private string _groupFilter = "";
    private string _sort = "username";
    private bool _descending;
    private string _historyProfile = "";
    private string _historyDays = "30";
    private string _historyProfileDraft = "";
    private string _historyDaysDraft = "30";
    private string _downloadDevice = "";
    private string _downloadProfile = "";
    private string _downloadStatus = "";
    private AdminUserSettingIdentity? _settingIdentity;

    private sealed record UserPageRows(string Path, int Limit, bool Unpaged, List<JsonObject> Items, string? Next, HashSet<string> Seen, bool Complete, List<AdminUsersReadback> Baselines);
    private sealed record ReadScope(string Query, int Limit = 0, bool Unpaged = false);
    private sealed class UsersData
    {
        public readonly Dictionary<string, JsonObject> Documents = new(StringComparer.Ordinal);
        public readonly Dictionary<string, UserPageRows> Rows = new(StringComparer.Ordinal);
        public readonly Dictionary<string, ReadScope> Scopes = new(StringComparer.Ordinal);
        public readonly Dictionary<string, long> Fresh = new(StringComparer.Ordinal);
        public AdminUserReview? Review;
        public long? ReviewedAt;
    }

    public AdminUsersPage()
    {
        InitializeComponent();
        _api = App.Services.GetRequiredService<AdminUsersApi>();
        _workspace = CreateWorkspace();
        _notice.Visibility = Visibility.Collapsed;
        _notice.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => _notice.Visibility = string.IsNullOrEmpty(_notice.Text) ? Visibility.Collapsed : Visibility.Visible);
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Unloaded += (_, _) => { SuspendAdminPage(); _workspace.Dispose(); _workspaceDisposed = true; };
        SizeChanged += (_, _) => ApplyWidth();
        Loaded += (_, _) => ApplyWidth();
    }
    private AdminUsersWorkspace CreateWorkspace()
    {
        var workspace = new AdminUsersWorkspace(App.Services.GetRequiredService<AdminMonitorAuthority>());
        workspace.Retired += () =>
        {
            var retired = workspace.Generation;
            DispatcherQueue.TryEnqueue(() => { if (!ReferenceEquals(_workspace, workspace) || workspace.Generation != retired) return; _loading = _loadingLocation = false; Clear(); _notice.Text = "Admin authority changed. Reopen Users to continue."; });
        };
        return workspace;
    }
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args); _activePage = true;
        _ = LoadLocationAsync(args.Parameter as AdminRouteRequest ?? new(AdminRoute.Users), force: false);
    }
    protected override void OnNavigatedFrom(NavigationEventArgs args) { SuspendAdminPage(); base.OnNavigatedFrom(args); }
    public bool RequiresLeaveConfirmation => _dialogDirty && _workspace.IsCurrent(_generation);
    public Task<bool> CanLeaveAsync() => Task.FromResult(!RequiresLeaveConfirmation);
    public void SuspendAdminPage() { _activePage = false; _backToUsers = false; _workspace.Suspend(); Clear(); }
    private void Clear()
    {
        RetirePageActions();
        _listData = new(); _detailData = new(); _data = new(); _rows = _data.Rows; _detailAccount = null; _dataEpoch = null;
        _listIdentity = null; _exactDraft = ""; _listOffset = 0; _pendingRender = 0; _displayPage = 0;
        _filters.Clear(); _usersResults = null; _settingIdentity = null;
        _loadErrors.Clear(); _review = null; _body.Children.Clear(); _body.Children.Add(_notice);
    }
    private void RetirePageActions()
    {
        ++_actionRevision;
        _actionBusy = false;
        CancelPageActions();
    }
    private void CancelPageActions()
    {
        _dialog?.Hide(); _dialog = null; _dialogDirty = false;
        _previewRead?.Cancel(); _previewRead?.Dispose(); _previewRead = null;
        _preview?.Dispose(); _preview = null;
    }
    private Task LoadAsync(AdminRouteRequest request) => LoadLocationAsync(request, force: true);

    private async Task LoadLocationAsync(AdminRouteRequest request, bool force)
    {
        if (!_activePage) return;
        if (_workspaceDisposed) { _workspace = CreateWorkspace(); _workspaceDisposed = false; }
        var revision = ++_loadRevision;
        var scroller = (ScrollViewer)Content;
        var previous = _location;
        var previousAccount = previous.Segment(0);
        var account = request.Segment(0);
        var changedTab = previousAccount != account || (previous.Get("tab") ?? "users") != (request.Get("tab") ?? "users");
        _location = request; // Recovery must retry the intended route even if activation fails.
        CancelPageActions();
        _loading = _loadingLocation = true; _actionBusy = false; _pendingRender = 0;
        var generation = _generation;
        var activated = false;
        try
        {
            generation = _workspace.Activate(preserveReview: true); _generation = generation; activated = true;
            _reconcileGeneration = force && _workspace.NeedsReloadReview ? generation : 0;
            var epoch = _workspace.EpochOf(generation);
            if (_dataEpoch != epoch) Clear();
            else if (previousAccount == null && previous.Get("tab") is not ("invitations" or "invite-codes"))
                _listOffset = scroller.VerticalOffset;
            _dataEpoch = epoch;
            if (account != null && _detailAccount != account)
            {
                _detailData = new(); _detailAccount = account;
                _historyProfileDraft = _historyProfile = ""; _historyDaysDraft = _historyDays = "30";
                _downloadDevice = ""; _downloadProfile = ""; _downloadStatus = ""; _settingIdentity = null;
            }
            if (account == null && request.Get("tab") is not ("invitations" or "invite-codes") && _listIdentity != request.Get("identity"))
            {
                _listIdentity = request.Get("identity");
                _exactDraft = _listIdentity ?? ""; _listOffset = 0; _displayPage = 0;
            }
            _data = account == null ? _listData : _detailData;
            _rows = _data.Rows; _review = _data.Review;
            if (account == null) _backToUsers = false;
            _loadErrors.Clear();
            if (force)
            {
                _listData.Fresh.Clear(); _listData.ReviewedAt = null;
                _detailData.Fresh.Clear(); _detailData.ReviewedAt = null;
            }
            _notice.Text = _review == null ? "Loading Users…" : "";
            _initialOffset = changedTab ? account == null && request.Get("tab") is not ("invitations" or "invite-codes") ? _listOffset : 0
                : scroller.VerticalOffset;
            if (!force) { AlignVisibleQueries(); RenderAt(); }
            else if (!request.Equals(previous)) { _body.Children.Clear(); _body.Children.Add(_notice); }
            if (!CanPublish(generation)) return;
            if (_review == null || _data.ReviewedAt is not long readAt || Environment.TickCount64 - readAt >= 30_000)
            {
                AdminUserReview review;
                try { review = await _workspace.ReadAsync(generation, (lease, token) => _api.ReviewAsync(lease, account, token)); }
                catch (Exception error) when (CanPublish(generation))
                {
                    _data.ReviewedAt = null;
                    if (_reconcileGeneration == generation && account != null && _workspace.Reconciliation is { Operation: AdminUsersOperation.DeleteAccount } &&
                        error is ApiException { StatusCode: 404 } && AdminUsersApi.RefusedReadPath(error) == AdminUsersApi.UserPath(account))
                        _workspace.AcceptReloadReadback(generation, account, null);
                    if (!AdminUsersApi.CanRetainAfterReadFailure(error)) InvalidateReview();
                    throw;
                }
                if (!CanPublish(generation)) return;
                if (!AdminUsersRules.Available(review.Capabilities))
                {
                    InvalidateGeneralCapability();
                    if (account != null) throw new InvalidOperationException("Admin Users capability is unavailable. Reload to review current access.");
                }
                _data.Review = _review = review;
                _data.ReviewedAt = Environment.TickCount64;
                AlignVisibleQueries();
                if (AdminUsersRules.Available(review.Capabilities)) AcceptReloadReadback(generation, null);
                _notice.Text = "";
                QueueReadRender(generation);
            }
            if (account == null) await ReadListAsync(generation);
            else await ReadDetailAsync(generation);
            if (!CanPublish(generation)) return;
            _notice.Text = ""; _loading = _loadingLocation = false; _pendingRender = 0;
            RenderAt();
        }
        catch (Exception error)
        {
            if (_activePage && revision == _loadRevision && (!activated || CanPublish(generation)))
            {
                _loading = _loadingLocation = false;
                _pendingRender = 0;
                if (!activated) Clear();
                else RenderAt();
                ShowError(error is OperationCanceledException ? new TimeoutException("Users could not be loaded. Reload to try again.", error) : error, allowInactiveWorkspace: !activated);
            }
        }
    }
    private bool CanPublish(long generation) => _failedRender != generation && _workspace.IsCurrent(generation);
    private void AcceptReloadReadback(long generation, string? path, bool complete = true)
    {
        if (_reconcileGeneration == generation && _workspace.NeedsReloadReview)
            _workspace.AcceptReloadReadback(generation, _location.Segment(0), path, complete);
    }
    private Border ReconciliationAlert(AdminUsersReconciliation pending, bool affected, bool offerReload = false)
    {
        var operation = pending.Operation == AdminUsersOperation.ViewAs ? "View as" : "password reset";
        var message = !pending.CanReadBack
            ? $"The {operation} outcome is unknown. Verify it separately before using {operation} again for this account."
            : pending.Operation == AdminUsersOperation.CreateUser && UsersPath().Contains('?')
                ? "Clear the exact-identity filter and reload Users, or verify the change separately."
                : pending.Collection
                    ? "Reload the affected collection to review it. If you can't review the full collection here, verify the change separately."
                    : "Writes are blocked. Reload the affected account or tab to review the current server state.";
        var panel = Panel(8);
        panel.Children.Add(Text(message));
        if (offerReload) panel.Children.Add(Action("Reload / review current server state", () => LoadAsync(_location)));
        if (affected && (pending.Collection || !pending.CanReadBack))
            panel.Children.Add(Action("I verified the outcome separately…", () => ConfirmSeparateVerificationAsync(pending)));
        return AdminUsersUi.AlertBox(panel);
    }

    private async Task ConfirmSeparateVerificationAsync(AdminUsersReconciliation pending)
    {
        if (_dialog != null || XamlRoot == null) return;
        var generation = _generation;
        var scope = pending.Collection ? $"the change to {pending.Path}"
            : pending.Operation == AdminUsersOperation.ViewAs ? "the temporary sign-in for this account" : "the password reset for this account";
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Confirm separate verification",
            Content = Text($"Confirm only if you verified the outcome of {scope} outside this page. This clears the review requirement without retrying the previous request."),
            PrimaryButtonText = "Confirm verification", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        _dialog = dialog;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || !CanPublish(generation)) return;
            _workspace.AcceptVerifiedOutcome(generation, pending);
            _notice.Text = "";
            RenderAt();
        }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    private void InvalidateReview() => Clear(); // Viewer, target and general capability authorize both caches.
    private void InvalidateGeneralCapability()
    {
        RetirePageActions();
        EvictRead(_listData, "users"); EvictRead(_listData, "groups");
        _listData.Review = null; _listData.ReviewedAt = null;
        _detailData = new(); _settingIdentity = null;
        _data = _location.Segment(0) == null ? _listData : _detailData;
        _rows = _data.Rows; _review = null;
        _filters.Clear(); _usersResults = null;
        _body.Children.Clear(); _body.Children.Add(_notice);
    }
    private void AlignVisibleQueries()
    {
        if (_location.Segment(0) is not { } account)
        {
            if (_location.Get("tab") is not ("invitations" or "invite-codes")) AlignRead("users", new(UsersPath(), _pageSize));
            return;
        }
        AlignRead("ips", new(AdminUsersApi.UserPath(account) + "/ips?days=" + _historyDays, 100));
        AlignRead("history", new(HistoryQuery, 100));
        AlignRead("overview-history", new(HistoryQuery, 25));
        if (GroupQuotaPath() is { } quota) AlignRead("group-quota", new(quota));
        else EvictRead(_data, "group-quota");
    }
    private void AlignRead(string key, ReadScope scope)
    {
        if (_data.Scopes.GetValueOrDefault(key) != scope) EvictRead(_data, key);
    }
    private bool IsFresh(string key) => _data.Fresh.TryGetValue(key, out var at) && Environment.TickCount64 - at < 30_000;

    private async Task DocumentAsync(string key, string path, long generation)
    {
        if (!CanPublish(generation)) return;
        var scope = new ReadScope(path);
        AlignRead(key, scope);
        if (IsFresh(key)) return;
        _loadErrors.Remove(key);
        try
        {
            var result = await _workspace.ReadAsync(generation, (lease, token) => _api.ReadAsync(lease, path, token));
            if (!CanPublish(generation)) return;
            _data.Fresh[key] = Environment.TickCount64;
            _data.Scopes[key] = scope;
            var changed = !JsonNode.DeepEquals(Document(key), result.Body);
            _documents[key] = result.Body!;
            AcceptReloadReadback(generation, path);
            if (EvictUnavailableDependents(key, result.Body)) { RetirePageActions(); RenderAt(); }
            else if (changed) QueueReadRender(generation);
        }
        catch (Exception error) when (CanPublish(generation)) { FailedRead(key, error); }
    }
    private async Task RowsAsync(string key, string path, long generation, bool unpaged = false, int limit = 100, string? filter = null)
    {
        if (!CanPublish(generation)) return;
        // History's relative window has a stable filter, but pagination must reuse the original time cutoff.
        var scope = new ReadScope(filter ?? path, limit, unpaged);
        AlignRead(key, scope);
        if (IsFresh(key)) return;
        _loadErrors.Remove(key);
        try
        {
            var page = await _workspace.ReadAsync(generation, (lease, token) => _api.PageAsync(lease, path, limit, unpaged: unpaged, ct: token));
            if (!CanPublish(generation)) return;
            var complete = page.NextCursor == null;
            _data.Fresh[key] = Environment.TickCount64;
            _data.Scopes[key] = scope;
            // Equal page one does not revalidate any continuation pages.
            var changed = _rows.GetValueOrDefault(key) is not { Baselines.Count: 1 } previous || previous.Path != path ||
                !JsonNode.DeepEquals(previous.Baselines[0].Body, page.Envelope);
            if (changed)
                _rows[key] = new(path, limit, unpaged, page.Items.ToList(), page.NextCursor, new(StringComparer.Ordinal), complete,
                    [new(unpaged ? path : AdminPaging.WithPage(path, limit, null), page.Envelope.Copy())]);
            if (key != "home-overrides") AcceptReloadReadback(generation, path, complete);
            if (changed) QueueReadRender(generation);
        }
        catch (Exception error) when (CanPublish(generation)) { FailedRead(key, error); }
    }
    private void FailedRead(string key, Exception error)
    {
        _loadErrors[key] = key == "library-choices" ? "Current library choices could not be loaded. Reload before editing." :
            "Read failed or timed out. Reload this tab to try again.";
        _data.Fresh.Remove(key);
        if (AdminUsersApi.CanRetainAfterReadFailure(error)) { QueueReadRender(_generation); return; }
        EvictRead(_data, key);
        EvictUnavailableDependents(key, null);
        RetirePageActions();
        RenderAt();
    }
    private static bool EvictRead(UsersData data, string key)
    {
        data.Fresh.Remove(key);
        data.Scopes.Remove(key);
        return data.Rows.Remove(key) | data.Documents.Remove(key);
    }
    private bool EvictUnavailableDependents(string key, JsonObject? capability) => key switch
    {
        "codes-capabilities" when !AdminUsersRules.Available(capability) => EvictRead(_data, "codes"),
        "invitations-capabilities" when !AdminUsersRules.Available(capability) => EvictRead(_data, "invitations"),
        "external-capabilities" when !AdminUsersRules.Feature(capability, "admin_identities") => EvictRead(_data, "identities") | EvictRead(_data, "providers"),
        "sessions-capabilities" when !AdminUsersRules.Feature(capability, "admin_management") => EvictRead(_data, "login-sessions"),
        "settings-capabilities" when !AdminUsersRules.Available(capability) => EvictRead(_data, "settings") | EvictRead(_data, "manifest") |
            EvictRead(_data, "home-settings") | EvictRead(_data, "home-overrides"),
        _ => false,
    };

    private bool HandlePreflightRefusal(long generation, Exception error)
    {
        if (!CanPublish(generation) || AdminUsersApi.RefusedReadPath(error) is not { } path) return false;
        var resource = path.Split('?')[0];
        if (resource == "/api/v2/admin/users/capabilities" || _review != null &&
            (resource == AdminUsersApi.UserPath(_review.Viewer.Text("id")) || resource == AdminUsersApi.UserPath(_review.User.Text("id"))))
        {
            InvalidateReview();
        }
        else
        {
            var key = resource switch
            {
                "/api/v2/admin/invite-codes/capabilities" => "codes-capabilities",
                "/api/v2/admin/invitations/capabilities" => "invitations-capabilities",
                "/api/v2/auth/external-sign-in/capabilities" => "external-capabilities",
                "/api/v2/auth/sessions/capabilities" => "sessions-capabilities",
                "/api/v2/settings/contract/capabilities" => "settings-capabilities",
                _ => null,
            };
            var keys = key != null ? new[] { key } : _rows.Where(entry => IsAtOrBelow(resource, entry.Value.Path.Split('?')[0])).Select(entry => entry.Key)
                .Concat(_documents.Keys.Where(name => _data.Scopes.GetValueOrDefault(name)?.Query == path)).ToArray();
            if (keys.Length == 0) return false;
            foreach (var matched in keys)
            {
                EvictRead(_data, matched);
                EvictUnavailableDependents(matched, null);
                _loadErrors[matched] = "The current read was refused. Reload this tab to review access.";
            }
            RetirePageActions();
        }
        RenderAt(); ShowError(error);
        return true;

        static bool IsAtOrBelow(string path, string root) => path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
    }
    private void QueueReadRender(long generation)
    {
        if (!_loading || _pendingRender == generation || !CanPublish(generation)) return;
        _pendingRender = generation;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_pendingRender != generation || !CanPublish(generation)) return;
            _pendingRender = 0;
            RenderAt();
        });
    }
    private T KeepFilterFocus<T>(T control, string key) where T : Control
    {
        _filters[key] = control;
        return control;
    }
    private void RenderAt()
    {
        try
        {
            var scroller = (ScrollViewer)Content;
            var y = _initialOffset ?? scroller.VerticalOffset;
            var focused = XamlRoot == null ? null : FocusManager.GetFocusedElement(XamlRoot);
            var filter = _filters.FirstOrDefault(pair => ReferenceEquals(pair.Value, focused)).Key;
            var focusState = (focused as Control)?.FocusState ?? FocusState.Programmatic;
            var selectionStart = (focused as TextBox)?.SelectionStart ?? 0;
            var selectionLength = (focused as TextBox)?.SelectionLength ?? 0;
            _filters.Clear(); _usersResults = null;
            RenderCore();
            if (filter != null && _filters.TryGetValue(filter, out var replacement))
            {
                scroller.UpdateLayout();
                replacement.Focus(focusState == FocusState.Unfocused ? FocusState.Programmatic : focusState);
                if (replacement is TextBox text)
                {
                    var start = Math.Min(selectionStart, text.Text.Length);
                    text.Select(start, Math.Min(selectionLength, text.Text.Length - start));
                }
            }
            if (_initialOffset != null || Math.Abs(y - scroller.VerticalOffset) >= 0.5)
            {
                scroller.UpdateLayout();
                scroller.ChangeView(null, y, null, true);
                if (!_loading || scroller.ScrollableHeight + 0.5 >= y) _initialOffset = null;
            }
        }
        catch (Exception)
        {
            // Never reuse a snapshot that the renderer could not consume, including dispatcher paints.
            _failedRender = _generation;
            _loading = _loadingLocation = false;
            Clear();
            ShowError(new InvalidDataException("Admin Users could not display the server data. Reload Users to fetch it again."));
        }
    }
    private async Task MoreAsync(string key)
    {
        if (_loading || !_rows.TryGetValue(key, out var rows) || rows.Next == null || rows.Items.Count >= AdminUsersApi.MaxRows || rows.Seen.Count >= 9) return;
        var generation = _generation; _loading = true;
        try
        {
            var next = rows.Next;
            if (rows.Seen.Contains(next)) throw new InvalidDataException("Repeated collection continuation; reload.");
            AdminUsersCollectionPage page;
            try { page = await _workspace.ReadAsync(generation, (lease, token) => _api.PageAsync(lease, rows.Path, rows.Limit, next, ct: token)); }
            catch (Exception error) when (AdminUsersApi.RefusedReadPath(error) != null && CanPublish(generation))
            {
                _loading = false;
                FailedRead(key, error);
                return;
            }
            if (!CanPublish(generation)) return;
            if (page.NextCursor != null && rows.Seen.Contains(page.NextCursor)) throw new InvalidDataException("Repeated collection continuation; reload.");
            if (rows.Items.Count + page.Items.Count > AdminUsersApi.MaxRows) throw new InvalidDataException("The retained-row bound was reached; narrow the filter.");
            var baseline = new AdminUsersReadback(AdminPaging.WithPage(rows.Path, rows.Limit, next), page.Envelope.Copy());
            rows.Items.AddRange(page.Items);
            rows.Baselines.Add(baseline);
            rows.Seen.Add(next);
            _rows[key] = rows with { Next = page.NextCursor, Complete = page.NextCursor == null };
            // A continuation of a retained first page is not a reconciliation if its reload failed.
            if (_data.Fresh.ContainsKey(key)) AcceptReloadReadback(generation, rows.Path, page.NextCursor == null);
            _loading = false;
            _notice.Text = "";
            RenderAt();
        }
        catch (OperationCanceledException error) { if (_workspace.IsCurrent(generation)) ShowError(new TimeoutException("The read timed out or was cancelled. The retained page is unchanged; try loading the next page again.", error)); }
        catch (Exception error) { if (_workspace.IsCurrent(generation)) ShowError(error); }
        finally { if (_workspace.IsCurrent(generation)) _loading = false; }
    }
    private IReadOnlyList<JsonObject> Rows(string key) => _rows.TryGetValue(key, out var rows) ? rows.Items : [];
    private IReadOnlyList<AdminUsersReadback> Baselines(string key) => _rows.TryGetValue(key, out var state) ? state.Baselines.ToArray() : [];
    private JsonObject? Document(string key) => _documents.GetValueOrDefault(key);
    private bool Feature(string key) => AdminUsersRules.Feature(_review?.Capabilities, key);
    private bool Manage => _review != null && AdminUsersRules.CanManage(_review.User, _review.Viewer.Text("id"), _review.Viewer.Flag("is_owner"));
    private bool PasswordManage => Manage && (_review!.User.Text("id") != _review.Viewer.Text("id") || _review.User.Flag("password_login") || _review.Viewer.Flag("is_owner"));
    private bool Policy => _review != null && AdminUsersRules.CanChangePolicy(_review.User, _review.Viewer.Text("id"), _review.Viewer.Flag("is_owner"));
    private bool ProfilesManage => _review != null && Feature("profile_management") && AdminUsersRules.CanManageProfiles(_review.User, _review.Viewer.Text("id"), _review.Viewer.Flag("is_owner"));
    private bool UpdateWidth()
    {
        var width = XamlRoot?.Size.Width ?? ActualWidth;
        if (width <= 0) return false;
        var wide = width >= 1024; var phone = width < 640; var tight = width < 1200;
        if (wide == _wide && phone == _phone && tight == _tight) return false;
        _wide = wide; _phone = phone; _tight = tight;
        return true;
    }
    private void ApplyWidth()
    {
        if (UpdateWidth() && !_loading && _dialog == null && _review != null && _preview == null) Render();
    }
    private void Render() => RenderAt();
    private void RenderCore()
    {
        UpdateWidth();
        AdminUsersUi.Gutter = _phone ? 16 : 20;
        AdminUsersUi.CellPad = _tight ? 8 : 12;
        _body.Children.Clear(); _body.Children.Add(_notice);
        if (_review == null) return;
        if (_location.Segment(0) == null) RenderList(); else RenderDetail();
    }
    /// <summary>A failed read is shown as a red alert with one retry button; unsupported and failed are never shown as empty.</summary>
    private void AddLoadFailures()
    {
        if (_loadErrors.Count == 0) return;
        var messages = _loadErrors.Select(failure => failure.Key == "users" ? AdminUsersUi.T("web.admin.users.list.loadFailed") : failure.Value).Distinct().ToArray();
        var panel = Panel(8);
        foreach (var message in messages) panel.Children.Add(AdminUsersUi.Text(message, 14));
        panel.Children.Add(Action(AdminUsersUi.T("admin.native.users.reload"), () => LoadAsync(_location)));
        _body.Children.Add(AdminUsersUi.AlertBox(panel));
    }
    private void Navigate(string? id, string tab, string? level = null, bool force = false)
    {
        if (id != null) _ = AdminUsersApi.UserPath(id);
        var query = new Dictionary<string, string> { ["tab"] = tab };
        if (level != null) query["level"] = level;
        if (id == null && tab == "users" && _listIdentity != null) query["identity"] = _listIdentity;
        var request = new AdminRouteRequest(AdminRoute.Users, query) { Segments = id == null ? [] : [id] };
        if (!AdminShellPage.TryReplace(this, request)) return;
        if (id != null && _location.Segment(0) == null && _location.Get("tab") is not ("invitations" or "invite-codes"))
            _backToUsers = true;
        _ = LoadLocationAsync(request, force);
    }
    internal bool CanGoBackToUsers => _activePage && _backToUsers && _location.Segment(0) != null;
    internal void GoBackToUsers() { if (CanGoBackToUsers) Navigate(null, "users"); }
    private void ShowError(Exception error, bool allowInactiveWorkspace = false)
    {
        if (error is OperationCanceledException || !allowInactiveWorkspace && !_workspace.IsCurrent(_generation)) return;
        _notice.Text = error switch
        {
            AdminUsersReviewRequiredException or TimeoutException => error.Message,
            ApiException api => $"Server refused this operation ({api.StatusCode}; {api.ProblemType ?? api.ErrorCode ?? "request_failed"}). Reload/review; no mutation was replayed.",
            ArgumentException or InvalidOperationException or InvalidDataException => error.Message,
            _ => "The request failed or its outcome is unknown. Read back and review locally before making another change."
        };
        if (!_body.Children.Contains(_notice)) _body.Children.Insert(0, _notice);
        if (!_body.Children.OfType<Button>().Any())
            _body.Children.Add(Action("Reload Users", () => LoadAsync(_location), recovery: allowInactiveWorkspace));
    }
    private static TextBlock Text(string value, double size = 14, bool muted = false)
    {
        var block = AdminUsersUi.Text(value, size, muted);
        block.IsTextSelectionEnabled = true;
        return block;
    }
    private Button Action(string label, Func<Task> run, bool enabled = true, bool nested = false, bool recovery = false) =>
        Wire(AdminUsersUi.ActionButton(label), run, enabled, nested, recovery);
    /// <summary>Connects a button to the page's action guard: one action at a time, none while loading, none once the page or its authority has moved on.</summary>
    private Button Wire(Button button, Func<Task> run, bool enabled = true, bool nested = false, bool recovery = false)
    {
        button.IsEnabled = enabled;
        var guarded = Guarded(run, nested, recovery, value => button.IsEnabled = value, enabled);
        button.Click += async (_, _) => await guarded();
        return button;
    }
    private Func<Task> Guarded(Func<Task> run, bool nested = false, bool recovery = false, Action<bool>? enable = null, bool enabled = true)
    {
        var generation = _generation;
        var revision = _loadRevision;
        var actionRevision = _actionRevision;
        bool Current() => revision == _loadRevision && actionRevision == _actionRevision && (recovery ? _activePage : _workspace.IsCurrent(generation));
        return async () =>
        {
            if (!enabled || !Current() || _loading || _actionBusy && !nested) return;
            if (!nested) _actionBusy = true; enable?.Invoke(false);
            try { await run(); }
            catch (OperationCanceledException error)
            {
                if (Current()) ShowError(new TimeoutException("The request timed out or was cancelled. Read back/review before making another change.", error));
            }
            catch (Exception error) { if (Current()) ShowError(error, allowInactiveWorkspace: recovery); }
            finally { if (Current()) { if (!nested) _actionBusy = false; enable?.Invoke(enabled); } }
        };
    }
    private static StackPanel Panel(double spacing = 10) => new() { Spacing = spacing };
    private bool AwaitingRead(string key) => _loadingLocation && !_loadErrors.ContainsKey(key) && !_data.Scopes.ContainsKey(key);
    private Border Failure(string key, string message, Button? retry) => AwaitingRead(key)
        ? AdminUsersUi.Empty("Loading Users…") : AdminUsersUi.Failure(message, retry);
    private static LogsWrapPanel Inline() => new() { Spacing = 8, RowSpacing = 8 };
    private static Border Card(string title, Panel body)
    {
        var content = Panel(); content.Children.Add(Text(title, 20)); content.Children.Add(body);
        return new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush, Child = content };
    }
    private static string Label(string key) => string.Join(" ", key.Split('_').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
    private static ComboBox Choice(string label, IEnumerable<(string Id, string Name)> choices, string? selected = null)
    {
        var box = new ComboBox { Header = label, MinWidth = 180, MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in choices) box.Items.Add(new ComboBoxItem { Content = choice.Name, Tag = choice.Id });
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected) ?? box.Items.OfType<ComboBoxItem>().FirstOrDefault();
        AutomationProperties.SetName(box, label);
        return box;
    }
    private static string Selected(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
}
