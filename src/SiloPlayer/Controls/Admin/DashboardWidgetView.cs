using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using static SiloPlayer.Controls.Admin.DashboardUi;

namespace SiloPlayer.Controls.Admin;

/// <summary>Monitor destinations a dashboard link may open. Nothing outside the Monitor scope.</summary>
public enum DashboardDestination { Activity, Logs }

/// <summary>What a widget knows about its placement: its window, its grid mode and its host's callbacks.</summary>
internal sealed class DashboardWidgetContext
{
    public required DashboardDataStore Store { get; init; }
    public required string Id { get; init; }
    public required DashboardWidgetRange Range { get; set; }
    public required Action<DashboardWidgetRange> SetRange { get; init; }
    /// <summary>
    /// The presentation this placement renders: <see cref="DashboardLayoutDocument.EffectiveView"/>
    /// of its layout entry, null for widgets without views. The layout is the only
    /// source; a widget never keeps its own copy.
    /// </summary>
    public string? View { get; set; }
    /// <summary>Stores a picked view in the layout entry (the range picker's debounced save); the layout echoes it back.</summary>
    public required Action<string> SetView { get; init; }
    public required Action<bool> SetCollapsed { get; init; }
    public required Func<DashboardGridMode> Mode { get; init; }
    public required Func<double> ViewportWidth { get; init; }
    /// <summary>Null when no host handles the destination; links then render as plain text.</summary>
    public required Func<DashboardDestination, Action?> Navigate { get; init; }
    /// <summary>
    /// Opens another admin page by its WebUI href (e130f262d widget links: History, Users, Libraries, Autoscan, Nodes, Tasks). Null, or a null result, when the
    /// destination has no native page or no host handles it: the link then renders as the same plain text.
    /// </summary>
    public Func<string, Action?>? OpenHref { get; init; }
    /// <summary>Opens a catalog item's detail page (the official <c>/item/{id}</c> links); null result when there is no item or no host.</summary>
    public Func<string, Action?>? OpenItem { get; init; }
    /// <summary>Optional compact session controls supplied by the Activity surface owner.</summary>
    public Func<DashboardSession, FrameworkElement?>? SessionActions { get; init; }
    public required Func<string?, string?> ResolveImage { get; init; }
    public required Func<XamlRoot?> Root { get; init; }
    /// <summary>Registers an open dialog so authority retirement can close it.</summary>
    public required Func<ContentDialog, IDisposable> TrackDialog { get; init; }
    public bool IsCustomizing { get; set; }
}

/// <summary>
/// Base for the 34 widgets. A widget mounts the queries it renders (so only
/// placed widgets fetch or poll), re-renders when one of them or a named
/// trigger changes, and drops everything when detached.
/// </summary>
internal abstract class DashboardWidgetView : Grid
{
    protected DashboardWidgetContext C { get; }
    private readonly List<IDisposable> _mounts = [];
    private HashSet<string> _keys = new(StringComparer.Ordinal);
    private bool _attached;
    private bool _renderQueued;

    protected DashboardWidgetView(DashboardWidgetContext context)
    {
        C = context;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    protected DashboardDataStore Store => C.Store;
    protected DateTimeOffset Now => Store.Now;
    protected bool Fixed => C.Mode() == DashboardGridMode.Twelve;
    /// <summary>Below lg a fill-height chart keeps a fixed-height chart's 10rem.</summary>
    protected double ChartMinHeight => Fixed ? 0 : DashboardGridPlacement.StackedChartMinHeight;
    protected DashboardWidgetRange Range => C.Range;
    /// <summary>The stored view to render (see <see cref="DashboardWidgetContext.View"/>).</summary>
    public string? View => C.View;
    /// <summary>Completed renders; fixtures use it to prove a resize settles instead of re-rendering in a loop.</summary>
    public int RenderCount { get; private set; }

    protected abstract IEnumerable<string> Keys { get; }
    protected virtual IEnumerable<string> RenderTriggers => [];
    protected abstract FrameworkElement Render();

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        Store.Changed += OnStoreChanged;
        Remount();
        RenderNow();
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        Store.Changed -= OnStoreChanged;
        foreach (var mount in _mounts) mount.Dispose();
        _mounts.Clear();
        C.SetCollapsed(false);
    }

    /// <summary>The window changed: mount the new range's queries and draw.</summary>
    public void SetRange(DashboardWidgetRange range)
    {
        if (C.Range == range) return;
        C.Range = range;
        if (!_attached) return;
        Remount();
        RenderNow();
    }

    /// <summary>
    /// The layout entry's view changed (a pick, Reload, Reset or another
    /// device's layout): draw it. Views change presentation only, so nothing
    /// remounts.
    /// </summary>
    public void SetView(string? view)
    {
        if (string.Equals(C.View, view, StringComparison.Ordinal)) return;
        C.View = view;
        if (_attached) RenderNow();
    }

    public void SetCustomizing(bool customizing)
    {
        if (C.IsCustomizing == customizing) return;
        C.IsCustomizing = customizing;
        if (_attached) RenderNow();
    }

    public void Refresh()
    {
        if (_attached) RenderNow();
    }

    /// <summary>Refreshes time-dependent presentation without fetching or invalidating query data.</summary>
    public void RefreshClock()
    {
        if (_attached && Store.IsCurrent) UpdateClock();
    }

    protected virtual void UpdateClock() { }

    protected void Remount()
    {
        foreach (var mount in _mounts) mount.Dispose();
        _mounts.Clear();
        _keys = Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in _keys) _mounts.Add(Store.Mount(key));
    }

    private void OnStoreChanged(string key)
    {
        if (!_attached || (!_keys.Contains(key) && !RenderTriggers.Contains(key))) return;
        if (_renderQueued) return;
        _renderQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _renderQueued = false;
            if (_attached && Store.IsCurrent) RenderNow();
        });
    }

    protected void RenderNow()
    {
        if (!Store.IsCurrent) { Children.Clear(); return; }
        var content = Render();
        if (Children.Count == 0 || !ReferenceEquals(Children[0], content))
        {
            var offsets = Children.Count == 0 ? [] : Scrollers(Children[0])
                .Select(scroll => (scroll.HorizontalOffset, scroll.VerticalOffset)).ToArray();
            foreach (var (scroll, offset) in Scrollers(content).Zip(offsets))
            {
                if (offset == (0d, 0d)) continue;
                scroll.Loaded += (_, _) => scroll.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!scroll.IsLoaded || !Store.IsCurrent) return;
                    scroll.UpdateLayout();
                    scroll.ChangeView(offset.HorizontalOffset, offset.VerticalOffset, null, disableAnimation: true);
                });
            }
            Children.Clear();
            Children.Add(content);
        }
        RenderCount++;
    }

    private static IEnumerable<ScrollViewer> Scrollers(UIElement element)
    {
        if (element is ScrollViewer scroll) yield return scroll;
        IEnumerable<UIElement> children = element switch
        {
            Panel panel => panel.Children,
            Border { Child: { } child } => [child],
            ContentControl { Content: UIElement child } => [child],
            _ => [],
        };
        foreach (var child in children)
            foreach (var nested in Scrollers(child))
                yield return nested;
    }

    protected void Mount(string key)
    {
        if (_keys.Add(key)) _mounts.Add(Store.Mount(key));
    }

    // ---- Shells ---------------------------------------------------------

    /// <summary>shadcn Card filling its cell: header (pb-3), then content after the 24px card gap.</summary>
    protected FrameworkElement Card(FrameworkElement header, FrameworkElement body, bool scroll = true,
        double paddingTop = 24, double paddingBottom = 24, double gap = 24, double headerBottom = 12)
    {
        var grid = new Grid { RowSpacing = gap };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        header.Margin = new Thickness(24, 0, 24, headerBottom);
        grid.Children.Add(header);
        FrameworkElement content = scroll
            ? new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, Padding = new Thickness(24, 0, 24, 0) }
            : new Border { Child = body, Padding = new Thickness(24, 0, 24, 0) };
        Grid.SetRow(content, 1);
        grid.Children.Add(content);
        var card = CardShell(grid, new Thickness(0, paddingTop, 0, paddingBottom));
        card.VerticalAlignment = Fixed ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        return card;
    }

    /// <summary>WidgetHeader: a bold 14px title and a right-aligned group (summary, link, picker).</summary>
    protected static FrameworkElement Header(string title, params FrameworkElement?[] trailing)
    {
        var titleText = Text(title, 14, Bold, wrap: true);
        var group = new DashboardWrapPanel { Spacing = 8, RowSpacing = 8, JustifyEnd = true };
        foreach (var item in trailing)
            if (item != null) { item.VerticalAlignment = VerticalAlignment.Center; group.Children.Add(item); }
        return new DashboardWidgetHeader(titleText, group);
    }

    protected FrameworkElement? RangePicker()
    {
        var ranges = DashboardRegistry.Find(C.Id)?.Ranges;
        if (ranges == null || ranges.Allowed.Count < 2) return null;
        var options = DashboardRanges.Order.Where(ranges.Allowed.Contains)
            .Select(range => (range, DashboardRanges.Label(range), DashboardRanges.ShowLabel(range))).ToList();
        return Segmented(options, Range, C.SetRange);
    }

    protected FrameworkElement Link(string text, DashboardDestination? destination, double size = 11)
        => Destination(text, size, destination is { } d ? C.Navigate(d) : null);

    /// <summary>A header link to another admin page; plain text while that page is not registered.</summary>
    protected FrameworkElement Link(string text, string href, double size = 11)
        => Destination(text, size, C.OpenHref?.Invoke(href));

    /// <summary>The header container widths below which a card hides "Insights ›" (the WebUI's <c>@sm</c>, <c>@md</c> and <c>@lg</c> card-header queries).</summary>
    protected const double HeaderSm = 384, HeaderMd = 448, HeaderLg = 512;

    /// <summary>
    /// "Insights ›" for a card's header, opening <c>/admin/insights?tab=&lt;tab&gt;&amp;range=&lt;window&gt;</c>. Null (nothing rendered, not
    /// plain text) while the Insights page cannot open, and hidden while the header is narrower than <paramref name="minHeaderWidth"/>.
    /// </summary>
    protected FrameworkElement? InsightsLink(AdminInsightsTab tab, double minHeaderWidth)
        => C.OpenHref?.Invoke(DashboardRanges.InsightsHref(tab, Range)) is { } open
            ? new DashboardHeaderGate(minHeaderWidth, Destination(T("web.admin.dashboard.widgets.openInsights"), 11, open))
            : null;

    protected static TextBlock Muted11(string text) => Text(text, 11, color: MutedForeground);

    protected FrameworkElement? Poster(string? url, double width, double height, bool dim = false)
    {
        var resolved = AdminPosterArtwork.WebUrl(C.ResolveImage(url));
        if (resolved == null) return null;
        var (image, artwork) = CreatePoster(width, height);
        image.Opacity = dim ? 0.45 : 1;
        artwork.SetUrl(resolved);
        return image;
    }

    protected (Image Image, AdminPosterArtwork Artwork) CreatePoster(double width, double height)
    {
        var image = new Image { Stretch = Stretch.UniformToFill, Width = width, Height = height };
        return (image, new AdminPosterArtwork(image, Store.Lease, (int)Math.Ceiling(width * 2), source => image.Source = source));
    }

    protected static string T(string key, params (string, object?)[] values) => DashboardText.T(key, values);
    protected static string N(long value) => DashboardFormat.Integer(value);
}

/// <summary>Maps each registry id to its widget.</summary>
internal static class DashboardWidgetFactory
{
    public static DashboardWidgetView Create(DashboardWidgetContext context) => context.Id switch
    {
        DashboardRegistry.StatActiveStreams => new ActiveStreamsTile(context),
        DashboardRegistry.StatEgressNow => new EgressNowTile(context),
        DashboardRegistry.StatTranscodeShare => new TranscodeShareTile(context),
        DashboardRegistry.StatProfilesActive => new ProfilesActiveTile(context),
        DashboardRegistry.StatMovies => new MoviesTile(context),
        DashboardRegistry.StatShows => new ShowsTile(context),
        DashboardRegistry.StatAdvisoryCoverage => new AdvisoryCoverageTile(context),
        DashboardRegistry.StatUsers => new UsersTile(context),
        DashboardRegistry.StatStorage => new StorageTile(context),
        DashboardRegistry.HealthStrip => new HealthStripWidget(context),
        DashboardRegistry.ServerResources => new ServerResourcesWidget(context),
        DashboardRegistry.Playback24h => new PlaybackActivityWidget(context),
        DashboardRegistry.ConcurrentStreams24h => new ConcurrentStreamsWidget(context),
        DashboardRegistry.Egress24h => new EgressWidget(context),
        DashboardRegistry.PlaybackReliability => new PlaybackReliabilityWidget(context),
        DashboardRegistry.TopTitles => new TopTitlesWidget(context),
        DashboardRegistry.TopProfiles => new TopProfilesWidget(context),
        DashboardRegistry.WatchProviders => new WatchProvidersWidget(context),
        DashboardRegistry.NowPlaying => new NowPlayingWidget(context),
        DashboardRegistry.TranscodeNodes => new TranscodeNodesWidget(context),
        DashboardRegistry.Scanner => new ScannerWidget(context),
        DashboardRegistry.Libraries => new LibrariesWidget(context),
        DashboardRegistry.Users => new UsersWidget(context),
        DashboardRegistry.Downloads => new DownloadsWidget(context),
        DashboardRegistry.ScanActivity => new ScanActivityWidget(context),
        DashboardRegistry.RecentErrors => new RecentErrorsWidget(context),
        DashboardRegistry.RecentActivity => new RecentActivityWidget(context),
        DashboardRegistry.ApiClients => new ApiClientsWidget(context),
        DashboardRegistry.InsightsPlays => new InsightsPlaysWidget(context),
        DashboardRegistry.InsightsViewingHours => new InsightsViewingHoursWidget(context),
        DashboardRegistry.InsightsWeekday => new InsightsWeekdayWidget(context),
        DashboardRegistry.InsightsHourOfDay => new InsightsHourOfDayWidget(context),
        DashboardRegistry.InsightsActivePeople => new InsightsActivePeopleWidget(context),
        DashboardRegistry.InsightsLibraryCoverage => new InsightsLibraryCoverageWidget(context),
        _ => throw new ArgumentException("Unknown dashboard widget " + context.Id),
    };
}
