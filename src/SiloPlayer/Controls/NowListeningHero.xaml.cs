using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Services;
using System.Runtime.InteropServices.WindowsRuntime;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

namespace SiloPlayer.Controls;

public sealed partial class NowListeningHero : UserControl
{
    private MediaItem? _item;
    private double _progress;
    private MediaItemDetail? _detail;
    private ApiRequestContext? _detailContext;
    private ApiRequestContext? _detailRequestContext;
    private CancellationTokenSource? _detailCts;
    private PlayerService? _player;
    private readonly List<NowListeningPresentation.Chapter> _chapters = [];
    private int? _libraryId;
    private bool _subscribed;
    private byte[]? _posterBytes;
    private CancellationTokenSource? _artworkCts;
    private CancellationTokenSource? _backdropCts;

    public int? LibraryId
    {
        get => _libraryId;
        set
        {
            if (_libraryId == value) return;
            _libraryId = value;
            _detailCts?.Cancel();
            _detail = null;
            _detailContext = null;
            _chapters.Clear();
            if (_item != null) _ = HydrateDetailAsync(_item);
        }
    }

    public NowListeningHero()
    {
        InitializeComponent();
        SetTransportIcon(false);
        InfoIcon.Children.Add(WebUiIcon.Create("info", 16, (Brush)Application.Current.Resources["PrimaryTextBrush"]));
        UpdateScrims();
        DeckInfo.SizeChanged += (_, _) => BalanceTitle();
        Loaded += (_, _) =>
        {
            SubscribePlayer();
            InvalidateRetiredDetail();
            if (_item != null && _detail == null && _detailCts is not { IsCancellationRequested: false })
                _ = HydrateDetailAsync(_item);
            if (_item is { PosterUrl.Length: > 0 } item && (CoverImage.Source == null || _posterBytes == null) && (_artworkCts == null || _artworkCts.IsCancellationRequested))
                _ = LoadPosterAsync(item);
            UpdateLiveState(); ScheduleBackdrop();
        };
        Unloaded += (_, _) => { _detailCts?.Cancel(); _artworkCts?.Cancel(); _backdropCts?.Cancel(); UnsubscribePlayer(); };
    }
    private void Hero_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width; if (width <= 0) return;
        var compact = width < 640; var cover = compact ? 144d : width < 1024 ? 192d : 224d;
        CoverBorder.Width = CoverBorder.Height = cover;
        CoverBorder.HorizontalAlignment = HorizontalAlignment.Left;
        DeckGrid.RowSpacing = compact ? 24 : 0;
        DeckGrid.ColumnSpacing = compact ? 0 : 40;
        DeckGrid.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(cover);
        DeckGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(DeckInfo, compact ? 0 : 1); Grid.SetRow(DeckInfo, compact ? 1 : 0);
        var gutter = width >= 1280 ? 48 : width >= 1024 ? 40 : compact ? 16 : 24;
        DeckGrid.Margin = new Thickness(gutter, compact ? 112 : 128, gutter, compact ? 40 : 48);
        TitleText.FontSize = compact ? 30 : width < 1024 ? 36 : 48;
        TitleText.LineHeight = compact ? 36 : width < 1024 ? 40 : 48;
        CreditsText.FontSize = compact ? 14 : 16;
        CreditsText.LineHeight = compact ? 20 : 24;
        PositionText.FontSize = TimeLeftText.FontSize = compact ? 12 : 14;
        PositionText.LineHeight = TimeLeftText.LineHeight = compact ? 16 : 20;
        BalanceTitle();
        RootGrid.Clip = new RectangleGeometry { Rect = new(0, 0, width, e.NewSize.Height) };
        BackgroundImage.Width = width; BackgroundImage.Height = e.NewSize.Height;
        BackgroundImage.RenderTransform = new CompositeTransform { CenterX = width / 2, CenterY = e.NewSize.Height / 2, ScaleX = 1.1, ScaleY = 1.1 };
        var visual = ElementCompositionPreview.GetElementVisual(CoverImage);
        var rounded = visual.Compositor.CreateRoundedRectangleGeometry(); rounded.Size = new Vector2((float)cover); rounded.CornerRadius = new Vector2(16);
        visual.Clip = visual.Compositor.CreateGeometricClip(rounded);
        ScheduleBackdrop();
    }
    private void SubscribePlayer()
    {
        _player ??= App.Services.GetRequiredService<PlayerService>();
        if (_subscribed) return; _subscribed = true;
        _player.PositionChanged += LivePositionChanged; _player.PauseChanged += LivePauseChanged;
        _player.ContentLoaded += LiveContentChanged; _player.ChaptersChanged += LiveContentChanged;
    }
    private void UnsubscribePlayer()
    {
        if (!_subscribed || _player == null) return; _subscribed = false;
        _player.PositionChanged -= LivePositionChanged; _player.PauseChanged -= LivePauseChanged;
        _player.ContentLoaded -= LiveContentChanged; _player.ChaptersChanged -= LiveContentChanged;
    }
    private void LivePositionChanged(double _) => DispatcherQueue.TryEnqueue(UpdateLiveState);
    private void LivePauseChanged(bool _) => DispatcherQueue.TryEnqueue(UpdateLiveState);
    private void LiveContentChanged() => DispatcherQueue.TryEnqueue(UpdateLiveState);
    private void UpdateLiveState()
    {
        if (_item == null) return;
        var active = _player?.ContentId == _item.ContentId && _player.IsAudiobook;
        var position = active ? _player!.Position : _detail?.UserData?.PositionSeconds ?? _item.PositionSeconds ?? 0;
        var duration = NowListeningPresentation.ResolveDuration(_detail?.Audiobook?.TotalDurationSeconds,
            _detail?.Versions.Sum(version => version.Duration) ?? 0, _item.DurationSeconds);
        if (duration <= 0 && active) duration = _player!.Duration;
        _progress = duration > 0 ? Math.Clamp(position / duration, 0, 1) : 0;
        var playing = active && !_player!.IsPaused;
        ResumeText.Text = playing ? "Pause" : position > 0 ? "Resume" : "Listen";
        SetTransportIcon(playing);
        var live = active ? _player!.CurrentAudiobookChapter : null;
        PositionText.Text = _chapters.Count > 0 ? NowListeningPresentation.ChapterLine(_chapters, position, duration)
            : live != null ? NowListeningPresentation.FormatChapter(live.Index + 1, _player!.AudiobookChapters.Count, live.Title)
            : NowListeningPresentation.ChapterLine(_chapters, position, duration);
        TimeLeftText.Text = NowListeningPresentation.TimeLeft(position, duration) ?? "";
        UpdateProgressWidth();
    }
    private async Task HydrateDetailAsync(MediaItem item)
    {
        var owner = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _detailCts, owner); previous?.Cancel(); previous?.Dispose();
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var context = catalog.CaptureContext();
        _detailRequestContext = context;
        var libraryId = LibraryId;
        try
        {
            var detail = await catalog.GetItemDetailAsync(item.ContentId, libraryId, owner.Token);
            if (owner.IsCancellationRequested || !ReferenceEquals(_detailCts, owner)
                || catalog.CaptureContext() != context || LibraryId != libraryId
                || _item?.ContentId != detail.ContentId || detail.Type != "audiobook") return;
            _detail = detail; _detailContext = context; _chapters.Clear();
            _chapters.AddRange(NowListeningPresentation.BuildChapters(detail.Versions));
            var author = string.Join(", ", detail.Audiobook?.Authors.Select(person => person.Name?.Trim()).Where(name => !string.IsNullOrEmpty(name)) ?? []);
            var narrator = string.Join(", ", detail.Audiobook?.Narrators.Select(person => person.Name?.Trim()).Where(name => !string.IsNullOrEmpty(name)) ?? []);
            CreditsText.Text = author + (!string.IsNullOrEmpty(narrator) ? (string.IsNullOrEmpty(author) ? "" : " · ") + "Narrated by " + narrator : "");
            CreditsText.Visibility = string.IsNullOrWhiteSpace(CreditsText.Text) ? Visibility.Collapsed : Visibility.Visible;
            UpdateLiveState();
        }
        catch (OperationCanceledException) { } catch { /* Section progress remains usable if optional detail fails. */ }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _detailCts, null, owner), owner))
            {
                _detailRequestContext = null;
                owner.Dispose();
            }
        }
    }

    public void Bind(MediaItem item)
    {
        var changed = _item?.ContentId != item.ContentId;
        _item = item;
        InvalidateRetiredDetail();
        if (changed) { _detail = null; _detailContext = null; _chapters.Clear(); _posterBytes = null; _artworkCts?.Cancel(); _backdropCts?.Cancel(); CoverImage.Source = BackgroundImage.Source = null; }
        if (changed || (_detail == null && _detailCts is not { IsCancellationRequested: false })) _ = HydrateDetailAsync(item);
        TitleText.Text = item.Title;
        BalanceTitle();
        var authors = item.Audiobook?.Authors.Select(person => person.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList() ?? [];
        var narrators = item.Audiobook?.Narrators.Select(person => person.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList() ?? [];
        var credits = new List<string>();
        if (authors.Count > 0) credits.Add(string.Join(", ", authors));
        if (narrators.Count > 0) credits.Add($"Narrated by {string.Join(", ", narrators)}");
        if (_detail == null || credits.Count > 0)
        {
            CreditsText.Text = string.Join(" · ", credits);
            CreditsText.Visibility = credits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        var position = Math.Max(0, item.PositionSeconds ?? 0);
        var duration = Math.Max(0, item.DurationSeconds ?? item.Audiobook?.TotalDurationSeconds ?? 0);
        _progress = duration > 0 ? Math.Clamp(position / duration, 0, 1) : 0;
        ResumeText.Text = position > 0 ? "Resume" : "Listen";
        PositionText.Text = duration > 0 ? $"{FormatDuration(position)} of {FormatDuration(duration)}" : "";
        TimeLeftText.Text = duration > 0 ? $"{FormatDuration(duration - position)} left" : "";
        UpdateProgressWidth();

        UpdateLiveState();
        if (changed && !string.IsNullOrWhiteSpace(item.PosterUrl)) _ = LoadPosterAsync(item);
    }

    private void InvalidateRetiredDetail()
    {
        var context = _detailContext ?? _detailRequestContext;
        if (!context.HasValue || App.Services.GetRequiredService<CatalogApi>().CaptureContext() == context.Value) return;
        _detailCts?.Cancel();
        _detail = null;
        _detailContext = null;
        _chapters.Clear();
    }

    private async Task LoadPosterAsync(MediaItem item)
    {
        var owner = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _artworkCts, owner); previous?.Cancel(); previous?.Dispose();
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var path = await imageService.GetImageDiskPathAsync(item.ContentId, "poster", item.PosterUrl!, httpClient, owner.Token);
            if (owner.IsCancellationRequested || !ReferenceEquals(_artworkCts, owner) || _item?.ContentId != item.ContentId || string.IsNullOrWhiteSpace(path)) return;
            var image = new BitmapImage { UriSource = new Uri(path), DecodePixelWidth = 560 };
            CoverImage.Source = image;
            var bytes = await File.ReadAllBytesAsync(path, owner.Token);
            if (owner.IsCancellationRequested || !ReferenceEquals(_artworkCts, owner) || _item?.ContentId != item.ContentId) return;
            _posterBytes = bytes; ScheduleBackdrop();
        }
        catch { }
        finally { if (ReferenceEquals(Interlocked.CompareExchange(ref _artworkCts, null, owner), owner)) owner.Dispose(); }
    }

    private void SetTransportIcon(bool playing)
    {
        var kind = playing ? "pause" : "play";
        if (ResumeIcon.Tag?.ToString() == kind) return;
        ResumeIcon.Tag = kind; ResumeIcon.Children.Clear();
        ResumeIcon.Children.Add(HeroActionIcon.Create(kind, (Brush)Application.Current.Resources["AccentForegroundBrush"]));
    }

    private void BalanceTitle()
    {
        // A CSS max-width block starts at its parent's left edge. WinUI
        // centers a stretched MaxWidth panel unless its width is explicit.
        ProgressPanel.Width = Math.Min(576, Math.Max(0, DeckInfo.ActualWidth));
        var available = Math.Min(768, DeckInfo.ActualWidth);
        if (available <= 0 || TitleText.LineHeight <= 0) return;
        var probe = new TextBlock { Text = TitleText.Text, FontFamily = TitleText.FontFamily, FontSize = TitleText.FontSize, FontWeight = TitleText.FontWeight,
            CharacterSpacing = TitleText.CharacterSpacing, TextWrapping = TextWrapping.Wrap, LineHeight = TitleText.LineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        probe.Measure(new(available, double.PositiveInfinity));
        var fullHeight = probe.DesiredSize.Height; var lines = (int)Math.Round(fullHeight / TitleText.LineHeight); var balanced = available;
        if (lines is > 1 and <= 6)
        {
            var low = available / lines; var high = available;
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var candidate = (low + high) / 2; probe.Measure(new(candidate, double.PositiveInfinity));
                if (probe.DesiredSize.Height <= fullHeight + .5) high = candidate; else low = candidate;
            }
            balanced = Math.Ceiling(high);
        }
        TitleText.Width = balanced;
    }

    private void UpdateScrims()
    {
        var color = ((SolidColorBrush)Application.Current.Resources["AppBackgroundBrush"]).Color;
        Windows.UI.Color Alpha(double alpha) => Microsoft.UI.ColorHelper.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);
        TopStrong.Color = Alpha(.7); TopSoft.Color = Alpha(.3);
        BottomLight.Color = Alpha(.12); BottomMid.Color = Alpha(.45); BottomStrong.Color = Alpha(.86);
        BottomSolidStart.Color = BottomSolid.Color = color;
    }

    private void ScheduleBackdrop()
    {
        if (!IsLoaded || _posterBytes == null || ActualWidth <= 0 || ActualHeight <= 0) return;
        var owner = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _backdropCts, owner); previous?.Cancel(); previous?.Dispose();
        _ = RefreshBackdropAsync(_posterBytes, _item?.ContentId, owner);
    }

    private async Task RefreshBackdropAsync(byte[] bytes, string? contentId, CancellationTokenSource owner)
    {
        try
        {
            // Coalesce resize/layout passes; one poster decode/effect per settled viewport.
            await Task.Delay(100, owner.Token);
            var width = ActualWidth; var height = ActualHeight;
            var transformed = await ArtworkEffects.TransformBackdropAsync(bytes, width, height, owner.Token);
            if (owner.IsCancellationRequested || !ReferenceEquals(_backdropCts, owner) || _item?.ContentId != contentId || !IsLoaded) return;
            using var stream = new MemoryStream(transformed);
            var background = new BitmapImage(); await background.SetSourceAsync(stream.AsRandomAccessStream());
            if (!owner.IsCancellationRequested && ReferenceEquals(_backdropCts, owner) && _item?.ContentId == contentId && IsLoaded && Math.Abs(ActualWidth - width) < .1 && Math.Abs(ActualHeight - height) < .1)
                BackgroundImage.Source = background;
        }
        catch (OperationCanceledException) { }
        catch { /* The decoded foreground cover remains usable if its optional background effect fails. */ }
        finally { if (ReferenceEquals(Interlocked.CompareExchange(ref _backdropCts, null, owner), owner)) owner.Dispose(); }
    }

    private void ProgressTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateProgressWidth();

    private void UpdateProgressWidth()
    {
        if (ProgressTrack.ActualWidth > 0)
            ProgressFill.Width = ProgressTrack.ActualWidth * _progress;
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (_item == null) return;
        var player = _player ?? App.Services.GetRequiredService<PlayerService>();
        if (player.IsAudiobook && player.ContentId == _item.ContentId) player.ToggleAudiobookPlayback();
        else _ = player.PlayAsync(_item.ContentId, libraryId: LibraryId);
    }

    private void MoreInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_item == null) return;
        App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(MediaNavigationContext.Detail(_item.ContentId, LibraryId));
    }

    private static string FormatDuration(double seconds)
    {
        return NowListeningPresentation.FormatDuration(seconds);
    }
}
