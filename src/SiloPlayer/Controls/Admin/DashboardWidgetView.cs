using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
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
    public required Action<bool> SetCollapsed { get; init; }
    public required Func<DashboardGridMode> Mode { get; init; }
    /// <summary>Null when no host handles the destination; links then render as plain text.</summary>
    public required Func<DashboardDestination, Action?> Navigate { get; init; }
    /// <summary>Optional compact session controls supplied by the Activity surface owner.</summary>
    public Func<DashboardSession, FrameworkElement?>? SessionActions { get; init; }
    public required Func<string?, string?> ResolveImage { get; init; }
    public required Func<XamlRoot?> Root { get; init; }
    /// <summary>Registers an open dialog so authority retirement can close it.</summary>
    public required Func<ContentDialog, IDisposable> TrackDialog { get; init; }
    public bool IsCustomizing { get; set; }
}

/// <summary>
/// Base for the 28 widgets. A widget mounts the queries it renders (so only
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
        Children.Clear();
        Children.Add(content);
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
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = Text(title, 14, Bold, wrap: true);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(titleText);
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var item in trailing)
            if (item != null) { item.VerticalAlignment = VerticalAlignment.Center; group.Children.Add(item); }
        Grid.SetColumn(group, 1);
        grid.Children.Add(group);
        return grid;
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

    protected static TextBlock Muted11(string text) => Text(text, 11, color: MutedForeground);

    protected FrameworkElement? Poster(string? url, double width, double height, bool dim = false)
    {
        var resolved = C.ResolveImage(url);
        if (resolved == null || !Uri.TryCreate(resolved, UriKind.Absolute, out var uri)) return null;
        return new Image
        {
            Source = new BitmapImage(uri) { DecodePixelWidth = (int)Math.Ceiling(width * 2), DecodePixelType = DecodePixelType.Logical },
            Stretch = Stretch.UniformToFill,
            Width = width,
            Height = height,
            Opacity = dim ? 0.45 : 1,
        };
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
        _ => throw new ArgumentException("Unknown dashboard widget " + context.Id),
    };
}
