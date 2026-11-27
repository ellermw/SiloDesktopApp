using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
    private long _pendingRender;
    private AdminRouteRequest _location = new(AdminRoute.Users);
    private AdminUserReview? _review;
    private long _generation;
    private long _loadRevision;
    private bool _loading;
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
    private string _groupFilter = "";
    private string _sort = "username";
    private bool _descending;
    private string _historyProfile = "";
    private string _historyDays = "30";
    private string _downloadDevice = "";
    private string _downloadProfile = "";
    private string _downloadStatus = "";
    private AdminUserSettingIdentity? _settingIdentity;

    private sealed record UserPageRows(string Path, int Limit, bool Unpaged, List<JsonObject> Items, string? Next, HashSet<string> Seen, bool Complete, List<AdminUsersReadback> Baselines);
    private sealed record FreshRead(string Key, int Limit, long At)
    {
        public bool Matches(string key, int limit) => Key == key && Limit == limit && Environment.TickCount64 - At < 30_000;
    }
    private sealed class UsersData
    {
        public readonly Dictionary<string, JsonObject> Documents = new(StringComparer.Ordinal);
        public readonly Dictionary<string, UserPageRows> Rows = new(StringComparer.Ordinal);
        public readonly Dictionary<string, FreshRead> Fresh = new(StringComparer.Ordinal);
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
            DispatcherQueue.TryEnqueue(() => { if (!ReferenceEquals(_workspace, workspace) || workspace.Generation != retired) return; Clear(); _notice.Text = "Admin authority changed. Reopen Users to continue."; });
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
    public void SuspendAdminPage() { _activePage = false; _workspace.Suspend(); Clear(); }
    private void Clear()
    {
        CancelPageActions();
        _listData = new(); _detailData = new(); _data = new(); _rows = _data.Rows; _detailAccount = null; _dataEpoch = null;
        _listIdentity = null; _listOffset = 0; _pendingRender = 0; _displayPage = 0;
        _loadErrors.Clear(); _review = null; _body.Children.Clear(); _body.Children.Add(_notice);
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
        var previousAccount = _location.Segment(0);
        var account = request.Segment(0);
        var changedTab = previousAccount != account || (_location.Get("tab") ?? "users") != (request.Get("tab") ?? "users");
        CancelPageActions();
        _loading = true; _actionBusy = false; _pendingRender = 0;
        var generation = _generation;
        var activated = false;
        try
        {
            generation = _workspace.Activate(preserveReview: !force); _generation = generation; activated = true;
            var epoch = _workspace.EpochOf(generation);
            if (_dataEpoch != epoch) Clear();
            _dataEpoch = epoch;
            if (previousAccount == null && _location.Get("tab") is not ("invitations" or "invite-codes"))
                _listOffset = scroller.VerticalOffset;
            if (account != null && _detailAccount != account)
            {
                _detailData = new(); _detailAccount = account;
                _historyProfile = ""; _historyDays = "30";
                _downloadDevice = ""; _downloadProfile = ""; _downloadStatus = ""; _settingIdentity = null;
            }
            if (account == null && request.Get("tab") is not ("invitations" or "invite-codes") && _listIdentity != request.Get("identity"))
            {
                _listIdentity = request.Get("identity");
                _listData.Rows.Remove("users"); _listData.Fresh.Remove("users"); _listOffset = 0;
            }
            _data = account == null ? _listData : _detailData;
            _rows = _data.Rows; _review = _data.Review; _location = request;
            _loadErrors.Clear();
            if (force)
            {
                _listData.Fresh.Clear(); _listData.ReviewedAt = null;
                _detailData.Fresh.Clear(); _detailData.ReviewedAt = null;
            }
            _notice.Text = _review == null ? "Loading Users…" : "";
            var offset = changedTab ? account == null && request.Get("tab") is not ("invitations" or "invite-codes") ? _listOffset : 0
                : scroller.VerticalOffset;
            RenderAt(offset);
            if (_review == null || _data.ReviewedAt is not long readAt || Environment.TickCount64 - readAt >= 30_000)
            {
                var review = await _workspace.ReadAsync(generation, (lease, token) => _api.ReviewAsync(lease, account, token));
                if (!_workspace.IsCurrent(generation)) return;
                _data.Review = _review = review;
                _data.ReviewedAt = Environment.TickCount64;
                _notice.Text = "";
                QueueReadRender(generation);
            }
            if (account == null) await ReadListAsync(generation);
            else await ReadDetailAsync(generation);
            if (!_workspace.IsCurrent(generation)) return;
            _notice.Text = ""; _loading = false; _pendingRender = 0;
            RenderAt();
        }
        catch (Exception error)
        {
            if (_activePage && revision == _loadRevision && (!activated || _workspace.IsCurrent(generation)))
            {
                _loading = false;
                _pendingRender = 0;
                if (!activated) Clear();
                ShowError(error is OperationCanceledException ? new TimeoutException("Users could not be loaded. Reload to try again.", error) : error, allowInactiveWorkspace: !activated);
            }
        }
    }
    private async Task DocumentAsync(string key, string path, long generation)
    {
        if (_data.Fresh.TryGetValue(key, out var read) && read.Matches(path, 0)) return;
        try
        {
            var result = await _workspace.ReadAsync(generation, (lease, token) => _api.ReadAsync(lease, path, token));
            if (!_workspace.IsCurrent(generation)) return;
            _data.Fresh[key] = new(path, 0, Environment.TickCount64);
            if (JsonNode.DeepEquals(Document(key), result.Body)) return;
            _documents[key] = result.Body!;
        }
        catch (Exception) when (_workspace.IsCurrent(generation)) { _loadErrors[key] = "Read failed or timed out. Reload this tab; unsupported and failed are not empty."; }
        QueueReadRender(generation);
    }
    private async Task RowsAsync(string key, string path, long generation, bool unpaged = false, int limit = 100, string? filter = null)
    {
        var readKey = filter ?? path;
        if (_data.Fresh.TryGetValue(key, out var read) && read.Matches(readKey, limit)) return;
        try
        {
            var page = await _workspace.ReadAsync(generation, (lease, token) => _api.PageAsync(lease, path, limit, unpaged: unpaged, ct: token));
            if (!_workspace.IsCurrent(generation)) return;
            _data.Fresh[key] = new(readKey, limit, Environment.TickCount64);
            if (_rows.GetValueOrDefault(key) is { } previous && previous.Path == path && previous.Limit == limit &&
                JsonNode.DeepEquals(previous.Baselines[0].Body, page.Envelope)) return;
            _rows[key] = new(path, limit, unpaged, page.Items.ToList(), page.NextCursor, new(StringComparer.Ordinal), page.NextCursor == null,
                [new(unpaged ? path : AdminPaging.WithPage(path, limit, null), page.Envelope.Copy())]);
        }
        catch (Exception) when (_workspace.IsCurrent(generation)) { _loadErrors[key] = "Read failed or timed out. No empty-state or write-ready snapshot was inferred."; }
        QueueReadRender(generation);
    }
    private void QueueReadRender(long generation)
    {
        if (!_loading || _pendingRender == generation || !_workspace.IsCurrent(generation)) return;
        _pendingRender = generation;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_pendingRender != generation || !_workspace.IsCurrent(generation)) return;
            _pendingRender = 0;
            RenderAt();
        });
    }
    private void RenderAt(double? offset = null)
    {
        var scroller = (ScrollViewer)Content;
        var y = offset ?? scroller.VerticalOffset;
        Render();
        if (Math.Abs(y - scroller.VerticalOffset) < 0.5) return;
        scroller.UpdateLayout();
        scroller.ChangeView(null, y, null, true);
    }
    private async Task MoreAsync(string key)
    {
        if (_loading || !_rows.TryGetValue(key, out var rows) || rows.Next == null || rows.Items.Count >= AdminUsersApi.MaxRows || rows.Seen.Count >= 9) return;
        var generation = _generation; _loading = true;
        try
        {
            var next = rows.Next;
            if (rows.Seen.Contains(next)) throw new InvalidDataException("Repeated collection continuation; reload.");
            var page = await _workspace.ReadAsync(generation, (lease, token) => _api.PageAsync(lease, rows.Path, rows.Limit, next, ct: token));
            if (!_workspace.IsCurrent(generation)) return;
            if (page.NextCursor != null && rows.Seen.Contains(page.NextCursor)) throw new InvalidDataException("Repeated collection continuation; reload.");
            if (rows.Items.Count + page.Items.Count > AdminUsersApi.MaxRows) throw new InvalidDataException("The retained-row bound was reached; narrow the filter.");
            var baseline = new AdminUsersReadback(AdminPaging.WithPage(rows.Path, rows.Limit, next), page.Envelope.Copy());
            rows.Items.AddRange(page.Items);
            rows.Baselines.Add(baseline);
            rows.Seen.Add(next);
            _rows[key] = rows with { Next = page.NextCursor, Complete = page.NextCursor == null };
            Render();
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
    private void Render()
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
        var query = new Dictionary<string, string> { ["tab"] = tab };
        if (level != null) query["level"] = level;
        if (id == null && tab == "users" && _listIdentity != null) query["identity"] = _listIdentity;
        var request = new AdminRouteRequest(AdminRoute.Users, query) { Segments = id == null ? [] : [id] };
        AdminShellPage.TryReplace(this, request);
        _ = LoadLocationAsync(request, force);
    }
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
        if (_review == null || !_body.Children.OfType<Button>().Any())
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
        bool Current() => revision == _loadRevision && (recovery ? _activePage : _workspace.IsCurrent(generation));
        return async () =>
        {
            if (!Current() || _loading || _actionBusy && !nested) return;
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
    private Border Failure(string message, Button? retry) => _loading
        ? AdminUsersUi.Failure("Loading Users…", null) : AdminUsersUi.Failure(message, retry);
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
