using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Converters;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class RequestDetailPage : Page
{
    private static readonly UrlToImageSourceConverter RemoteImageConverter = new();
    private readonly RequestsApi _api = App.Services.GetRequiredService<RequestsApi>();
    private readonly CancellationTokenSource _lifetime = new();
    private RequestMediaDetail? _item;
    private RequestFeatureStatus _features = new();
    private RequestDetailNavigation? _navigation;
    private string? _libraryLink;
    private double _recommendationWidth;
    private readonly DispatcherTimer _downloadRefreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly SiloApiClient _downloadRefreshClient = App.Services.GetRequiredService<SiloApiClient>();
    private CancellationTokenSource? _downloadRefreshOwner;
    private ApiRequestContext? _downloadRefreshContext;
    private bool _downloadRefreshBusy;
    private readonly HashSet<string> _pendingActions = [];

    public RequestDetailPage()
    {
        InitializeComponent();
        foreach (var section in new[] { SeasonsSection, CastSection, RecommendationsSection })
        {
            var scroll = section.Children.OfType<ScrollViewer>().Single();
            var index = section.Children.IndexOf(scroll);
            section.Children.RemoveAt(index);
            section.Children.Insert(index, new CarouselRail(scroll));
        }
        _downloadRefreshTimer.Tick += async (_, _) => await RefreshDownloadAsync();
        Loaded += (_, _) => { ApplyHeroColors(); UpdateDownloadRefresh(); };
        Unloaded += (_, _) => StopDownloadRefresh();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not RequestDetailNavigation nav) { Fail("This title could not be identified."); return; }
        _navigation = nav;
        await LoadAsync(nav);
    }

    private async Task LoadAsync(RequestDetailNavigation nav)
    {
        LoadingLayer.Visibility = Visibility.Visible;
        LoadingText.Visibility = Visibility.Collapsed;
        RetryTitleButton.Visibility = Visibility.Collapsed;
        DetailSkeleton.Visibility = Visibility.Visible;
        try
        {
            _item = await _api.GetDetailAsync(nav.MediaType, nav.TmdbId, _lifetime.Token);
            try { _features = await _api.GetStatusAsync(_lifetime.Token); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { throw; }
            catch { _features = new(); }
            var library = await ExternalTitleResolution.ResolveAsync(_item, App.Services.GetRequiredService<ItemDetailPrefetchCache>(), _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            _libraryLink = library.LibraryLink;
            if (library.LibraryItem != null)
            {
                App.Services.GetRequiredService<NavigationService>().NavigateReplacingCurrentEntry<ItemDetailPage>(library.LibraryItem.ContentId);
                return;
            }
            Render(_item);
            LoadingLayer.Visibility = Visibility.Collapsed;
            if (App.MainWindowInstance is MainWindow window) window.SetDynamicTitle(_item.Title);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Request detail load failed: {ex}");
            var kind = DetailFailurePolicy.Classify(ex);
            Fail(kind is DetailFailureKind.NotFound or DetailFailureKind.AccessDenied
                ? "This item isn't available.\nIt may have been removed, or you may not have access to it."
                : "Couldn't load this title.\nTry again in a moment.");
            RetryTitleButton.Visibility = kind == DetailFailureKind.Transient ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void RetryTitle_Click(object sender, RoutedEventArgs e) { if (_navigation != null) await LoadAsync(_navigation); }

    protected override void OnNavigatedFrom(NavigationEventArgs e) { StopDownloadRefresh(); _lifetime.Cancel(); base.OnNavigatedFrom(e); }

    private void StopDownloadRefresh()
    {
        _downloadRefreshTimer.Stop();
        _downloadRefreshOwner?.Cancel(); _downloadRefreshOwner?.Dispose();
        _downloadRefreshOwner = null; _downloadRefreshContext = null;
    }
    private void UpdateDownloadRefresh()
    {
        if (!IsLoaded || _lifetime.IsCancellationRequested || _navigation == null || _item?.Request.Download == null)
        { StopDownloadRefresh(); return; }
        if (_downloadRefreshOwner != null) return;
        _downloadRefreshOwner = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _downloadRefreshContext = _downloadRefreshClient.CaptureContext();
        _downloadRefreshTimer.Start();
    }
    private async Task RefreshDownloadAsync()
    {
        if (_downloadRefreshBusy || _downloadRefreshOwner is not { } owner || _navigation is not { } nav) return;
        if (_downloadRefreshContext is not { } context || !_downloadRefreshClient.IsCurrentContext(context))
        { StopDownloadRefresh(); return; }
        _downloadRefreshBusy = true;
        try
        {
            var fresh = await _api.GetDetailAsync(nav.MediaType, nav.TmdbId, owner.Token);
            if (!ReferenceEquals(owner, _downloadRefreshOwner) || owner.IsCancellationRequested || !IsLoaded) return;
            if (!_downloadRefreshClient.IsCurrentContext(context)) { StopDownloadRefresh(); return; }
            if (!string.IsNullOrWhiteSpace(fresh.LibraryContentId))
            {
                var library = await ExternalTitleResolution.ResolveAsync(fresh, App.Services.GetRequiredService<ItemDetailPrefetchCache>(), owner.Token);
                if (!ReferenceEquals(owner, _downloadRefreshOwner) || owner.IsCancellationRequested || !IsLoaded || !_downloadRefreshClient.IsCurrentContext(context)) return;
                _libraryLink = library.LibraryLink;
                if (library.LibraryItem != null)
                {
                    StopDownloadRefresh();
                    App.Services.GetRequiredService<NavigationService>().NavigateReplacingCurrentEntry<ItemDetailPage>(library.LibraryItem.ContentId);
                    return;
                }
            }
            _item = fresh;
            // Update request state without rebuilding metadata/artwork or
            // disturbing the reader's scroll position on each poll.
            BuildActions(fresh); UpdateDownloadRefresh();
        }
        catch (OperationCanceledException) { }
        catch { /* Keep the last successful state; the next mounted tick retries. */ }
        finally { _downloadRefreshBusy = false; }
    }

    private void Render(RequestMediaDetail item)
    {
        TitleText.Text = item.Title;
        ContextText.Text = item.MediaType == "series" ? "Series" : "Movie";
        PosterFallback.Text = item.Title;
        TaglineText.Text = item.Tagline ?? "";
        TaglineText.Visibility = string.IsNullOrWhiteSpace(item.Tagline) ? Visibility.Collapsed : Visibility.Visible;
        OverviewText.Text = item.Overview ?? "";
        StudioText.Text = item.MediaType == "series" ? item.Networks?.FirstOrDefault() ?? "" : item.ProductionCompanies?.FirstOrDefault() ?? "";
        StudioText.Visibility = string.IsNullOrWhiteSpace(StudioText.Text) ? Visibility.Collapsed : Visibility.Visible;
        ScoreText.Text = item.VoteAverage is > 0
            ? $"★ {item.VoteAverage:0.0} TMDB{(item.VoteCount is > 0 ? $"  ·  {FormatVoteCount(item.VoteCount.Value)} votes" : "")}"
            : "";
        CrewText.Text = BuildCrew(item);
        ScoreText.Visibility = string.IsNullOrWhiteSpace(ScoreText.Text) ? Visibility.Collapsed : Visibility.Visible;
        CrewText.Visibility = string.IsNullOrWhiteSpace(CrewText.Text) ? Visibility.Collapsed : Visibility.Visible;
        SetImage(BackdropImage, Tmdb(item.BackdropPath, "original"));
        SetImage(PosterImage, Tmdb(item.PosterPath, "w500"));
        BuildMetadata(item);
        BuildActions(item);
        BuildSeasons(item);
        BuildCast(item.Cast ?? []);
        BuildRecommendations(item.Recommendations ?? []);
        ApplyHeroColors();
        UpdateDownloadRefresh();
    }

    private void BuildMetadata(RequestMediaDetail item)
    {
        MetadataPanel.Children.Clear();
        var values = new List<string>();
        if (item.Year is > 0) values.Add(item.Year.Value.ToString());
        if (!string.IsNullOrWhiteSpace(item.ContentRating)) values.Add(item.ContentRating);
        if (item.MediaType == "movie" && item.Runtime is > 0) values.Add(FormatDuration(item.Runtime.Value));
        if (item.MediaType == "series" && item.NumberOfSeasons is > 0) values.Add($"{item.NumberOfSeasons} season{(item.NumberOfSeasons == 1 ? "" : "s")}");
        if (item.MediaType == "series" && !string.IsNullOrWhiteSpace(item.Status)) values.Add(item.Status);
        foreach (var value in values)
            MetadataPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(102, 26, 27, 31)),
                BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(96, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock { Text = value, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium },
            });
        foreach (var genre in (item.Genres ?? []).Take(4))
            MetadataPanel.Children.Add(new TextBlock
            {
                Text = genre,
                FontSize = 12,
                Foreground = Brush("SecondaryTextBrush"),
                Padding = new Thickness(4, 3, 4, 3),
                VerticalAlignment = VerticalAlignment.Center,
            });
    }

    private void BuildActions(RequestMediaDetail item)
    {
        ActionsPanel.Children.Clear();
        LinksPanel.Children.Clear(); DownloadProgressHost.Children.Clear();
        if (item.Request.Requestable)
        {
            var button = SecondaryAction("request", $"Request {(item.MediaType == "series" ? "series" : "movie")}", "plus");
            StylePrimaryAction(button);
            button.Background = Brush("AccentBrush"); button.Foreground = Brush("AccentForegroundBrush"); button.BorderThickness = new Thickness(0);
            AutomationProperties.SetName(button, $"Request {(item.MediaType == "series" ? "series" : "movie")}");
            button.Click += Request_Click;
            ActionsPanel.Children.Add(button);
        }
        else if (item.Availability == "available" && string.IsNullOrWhiteSpace(item.Request.Status))
        {
            ActionsPanel.Children.Add(StatusPill("✓  Already in your library", "emerald"));
            if (!string.IsNullOrWhiteSpace(_libraryLink))
            {
                var open = new Button { Content = "Open in library", Tag = _libraryLink, CornerRadius = new CornerRadius(22), Padding = new Thickness(18, 10, 18, 10) };
                open.Click += OpenLibrary_Click;
                ActionsPanel.Children.Add(open);
            }
        }
        else
        {
            var label = !string.IsNullOrWhiteSpace(item.Request.Status) || !string.IsNullOrWhiteSpace(item.Request.State)
                ? RequestViewerPolicy.Label(item.Request.Status, null, item.Request.State)
                : Reason(item.Request.Reason);
            var status = SecondaryAction("request-state", label, item.Request.State == "processing" || item.Request.Status is "queued" or "downloading" ? "hourglass" : "clock");
            StylePrimaryAction(status);
            status.Background = Brush("AccentBrush"); status.Foreground = Brush("AccentForegroundBrush"); status.BorderThickness = new Thickness(0);
            status.IsEnabled = false; ActionsPanel.Children.Add(status);
        }

        if (!string.IsNullOrWhiteSpace(_libraryLink) && !ActionsPanel.Children.OfType<Button>().Any(b => b.Tag is string id && id == _libraryLink))
        {
            var open = new Button { Content = "Open in library", Tag = _libraryLink, CornerRadius = new CornerRadius(22) };
            open.Click += OpenLibrary_Click;
            ActionsPanel.Children.Add(open);
        }
        if (RequestViewerPolicy.CanFollow(_features, item.Request))
        {
            var follow = SecondaryAction("follow", item.Request.Following == true ? "Stop notifying me" : "Notify me when available", item.Request.Following == true ? "bell-off" : "bell", item.Request.Following == true);
            follow.Click += Follow_Click;
            ActionsPanel.Children.Add(follow);
        }
        if (_features.WatchlistTitlesSupported)
        {
            var watchlist = SecondaryAction("watchlist", item.InWatchlist == true ? "On Watchlist" : "Add to Watchlist", item.InWatchlist == true ? "bookmark-check" : "bookmark", item.InWatchlist == true);
            watchlist.Click += Watchlist_Click;
            if (item.InWatchlist != true && _features.WatchlistRequests && item.Request.Requestable && item.Availability != "available")
                ToolTipService.SetToolTip(watchlist, "Adding this title also requests it. You can change this in Settings.");
            ActionsPanel.Children.Add(watchlist);
        }
        if (item.Request.RequestedByViewer == true && !string.IsNullOrWhiteSpace(item.Request.RequestId))
            _ = AddCancelActionAsync(item);

        DownloadText.Text = RequestViewerPolicy.DownloadLabel(item.Request.Download);
        DownloadText.Visibility = Visibility.Collapsed;
        if (item.Request.Download != null) DownloadProgressHost.Children.Add(RequestDownloadProgress.Build(item.Request.Download, 320));
        AutoRequestExplanation.Visibility = _features.WatchlistTitlesSupported && _features.WatchlistRequests && item.InWatchlist != true && item.Request.Requestable && item.Availability != "available" ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(item.ImdbId))
            ActionsPanel.Children.Add(ExternalLinkButton("IMDb", $"https://www.imdb.com/title/{item.ImdbId}"));
        ActionsPanel.Children.Add(ExternalLinkButton("TMDB", $"https://www.themoviedb.org/{(item.MediaType == "series" ? "tv" : "movie")}/{item.TmdbId}"));
        foreach (var action in ActionsPanel.Children.OfType<Button>()) ApplyPendingAction(action);
        UpdateDownloadRefresh();
    }
    private void ApplyPendingAction(Button button)
    {
        if (!_pendingActions.Contains(AutomationProperties.GetAutomationId(button))) return;
        button.IsEnabled = false;
        button.Content = new ProgressRing { IsActive = true, Width = 18, Height = 18 };
    }
    private static Button SecondaryAction(string id, string label, string icon, bool active = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(WebUiIcon.Create(icon, 18));
        content.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Tag = id, Content = content, Height = 44, MinWidth = 0, MinHeight = 0, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(22), Background = Brush(active ? "SurfaceRaisedBrush" : "SurfaceBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1) };
        AutomationProperties.SetName(button, label); AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetHelpText(button, active ? "On" : "Off"); ToolTipService.SetToolTip(button, label); return button;
    }
    private void RequestSettings_Click(object sender, RoutedEventArgs e) => App.Services.GetRequiredService<NavigationService>().Navigate<SettingsPage>("requests");

    private static void StylePrimaryAction(Button button)
    {
        button.Padding = new Thickness(12, 0, 12, 0);
        if (button.Content is StackPanel content)
        {
            content.Spacing = 10;
            foreach (var label in content.Children.OfType<TextBlock>())
            {
                label.FontSize = 15;
                label.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            }
        }
    }

    private async Task AddCancelActionAsync(RequestMediaDetail item)
    {
        try
        {
            var request = await _api.GetAsync(item.Request.RequestId, _lifetime.Token);
            if (_item != item || _lifetime.IsCancellationRequested || !RequestViewerPolicy.CanCancel(request)) return;
            var cancel = SecondaryAction("cancel", "Cancel request", "x");
            cancel.Click += async (_, _) =>
            {
                var confirmation = new ContentDialog { XamlRoot = XamlRoot, Title = "Cancel this request?", Content = $"Cancel your request for {item.Title}?", PrimaryButtonText = "Cancel request", CloseButtonText = "Keep request", DefaultButton = ContentDialogButton.Close };
                if (await confirmation.ShowAsync() != ContentDialogResult.Primary || _item != item || _lifetime.IsCancellationRequested) return;
                await MutateAsync(cancel, () => _api.CancelAsync(request.Id, _lifetime.Token));
            };
            ApplyPendingAction(cancel); ActionsPanel.Children.Add(cancel);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Request cancellation eligibility unavailable: {ex.GetType().Name}"); }
    }

    private async Task MutateAsync(Button button, Func<Task> action)
    {
        if (_item == null || _lifetime.IsCancellationRequested) return;
        var actionId = AutomationProperties.GetAutomationId(button);
        if (!_pendingActions.Add(actionId)) return;
        var context = _downloadRefreshClient.CaptureContext();
        var mediaType = _item.MediaType; var tmdbId = _item.TmdbId;
        var original = button.Content;
        button.IsEnabled = false;
        button.Content = new ProgressRing { IsActive = true, Width = 18, Height = 18 };
        try
        {
            await action();
            var fresh = await _api.GetDetailAsync(mediaType, tmdbId, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || !_downloadRefreshClient.IsCurrentContext(context)) return;
            _item = fresh;
            BuildActions(_item); BuildSeasons(_item);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        finally
        {
            _pendingActions.Remove(actionId); button.IsEnabled = true; button.Content = original;
            if (_item != null && !_lifetime.IsCancellationRequested && _downloadRefreshClient.IsCurrentContext(context)) BuildActions(_item);
        }
    }

    private async void Follow_Click(object sender, RoutedEventArgs e)
    {
        if (_item == null || sender is not Button button) return;
        var item = _item;
        await MutateAsync(button, () => item.Request.Following == true
            ? _api.UnfollowAsync(item.MediaType, item.TmdbId, _lifetime.Token)
            : _api.FollowAsync(item.MediaType, item.TmdbId, _lifetime.Token));
    }

    private async void Watchlist_Click(object sender, RoutedEventArgs e)
    {
        if (_item == null || sender is not Button button) return;
        var item = _item;
        await MutateAsync(button, async () =>
        {
            if (item.InWatchlist == true) await _api.RemoveWatchlistTitleAsync(item.MediaType, item.TmdbId, _lifetime.Token);
            else
            {
                var entry = await _api.AddWatchlistTitleAsync(item.MediaType, item.TmdbId, _lifetime.Token);
                if (_features.WatchlistRequests && !string.IsNullOrWhiteSpace(entry.Request.Reason) && !entry.Request.Requestable && string.IsNullOrWhiteSpace(entry.Request.Status))
                    App.Services.GetRequiredService<ToastService>().Info($"Added to watchlist. {Reason(entry.Request.Reason)}");
            }
            App.Services.GetRequiredService<SiloPlayer.ViewModels.WatchlistViewModel>().InvalidateExternalTitles();
        });
    }

    private void BuildSeasons(RequestMediaDetail item)
    {
        SeasonsPanel.Children.Clear();
        foreach (var season in item.Seasons ?? [])
        {
            var state = RequestSeasonPickerState.Status(season, DateOnly.FromDateTime(DateTime.UtcNow));
            var card = new StackPanel { Width = 170, Spacing = 6 };
            var image = new Image { Width = 170, Height = 255, Stretch = Stretch.UniformToFill };
            SetImage(image, Tmdb(season.PosterPath, "w342"));
            var poster = new Grid { Width = 170, Height = 255 };
            poster.Children.Add(new TextBlock { Text = $"Season {season.SeasonNumber}", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            poster.Children.Add(image);
            card.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Brush("SurfaceRaisedBrush"), Child = poster });
            card.Children.Add(new TextBlock { Text = season.Name ?? $"Season {season.SeasonNumber}", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var meta = new[] { season.AirDate is { Length: >= 4 } airDate ? airDate[..4] : null, season.EpisodeCount > 0 ? $"{season.EpisodeCount} episode{(season.EpisodeCount == 1 ? "" : "s")}" : null }.Where(s => s != null);
            card.Children.Add(new TextBlock { Text = string.Join(" · ", meta) is { Length: > 0 } info ? info : "Not announced", FontSize = 12, Opacity = 0.65 });
            if (state != null) card.Children.Add(new TextBlock { Text = state, FontSize = 12, Opacity = 0.75 });
            SeasonsPanel.Children.Add(card);
        }
        SeasonsSection.Visibility = SeasonsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Request_Click(object sender, RoutedEventArgs e)
    {
        if (_item == null || _lifetime.IsCancellationRequested || sender is not Button button || !_pendingActions.Add("request")) return;
        var context = _downloadRefreshClient.CaptureContext();
        var item = _item; var original = button.Content;
        button.IsEnabled = false;
        try
        {
            if (_features.SeasonRequestsSupported && item.MediaType == "series" && item.Seasons?.Count > 0)
            {
                if (await Dialogs.RequestSeasonsDialog.PickAsync(XamlRoot, item, seasons => SubmitRequestAsync(item, seasons)) == null) return;
            }
            else { button.Content = "Sending…"; await SubmitRequestAsync(item, null); }
            var fresh = await _api.GetDetailAsync(item.MediaType, item.TmdbId, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || !_downloadRefreshClient.IsCurrentContext(context)) return;
            _item = fresh;
            BuildActions(_item);
            BuildSeasons(_item);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error($"Request failed: {ex.Message}");
        }
        finally
        {
            _pendingActions.Remove("request"); button.IsEnabled = true; button.Content = original;
            if (_item != null && !_lifetime.IsCancellationRequested && _downloadRefreshClient.IsCurrentContext(context)) BuildActions(_item);
        }
    }
    private Task SubmitRequestAsync(RequestMediaDetail item, List<int>? seasons)
        => _api.CreateAsync(new CreateMediaRequestInput { MediaType = item.MediaType, TmdbId = item.TmdbId, TvdbId = item.TvdbId, ImdbId = item.ImdbId, Title = item.Title, Year = item.Year, Overview = item.Overview, PosterPath = item.PosterPath, BackdropPath = item.BackdropPath, Seasons = seasons }, _lifetime.Token);

    private void BuildCast(IEnumerable<RequestMediaCastMember> cast)
    {
        CastPanel.Children.Clear();
        foreach (var member in cast.Take(20))
        {
            var panel = new StackPanel { Width = 112, Spacing = 5 };
            var image = new Image { Width = 112, Height = 168, Stretch = Stretch.UniformToFill };
            SetImage(image, Tmdb(member.ProfilePath, "w185"));
            panel.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = Brush("SurfaceRaisedBrush"), Child = image });
            panel.Children.Add(new TextBlock { Text = member.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = member.Character ?? "", FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap });
            CastPanel.Children.Add(panel);
        }
        CastSection.Visibility = CastPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildRecommendations(IEnumerable<RequestMediaResult> items)
    {
        RecommendationsPanel.Children.Clear();
        var width = ActualWidth < 640 ? 140d : ActualWidth < 1024 ? 160d : 185d;
        var size = App.Services.GetService<UICustomizationService>()?.CardPresentation.PosterSize;
        if (size == "compact") width -= 20;
        if (size == "large") width += ActualWidth < 640 ? 30 : 35;
        _recommendationWidth = width;
        foreach (var item in items)
        {
            Func<Task>? request = item.Request.Requestable ? async () =>
            {
                await _api.CreateAsync(new CreateMediaRequestInput { MediaType = item.MediaType, TmdbId = item.TmdbId, Title = item.Title, Year = item.Year, Overview = item.Overview, PosterPath = item.PosterPath, BackdropPath = item.BackdropPath }, _lifetime.Token);
                item.Request.Requestable = false; item.Request.Status = "pending";
                if (_item?.Recommendations != null) BuildRecommendations(_item.Recommendations);
            } : null;
            Func<Task>? watchlist = _features.WatchlistTitlesSupported ? async () =>
            {
                if (item.InWatchlist == true) await _api.RemoveWatchlistTitleAsync(item.MediaType, item.TmdbId, _lifetime.Token);
                else await _api.AddWatchlistTitleAsync(item.MediaType, item.TmdbId, _lifetime.Token);
                item.InWatchlist = item.InWatchlist != true;
                App.Services.GetService<SiloPlayer.ViewModels.WatchlistViewModel>()?.InvalidateExternalTitles();
            } : null;
            RecommendationsPanel.Children.Add(ExternalTitleCard.Build(item, width, request, watchlist));
        }
        RecommendationsSection.Visibility = RecommendationsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Recommendation_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { Tag: RequestMediaResult item }) App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(item.MediaType, item.TmdbId)); }
    private async void RecommendationRequest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RequestMediaResult item } button) return;
        if (item.MediaType == "series") { Recommendation_Click(sender, e); return; }
        button.IsEnabled = false;
        button.Content = "Submitting…";
        try
        {
            await _api.CreateAsync(new CreateMediaRequestInput { MediaType = item.MediaType, TmdbId = item.TmdbId, Title = item.Title, Year = item.Year, Overview = item.Overview, PosterPath = item.PosterPath, BackdropPath = item.BackdropPath }, _lifetime.Token);
            button.Content = "Requested";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            button.IsEnabled = true;
            button.Content = "Request";
        }
        catch (Exception ex)
        {
            button.IsEnabled = true;
            button.Content = "Request";
            App.Services.GetRequiredService<ToastService>().Error($"Request failed: {ex.Message}");
        }
    }
    private void OpenLibrary_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string id }) App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(id); }
    private async void ExternalLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }
    private void Back_Click(object sender, RoutedEventArgs e) => App.Services.GetRequiredService<NavigationService>().GoBack();
    private void Fail(string text) { DetailSkeleton.Visibility = Visibility.Collapsed; LoadingText.Text = text; LoadingText.Visibility = Visibility.Visible; }

    private static Border StatusPill(string text, string tone)
        => ExternalTitleCard.StatusBadge(text);

    private static string StatusTone(string? status) => status switch
    {
        "pending" => "amber",
        "queued" or "downloading" => "sky",
        "approved" or "completed" => "emerald",
        _ => "zinc",
    };
    private Button ExternalLinkButton(string label, string url)
    {
        var button = SecondaryAction("external-link", label, "external-link"); button.Tag = url;
        button.Padding = new Thickness(16, 0, 16, 0);
        if (button.Content is StackPanel content)
        {
            var icon = content.Children[0];
            content.Children.RemoveAt(0);
            content.Children.Add(icon);
            if (icon is FrameworkElement element) { element.Width = 14; element.Height = 14; element.Opacity = .6; }
            foreach (var text in content.Children.OfType<TextBlock>()) text.FontSize = 13;
        }
        button.Click += ExternalLink_Click;
        return button;
    }
    private static string BuildCrew(RequestMediaDetail item) { var p = new List<string>(); if (!string.IsNullOrWhiteSpace(item.Director)) p.Add($"Director: {item.Director}"); if (item.Creators?.Count > 0) p.Add($"Created by: {string.Join(", ", item.Creators)}"); if (item.Networks?.Count > 0) p.Add($"Network: {string.Join(", ", item.Networks)}"); return string.Join("  ·  ", p); }
    private static string FormatDuration(int minutes) => minutes >= 60 ? $"{minutes / 60}h{(minutes % 60 == 0 ? "" : $" {minutes % 60}m")}" : $"{minutes}m";
    private static string FormatVoteCount(int count) => count >= 1000 ? $"{count / 1000d:0.0}k" : count.ToString();
    private static string Format(string value) => string.IsNullOrWhiteSpace(value) ? "Requested" : char.ToUpperInvariant(value[0]) + value[1..];
    private static string Reason(string value) => value switch { "already_requested" => "Already requested", "already_available" => "Available", "requests_disabled" => "Requests disabled", "quota_exceeded" => "Limit reached", "blocked" => "Blocked", _ => "Unavailable" };
    private static string? Tmdb(string? path, string size) => string.IsNullOrWhiteSpace(path) ? null : $"https://image.tmdb.org/t/p/{size}{path}";
    private static void SetImage(Image image, string? url)
    {
        if (url != null)
            image.Source = (ImageSource)RemoteImageConverter.Convert(
                url,
                typeof(ImageSource),
                null!,
                string.Empty);
    }
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private void ApplyHeroColors()
    {
        var background = ((SolidColorBrush)Brush("AppBackgroundBrush")).Color;
        Windows.UI.Color Alpha(byte alpha) => Windows.UI.Color.FromArgb(alpha, background.R, background.G, background.B);
        HeroLeftSolid.Color = HeroBottomSolid.Color = background;
        HeroLeftTransparent.Color = HeroBottomTransparent.Color = Alpha(0);
        HeroLeftStrong.Color = Alpha(0xCC); HeroLeftSoft.Color = Alpha(0x66);
        HeroBottomSoft.Color = Alpha(0x33); HeroBottomMid.Color = Alpha(0x8C); HeroBottomStrong.Color = Alpha(0xEB);
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;
        var gutter = width < 640 ? 16d : width < 1024 ? 24d : width < 1280 ? 40d : 48d;
        var compact = width < 1024;

        BackButton.Margin = new Thickness(8, width >= 640 ? 24 : 16, 0, 0);
        LoadingBackButton.Margin = BackButton.Margin;
        LowerContent.Padding = new Thickness(gutter, 20, gutter, 50);
        HeroContent.Margin = new Thickness(gutter, 112, gutter, 32);
        // Do not derive the hero from Page.ActualHeight: this page sits inside
        // a ScrollViewer, so its measured content height includes the hero
        // itself and creates a growth feedback loop. On wide windows that put
        // the title/poster below the viewport. The WebUI uses a bounded hero
        // whose height follows the viewport width.
        var viewportHeight = double.IsFinite(Height) ? Height : XamlRoot?.Size.Height ?? 720;
        HeroGrid.MinHeight = Math.Max(300, viewportHeight * (compact ? .60 : .72));
        TitleText.FontSize = width < 640 ? 36 : width < 1024 ? 48 : 72;
        OverviewText.FontSize = width < 640 ? 14 : 15;
        TitleText.CharacterSpacing = -50; TitleText.LineHeight = TitleText.FontSize * .98;
        HeroContent.ColumnSpacing = 24; HeroContent.MaxWidth = 1520 - gutter * 2;
        var posterWidth = width >= 640 ? 220 : 170;

        HeroContent.ColumnDefinitions[0].Width = compact
            ? new GridLength(1, GridUnitType.Star) : new GridLength(posterWidth);
        Grid.SetRow(PosterBorder, 0);
        Grid.SetColumn(PosterBorder, 0);
        Grid.SetRow(HeroInfo, compact ? 1 : 0);
        Grid.SetColumn(HeroInfo, compact ? 0 : 1);
        Grid.SetColumnSpan(HeroInfo, compact ? 2 : 1);
        PosterBorder.Width = posterWidth;
        PosterBorder.Height = posterWidth * 1.5;

        DetailSkeleton.ColumnDefinitions[0].Width = compact
            ? new GridLength(1, GridUnitType.Star) : new GridLength(posterWidth);
        Grid.SetRow(DetailSkeletonPoster, 0);
        Grid.SetColumn(DetailSkeletonPoster, 0);
        Grid.SetRow(DetailSkeletonInfo, compact ? 1 : 0);
        Grid.SetColumn(DetailSkeletonInfo, compact ? 0 : 1);
        Grid.SetColumnSpan(DetailSkeletonInfo, compact ? 2 : 1);
        DetailSkeletonPoster.Width = posterWidth;
        DetailSkeletonPoster.Height = posterWidth * 1.5;
        DetailSkeleton.Margin = new Thickness(gutter, compact ? 96 : 105, gutter, 50);
        ApplyHeroColors();
        var recommendationWidth = width < 640 ? 140d : width < 1024 ? 160d : 185d;
        var size = App.Services.GetService<UICustomizationService>()?.CardPresentation.PosterSize;
        if (size == "compact") recommendationWidth -= 20;
        if (size == "large") recommendationWidth += width < 640 ? 30 : 35;
        if (_item?.Recommendations != null && Math.Abs(_recommendationWidth - recommendationWidth) > 1) BuildRecommendations(_item.Recommendations);
    }
}

public sealed record RequestDetailNavigation(string MediaType, int TmdbId);
