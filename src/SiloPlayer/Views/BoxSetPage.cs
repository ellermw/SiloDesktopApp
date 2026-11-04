using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
namespace SiloPlayer.Views;

public sealed partial class BoxSetPage : Page, IRecipient<MediaSurfaceChanged>, IRecipient<PlaybackProgressUpdated>
{
    public BoxSetViewModel ViewModel { get; }
    public int? CurrentLibraryId => int.TryParse(ViewModel.Detail?.LibraryId, out var id) ? id : null;
    public StackPanel Body { get; } = new() { Spacing = 20, Margin = new Thickness(24), MaxWidth = 1400, HorizontalAlignment = HorizontalAlignment.Stretch };
    private SiloPlayer.Core.Models.Catalog.BoxSetDetail? _knownMembership;
    private BoxSetDetail? _renderedSet;
    private TextBlock? _title, _counts, _overview, _playText, _refreshNotice;
    private Button? _playButton, _refreshRetry;
    private ItemsControl? _movies;
    private bool _playPending;
    private bool _active;
    private long _visit;
    private readonly CatalogApi _catalog;
    private readonly SiloApiClient _client;
    public BoxSetPage() : this(App.Services.GetRequiredService<CatalogApi>(), App.Services.GetRequiredService<SiloApiClient>()) { }
    public BoxSetPage(CatalogApi catalog, SiloApiClient client)
    {
        InitializeComponent();
        _catalog = catalog; _client = client; ViewModel = new(catalog);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var back = new Button { Name = "BoxSetBack", Content = new FontIcon { Glyph = "\uE72B" }, Width = 40, Height = 40, Margin = new Thickness(24, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(back, "Go back");
        ToolTipService.SetToolTip(back, "Go back");
        back.Click += (_, _) =>
        {
            var navigation = App.Services.GetRequiredService<NavigationService>();
            if (navigation.CanGoBack) navigation.GoBack(); else navigation.Navigate<HomePage>();
        };
        root.Children.Add(back);
        var scroll = new ScrollViewer { Content = Body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll); Content = root;
        ViewModel.PropertyChanged += (_, _) => Render();
        Loaded += (_, _) => { _active = true; _visit++; WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(this); WeakReferenceMessenger.Default.Register<PlaybackProgressUpdated>(this); _catalog.LibraryCacheInvalidated += Refresh; _client.LocalizedReads.Invalidated += LanguageChanged; };
        Unloaded += (_, _) => { _active = false; _visit++; _knownMembership = null; WeakReferenceMessenger.Default.UnregisterAll(this); _catalog.LibraryCacheInvalidated -= Refresh; _client.LocalizedReads.Invalidated -= LanguageChanged; ViewModel.Cancel(); };
    }
    private void Refresh()
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(Refresh); return; }
        if (_active) _ = ViewModel.RefreshAsync();
    }
    private void LanguageChanged(long _) => Refresh();
    public void Receive(MediaSurfaceChanged message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (message.Context is {} context && !_catalog.IsCurrentContext(context)) return;
            // Until the initial read completes, membership is unknown. A change
            // must replace that read rather than let its pre-mutation state win.
            if (!_active || (_knownMembership != null ? !_knownMembership.Items.Any(x => x.ContentId == message.ContentId) : !ViewModel.IsLoading)) return;
            if (message.Kind is MediaSurfaceChangeKind.WatchedMarked or MediaSurfaceChangeKind.WatchedCleared or MediaSurfaceChangeKind.PlaybackProgress or MediaSurfaceChangeKind.ItemMetadataRefreshed) Refresh();
        });
    }
    public void Receive(PlaybackProgressUpdated message) => DispatcherQueue.TryEnqueue(async () =>
    {
        var detail = _knownMembership; var visit = _visit; var owner = _catalog.CaptureIdentityContext();
        if (!_active || (detail != null ? !detail.Items.Any(x => x.ContentId == message.ContentId) : !ViewModel.IsLoading)) return;
        try { await message.Persisted; } catch { return; }
        DispatcherQueue.TryEnqueue(() =>
        {
            // Initial membership may arrive before the final playback write.
            // Recheck it after persistence; an access-cache refresh does not
            // retire this same-viewer invalidation, but a different visit does.
            if (_active && visit == _visit && _catalog.IsCurrentIdentityContext(owner) &&
                (_knownMembership != null ? _knownMembership.Items.Any(x => x.ContentId == message.ContentId) : ViewModel.IsLoading)) Refresh();
        });
    });
    protected override async void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); _knownMembership = null; _visit++; if (e.Parameter is BoxSetNavigationArgs route) await ViewModel.LoadAsync(route.Id, route.LibraryId); }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { _active = false; _visit++; _knownMembership = null; ViewModel.Cancel(); base.OnNavigatedFrom(e); }
    private async Task PlayPrimaryAsync()
    {
        var identity = _client.CaptureIdentityContext(); var visit = _visit;
        await ViewModel.PlayAsync((id, library, owner, ct) =>
        {
            // PlayAsync may have revalidated access and selected a new first
            // unplayed member. Use that refreshed movie, not the old hero target.
            var movie = ViewModel.Detail?.Items.FirstOrDefault(item => item.ContentId == id);
            if (movie is null) throw new InvalidOperationException("This movie is no longer available. Refresh the box set and try again.");
            return MoviePlaybackEntry.PlayAsync(movie, library, owner, ct,
                () => _active && visit == _visit && _client.IsCurrentIdentityContext(identity),
                (target, scope, context, cancellation) => App.Services.GetRequiredService<PlayerService>()
                    .PlayPrimaryAsync(target, scope, context, identity, cancellation));
        });
    }
    public void Render()
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(Render); return; }
        if (ViewModel.Detail is {} visible)
        {
            _knownMembership = visible;
            if (_renderedSet is {} previous && SameLayout(previous, visible)) { UpdateDetail(visible); return; }
        }
        else if (!ViewModel.IsLoading) _knownMembership = null;
        Body.Children.Clear();
        _renderedSet = null;
        if (ViewModel.Detail == null && ViewModel.IsLoading)
        {
            Body.Children.Add(new ProgressRing { IsActive = true });
            Body.Children.Add(new SkeletonPoster { Width = 160, Height = 240 });
            Body.Children.Add(new SkeletonText { Width = 300, Height = 32 });
            Body.Children.Add(new TextBlock { Text = "Loading box set…" }); return;
        }
        if (ViewModel.Detail == null && ViewModel.Failure != DetailFailureKind.None)
        {
            var unavailable = ViewModel.Failure is DetailFailureKind.NotFound or DetailFailureKind.AccessDenied;
            Body.Children.Add(new TextBlock { Text = unavailable ? "Box set unavailable" : "Could not load box set", FontSize = 24, TextWrapping = TextWrapping.Wrap });
            if (ViewModel.Failure == DetailFailureKind.AccessDenied)
                Body.Children.Add(new TextBlock { Text = DetailFailurePolicy.Message(ViewModel.Failure), TextWrapping = TextWrapping.Wrap });
            if (!unavailable) { var retry = new Button { Content = "Try again" }; retry.Click += async (_, _) => await ViewModel.RefreshAsync(); Body.Children.Add(retry); } return;
        }
        if (ViewModel.Detail is not {} set) return;
        var hero = new Grid { MinHeight = 220 };
        if (!string.IsNullOrWhiteSpace(set.BackdropUrl))
        {
            var backdrop = new Image { Source = new SiloPlayer.Converters.UrlToImageSourceConverter().Convert(set.BackdropUrl, typeof(ImageSource), null!, "") as ImageSource, Stretch = Stretch.UniformToFill, Opacity = .22, MaxHeight = 300 };
            Grid.SetColumnSpan(backdrop, 2); hero.Children.Add(backdrop);
        }
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hero.ColumnDefinitions.Add(new ColumnDefinition());
        var info = new StackPanel { Spacing = 12, Padding = new Thickness(12) };
        var artwork = new Grid { Width = 160, Height = 240, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 12, 12) };
        var fallback = new DefaultArtwork();
        fallback.Reset("movie", set.PosterThumbhash);
        _ = fallback.ShowThumbhashAsync();
        artwork.Children.Add(fallback);
        if (!string.IsNullOrWhiteSpace(set.PosterUrl)) artwork.Children.Add(new Image { Source = new SiloPlayer.Converters.UrlToImageSourceConverter().Convert(set.PosterUrl, typeof(ImageSource), null!, "") as ImageSource, Stretch = Stretch.UniformToFill });
        hero.Children.Add(artwork); Grid.SetColumn(info, 1);
        info.Children.Add(new TextBlock { Text = "Box set" });
        info.Children.Add(_title = new TextBlock { FontSize = 32, TextWrapping = TextWrapping.Wrap });
        info.Children.Add(_counts = new TextBlock { TextWrapping = TextWrapping.Wrap });
        info.Children.Add(_overview = new TextBlock { TextWrapping = TextWrapping.Wrap });
        var actionText = set.PlayTarget is {} next && !string.IsNullOrWhiteSpace(next.Title)
            ? (set.PlayLabel == "Continue" ? "Continue · " : "Play ") + next.Title : set.PlayLabel;
        var play = new Button { Name = "BoxSetPlay", Style = (Style)Application.Current.Resources["AccentButtonStyle"], Height = 44, CornerRadius = new CornerRadius(22), Padding = new Thickness(24, 0, 24, 0), Content = new TextBlock { Text = actionText, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 400 }, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 440, IsEnabled = set.PlayTarget != null && ViewModel.CanPlay };
        AutomationProperties.SetName(play, actionText);
        _playButton = play; _playText = (TextBlock)play.Content;
        play.Click += async (_, _) =>
        {
            if (!play.IsEnabled || _playPending) return;
            var visit = _visit;
            _playPending = true; play.IsEnabled = false;
            try { await PlayPrimaryAsync(); }
            catch (Exception) when (!_active || visit != _visit) { }
            catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex); }
            finally { _playPending = false; if (ViewModel.Detail is {} current && _renderedSet != null) UpdateDetail(current); }
        };
        info.Children.Add(play); hero.Children.Add(info); Body.Children.Add(hero);
        hero.SizeChanged += (_, _) => { var narrow = hero.ActualWidth < 400; artwork.Width = hero.ActualWidth < 600 ? 110 : 160; artwork.Height = artwork.Width * 1.5; hero.ColumnDefinitions[0].Width = narrow ? new GridLength(0) : GridLength.Auto; artwork.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible; };
        Body.Children.Add(new TextBlock { Text = "Movies", FontSize = 24 });
        var movies = new ItemsControl { ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal'/></ItemsPanelTemplate>") };
        foreach (var movie in set.Items.Where(BoxSetDetail.IsMovieMember)) movies.Items.Add(new PosterCard { MediaItem = movie, Margin = new Thickness(0, 0, 16, 16) });
        _movies = movies; Body.Children.Add(movies);
        if (set.Items.Count == 0) Body.Children.Add(new TextBlock { Text = "No movies available." });
        Body.Children.Add(_refreshNotice = new TextBlock { Text = "Could not refresh box set. Showing the last loaded details.", TextWrapping = TextWrapping.Wrap });
        _refreshRetry = new Button { Content = "Try again" };
        _refreshRetry.Click += async (_, _) => await ViewModel.RefreshAsync();
        Body.Children.Add(_refreshRetry);
        UpdateDetail(set);
    }
    private static bool SameLayout(BoxSetDetail previous, BoxSetDetail next) => previous.Id == next.Id && previous.LibraryId == next.LibraryId
        && LibraryPosterBinding.HasSameArtworkUrl(previous.PosterUrl, next.PosterUrl) && previous.PosterThumbhash == next.PosterThumbhash
        && LibraryPosterBinding.HasSameArtworkUrl(previous.BackdropUrl, next.BackdropUrl)
        && previous.Items.Where(BoxSetDetail.IsMovieMember).Select(x => x.ContentId).SequenceEqual(next.Items.Where(BoxSetDetail.IsMovieMember).Select(x => x.ContentId));
    private void UpdateDetail(BoxSetDetail set)
    {
        _title!.Text = set.Title;
        _counts!.Text = $"{set.MovieCount} films  ·  {set.Years}  ·  {set.ProgressText}";
        _overview!.Text = set.Overview;
        var actionText = set.PlayTarget is {} next && !string.IsNullOrWhiteSpace(next.Title)
            ? (set.PlayLabel == "Continue" ? "Continue · " : "Play ") + next.Title : set.PlayLabel;
        _playText!.Text = actionText;
        AutomationProperties.SetName(_playButton!, actionText);
        _playButton!.IsEnabled = !_playPending && set.PlayTarget != null && ViewModel.CanPlay;
        foreach (var (card, movie) in _movies!.Items.OfType<PosterCard>().Zip(set.Items.Where(BoxSetDetail.IsMovieMember)))
            if (!ReferenceEquals(card.MediaItem, movie)) card.RefreshItem(movie);
        var notice = ViewModel.Failure != DetailFailureKind.None && !ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        _refreshNotice!.Visibility = notice; _refreshRetry!.Visibility = notice;
        _renderedSet = set;
    }
}
