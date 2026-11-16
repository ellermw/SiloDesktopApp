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

public sealed partial class AdminUsersPage : Page, IAdminNavigationGuard, IAdminPageSuspension
{
    private readonly AdminUsersApi _api;
    private AdminUsersWorkspace _workspace;
    private bool _workspaceDisposed;
    private bool _activePage;
    private readonly StackPanel _body = new() { Spacing = 16, Padding = new Thickness(20), MaxWidth = 1440, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _notice = Text("");
    private readonly Dictionary<string, JsonObject> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UserPageRows> _rows = new(StringComparer.Ordinal);
    private AdminRouteRequest _location = new(AdminRoute.Users);
    private AdminUserReview? _review;
    private long _generation;
    private long _loadRevision;
    private bool _loading;
    private bool _actionBusy;
    private bool _compact = true;
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

    public AdminUsersPage()
    {
        InitializeComponent();
        _api = App.Services.GetRequiredService<AdminUsersApi>();
        _workspace = CreateWorkspace();
        Content = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Unloaded += (_, _) => { SuspendAdminPage(); _workspace.Dispose(); _workspaceDisposed = true; };
        SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 1100;
            if (compact == _compact) return;
            _compact = compact;
            if (!_loading && _dialog == null && _review != null && _preview == null) Render();
        };
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
        _ = LoadAsync(args.Parameter as AdminRouteRequest ?? new(AdminRoute.Users));
    }
    protected override void OnNavigatedFrom(NavigationEventArgs args) { SuspendAdminPage(); base.OnNavigatedFrom(args); }
    public bool RequiresLeaveConfirmation => _dialogDirty && _workspace.IsCurrent(_generation);
    public Task<bool> CanLeaveAsync() => Task.FromResult(!RequiresLeaveConfirmation);
    public void SuspendAdminPage() { _activePage = false; _workspace.Suspend(); Clear(); }
    private void Clear()
    {
        _dialog?.Hide(); _dialog = null; _dialogDirty = false;
        _documents.Clear(); _rows.Clear(); _loadErrors.Clear(); _review = null; _body.Children.Clear(); _body.Children.Add(_notice);
        _previewRead?.Cancel(); _previewRead?.Dispose(); _previewRead = null;
        _preview?.Dispose(); _preview = null;
    }
    private async Task LoadAsync(AdminRouteRequest request)
    {
        if (!_activePage) return;
        if (_workspaceDisposed) { _workspace = CreateWorkspace(); _workspaceDisposed = false; }
        var revision = ++_loadRevision;
        var previousAccount = _location.Segment(0);
        _workspace.Suspend(); Clear(); _location = request;
        if (previousAccount != request.Segment(0))
        {
            _historyProfile = ""; _historyDays = "30";
            _downloadDevice = ""; _downloadProfile = ""; _downloadStatus = ""; _settingIdentity = null;
        }
        _loading = true; _actionBusy = false; _notice.Text = "Loading Users…";
        var generation = _generation;
        var activated = false;
        try
        {
            generation = _workspace.Activate(); _generation = generation; activated = true;
            _review = await _workspace.ReadAsync(generation, (lease, token) => _api.ReviewAsync(lease, request.Segment(0), token));
            if (!_workspace.IsCurrent(generation)) return;
            if (request.Segment(0) == null) await ReadListAsync(generation);
            else await ReadDetailAsync(generation);
            if (!_workspace.IsCurrent(generation)) return;
            _notice.Text = ""; _loading = false; Render();
        }
        catch (Exception error)
        {
            if (_activePage && revision == _loadRevision && (!activated || _workspace.IsCurrent(generation)))
            {
                _loading = false;
                ShowError(error is OperationCanceledException ? new TimeoutException("Users could not be loaded. Reload to try again.", error) : error, allowInactiveWorkspace: !activated);
            }
        }
    }
    private async Task DocumentAsync(string key, string path, long generation)
    {
        try
        {
            var result = await _workspace.ReadAsync(generation, (lease, token) => _api.ReadAsync(lease, path, token));
            if (_workspace.IsCurrent(generation)) _documents[key] = result.Body!;
        }
        catch (Exception) when (_workspace.IsCurrent(generation)) { _loadErrors[key] = "Read failed or timed out. Reload this tab; unsupported and failed are not empty."; }
    }
    private async Task RowsAsync(string key, string path, long generation, bool unpaged = false, int limit = 100)
    {
        try
        {
            var page = await _workspace.ReadAsync(generation, (lease, token) => _api.PageAsync(lease, path, limit, unpaged: unpaged, ct: token));
            if (!_workspace.IsCurrent(generation)) return;
            _rows[key] = new(path, limit, unpaged, page.Items.ToList(), page.NextCursor, new(StringComparer.Ordinal), page.NextCursor == null,
                [new(unpaged ? path : AdminPaging.WithPage(path, limit, null), page.Envelope.Copy())]);
        }
        catch (Exception) when (_workspace.IsCurrent(generation)) { _loadErrors[key] = "Read failed or timed out. No empty-state or write-ready snapshot was inferred."; }
    }
    private async Task MoreAsync(string key)
    {
        if (_loading || !_rows.TryGetValue(key, out var rows) || rows.Next == null || rows.Items.Count >= AdminUsersApi.MaxRows || rows.Seen.Count >= 9) return;
        var generation = _generation; _loading = true;
        try
        {
            var next = rows.Next;
            if (!rows.Seen.Add(next)) throw new InvalidDataException("Repeated collection continuation; reload.");
            var page = await _workspace.ReadAsync(generation, (lease, token) => _api.PageAsync(lease, rows.Path, rows.Limit, next, ct: token));
            if (!_workspace.IsCurrent(generation)) return;
            if (page.NextCursor != null && rows.Seen.Contains(page.NextCursor)) throw new InvalidDataException("Repeated collection continuation; reload.");
            if (rows.Items.Count + page.Items.Count > AdminUsersApi.MaxRows) throw new InvalidDataException("The retained-row bound was reached; narrow the filter.");
            rows.Items.AddRange(page.Items);
            rows.Baselines.Add(new(AdminPaging.WithPage(rows.Path, rows.Limit, next), page.Envelope.Copy()));
            _rows[key] = rows with { Next = page.NextCursor, Complete = page.NextCursor == null };
            Render();
        }
        catch (OperationCanceledException error) { if (_workspace.IsCurrent(generation)) ShowError(new TimeoutException("The read timed out or was cancelled. Reload this tab before continuing.", error)); }
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
    private void Render()
    {
        _body.Children.Clear(); _body.Children.Add(_notice);
        if (_review == null) return;
        _body.Children.Add(Action("Reload / review current server state", () => LoadAsync(_location)));
        foreach (var failure in _loadErrors) _body.Children.Add(Text(failure.Key + ": " + failure.Value));
        if (_loadErrors.Count > 0) _body.Children.Add(Action("Reload current tab", () => LoadAsync(_location)));
        if (_location.Segment(0) == null) RenderList(); else RenderDetail();
    }
    private void Navigate(string? id, string tab, string? level = null)
    {
        var query = new Dictionary<string, string> { ["tab"] = tab };
        if (level != null) query["level"] = level;
        var request = new AdminRouteRequest(AdminRoute.Users, query) { Segments = id == null ? [] : [id] };
        AdminShellPage.TryReplace(this, request);
        _ = LoadAsync(request);
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
    private static TextBlock Text(string value, double size = 14) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private Button Action(string label, Func<Task> run, bool enabled = true, bool nested = false, bool recovery = false)
    {
        var button = new Button { Content = label, MinHeight = 40, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, label);
        var generation = _generation;
        var revision = _loadRevision;
        bool Current() => recovery ? _activePage && revision == _loadRevision : _workspace.IsCurrent(generation);
        button.Click += async (_, _) =>
        {
            if (!Current() || _loading || _actionBusy && !nested) return;
            if (!nested) _actionBusy = true; button.IsEnabled = false;
            try { await run(); }
            catch (OperationCanceledException error)
            {
                if (_workspace.IsCurrent(generation)) ShowError(new TimeoutException("The request timed out or was cancelled. Read back/review before making another change.", error));
            }
            catch (Exception error) { if (Current()) ShowError(error, allowInactiveWorkspace: recovery); }
            finally { if (Current()) { if (!nested) _actionBusy = false; button.IsEnabled = enabled; } }
        };
        return button;
    }
    private static StackPanel Panel(double spacing = 10) => new() { Spacing = spacing };
    private static LogsWrapPanel Inline() => new() { Spacing = 8, RowSpacing = 8 };
    private static Border Card(string title, Panel body)
    {
        var content = Panel(); content.Children.Add(Text(title, 20)); content.Children.Add(body);
        return new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush, Child = content };
    }
    private void AddRows(string key, string title, string[] columns, Action<StackPanel, JsonObject>? actions = null, IEnumerable<JsonObject>? filtered = null)
    {
        var card = Panel();
        var items = (filtered ?? Rows(key)).ToList();
        if (items.Count == 0) card.Children.Add(Text(_rows.ContainsKey(key) ? "No records." : "No successful snapshot is loaded."));
        foreach (var row in items)
        {
            var content = Panel(4);
            foreach (var column in columns) content.Children.Add(Text(Label(column) + ": " + row[column].Display()));
            actions?.Invoke(content, row);
            card.Children.Add(new Border { Child = content, Padding = new Thickness(8), BorderThickness = new Thickness(0, 0, 0, 1) });
        }
        if (_rows.TryGetValue(key, out var state) && !state.Complete)
        {
            card.Children.Add(Text($"Partial list: {state.Items.Count} retained. Narrow filters when the 1,000-row/10-page bound is reached."));
            card.Children.Add(Action("Load next bounded page", () => MoreAsync(key), state.Items.Count < AdminUsersApi.MaxRows && state.Seen.Count < 9));
        }
        _body.Children.Add(Card(title, card));
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
