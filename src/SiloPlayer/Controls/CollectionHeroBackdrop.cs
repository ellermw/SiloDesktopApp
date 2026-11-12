using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using Windows.Storage.Streams;
using Windows.UI;

namespace SiloPlayer.Controls;

/// <summary>
/// A collection page's backdrop in the homepage hero's treatment (pinned WebUI <c>CollectionPageHero</c>): the
/// artwork runs behind the title and the filter bar with a slow pan, a dimming wash, an ambient glow tinted from
/// the thumbhash, a left scrim, and a strong bottom fade that melts into the page. The thumbhash shows first, then
/// the real artwork through the shared image cache; nothing shows (collapsed) for a collection without a backdrop.
/// </summary>
public sealed class CollectionHeroBackdrop : Grid
{
    private readonly BackdropImage _image = new() { AnchorY = 0.2, IsHitTestVisible = false };
    private readonly GradientStop _ambient = new() { Offset = 0 };
    private readonly Border _phoneScrim = new() { Visibility = Visibility.Collapsed };
    private CancellationTokenSource? _loadCts;
    private string? _url, _thumbhash;

    public CollectionHeroBackdrop()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var background = ((SolidColorBrush)Application.Current.Resources["AppBackgroundBrush"]).Color;
        Color Wash(double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), background.R, background.G, background.B);
        Children.Add(_image);
        // brightness(.9): the hero's dimming wash.
        Children.Add(new Border { Background = new SolidColorBrush(Colors.Black), Opacity = 0.10 });
        // Ambient glow, tinted from the thumbhash, under the bottom fade so its edge leaves no seam.
        _ambient.Color = Colors.Transparent;
        Children.Add(new Border
        {
            Opacity = 0.12,
            Background = new RadialGradientBrush { Center = new Windows.Foundation.Point(.5, .8), GradientOrigin = new Windows.Foundation.Point(.5, .8), RadiusX = .7, RadiusY = .7,
                GradientStops = { _ambient, new GradientStop { Color = Colors.Transparent, Offset = 1 } } },
        });
        Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = Wash(0), Offset = 0 }, new GradientStop { Color = Wash(0), Offset = .12 },
                    new GradientStop { Color = Wash(.12), Offset = .30 }, new GradientStop { Color = Wash(.45), Offset = .52 },
                    new GradientStop { Color = Wash(.86), Offset = .74 }, new GradientStop { Color = Wash(1), Offset = .90 },
                    new GradientStop { Color = Wash(1), Offset = 1 },
                },
            },
        });
        // On a phone the title and description stack up over the middle of the art, above where the bottom fade starts.
        _phoneScrim.Background = new SolidColorBrush(Wash(.45));
        Children.Add(_phoneScrim);
        Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0),
                GradientStops =
                {
                    new GradientStop { Color = Wash(.88), Offset = 0 }, new GradientStop { Color = Wash(.58), Offset = .16 },
                    new GradientStop { Color = Wash(.22), Offset = .34 }, new GradientStop { Color = Wash(0), Offset = .5 },
                },
            },
        });
        SizeChanged += (_, e) => _phoneScrim.Visibility = e.NewSize.Width < 640 ? Visibility.Visible : Visibility.Collapsed;
        Unloaded += (_, _) => { if (!IsLoaded) Cancel(); };
    }

    public string? BackdropUrl => _url;
    public string? BackdropThumbhash => _thumbhash;
    /// <summary>The thumbhash is on screen (the real artwork has not replaced it).</summary>
    public bool ShowsPlaceholder => _image.Source is WriteableBitmap;
    /// <summary>The real artwork is on screen.</summary>
    public bool ShowsArtwork => _image.Source is BitmapImage;
    public bool PansSlowly => _image.EnableKenBurns;

    /// <summary>Shows a collection's backdrop, or collapses for one without. <paramref name="imageKey"/> names it in the image cache.</summary>
    public void Show(string? url, string? thumbhash, string imageKey)
    {
        url = Clean(url); thumbhash = Clean(thumbhash);
        if (url == _url && thumbhash == _thumbhash && Visibility == Visibility.Visible) return;
        Cancel();
        _url = url; _thumbhash = thumbhash;
        if (url == null && thumbhash == null) { Clear(); return; }
        Visibility = Visibility.Visible;
        _image.Source = null!;
        _image.EnableKenBurns = AreSystemAnimationsEnabled();
        var ambient = ThumbhashDecoder.GetAmbientColor(thumbhash);
        _ambient.Color = ambient is { } color ? Color.FromArgb(255, color.R, color.G, color.B) : Colors.Transparent;
        var owner = _loadCts = new CancellationTokenSource();
        _ = LoadAsync(url, thumbhash, imageKey, owner);
    }

    public void Clear()
    {
        Cancel();
        _url = _thumbhash = null;
        _image.EnableKenBurns = false;
        _image.Source = null!;
        Visibility = Visibility.Collapsed;
    }

    private void Cancel()
    {
        var previous = _loadCts; _loadCts = null;
        previous?.Cancel(); previous?.Dispose();
    }

    private async Task LoadAsync(string? url, string? thumbhash, string imageKey, CancellationTokenSource owner)
    {
        bool Current() => ReferenceEquals(_loadCts, owner) && !owner.IsCancellationRequested;
        try
        {
            // The blur shows first, so there is something to fade from while the artwork fetches.
            if (thumbhash != null)
            {
                var placeholder = ThumbhashBitmaps.TryGet(thumbhash, false, out var cached) ? cached : await ThumbhashBitmaps.GetAsync(thumbhash, false);
                if (!Current()) return;
                if (placeholder != null && _image.Source == null) _image.Source = placeholder;
            }
            if (url == null) return;
            var images = App.Services.GetService<ImageService>(); var http = App.Services.GetService<HttpClient>();
            if (images == null || http == null) return;
            var decoded = DecodedArtworkCache.Capture(images, App.Services.GetService<SiloApiClient>(), url, 0, false);
            if (DecodedArtworkCache.TryGet(decoded, out var ready) && ready != null) { _image.Source = ready; return; }
            var bytes = await images.GetImageAsync(imageKey, "backdrop", url, http, owner.Token);
            if (!Current() || bytes == null) return;
            var bitmap = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            if (!Current()) return;
            _image.Source = bitmap;
            DecodedArtworkCache.Remember(decoded, bitmap, this);
        }
        catch (OperationCanceledException) { }
        catch { /* A failed fetch keeps the thumbhash, or the plain page. */ }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool AreSystemAnimationsEnabled()
    {
        try { return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch { return true; }
    }
}
