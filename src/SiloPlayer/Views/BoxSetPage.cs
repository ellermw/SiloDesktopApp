using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
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
    private bool _active;
    private long _visit;
    private readonly CatalogApi _catalog;
    private readonly SiloApiClient _client;
    public BoxSetPage() : this(App.Services.GetRequiredService<CatalogApi>(), App.Services.GetRequiredService<SiloApiClient>()) { }
    public BoxSetPage(CatalogApi catalog, SiloApiClient client)
    {
        InitializeComponent();
        _catalog = catalog; _client = client; ViewModel = new(catalog);
        Content = new ScrollViewer { Content = Body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        ViewModel.PropertyChanged += (_, _) => Render();
        Loaded += (_, _) => { _active = true; _visit++; WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(this); WeakReferenceMessenger.Default.Register<PlaybackProgressUpdated>(this); _catalog.LibraryCacheInvalidated += Refresh; _client.LocalizedReads.Invalidated += LanguageChanged; };
        Unloaded += (_, _) => { _active = false; _visit++; WeakReferenceMessenger.Default.UnregisterAll(this); _catalog.LibraryCacheInvalidated -= Refresh; _client.LocalizedReads.Invalidated -= LanguageChanged; ViewModel.Cancel(); };
    }
    private async void Refresh() { if (_active) await ViewModel.RefreshAsync(); }
    private void LanguageChanged(long _) => DispatcherQueue.TryEnqueue(Refresh);
    public void Receive(MediaSurfaceChanged message)
    {
        if (message.Context is {} context && !_catalog.IsCurrentContext(context)) return;
        if (message.Kind is MediaSurfaceChangeKind.WatchedMarked or MediaSurfaceChangeKind.WatchedCleared or MediaSurfaceChangeKind.PlaybackProgress or MediaSurfaceChangeKind.ItemMetadataRefreshed) DispatcherQueue.TryEnqueue(Refresh);
    }
    public async void Receive(PlaybackProgressUpdated message)
    {
        var detail = ViewModel.Detail; var visit = _visit; var owner = _catalog.CaptureContext();
        if (!_active || detail?.Items.Any(x => x.ContentId == message.ContentId) != true) return;
        try { await message.Persisted; } catch { return; }
        if (_active && visit == _visit && _catalog.IsCurrentContext(owner) && ViewModel.IsCurrent
            && ViewModel.Detail is {} current && current.Id == detail.Id && current.LibraryId == detail.LibraryId) await ViewModel.RefreshAsync();
    }
    protected override async void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); if (e.Parameter is BoxSetNavigationArgs route) await ViewModel.LoadAsync(route.Id, route.LibraryId); }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { _active = false; _visit++; ViewModel.Cancel(); base.OnNavigatedFrom(e); }
    public void Render()
    {
        Body.Children.Clear();
        if (ViewModel.IsLoading)
        {
            Body.Children.Add(new ProgressRing { IsActive = true });
            Body.Children.Add(new SkeletonPoster { Width = 160, Height = 240 });
            Body.Children.Add(new SkeletonText { Width = 300, Height = 32 });
            Body.Children.Add(new TextBlock { Text = "Loading box set…" }); return;
        }
        if (ViewModel.Failure != DetailFailureKind.None)
        {
            Body.Children.Add(new TextBlock { Text = ViewModel.Failure == DetailFailureKind.NotFound ? "Box set unavailable" : "Could not load box set", FontSize = 24, TextWrapping = TextWrapping.Wrap });
            if (ViewModel.Failure != DetailFailureKind.NotFound) { var retry = new Button { Content = "Try again" }; retry.Click += async (_, _) => await ViewModel.RefreshAsync(); Body.Children.Add(retry); } return;
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
        info.Children.Add(new TextBlock { Text = set.Title, FontSize = 32, TextWrapping = TextWrapping.Wrap });
        info.Children.Add(new TextBlock { Text = $"{set.MovieCount} films  ·  {set.Years}  ·  {set.ProgressText}", TextWrapping = TextWrapping.Wrap });
        info.Children.Add(new TextBlock { Text = set.Overview, TextWrapping = TextWrapping.Wrap });
        var actionText = set.PlayTarget is {} next && !string.IsNullOrWhiteSpace(next.Title)
            ? (set.PlayLabel == "Continue" ? "Continue · " : "Play ") + next.Title : set.PlayLabel;
        var play = new Button { Name = "BoxSetPlay", Style = (Style)Application.Current.Resources["AccentButtonStyle"], Height = 44, CornerRadius = new CornerRadius(22), Padding = new Thickness(24, 0, 24, 0), Content = new TextBlock { Text = actionText, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 400 }, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 440, IsEnabled = set.PlayTarget != null && ViewModel.IsCurrent };
        AutomationProperties.SetName(play, actionText);
        play.Click += async (_, _) => { try { await ViewModel.PlayAsync((id, library, owner, ct) => App.Services.GetRequiredService<PlayerService>().PlayAsync(id, libraryId: library, expectedContext: owner, cancellationToken: ct)); } catch (OperationCanceledException) { } catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex); } };
        info.Children.Add(play); hero.Children.Add(info); Body.Children.Add(hero);
        hero.SizeChanged += (_, _) => { var narrow = hero.ActualWidth < 400; artwork.Width = hero.ActualWidth < 600 ? 110 : 160; artwork.Height = artwork.Width * 1.5; hero.ColumnDefinitions[0].Width = narrow ? new GridLength(0) : GridLength.Auto; artwork.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible; };
        Body.Children.Add(new TextBlock { Text = "Movies", FontSize = 24 });
        var movies = new ItemsControl { ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal'/></ItemsPanelTemplate>") };
        foreach (var movie in set.Items) if (movie.Type == "movie" && !movie.ContentId.StartsWith("box_set:")) movies.Items.Add(new PosterCard { MediaItem = movie, Margin = new Thickness(0, 0, 16, 16) });
        Body.Children.Add(movies);
        if (set.Items.Count == 0) Body.Children.Add(new TextBlock { Text = "No movies available." });
    }
}
