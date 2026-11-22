using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Native Configure > Providers parity for Silo Server 217ff25, with the subtitle tile's failed-save recovery of a84b255.</summary>
public sealed partial class AdminProvidersPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private sealed class ScrollPosition { internal double Offset; }
    private static readonly ConditionalWeakTable<AdminRouteRequest, ScrollPosition> ScrollPositions = new();
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _header = new() { Spacing = 8, Margin = new(0, 0, 0, 24) };
    private readonly StackPanel _loading = new() { Spacing = 16 };
    private readonly StackPanel _alerts = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _error = U.Text("", 13);
    private readonly Border _errorAlert;
    private readonly Button _reload = U.Action("Reload", "ProvidersReload");
    private readonly StackPanel _groups = new() { Spacing = 20 };
    private readonly TextBlock _feedback = U.Text("", 13, true);
    private readonly AdminMonitorAuthority _authority;
    private readonly Dictionary<string, AdminSubtitleProviderEditor> _subtitleEditors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdminSubtitleProviderDraft> _subtitleDrafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdminMarkerProviderDraft> _markerDrafts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _markerAdvanced = new(StringComparer.Ordinal);
    private AdminProvidersController? _controller;
    private AdminLeaveGuard? _leave;
    private AdminRouteRequest? _location;
    private string? _expandedTile;
    private string _mdblistDraft = "";
    private bool _mdblistEdited;
    private double? _restoreOffset;
    private bool _active, _opening;
    private ContentDialog? _dialog;

    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    public AdminProvidersController? Controller => _controller;

    public AdminProvidersPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminProvidersPage");
        AutomationProperties.SetName(_scroller, "Provider settings");
        _header.Children.Add(U.Title("Providers"));
        _body.Children.Add(_header);
        AutomationProperties.SetName(_loading, "Loading provider settings");
        AutomationProperties.SetLiveSetting(_loading, AutomationLiveSetting.Polite);
        _loading.Children.Add(U.Skeleton(34, 220));
        for (var i = 0; i < 4; i++) _loading.Children.Add(U.Skeleton(96));
        _body.Children.Add(_loading);
        AutomationProperties.SetAutomationId(_error, "ProvidersError");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Assertive);
        _reload.Click += async (_, _) => await ReloadPageAsync();
        _errorAlert = U.Alert(_error); _alerts.Children.Add(_errorAlert); _alerts.Children.Add(_reload); _body.Children.Add(_alerts);
        _feedback.Margin = new(0, 0, 0, 4); _feedback.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_feedback, "ProvidersFeedback");
        AutomationProperties.SetLiveSetting(_feedback, AutomationLiveSetting.Polite);
        _body.Children.Add(_feedback); _body.Children.Add(_groups);
        _scrollContent.Children.Add(_body); _scroller.Content = _scrollContent; _root.Children.Add(_scroller); Content = _root;
        _scroller.SizeChanged += (_, _) => FitScrollContent();
        Loaded += (_, _) => { FitScrollContent(); Activate(); };
        Unloaded += (_, _) => Close();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _location = e.Parameter as AdminRouteRequest;
        _restoreOffset = e.NavigationMode == NavigationMode.Back && _location != null && ScrollPositions.TryGetValue(_location, out var position) ? position.Offset : 0;
        base.OnNavigatedTo(e);
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); base.OnNavigatedFrom(e); }

    private void FitScrollContent()
    {
        var available = _scroller.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (available > 0) _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, available);
    }

    private void Activate()
    {
        if (_active) return;
        _active = true;
        _controller = App.Services.GetRequiredService<AdminProvidersController>();
        _controller.Changed += QueueRender;
        _controller.Saved += OnSaved;
        _leave = new(() => _controller?.State.Owner, HasDraft, DiscardAll);
        _authority.AvailabilityChanged += OnAuthorityChanged;
        _ = OpenAsync();
    }

    private async Task OpenAsync()
    {
        if (_opening || !_active || _controller == null || !_authority.IsAvailable) return;
        _opening = true;
        try { await _controller.OpenAsync(); }
        finally
        {
            _opening = false;
            if (_active && _authority.IsAvailable && _controller.State.Owner == null)
                DispatcherQueue.TryEnqueue(() => { if (_active) _ = OpenAsync(); });
        }
    }

    private void OnAuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        // A confirmation opened for the previous authority closes with it.
        _dialog?.Hide();
        ClearPrivateDrafts();
        if (_active && _controller?.State.Owner == null) _ = OpenAsync();
    });

    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return;
        var state = controller.State;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_active && IsLoaded && ReferenceEquals(controller, _controller) && ReferenceEquals(state, controller.State)) Render(state);
        });
    }

    private void Render(AdminProvidersState state)
    {
        var current = state.Owner?.IsCurrent == true;
        _loading.Visibility = current && state.Loading ? Visibility.Visible : Visibility.Collapsed;
        // A conflict or unknown outcome keeps Reload reachable even when no page-level error text is set.
        // A section or the credential status that failed to load can only be recovered by reloading.
        var partial = state.Loaded && (state.SensitiveStatusError || state.SubtitleProvidersFailed || state.MarkerProvidersFailed || state.PluginProvidersFailed);
        _alerts.Visibility = current && (state.Error != null || state.ReloadRequired || partial) ? Visibility.Visible : Visibility.Collapsed;
        _errorAlert.Visibility = state.Error != null ? Visibility.Visible : Visibility.Collapsed;
        if (state.Error != null) U.UpdateLiveText(_error, state.Error);
        _reload.IsEnabled = current && !state.Loading && !state.SavingSettings && state.BusyProvider == null;
        _feedback.Visibility = current && !string.IsNullOrWhiteSpace(state.ProviderFeedback) ? Visibility.Visible : Visibility.Collapsed;
        if (_feedback.Visibility == Visibility.Visible) U.UpdateLiveText(_feedback, state.ProviderFeedback!);
        _groups.Visibility = current && state.Loaded && !state.Loading ? Visibility.Visible : Visibility.Collapsed;
        // A retired authority never leaves private provider controls (typed credentials) behind in the tree.
        if (!current) { _groups.Children.Clear(); _expandedTile = null; }
        if (_groups.Visibility == Visibility.Visible) RenderGroups(state);
        if (current && state.Loaded && _restoreOffset is { } offset)
        {
            _restoreOffset = null;
            DispatcherQueue.TryEnqueue(() => { if (_active) { _root.UpdateLayout(); _scroller.ChangeView(null, offset, null, true); } });
        }
    }

    private bool HasDraft() => _mdblistEdited || (_controller?.State.DirtyKeys.Count ?? 0) > 0 ||
        _subtitleDrafts.Values.Any(draft => draft.Dirty) || _markerDrafts.Values.Any(draft => draft.Dirty);

    private void DiscardAll()
    {
        _controller?.DiscardMdblist(); _mdblistDraft = ""; _mdblistEdited = false; ClearPrivateDrafts(); _expandedTile = null;
        if (_controller != null) Render(_controller.State);
    }

    private void ClearPrivateDrafts()
    {
        foreach (var draft in _subtitleDrafts.Values) draft.ClearSecrets();
        _subtitleDrafts.Clear(); _subtitleEditors.Clear(); _markerDrafts.Clear(); _markerAdvanced.Clear();
        _mdblistDraft = ""; _mdblistEdited = false;
    }

    public async Task<bool> CanLeaveAsync()
    {
        if (_leave == null) return true;
        return await _leave.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _dialog, value => _dialog = value));
    }

    private void OnSaved(IReadOnlyCollection<string> keys)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (keys.Contains("mdblist.api_key"))
                if (App.Services.GetService<NavigationService>()?.Frame?.Content is AdminShellPage shell) shell.RefreshSettingsStatus();
        });
    }

    public void SuspendAdminPage() => Close();
    private void Close()
    {
        if (!_active) return;
        _active = false;
        if (_location != null) ScrollPositions.GetOrCreateValue(_location).Offset = _scroller.VerticalOffset;
        _authority.AvailabilityChanged -= OnAuthorityChanged;
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Saved -= OnSaved; _controller.Dispose(); }
        _controller = null; _leave = null; _dialog?.Hide(); _dialog = null;
        ClearPrivateDrafts(); _groups.Children.Clear();
    }
}
