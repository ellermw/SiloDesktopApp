using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
namespace SiloPlayer.Controls;

/// <summary>Aggregate destination with no title mutation or quick-play surface.</summary>
public sealed class BoxSetCard : Button
{
    private Grid? _artworkStack;
    private StackPanel? _panel;
    private TextBlock? _title, _subtitle, _filmCount;
    private ProgressBar? _progress;
    private Image? _poster;
    private DefaultArtwork? _fallback;
    private string? _artworkUrl, _thumbhash;
    private CancellationTokenSource? _artworkLoad;
    public BoxSetSummary? Summary { get; private set; }
    public event Action<BoxSetSummary>? Invoked;
    public BoxSetCard()
    {
        Width = 170; Padding = new Thickness(0); Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Click += (_, _) => { if (Summary is {} set) Invoked?.Invoke(set); };
        Unloaded += (_, _) => CancelArtworkLoad();
        Loaded += (_, _) => { if (_poster?.Source is null && _artworkLoad is null) StartArtworkLoad(); };
    }
    protected override void OnTapped(Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        base.OnTapped(e);
        // The button's Click owns navigation. The same gesture must not also
        // activate a containing PosterCard or catalog slot.
        e.Handled = true;
    }
    /// <summary>Fit the shared aggregate presentation inside a virtual catalog slot.</summary>
    public void SetCatalogLayout(double width, double posterHeight, double height)
    {
        Width = width; Height = height; MinWidth = 0; Padding = new Thickness(0); BorderThickness = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Top;
        if (_artworkStack is null || _panel is null || _title is null || _subtitle is null) return;
        _artworkStack.Height = Math.Max(1, posterHeight - 8); _artworkStack.Width = Math.Max(1, width - 8);
        _panel.Spacing = 4;
        _title.FontSize = 14; _title.MaxLines = 1; _title.TextWrapping = TextWrapping.NoWrap; _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _subtitle.FontSize = 11; _subtitle.MaxLines = 1; _subtitle.TextWrapping = TextWrapping.NoWrap; _subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
    }
    public void SetSummary(BoxSetSummary set, string? posterUrl = null, string? posterThumbhash = null)
    {
        var callerPoster = !string.IsNullOrWhiteSpace(posterUrl);
        var summaryPoster = !string.IsNullOrWhiteSpace(set.PosterUrl);
        var artworkUrl = callerPoster ? posterUrl : summaryPoster ? set.PosterUrl : set.BackdropUrl;
        // A placeholder belongs to its image. A caller can borrow the summary
        // poster hash only when it selected that exact URL; URL-less artwork
        // keeps the existing best-available placeholder fallback.
        var thumbhash = callerPoster ? posterThumbhash ?? (posterUrl == set.PosterUrl ? set.PosterThumbhash : null)
            : summaryPoster ? set.PosterThumbhash
            : !string.IsNullOrWhiteSpace(set.BackdropUrl) ? set.BackdropThumbhash
            : posterThumbhash ?? set.PosterThumbhash ?? set.BackdropThumbhash;
        var identityChanged = Summary?.Id != set.Id || Summary?.LibraryId != set.LibraryId;
        var artworkChanged = _panel is null || identityChanged || !LibraryPosterBinding.HasSameArtworkUrl(_artworkUrl, artworkUrl);
        Summary = set;
        if (_panel is null) BuildContent();
        // Keep the Button and all its children mounted on a same-item refresh.
        _title!.Text = set.Title;
        _subtitle!.Text = set.PlayedCount > 0 ? set.ProgressText : set.Years;
        _filmCount!.Text = $"{set.MovieCount} films";
        _progress!.Maximum = Math.Max(1, set.MovieCount);
        _progress.Value = Math.Clamp(set.PlayedCount, 0, Math.Max(0, set.MovieCount));
        AutomationProperties.SetHelpText(_progress, set.ProgressText);
        AutomationProperties.SetName(this, $"{set.Title}, Box set, {set.MovieCount} films, {set.ProgressText}");
        if (!artworkChanged)
        {
            // Keep the mounted image, but use the renewed signature for any later load.
            var renewedUrl = _artworkUrl != artworkUrl;
            _artworkUrl = artworkUrl;
            if (_thumbhash != thumbhash)
            {
                _thumbhash = thumbhash;
                _fallback!.Reset("movie", thumbhash);
                if (_poster!.Source is null) _ = _fallback.ShowThumbhashAsync();
            }
            if (renewedUrl && _poster!.Source is null)
            {
                // The previous signed request may be pending or failed. A
                // renewed URL can recover it without rebuilding this control.
                CancelArtworkLoad();
                StartArtworkLoad();
            }
            return;
        }
        CancelArtworkLoad();
        _artworkUrl = artworkUrl; _thumbhash = thumbhash;
        _poster!.Source = null;
        _fallback!.Reset("movie", thumbhash);
        _fallback.Visibility = Visibility.Visible;
        StartArtworkLoad();
    }
    private void BuildContent()
    {
        var panel = _panel = new StackPanel { Spacing = 6 };
        var stack = _artworkStack = new Grid { Height = 245, Margin = new Thickness(0, 8, 8, 0) };
        stack.Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.DimGray), CornerRadius = new CornerRadius(9), Margin = new Thickness(8, -8, -8, 8) });
        stack.Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Gray), CornerRadius = new CornerRadius(9), Margin = new Thickness(4, -4, -4, 4) });
        var face = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.DarkSlateGray) };
        face.Children.Add(_fallback = new DefaultArtwork());
        face.Children.Add(_poster = new Image { Stretch = Stretch.UniformToFill });
        _poster.ImageOpened += (_, _) => _fallback.Visibility = Visibility.Collapsed;
        _poster.ImageFailed += (_, _) => { _poster.Source = null; _fallback.Visibility = Visibility.Visible; _ = _fallback.ShowThumbhashAsync(); };
        face.Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Black), Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = "Box set" } });
        face.Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Black), Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Child = _filmCount = new TextBlock() });
        stack.Children.Add(face); panel.Children.Add(stack);
        _progress = new ProgressBar { Minimum = 0, Height = 4 };
        AutomationProperties.SetName(_progress, "Films watched");
        panel.Children.Add(_progress);
        panel.Children.Add(_title = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 2, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(_subtitle = new TextBlock { TextWrapping = TextWrapping.Wrap });
        Content = panel;
    }
    internal void CancelArtworkLoad()
    {
        var pending = _artworkLoad; _artworkLoad = null;
        pending?.Cancel(); pending?.Dispose();
    }
    private void StartArtworkLoad()
    {
        if (_fallback is null || Summary is not {} set) return;
        if (string.IsNullOrWhiteSpace(_artworkUrl)) { _ = _fallback.ShowThumbhashAsync(); return; }
        var pending = _artworkLoad = new CancellationTokenSource();
        _ = LoadArtworkAsync(set, _artworkUrl, pending, pending.Token);
    }
    private async Task LoadArtworkAsync(BoxSetSummary set, string url, CancellationTokenSource pending, CancellationToken ct)
    {
        bool Current() => !ct.IsCancellationRequested && ReferenceEquals(_artworkLoad, pending);
        try
        {
            await Task.Delay(25, ct);
            var service = App.Services.GetRequiredService<ImageService>();
            var http = App.Services.GetRequiredService<HttpClient>();
            var path = await LibraryGridCard.ImageLoadThrottle.RunAsync(
                token => service.GetImageDiskPathAsync($"box_set:{set.LibraryId}:{set.Id}", "poster", url, http, token), ct);
            if (!Current()) return;
            if (string.IsNullOrWhiteSpace(path)) { await _fallback!.ShowThumbhashAsync(); return; }
            await LibraryGridCard.BitmapCreateLock.WaitAsync(ct);
            try
            {
                if (!Current()) return;
                _poster!.Source = new BitmapImage
                {
                    DecodePixelWidth = Math.Clamp((int)Math.Ceiling(Width * 1.25), 200, 640),
                    DecodePixelType = DecodePixelType.Logical, UriSource = new Uri(path),
                };
            }
            finally { LibraryGridCard.BitmapCreateLock.Release(); }
        }
        catch (OperationCanceledException) { }
        catch { if (Current()) await _fallback!.ShowThumbhashAsync(); }
        finally { if (ReferenceEquals(_artworkLoad, pending)) { _artworkLoad = null; pending.Dispose(); } }
    }
}
