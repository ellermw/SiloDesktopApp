using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Windows.System;
using SiloPlayer.Controls;
using Windows.UI;
using static SiloPlayer.Views.Admin.AdminLibrariesUi;
using P = SiloPlayer.Core.Services.Admin.AdminLibraryPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The page's three tabs (official <c>pages/AdminLibraries.tsx</c> at 93794454b): Libraries (the live-activity summary strip and the
/// list), Scan Activity (<c>LibraryActivityPanel</c>) and Catalog Health (<c>LibraryCatalogHealthPanel</c>, five tiles over the
/// diagnostics sections). The selected tab is kept in the route's <c>tab</c> query value (absent for Libraries) and replaces the history
/// entry; a tab change swaps the panel in place with no entrance, slide or opacity animation.
/// </summary>
public sealed partial class AdminLibrariesPage
{
    private static readonly AdminLibrariesTab[] TabOrder = [AdminLibrariesTab.Libraries, AdminLibrariesTab.Activity, AdminLibrariesTab.Health];
    private static readonly AdminHealthSection[] TileOrder =
        [AdminHealthSection.Unmatched, AdminHealthSection.Matcher, AdminHealthSection.Ambiguous, AdminHealthSection.Skipped, AdminHealthSection.Stale];

    private AdminLibrariesTab _tab = AdminLibrariesTab.Libraries;
    private AdminHealthSection _health = AdminHealthSection.Unmatched;
    /// <summary>The Scan Activity library filter (official <c>activityLibraryFilter</c>); null shows every library.</summary>
    private string? _activityFilter;
    private readonly HashSet<string> _expandedQueues = new(StringComparer.Ordinal);

    private readonly Border _tabStrip = new();
    // One tab stop for the strip (the selected tab); the arrow keys, Home and End move between tabs (Radix roving focus).
    private readonly StackPanel _tabRow = new() { Orientation = Orientation.Horizontal, TabFocusNavigation = Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Once };
    private readonly Dictionary<AdminLibrariesTab, SelectableButton> _tabButtons = [];
    private readonly Dictionary<AdminLibrariesTab, (TextBlock Label, ContentControl Badge)> _tabParts = [];
    private readonly ContentControl _summary = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly StackPanel _librariesPanel = new() { Spacing = 16 };
    private readonly ContentControl _activityPanel = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly StackPanel _healthPanel = new() { Spacing = 16 };
    private readonly Grid _healthTiles = new() { ColumnSpacing = 10, RowSpacing = 10, TabFocusNavigation = Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Once };
    private readonly ContentControl _healthBody = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly Dictionary<AdminHealthSection, (SelectableButton Button, ContentControl Count, Border Icon)> _tiles = [];
    private readonly Dictionary<AdminHealthSection, Border> _healthCards = [];
    private readonly Dictionary<AdminHealthSection, (long? Count, bool Error)> _tileStatus = [];

    internal static string NumberText(long value) => value.ToString("N0", Core.Localization.AppStrings.FormatCulture);

    /// <summary>The selected tab (tests and fixtures).</summary>
    public AdminLibrariesTab SelectedTab => _tab;

    /// <summary>The selected Catalog Health tile (tests and fixtures).</summary>
    public AdminHealthSection SelectedHealthSection => _health;

    // ── Build ────────────────────────────────────────────────────────────

    private static string TabLabel(AdminLibrariesTab tab) => T(tab switch
    {
        AdminLibrariesTab.Activity => "web.admin.libraries.tabs.activity",
        AdminLibrariesTab.Health => "web.admin.libraries.tabs.health",
        _ => "web.admin.libraries.tabs.libraries",
    });

    private void BuildTabs()
    {
        for (var index = 0; index < TabOrder.Length; index++)
        {
            var tab = TabOrder[index];
            var label = new TextBlock { Text = TabLabel(tab), FontSize = 14, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
            var badge = new ContentControl { VerticalAlignment = VerticalAlignment.Center, IsTabStop = false };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; // gap-2
            content.Children.Add(label); content.Children.Add(badge);
            // TabsList variant="line": a border-b strip, triggers with a bottom indicator. The padding keeps text clear of the (square) edges.
            var button = new SelectableButton
            {
                Content = content, Style = (Style)Application.Current.Resources["GhostButtonStyle"], MinHeight = 40, Padding = new(12, 6, 12, 6),
                BorderThickness = new(0, 0, 0, 2), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent), CornerRadius = new(0),
                SelectionContainer = _tabRow,
            };
            AutomationProperties.SetName(button, TabLabel(tab));
            AutomationProperties.SetAutomationId(button, "LibrariesTab_" + tab);
            AutomationProperties.SetPositionInSet(button, index + 1);
            AutomationProperties.SetSizeOfSet(button, TabOrder.Length);
            var captured = tab; var position = index;
            button.Choose = () => SelectTab(captured);
            // Only the selected tab is a tab stop, and a control that is not a tab stop cannot take focus: a tab chosen with the pointer is
            // focused after it is selected, so the arrow keys continue from it and focus does not stay in a panel that was just hidden.
            button.Click += (_, _) => { SelectTab(captured); if (button.FocusState == FocusState.Unfocused) button.Focus(FocusState.Pointer); };
            button.KeyDown += (_, args) => OnTabKey(args, position, TabOrder.Select(entry => (Button)_tabButtons[entry]).ToArray(), target => SelectTab(TabOrder[target]));
            _tabButtons[tab] = button; _tabParts[tab] = (label, badge);
            _tabRow.Children.Add(button);
        }
        _tabStrip.Child = new ScrollViewer
        {
            Content = _tabRow, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _tabStrip.BorderBrush = Brush("BorderBrush"); _tabStrip.BorderThickness = new(0, 0, 0, 1);
        _librariesPanel.Children.Add(_summary);
        _librariesPanel.Margin = new(0, 16, 0, 0); // TabsContent pt-4
        _activityPanel.Margin = new(0, 16, 0, 0);
        _healthPanel.Margin = new(0, 16, 0, 0);
        BuildHealthTiles();
        _healthPanel.Children.Add(_healthTiles);
        _healthPanel.Children.Add(_healthBody);
        ApplyPanelVisibility();
    }

    /// <summary>Radix tabs: Left/Right (mirrored in a right-to-left page), Home and End move focus and select; the ends loop.</summary>
    private static void OnTabKey(Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args, int index, IReadOnlyList<Button> buttons, Action<int> select)
    {
        var rtl = buttons.Count > 0 && buttons[0].FlowDirection == FlowDirection.RightToLeft;
        AdminTabKey? key = args.Key switch
        {
            VirtualKey.Right => rtl ? AdminTabKey.Previous : AdminTabKey.Next,
            VirtualKey.Left => rtl ? AdminTabKey.Next : AdminTabKey.Previous,
            VirtualKey.Home => AdminTabKey.First,
            VirtualKey.End => AdminTabKey.Last,
            _ => null,
        };
        if (key is not { } move) return;
        args.Handled = true;
        var target = AdminLibraryTabs.Navigate(buttons.Count, index, move);
        if (target < 0) return;
        select(target);
        buttons[target].Focus(FocusState.Keyboard);
    }

    private static Border Pill(string text, string tone, bool dot = false)
    {
        var foreground = tone switch { "success" => Solid(Success), "warning" => Solid(Warning), _ => Brush("SecondaryForegroundBrush") };
        var label = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        UIElement child = label;
        if (dot)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new(3), Background = Solid(Success), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(label);
            child = row;
        }
        var pill = new Border
        {
            Child = child, Padding = new(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            Background = tone switch { "success" => Solid(Success, 0.15), "warning" => Solid(Warning, 0.15), _ => Brush("SecondaryBackgroundBrush") },
        };
        // rounded-full: the radius follows the pill's size (a fixed 100 is drawn as an oval with pointed ends).
        RoundedBadge.SetRadius(pill, 999);
        return pill;
    }

    // ── Selection and the route ──────────────────────────────────────────

    private void SelectTab(AdminLibrariesTab tab, bool resetActivityFilter = true)
    {
        if (_tab == tab) return;
        // The filter belongs to the Scan Activity visit that set it: reaching the tab from its trigger starts unfiltered, and leaving drops its expanded queues.
        if (tab == AdminLibrariesTab.Activity && resetActivityFilter) _activityFilter = null;
        if (_tab == AdminLibrariesTab.Activity) _expandedQueues.Clear();
        // Official: Catalog Health's panel unmounts when its tab is left, so it returns on Unmatched with "Inspect" on the tiles that read lazily.
        if (_tab == AdminLibrariesTab.Health) ResetHealthPanel();
        _tab = tab;
        ReplaceTabRoute();
        ApplyTab();
    }

    /// <summary>The summary strip's "View queue" and a row's scan pill: Scan Activity, filtered to one library or to all, with focus on its tab.</summary>
    private void OpenActivityFor(string? libraryId)
    {
        _activityFilter = libraryId;
        SelectTab(AdminLibrariesTab.Activity, resetActivityFilter: false);
        EnqueueUi(() => _tabButtons[AdminLibrariesTab.Activity].Focus(FocusState.Programmatic));
    }

    /// <summary>The official <c>setSearchParams(..., {replace: true})</c>: the tab value (not for Libraries) next to every other query value.</summary>
    private void ReplaceTabRoute()
    {
        var query = new Dictionary<string, string>(_route.Query ?? new Dictionary<string, string>());
        query.Remove("tab");
        if (AdminLibraryTabs.Query(_tab) is { } value) query["tab"] = value;
        var request = new AdminRouteRequest(AdminRoute.Libraries, query.Count == 0 ? null : query);
        if (AdminShellPage.TryReplace(this, request)) _route = request;
    }

    private void ApplyPanelVisibility()
    {
        _librariesPanel.Visibility = _tab == AdminLibrariesTab.Libraries ? Visibility.Visible : Visibility.Collapsed;
        _activityPanel.Visibility = _tab == AdminLibrariesTab.Activity ? Visibility.Visible : Visibility.Collapsed;
        _healthPanel.Visibility = _tab == AdminLibrariesTab.Health ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyTab()
    {
        if (_tab != AdminLibrariesTab.Libraries)
        {
            CancelDrag();
            foreach (var flyout in _openMenus.ToArray()) flyout.Hide();
        }
        ApplyPanelVisibility();
        UpdateHealthOpen();
        _renderedListKey = null;
        if (_controller != null) Render(_controller.State);
    }

    // ── Tab strip ────────────────────────────────────────────────────────

    private void RenderTabs(AdminLibrariesSnapshot state, AdminActivityTotals totals)
    {
        var phone = !_wide;
        foreach (var tab in TabOrder)
        {
            var selected = tab == _tab;
            var button = _tabButtons[tab];
            var (label, badgeHost) = _tabParts[tab];
            button.MinHeight = phone ? 44 : 40; // max-lg:h-11
            button.BorderBrush = selected ? Brush("PrimaryTextBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            label.Foreground = selected ? Brush("PrimaryTextBrush") : Brush("SecondaryTextBrush");
            // The selected tab is the strip's one tab stop; the others are reached with the arrow keys, Home and End. Selection is a real selected
            // state through UI Automation's SelectionItem pattern (not item-status text).
            button.IsTabStop = selected;
            button.ChangeSelection(selected);
            string? badgeText = null;
            switch (tab)
            {
                case AdminLibrariesTab.Libraries:
                    badgeText = NumberText(state.Libraries.Count);
                    badgeHost.Content = Pill(badgeText, "secondary");
                    break;
                case AdminLibrariesTab.Activity:
                    if (totals.Active > 0)
                    {
                        badgeText = T("web.admin.libraries.tabs.activeCount", ("count", totals.Active));
                        badgeHost.Content = Pill(badgeText, totals.HasRunning ? "success" : "secondary", dot: totals.HasRunning);
                    }
                    else badgeHost.Content = null;
                    break;
                default:
                    if (HealthBadgeCount() is > 0 and { } health)
                    {
                        badgeText = NumberText(health);
                        badgeHost.Content = Pill(badgeText, "warning");
                    }
                    else badgeHost.Content = null;
                    break;
            }
            AutomationProperties.SetName(button, badgeText == null ? TabLabel(tab) : TabLabel(tab) + " " + badgeText);
        }
    }

    private long? HealthBadgeCount() =>
        AdminLibraryTabs.HealthBadge(_unmatched == null ? null : AdminLibraryTabs.UnmatchedTotal(_unmatched.State), _matcher == null ? null : AdminLibraryTabs.MatcherTotal(_matcher.State));

    // ── Libraries tab: the live-activity summary strip ───────────────────

    private void RenderSummary(AdminLibrariesSnapshot state, IReadOnlyList<AdminScanQueueGroup> groups, IReadOnlyList<AdminLibraryRefreshEntry> refreshes, AdminActivityTotals totals)
    {
        if (totals.RunningScans == 0 && totals.QueuedScans == 0 && totals.Refreshes == 0)
        {
            _summary.Content = null;
            _summary.Visibility = Visibility.Collapsed;
            _renderedSummaryKey = null;
            return;
        }
        var owner = RenderOwner(state);
        var headline = new List<string>();
        if (totals.RunningScans > 0) headline.Add(T("web.admin.libraries.scans.running", AdminLibrariesText.Count(totals.RunningScans)));
        if (totals.QueuedScans > 0) headline.Add(T("web.admin.libraries.scans.queued", AdminLibrariesText.Count(totals.QueuedScans)));
        if (totals.Refreshes > 0 && headline.Count == 0)
            headline.Add(totals.RunningRefreshes > 0 ? T("web.admin.libraries.activity.refreshingMetadata") : T("web.admin.libraries.activeWork.refreshQueued"));

        string? detail = null;
        if (groups.Count > 0)
        {
            var primary = groups[0];
            // Official: the name is `dir="auto"` and each path `dir="ltr"` (isolated here), so in a right-to-left interface the name, the parentheses and the
            // paths do not reorder each other.
            var sample = primary.Scans.Take(2).Select(scan => AdminPathText.Whole(P.ScanTarget(scan.Path))).ToArray();
            detail = AdminPathText.Name(primary.Name) + (sample.Length > 0 ? " (" + string.Join(", ", sample) + ")" : "");
            if (groups.Count - 1 > 0) detail += " " + T("web.admin.libraries.activity.moreLibraries", ("count", groups.Count - 1));
        }
        else if (refreshes.Count > 0)
        {
            var primary = refreshes[0];
            detail = AdminPathText.Name(primary.Library?.Name ?? T("web.admin.libraries.scanQueue.libraryFallback", ("id", primary.LibraryId)))
                + (primary.Job.State == "canceling" ? " · " + AdminPathText.Name(P.JobMessage(primary.Job)) : "");
        }

        // The strip is rebuilt only when what it draws changes: a rebuild on every scan event (about once a second) takes focus off "View queue" and
        // can replace the button under a click.
        var summaryKey = string.Join("\u001f", _wide, _phone, totals.HasRunning, string.Join(" · ", headline), detail ?? "");
        if (summaryKey == _renderedSummaryKey && ReferenceEquals(state.Owner, _renderedSummaryOwner) && _summary.Content != null) return;
        _renderedSummaryKey = summaryKey; _renderedSummaryOwner = state.Owner;
        var summaryFocus = FocusWithin(_summary);

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new Border
        {
            Width = 8, Height = 8, CornerRadius = new(4), VerticalAlignment = VerticalAlignment.Center,
            Background = totals.HasRunning ? Solid(Success) : Tint("SecondaryTextBrush", 0.5),
        });
        var texts = new LogsWrapPanel { Spacing = 10, RowSpacing = 2 };
        var headlineText = Text(string.Join(" · ", headline), 12); headlineText.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetAutomationId(headlineText, "LibrariesSummaryHeadline");
        texts.Children.Add(headlineText);
        if (detail != null)
        {
            var detailText = Text(detail, 12, true); detailText.TextTrimming = TextTrimming.CharacterEllipsis; detailText.TextWrapping = TextWrapping.NoWrap; detailText.MaxWidth = 640;
            AutomationProperties.SetAutomationId(detailText, "LibrariesSummaryDetail");
            texts.Children.Add(detailText);
        }
        left.Children.Add(texts);
        grid.Children.Add(left);

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        content.Children.Add(Text(T("web.admin.libraries.activity.viewQueue"), 12));
        content.Children.Add(Icon("arrow-right", 12, Brush("PrimaryTextBrush")));
        var view = new Button
        {
            Content = content, Style = ButtonStyle("OutlineButtonStyle"), MinHeight = _wide ? 28 : 36, Padding = new(10, 0, 10, 0), CornerRadius = new(RadiusMd),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(view, T("web.admin.libraries.activity.viewQueue"));
        AutomationProperties.SetAutomationId(view, "LibrariesViewQueue");
        view.Click += (_, _) => { if (owner.IsCurrent) OpenActivityFor(null); };
        if (_phone) { grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Grid.SetRow(view, 1); view.HorizontalAlignment = HorizontalAlignment.Stretch; }
        else { grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(view, 1); }
        grid.Children.Add(view);
        _summary.Visibility = Visibility.Visible;
        var strip = new Border
        {
            Child = grid, Padding = new(16, 10, 16, 10), CornerRadius = new(Radius2Xl), BorderThickness = new(1),
            Background = Tint("SurfaceBrush", 0.9), BorderBrush = totals.HasRunning ? Solid(Success, 0.30) : Tint("BorderBrush", 0.6),
        };
        AutomationProperties.SetAutomationId(strip, "LibrariesSummary");
        _summary.Content = strip;
        RestoreFocus(_summary, summaryFocus);
    }

    private string? _renderedSummaryKey;
    private MonitorAuthorityLease? _renderedSummaryOwner;

    /// <summary>The focused control inside <paramref name="scope"/> with how it was focused (keyboard, pointer, programmatic), or null when focus is elsewhere.</summary>
    private (string Id, FocusState State)? FocusWithin(DependencyObject scope)
    {
        var focused = Focused(XamlRoot);
        if (focused == null || !IsDescendant(focused, scope)) return null;
        var id = AutomationProperties.GetAutomationId(focused);
        return id.Length == 0 ? null : (id, focused.FocusState == FocusState.Unfocused ? FocusState.Programmatic : focused.FocusState);
    }

    /// <summary>Puts focus back on the control that had it before a rebuild, with the focus state it had (a keyboard user keeps the focus ring).</summary>
    private void RestoreFocus(DependencyObject scope, (string Id, FocusState State)? previous, int attempt = 0)
    {
        if (previous is not { } focus) return;
        // The replacement controls join the tree before they are laid out and loaded; focus only lands on a loaded control, so try again a few times
        // (low priority runs after the layout pass) instead of leaving focus nowhere.
        void Try()
        {
            var control = FindControl(scope, focus.Id);
            if (control == null) return;
            if (control.IsLoaded && control.Focus(focus.State)) return;
            if (attempt < 5) RestoreFocus(scope, previous, attempt + 1);
        }
        if (attempt == 0)
        {
            // Load the replacement now and focus it before the framework moves focus off the removed control by itself; if that is not possible, retry after layout.
            (scope as UIElement)?.UpdateLayout();
            var immediate = FindControl(scope, focus.Id);
            if (immediate is { IsLoaded: true } && immediate.Focus(focus.State)) return;
            EnqueueUi(Try);
        }
        else DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { try { Try(); } catch (Exception) { } });
    }

    // ── Scan Activity ────────────────────────────────────────────────────

    private void RenderActivity(AdminLibrariesSnapshot state, IReadOnlyList<AdminScanQueueGroup> groups, IReadOnlyList<AdminLibraryRefreshEntry> refreshes, AdminActivityTotals totals)
    {
        var owner = RenderOwner(state);
        var kind = AdminLibraryTabs.PanelKind(totals.Active, state.RefreshJobsFailed, !state.RefreshJobsLoaded && !state.RefreshJobsFailed);
        // The panel is rebuilt only when what it draws changes (its groups, scans, refreshes, filter, expanded queues, pending cancels, jobs footer, width). The
        // controller changes about once a second while scans run; a rebuild each time dropped keyboard focus, could replace a Stop under a click and reset an
        // expanded queue's scroll position. The progress texts and bars that change second by second are updated in place.
        var key = ActivityKey(state, groups, refreshes, kind);
        if (key == _renderedActivityKey && ReferenceEquals(state.Owner, _renderedActivityOwner) && _activityPanel.Content != null)
        {
            UpdateActivityLive(groups, refreshes);
            return;
        }
        _renderedActivityKey = key; _renderedActivityOwner = state.Owner;
        _scanLive.Clear(); _refreshLive.Clear();
        var focus = FocusWithin(_activityPanel);
        var panel = new StackPanel { Spacing = 16 };
        AutomationProperties.SetAutomationId(panel, "LibrariesActivity");
        switch (kind)
        {
            case AdminActivityPanelKind.Loading:
            {
                var loading = Text(T("common.loading"), 14, true); loading.HorizontalAlignment = HorizontalAlignment.Center; loading.TextAlignment = TextAlignment.Center;
                AutomationProperties.SetAutomationId(loading, "LibrariesActivityLoading");
                panel.Children.Add(new Border { Child = loading, Padding = new(24, 48, 24, 48), CornerRadius = new(Radius2Xl), Background = Tint("SurfaceBrush", 0.9) });
                break;
            }
            case AdminActivityPanelKind.FooterOnly:
                AddJobFooter(panel, state, owner);
                break;
            case AdminActivityPanelKind.Empty:
            {
                var empty = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
                empty.Children.Add(new Border
                {
                    Width = 40, Height = 40, CornerRadius = new(RadiusXl), Background = Tint("MutedBrush", 0.6), HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new Border { Child = Icon("circle-check", 20, Brush("SecondaryTextBrush")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                });
                var title = Text(T("web.admin.libraries.activity.emptyTitle"), 14); title.FontWeight = FontWeights.SemiBold; title.HorizontalAlignment = HorizontalAlignment.Center;
                AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
                AutomationProperties.SetAutomationId(title, "LibrariesActivityEmpty");
                empty.Children.Add(title);
                var description = Text(T("web.admin.libraries.activity.emptyDescription"), 12, true); description.TextAlignment = TextAlignment.Center; description.MaxWidth = 448; description.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(description);
                panel.Children.Add(new Border { Child = empty, Padding = new(24, 48, 24, 48), CornerRadius = new(Radius2Xl), Background = Tint("SurfaceBrush", 0.9) });
                AddJobFooter(panel, state, owner);
                break;
            }
            default:
                BuildActivityBody(panel, state, owner, groups, refreshes, totals);
                AddJobFooter(panel, state, owner);
                break;
        }
        _activityPanel.Content = panel;
        RestoreFocus(_activityPanel, focus);
    }

    private string? _renderedActivityKey;
    private MonitorAuthorityLease? _renderedActivityOwner;
    /// <summary>A running scan's second-by-second parts (its progress or started text and its bar), updated in place.</summary>
    private readonly Dictionary<string, (TextBlock Status, ProgressBar? Bar)> _scanLive = new(StringComparer.Ordinal);
    /// <summary>A refresh row's message and progress lines, updated in place.</summary>
    private readonly Dictionary<string, (TextBlock Message, TextBlock Progress)> _refreshLive = new(StringComparer.Ordinal);

    /// <summary>Everything the Scan Activity panel draws except the text and bar values that move while a scan runs.</summary>
    private string ActivityKey(AdminLibrariesSnapshot state, IReadOnlyList<AdminScanQueueGroup> groups, IReadOnlyList<AdminLibraryRefreshEntry> refreshes, AdminActivityPanelKind kind)
    {
        var key = new System.Text.StringBuilder();
        key.Append(kind).Append('|').Append(_wide).Append(_phone).Append('|').Append(_activityFilter).Append('|')
            .Append(state.RefreshJobsFailed).Append(state.RefreshJobsHasMore).Append(state.RefreshJobsLoadingMore).Append(state.RefreshJobsCapped).Append('|');
        foreach (var id in _expandedQueues.Order(StringComparer.Ordinal)) key.Append(id).Append(',');
        key.Append('|');
        foreach (var id in state.JobsCancelling.Order(StringComparer.Ordinal)) key.Append(id).Append(',');
        key.Append('|');
        foreach (var id in state.ScanCancelling.Order(StringComparer.Ordinal)) key.Append(id).Append(',');
        foreach (var group in groups)
        {
            key.Append("\ng").Append(group.LibraryId).Append(':').Append(group.Name).Append(':').Append(group.LibraryType);
            foreach (var scan in group.Scans)
                key.Append("\ns").Append(scan.Id).Append(scan.IsRunning ? 'r' : 'q').Append(scan.Mode).Append(':').Append(scan.Path).Append(':').Append(scan.Trigger)
                    .Append(AdminLibraryTabs.ScanProgressPercent(scan) != null ? 'b' : '-');
        }
        foreach (var entry in refreshes)
            key.Append("\nj").Append(entry.Job.Id).Append(':').Append(entry.LibraryId).Append(':').Append(entry.Library?.Name).Append(':').Append(entry.Library?.Type).Append(':').Append(entry.Job.State);
        return key.ToString();
    }

    private static string ScanStatusText(AdminLibraryScanRun scan, DateTimeOffset now)
    {
        var progress = P.ScanProgress(scan);
        return progress.Length > 0 ? progress : P.ScanStarted(scan.StartedAt, now);
    }

    private void UpdateActivityLive(IReadOnlyList<AdminScanQueueGroup> groups, IReadOnlyList<AdminLibraryRefreshEntry> refreshes)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var group in groups)
            foreach (var scan in group.Scans)
            {
                if (!_scanLive.TryGetValue(scan.Id, out var live)) continue;
                var text = ScanStatusText(scan, now);
                if (live.Status.Text != text) live.Status.Text = text;
                if (live.Bar != null && AdminLibraryTabs.ScanProgressPercent(scan) is { } percent && live.Bar.Value != percent) live.Bar.Value = percent;
            }
        foreach (var entry in refreshes)
        {
            if (!_refreshLive.TryGetValue(entry.Job.Id, out var live)) continue;
            var message = P.JobMessage(entry.Job); var progress = P.JobProgress(entry.Job);
            if (live.Message.Text != message) live.Message.Text = message;
            if (live.Progress.Text != progress) live.Progress.Text = progress;
        }
    }

    private void BuildActivityBody(StackPanel panel, AdminLibrariesSnapshot state, LibraryUiOwner owner, IReadOnlyList<AdminScanQueueGroup> groups, IReadOnlyList<AdminLibraryRefreshEntry> refreshes, AdminActivityTotals totals)
    {
        var entries = AdminLibraryTabs.FilterEntries(groups, refreshes, (id, library) => library?.Name ?? T("web.admin.libraries.scanQueue.libraryFallback", ("id", id)));
        var filter = AdminLibraryTabs.EffectiveFilter(_activityFilter, entries);
        var visibleGroups = filter == null ? groups : groups.Where(group => group.LibraryId == filter).ToArray();
        var visibleRefreshes = filter == null ? refreshes : refreshes.Where(entry => entry.LibraryId == filter).ToArray();

        if (entries.Count > 1)
        {
            var chips = new LogsWrapPanel { Spacing = 6, RowSpacing = 6 };
            chips.Children.Add(FilterChip("LibrariesFilter_all", T("web.admin.libraries.activity.filterAll", ("count", totals.Active)), null, filter == null, () => SetActivityFilter(null, owner)));
            foreach (var entry in entries)
            {
                var id = entry.LibraryId;
                chips.Children.Add(FilterChip("LibrariesFilter_" + id, entry.Name, entry.Count, filter == id, () => SetActivityFilter(id, owner)));
            }
            panel.Children.Add(chips);
        }

        if (visibleRefreshes.Count > 0)
        {
            var section = new StackPanel { Spacing = 10 };
            var heading = Text(Core.Localization.AppStrings.Uppercase(T("web.admin.libraries.activity.refreshSectionTitle")), 12, true);
            heading.FontWeight = FontWeights.SemiBold; heading.CharacterSpacing = 50; // tracking-wider
            AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
            section.Children.Add(heading);
            foreach (var entry in visibleRefreshes) section.Children.Add(RefreshRow(entry, state, owner));
            panel.Children.Add(section);
        }

        var cards = new StackPanel { Spacing = 12 };
        foreach (var group in visibleGroups) cards.Children.Add(ActivityCard(group, state, owner));
        panel.Children.Add(cards);
    }

    private void SetActivityFilter(string? libraryId, LibraryUiOwner owner)
    {
        if (!owner.IsCurrent) return;
        _activityFilter = libraryId;
        if (_controller != null) Render(_controller.State);
    }

    private Button FilterChip(string automationId, string label, int? count, bool selected, Action click)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var text = Text(label, 12); text.FontWeight = FontWeights.Medium; text.TextWrapping = TextWrapping.NoWrap;
        text.Foreground = selected ? Brush("AppBackgroundBrush") : Brush("SecondaryTextBrush");
        row.Children.Add(text);
        if (count is { } number)
        {
            var pill = new TextBlock { Text = NumberText(number), FontSize = 10, LineHeight = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = selected ? Brush("AppBackgroundBrush") : Brush("SecondaryForegroundBrush") };
            var countBadge = new Border
            {
                // inline-flex items-center: the count is centred against the label, not stretched to the label's line or left sitting low.
                Child = pill, Padding = new(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center,
                Background = selected ? Tint("AppBackgroundBrush", 0.2) : Brush("SecondaryBackgroundBrush"),
            };
            RoundedBadge.SetRadius(countBadge, 999);
            row.Children.Add(countBadge);
        }
        row.VerticalAlignment = VerticalAlignment.Center;
        var chip = new Button
        {
            Content = row, Style = (Style)Application.Current.Resources["GhostButtonStyle"], MinHeight = _wide ? 28 : 36, Padding = new(12, 4, 12, 4), BorderThickness = new(1),
            Background = selected ? Brush("PrimaryTextBrush") : Brush("SurfaceBrush"), BorderBrush = selected ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : Brush("BorderBrush"),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        RoundedBadge.SetRadius(chip, 999); // rounded-full
        AutomationProperties.SetName(chip, count is { } n ? label + " " + NumberText(n) : label);
        AutomationProperties.SetAutomationId(chip, automationId);
        if (selected) AutomationProperties.SetItemStatus(chip, "pressed");
        chip.Click += (_, _) => click();
        return chip;
    }

    private static Border TypeTile(string? type, double size, double radius)
    {
        var tile = new Border { Width = size, Height = size, CornerRadius = new(radius), Background = Brush("SecondaryBackgroundBrush"), VerticalAlignment = VerticalAlignment.Top };
        tile.Child = new Border { Child = Icon(P.FindType(type)?.Icon ?? "folder-open", 16, Brush("SecondaryTextBrush")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return tile;
    }

    /// <summary>The destructive-tinted outline "Stop" (<c>border-destructive/30 bg-destructive/10 text-destructive</c>); the accessible name says what stops.</summary>
    private Button StopPill(string name, string automationId, bool enabled)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(Icon("square", 10, Solid(Destructive), fill: true));
        var label = Text(T("web.admin.libraries.list.stopAction"), 12); label.Foreground = Solid(Destructive); label.FontWeight = FontWeights.Medium;
        content.Children.Add(label);
        var button = new Button
        {
            Content = content, Style = ButtonStyle("OutlineButtonStyle"), MinHeight = _wide ? 28 : 40, Padding = new(10, 0, 10, 0), CornerRadius = new(RadiusMd),
            Background = Solid(Destructive, 0.10), BorderBrush = Solid(Destructive, 0.30), Foreground = Solid(Destructive), IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }

    private FrameworkElement RefreshRow(AdminLibraryRefreshEntry entry, AdminLibrariesSnapshot state, LibraryUiOwner owner)
    {
        var name = entry.Library?.Name ?? T("web.admin.libraries.scanQueue.libraryFallback", ("id", entry.LibraryId));
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var left = new Grid { ColumnSpacing = 12 };
        left.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); left.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        left.Children.Add(TypeTile(entry.Library?.Type, 32, RadiusLg));
        var text = new StackPanel { Spacing = 2 };
        var head = new LogsWrapPanel { Spacing = 8, RowSpacing = 4 };
        var title = Text(name, 14); title.FontWeight = FontWeights.SemiBold; title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; title.MaxWidth = 480;
        head.Children.Add(title);
        head.Children.Add(Badge(T("web.admin.libraries.activeWork.metadata"), BadgeTone.Secondary, "database-backup", fontSize: 11));
        text.Children.Add(head);
        var message = Text(P.JobMessage(entry.Job), 12, true); message.TextTrimming = TextTrimming.CharacterEllipsis; message.TextWrapping = TextWrapping.NoWrap;
        text.Children.Add(message);
        var progress = Text(P.JobProgress(entry.Job), 11, true); progress.TextTrimming = TextTrimming.CharacterEllipsis; progress.TextWrapping = TextWrapping.NoWrap;
        text.Children.Add(progress);
        _refreshLive[entry.Job.Id] = (message, progress);
        Grid.SetColumn(text, 1); left.Children.Add(text);
        grid.Children.Add(left);
        var stop = StopPill(T("web.admin.libraries.activeWork.cancelRefresh"), "LibrariesStopRefresh_" + entry.LibraryId, !state.JobsCancelling.Contains(entry.Job.Id));
        stop.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.CancelJobAsync(entry.Job.Id, owner.Lease); };
        if (_phone) { grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Grid.SetRow(stop, 1); stop.HorizontalAlignment = HorizontalAlignment.Stretch; }
        else { grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(stop, 1); }
        grid.Children.Add(stop);
        var row = Surface(grid, Radius2Xl, 16);
        AutomationProperties.SetAutomationId(row, "LibrariesRefresh_" + entry.LibraryId);
        return row;
    }

    private static TextBlock PathLine(string path, double size, bool trim = true)
    {
        var text = Mono(AdminPathText.Isolate(path), size, false);
        if (trim) { text.TextTrimming = TextTrimming.CharacterEllipsis; text.TextWrapping = TextWrapping.NoWrap; }
        return text;
    }

    private FrameworkElement ActivityCard(AdminScanQueueGroup group, AdminLibrariesSnapshot state, LibraryUiOwner owner)
    {
        const int previewLimit = 2;
        var running = group.Scans.Where(scan => scan.IsRunning).ToArray();
        var queued = group.Scans.Where(scan => !scan.IsRunning).ToArray();
        var expanded = _expandedQueues.Contains(group.LibraryId);
        var stack = new StackPanel { Spacing = 12 };

        var head = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        var tile = TypeTile(group.LibraryType, 32, RadiusLg); tile.VerticalAlignment = VerticalAlignment.Center;
        identity.Children.Add(tile);
        var name = Text(group.Name, 14); name.FontWeight = FontWeights.SemiBold; name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.MaxWidth = 420;
        name.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(name, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level3);
        identity.Children.Add(name);
        var counts = new List<string>();
        if (group.Running > 0) counts.Add(T("web.admin.libraries.scans.running", AdminLibrariesText.Count(group.Running)));
        if (group.Queued > 0) counts.Add(T("web.admin.libraries.scans.queued", AdminLibrariesText.Count(group.Queued)));
        identity.Children.Add(Badge(string.Join(" · ", counts), BadgeTone.Secondary, fontSize: 11));
        head.Children.Add(identity);
        var stop = StopPill(T("web.admin.libraries.activeWork.cancelScans"), "LibrariesStopScans_" + group.LibraryId, !state.ScanCancelling.Contains(group.LibraryId));
        stop.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.CancelScansAsync(group.LibraryId, owner.Lease); };
        head.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(stop, 1);
        head.Children.Add(stop);
        stack.Children.Add(head);

        if (running.Length > 0)
        {
            var list = new StackPanel { Spacing = 8 };
            foreach (var scan in running) list.Children.Add(RunningScan(scan));
            stack.Children.Add(list);
        }

        if (queued.Length > 0)
        {
            var box = new StackPanel { Spacing = 8 };
            var line = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var flow = new LogsWrapPanel { Spacing = 6, RowSpacing = 2 };
            var queuedCount = Text(T("web.admin.libraries.scans.queued", AdminLibrariesText.Count(queued.Length)), 12); queuedCount.FontWeight = FontWeights.Medium;
            flow.Children.Add(queuedCount);
            flow.Children.Add(Text("—", 12, true));
            // The whole preview is one left-to-right run (official `<span dir="ltr">`): in a right-to-left interface the paths keep their order and their commas.
            var preview = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, FlowDirection = FlowDirection.LeftToRight };
            var targets = queued.Take(previewLimit).Select(scan => P.ScanTarget(scan.Path)).ToArray();
            for (var index = 0; index < targets.Length; index++)
            {
                if (index > 0) preview.Children.Add(Text(", ", 11, true, mono: true));
                var target = PathLine(targets[index], 11); target.MaxWidth = 280;
                preview.Children.Add(target);
            }
            flow.Children.Add(preview);
            if (queued.Length > previewLimit)
                flow.Children.Add(Text(T("web.admin.libraries.scanQueue.more", AdminLibrariesText.Count(queued.Length - previewLimit)), 12, true));
            line.Children.Add(flow);
            if (queued.Length > previewLimit || expanded)
            {
                var toggleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                toggleRow.Children.Add(Text(expanded ? T("web.admin.libraries.activity.hideQueue") : T("web.admin.libraries.activeWork.showAll", AdminLibrariesText.Count(queued.Length)), 11, true));
                toggleRow.Children.Add(Icon(expanded ? "chevron-up" : "chevron-down", 12, Brush("SecondaryTextBrush")));
                var toggle = new Button
                {
                    Content = toggleRow, Style = (Style)Application.Current.Resources["GhostButtonStyle"], MinHeight = _wide ? 24 : 36, Padding = new(8, 0, 8, 0), CornerRadius = new(RadiusSm),
                    VerticalAlignment = VerticalAlignment.Top,
                };
                AutomationProperties.SetName(toggle, expanded ? T("web.admin.libraries.activity.hideQueue") : T("web.admin.libraries.activeWork.showAll", AdminLibrariesText.Count(queued.Length)));
                AutomationProperties.SetAutomationId(toggle, "LibrariesQueueToggle_" + group.LibraryId);
                AutomationProperties.SetItemStatus(toggle, expanded ? "expanded" : "collapsed");
                toggle.Click += (_, _) =>
                {
                    if (!owner.IsCurrent) return;
                    if (!_expandedQueues.Remove(group.LibraryId)) _expandedQueues.Add(group.LibraryId);
                    if (_controller != null) Render(_controller.State);
                };
                line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(toggle, 1);
                line.Children.Add(toggle);
            }
            box.Children.Add(line);
            if (expanded)
            {
                var list = new StackPanel { Spacing = 4 };
                foreach (var scan in queued)
                {
                    var row = new Grid { ColumnSpacing = 8 };
                    row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    row.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new(3), Background = Tint("SecondaryTextBrush", 0.4), VerticalAlignment = VerticalAlignment.Center });
                    var mode = Text(P.ScanMode(scan.Mode), 11, true); Grid.SetColumn(mode, 1); row.Children.Add(mode);
                    var target = PathLine(P.ScanTarget(scan.Path), 11); Grid.SetColumn(target, 2); row.Children.Add(target);
                    var waiting = Text(T("web.admin.libraries.scanQueue.waiting"), 10, true); Grid.SetColumn(waiting, 3); row.Children.Add(waiting);
                    if (scan.Path != null) ToolTipService.SetToolTip(row, scan.Path);
                    AutomationProperties.SetAutomationId(row, "LibrariesQueueScan_" + scan.Id);
                    list.Children.Add(row);
                }
                box.Children.Add(new Border { Child = new ScrollViewer { Content = list, MaxHeight = 224, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, BorderBrush = Tint("BorderBrush", 0.4), BorderThickness = new(0, 1, 0, 0), Padding = new(0, 8, 0, 0) });
            }
            stack.Children.Add(DashedFrame(new Border { Child = box, Padding = new(12, 8, 12, 8) }, RadiusXl, Tint("BorderBrush", 0.6), null));
        }

        var card = Surface(stack, Radius2Xl, 16);
        AutomationProperties.SetAutomationId(card, "LibrariesActivityCard_" + group.LibraryId);
        return card;
    }

    private FrameworkElement RunningScan(AdminLibraryScanRun scan)
    {
        var box = new StackPanel { Spacing = 8 };
        var top = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
        top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new(4), Background = Solid(Success), VerticalAlignment = VerticalAlignment.Center });
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var mode = Text(P.ScanMode(scan.Mode), 12); mode.FontWeight = FontWeights.Medium;
        label.Children.Add(mode);
        var target = PathLine(P.ScanTarget(scan.Path), 12); target.MaxWidth = 420;
        if (scan.Path != null) ToolTipService.SetToolTip(target, scan.Path);
        label.Children.Add(target);
        left.Children.Add(label);
        if (!string.IsNullOrEmpty(scan.Trigger)) left.Children.Add(Badge(P.ScanTrigger(scan.Trigger), BadgeTone.Secondary, fontSize: 10));
        top.Children.Add(left);
        var status = Text(ScanStatusText(scan, DateTimeOffset.UtcNow), 11, true);
        status.TextWrapping = TextWrapping.NoWrap; status.VerticalAlignment = VerticalAlignment.Center;
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(status, 1);
        top.Children.Add(status);
        box.Children.Add(top);
        ProgressBar? bar = null;
        if (AdminLibraryTabs.ScanProgressPercent(scan) is { } percent)
        {
            // role=progressbar, aria-labelledby the mode and target (official #5087 review fix).
            bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = percent, Height = 6, MinHeight = 6, Foreground = Solid(Success), Background = Tint("BorderBrush", 0.7), CornerRadius = new(3) };
            AutomationProperties.SetLabeledBy(bar, label);
            AutomationProperties.SetName(bar, P.ScanMode(scan.Mode) + " " + P.ScanTarget(scan.Path));
            AutomationProperties.SetAutomationId(bar, "LibrariesScanProgress_" + scan.Id);
            box.Children.Add(bar);
        }
        _scanLive[scan.Id] = (status, bar);
        var frame = new Border
        {
            Child = box, Padding = new(12, 10, 12, 10), CornerRadius = new(RadiusXl), BorderThickness = new(1),
            BorderBrush = Tint("BorderBrush", 0.5), Background = Tint("SecondaryBackgroundBrush", 0.4),
        };
        AutomationProperties.SetAutomationId(frame, "LibrariesRunningScan_" + scan.Id);
        return frame;
    }

    /// <summary>Official <c>JobPageControls</c> (the Scan Activity footer): the failed-load message with Restart jobs, then Load older refresh jobs.</summary>
    private void AddJobFooter(StackPanel panel, AdminLibrariesSnapshot state, LibraryUiOwner owner)
    {
        var footer = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        if (state.RefreshJobsFailed)
        {
            var alert = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            AutomationProperties.SetLiveSetting(alert, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
            AutomationProperties.SetAutomationId(alert, "LibrariesJobsFailed");
            foreach (var (text, tag) in AdminLibrariesText.Segments(T("web.admin.common.jobPageControls.loadFailed")))
            {
                if (tag == "button")
                {
                    var restart = Action(text, "LibrariesRestartJobs");
                    restart.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.RestartJobsAsync(); };
                    alert.Children.Add(restart);
                }
                else if (text.Trim().Length > 0) alert.Children.Add(Text(text.Trim()));
            }
            footer.Children.Add(alert);
        }
        if (state.RefreshJobsHasMore)
        {
            var older = Action(T("web.admin.libraries.page.loadOlderRefreshJobs"), "LibrariesLoadOlderJobs");
            older.IsEnabled = !state.RefreshJobsLoadingMore;
            older.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.LoadOlderJobsAsync(); };
            footer.Children.Add(older);
        }
        if (state.RefreshJobsCapped) footer.Children.Add(Text(T("admin.native.libraries.pageCap", AdminLibrariesText.Count(AdminLibrariesController.MaxRefreshJobPages)), 12, true));
        if (footer.Children.Count == 0) return;
        panel.Children.Add(new Border { Child = footer, Padding = new(12) });
    }

    // ── Catalog Health ───────────────────────────────────────────────────

    private static string TileLabel(AdminHealthSection section) => T(section switch
    {
        AdminHealthSection.Matcher => "web.admin.common.libraryEditor.matcher.title",
        AdminHealthSection.Ambiguous => "web.admin.libraries.ambiguousRoots.title",
        AdminHealthSection.Skipped => "web.admin.libraries.skippedRoots.title",
        AdminHealthSection.Stale => "web.admin.libraries.staleIds.title",
        _ => "web.admin.libraries.unmatched.title",
    });

    private void BuildHealthTiles()
    {
        for (var index = 0; index < TileOrder.Length; index++)
        {
            var section = TileOrder[index];
            var (icon, tone, tint) = section switch
            {
                AdminHealthSection.Matcher => ("wrench", Amber, Amber),
                AdminHealthSection.Ambiguous => ("folder-open", Amber, Amber),
                AdminHealthSection.Skipped => ("alert-triangle", Amber, Amber),
                AdminHealthSection.Stale => ("unlink", Color.FromArgb(255, 248, 113, 113), Color.FromArgb(255, 239, 68, 68)),
                _ => ("search", Amber, Amber),
            };
            var count = new ContentControl { VerticalAlignment = VerticalAlignment.Center, IsTabStop = false, HorizontalAlignment = HorizontalAlignment.Left };
            var iconTile = new Border
            {
                Width = 28, Height = 28, CornerRadius = new(RadiusLg), Background = Solid(tint, 0.10), VerticalAlignment = VerticalAlignment.Center,
                Child = new Border { Child = Icon(icon, 16, Solid(tone)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var top = new Grid { ColumnSpacing = 8 };
            top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            top.Children.Add(count); Grid.SetColumn(iconTile, 1); top.Children.Add(iconTile);
            var label = Text(TileLabel(section), 12); label.FontWeight = FontWeights.SemiBold; label.TextWrapping = TextWrapping.Wrap;
            var content = new Grid { RowSpacing = 8 };
            content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            content.Children.Add(top); Grid.SetRow(label, 1); label.VerticalAlignment = VerticalAlignment.Bottom; content.Children.Add(label);
            // Stretch in both directions: the tiles of a row share its height (grid items stretch), so a label that wraps at 1024 does not make one tile taller than its neighbours.
            var button = new SelectableButton
            {
                Content = content, Style = (Style)Application.Current.Resources["GhostButtonStyle"], MinHeight = 80, Padding = new(14), CornerRadius = new(Radius2Xl), BorderThickness = new(1),
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                SelectionContainer = _healthTiles, // a Radix tab list: the tiles are tabs (role=tab)
            };
            AutomationProperties.SetAutomationId(button, "LibrariesTile_" + section);
            AutomationProperties.SetPositionInSet(button, index + 1);
            AutomationProperties.SetSizeOfSet(button, TileOrder.Length);
            var captured = section; var position = index;
            button.Choose = () => SelectHealth(captured);
            button.Click += (_, _) => { SelectHealth(captured); if (button.FocusState == FocusState.Unfocused) button.Focus(FocusState.Pointer); };
            button.KeyDown += (_, args) => OnTabKey(args, position, TileOrder.Select(entry => (Button)_tiles[entry].Button).ToArray(), target => SelectHealth(TileOrder[target]));
            _tiles[section] = (button, count, iconTile);
            _healthTiles.Children.Add(button);
        }
        AutomationProperties.SetName(_healthTiles, T("web.admin.libraries.tabs.health")); // the tab list's aria-label
        LayoutHealthTiles();
    }

    /// <summary>grid-cols-2 below sm (640 px), 3 from sm, 5 from lg (1024 px).</summary>
    private void LayoutHealthTiles()
    {
        var columns = _wide ? 5 : _phone ? 2 : 3;
        _healthTiles.ColumnDefinitions.Clear(); _healthTiles.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++) _healthTiles.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var row = 0; row < (TileOrder.Length + columns - 1) / columns; row++) _healthTiles.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < TileOrder.Length; index++)
        {
            var button = _tiles[TileOrder[index]].Button;
            Grid.SetRow(button, index / columns); Grid.SetColumn(button, index % columns);
        }
    }

    private void SelectHealth(AdminHealthSection section)
    {
        if (_health == section) return;
        _health = section;
        UpdateHealthOpen();
        RenderHealthTiles();
    }

    /// <summary>Only the selected tile's section is open: the heavy root and stale reads wait for their tile, and a tile left stops polling its detail.</summary>
    private void UpdateHealthOpen()
    {
        bool Shown(AdminHealthSection section) => _tab == AdminLibrariesTab.Health && _health == section;
        _ = _unmatched?.SetOpenAsync(Shown(AdminHealthSection.Unmatched));
        _matcher?.SetOpen(Shown(AdminHealthSection.Matcher));
        _ = _ambiguous?.SetOpenAsync(Shown(AdminHealthSection.Ambiguous));
        _ = _skipped?.SetOpenAsync(Shown(AdminHealthSection.Skipped));
        _ = _stale?.SetOpenAsync(Shown(AdminHealthSection.Stale));
        _healthBody.Content = _healthCards.GetValueOrDefault(_health);
        RenderHealthTiles();
        RenderDiagnosticsLayout();
    }

    /// <summary>The tile's count as the official panel publishes it: a search-filtered total never replaces the catalog-wide one.</summary>
    public (long? Count, bool Error) TileStatus(AdminHealthSection section)
    {
        switch (section)
        {
            case AdminHealthSection.Unmatched:
                return _unmatched == null ? (null, false) : (AdminLibraryTabs.UnmatchedTotal(_unmatched.State), AdminLibraryTabs.UnmatchedTileError(_unmatched.State));
            case AdminHealthSection.Matcher:
                return _matcher == null ? (null, false) : (AdminLibraryTabs.MatcherTotal(_matcher.State), AdminLibraryTabs.MatcherTileError(_matcher.State));
            case AdminHealthSection.Ambiguous when _ambiguous != null:
            {
                var state = _ambiguous.State;
                if (state.Search.Trim().Length == 0)
                    _tileStatus[section] = (AdminLibraryTabs.AmbiguousTileCount(Libraries.Count, state.Total == null && !state.LoadFailed, state.Total ?? 0), state.LoadFailed);
                break;
            }
            case AdminHealthSection.Skipped when _skipped != null:
            {
                var state = _skipped.State;
                if (state.Search.Trim().Length == 0) _tileStatus[section] = (state.Total, state.LoadFailed);
                break;
            }
            case AdminHealthSection.Stale when _stale != null:
            {
                var state = _stale.State;
                if (state.Search.Trim().Length == 0) _tileStatus[section] = (state.Total, state.LoadFailed);
                break;
            }
        }
        return _tileStatus.GetValueOrDefault(section, (null, false));
    }

    private void RenderHealthTiles()
    {
        foreach (var section in TileOrder)
        {
            var (button, host, _) = _tiles[section];
            var (count, error) = TileStatus(section);
            var selected = section == _health;
            string spoken;
            if (error)
            {
                var mark = new TextBlock { Text = "!", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Solid(Destructive), VerticalAlignment = VerticalAlignment.Center };
                spoken = T("web.admin.common.diagnosticsSection.countError");
                AutomationProperties.SetName(mark, spoken);
                host.Content = mark;
            }
            else if (count is { } number)
            {
                var value = new TextBlock { Text = NumberText(number), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = number == 0 ? Brush("SecondaryTextBrush") : Brush("PrimaryTextBrush"), VerticalAlignment = VerticalAlignment.Center };
                spoken = NumberText(number);
                host.Content = value;
            }
            else
            {
                spoken = T("web.admin.libraries.health.inspect");
                var inspect = Text(spoken, 12, true); inspect.FontWeight = FontWeights.Medium;
                host.Content = inspect;
            }
            button.MinHeight = 80;
            button.Background = selected ? Tint("SecondaryBackgroundBrush", 0.5) : Tint("SurfaceBrush", 0.9);
            button.BorderBrush = selected ? Tint("PrimaryTextBrush", 0.6) : Tint("BorderBrush", 0.5);
            // The selected tile is the grid's one tab stop (arrows move between tiles); selection is exposed through the SelectionItem pattern.
            button.IsTabStop = selected;
            button.ChangeSelection(selected);
            AutomationProperties.SetName(button, spoken + " " + TileLabel(section));
        }
        RenderTabsFromState();
    }

    private void RenderTabsFromState()
    {
        if (_controller == null || _controller.State.Owner == null || !_controller.State.Loaded) return;
        var state = _controller.State;
        var totals = AdminLibraryTabs.Totals(AdminLibrariesController.ScanGroups(state), AdminLibrariesController.ActivityRefreshes(state));
        RenderTabs(state, totals);
    }

    /// <summary>A diagnostics panel changed (a total arrived, a read failed): the tile counts and the tab badge follow, wherever the admin is.</summary>
    private void OnHealthChanged() => EnqueueUi(() => { if (_controller != null && IsLoaded) RenderHealthTiles(); });
}
