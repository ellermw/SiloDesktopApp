using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Converters;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

/// <summary>
/// One collection on a Home or library row of collections (official CollectionsRow / CollectionPosterCard, no pin and no menu): the poster with its
/// title count, the name, and "by Name" under another profile's shared collection. It looks like the cards of the Collections page and opens the
/// collection the same way.
/// </summary>
public sealed class CollectionPosterCard : UserControl
{
    public static readonly DependencyProperty CardProperty =
        DependencyProperty.Register(nameof(Card), typeof(SectionCollectionCard), typeof(CollectionPosterCard), new PropertyMetadata(null, (d, _) => ((CollectionPosterCard)d).Rebuild()));

    public SectionCollectionCard? Card
    {
        get => (SectionCollectionCard?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    private double _cardWidth = 185;
    private long _generation;
    private Border? _poster;
    private TextBlock? _byline;
    private SectionCollectionCard? _observedCard;

    public CollectionPosterCard()
    {
        IsTabStop = false;
        // Cards of different heights (a byline under one) share a top edge, as the row's title cards do.
        VerticalAlignment = VerticalAlignment.Top;
        // The row's card objects outlive a recycled control: it listens only while it is in the tree, and catches up when it comes back.
        Loaded += (_, _) => { ObservePosterUrl(Card); RefreshPoster(); };
        Unloaded += (_, _) => { ++_generation; if (!IsLoaded) ObservePosterUrl(null); };
    }

    private void ObservePosterUrl(SectionCollectionCard? card)
    {
        if (ReferenceEquals(_observedCard, card)) return;
        if (_observedCard != null) _observedCard.PosterUrlChanged -= OnPosterUrlChanged;
        _observedCard = card;
        if (_observedCard != null) _observedCard.PosterUrlChanged += OnPosterUrlChanged;
    }

    private void OnPosterUrlChanged(object? sender, EventArgs e)
    {
        if (IsLoaded && ReferenceEquals(sender, Card)) RefreshPoster();
    }

    private ImageSource PosterSource(SectionCollectionCard card)
        => (ImageSource)new UrlToImageSourceConverter().Convert(card.PosterUrl, typeof(ImageSource), _cardWidth, "");

    /// <summary>
    /// A refresh renews the card's presigned poster URL. A decoded poster stays as it is; one that never arrived (its earlier address may
    /// have expired before the card came into view) is read again from the card's current address.
    /// </summary>
    private void RefreshPoster()
    {
        if (Card is not { } card || _poster?.Child is not Grid art) return;
        var image = art.Children.OfType<Image>().FirstOrDefault();
        // A poster that came or went changes what the card draws.
        if ((image == null) != (card.PosterUrl.Length == 0)) { Rebuild(); return; }
        if (image == null || UrlToImageSourceConverter.GetLoadOutcome(image.Source) is { IsCompletedSuccessfully: true, Result: true }) return;
        image.Source = PosterSource(card);
    }

    /// <summary>The poster's width; its height follows at 2:3.</summary>
    public void SetCardWidth(double width)
    {
        var resized = _cardWidth != Math.Max(96, width);
        _cardWidth = Math.Max(96, width);
        Width = _cardWidth;
        if (_poster == null) return;
        _poster.Width = _cardWidth; _poster.Height = _cardWidth * 1.5;
        // The decode follows the width. At the same width a decoded poster stays, whatever address the card has been given since.
        if (!resized) { RefreshPoster(); return; }
        if (_poster.Child is Grid art && art.Children.OfType<Image>().FirstOrDefault() is { } image && Card is { PosterUrl.Length: > 0 } card)
            image.Source = PosterSource(card);
    }

    private void Rebuild()
    {
        var generation = ++_generation;
        _poster = null; _byline = null;
        ObservePosterUrl(IsLoaded ? Card : null);
        if (Card is not { } card) { Content = null; return; }
        Brush Resource(string key) => (Brush)Application.Current.Resources[key];

        var poster = new Border { Width = _cardWidth, Height = _cardWidth * 1.5, CornerRadius = new CornerRadius(16), Background = Resource("SurfaceBrush"),
            BorderThickness = new Thickness(1), BorderBrush = CollectionsPage.CollectionPosterBorder() };
        var art = new Grid();
        if (card.PosterUrl.Length > 0)
            art.Children.Add(new Image { Stretch = Stretch.UniformToFill, Source = PosterSource(card) });
        else
        {
            // No poster: the name stands in the poster, with the personal-collection mark above it.
            var placeholder = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) };
            if (card.IsPersonal)
            {
                var user = WebUiIcon.Create("user", 20, Resource("SecondaryTextBrush")); user.Opacity = .6; user.HorizontalAlignment = HorizontalAlignment.Center;
                placeholder.Children.Add(user);
            }
            placeholder.Children.Add(new TextBlock { Text = card.Title, FontSize = 14, FontWeight = FontWeights.Medium, Foreground = Resource("SecondaryTextBrush"), MaxLines = 3,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            art.Children.Add(placeholder);
        }
        // The count sits on the poster's bottom corner, as the WebUI's chip does.
        var count = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8), Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(153, 0, 0, 0)),
            Child = new TextBlock { Text = CollectionText.Number(card.ItemCount), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
            IsHitTestVisible = false,
        };
        art.Children.Add(count);
        poster.Child = art;
        poster.Loaded += (_, _) => CollectionsPage.ClipCollectionPoster(poster); poster.SizeChanged += (_, _) => CollectionsPage.ClipCollectionPoster(poster);
        _poster = poster;

        var stack = new StackPanel();
        stack.Children.Add(poster);
        stack.Children.Add(new TextBlock { Text = card.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 10, 2, 0) });
        var viewing = App.Services.GetRequiredService<AuthService>().SelectedProfileId;
        if (card.IsAnotherProfilesCollection(viewing))
        {
            _byline = new TextBlock { FontSize = 12, Foreground = Resource("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 2, 0) };
            SetByline(card, HomeRowOwnerNames.NameOf(card.CreatorProfileId!));
            stack.Children.Add(_byline);
            if (!HomeRowOwnerNames.Known(card.CreatorProfileId!))
                _ = NameOwnerAsync(card, generation);
        }
        else if (card.IsPersonal)
        {
            stack.Children.Add(new TextBlock { Text = CollectionText.Get("web.collections.posterCard.userCollection", "User collection"), FontSize = 12, Foreground = Resource("SecondaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 2, 0) });
        }

        var open = new Button { Content = stack, Padding = new Thickness(0), Style = (Style)Application.Current.Resources["PosterHitTargetButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(open, CollectionText.Open(card.Title));
        open.Click += (_, _) => Open(card);
        var layer = new Grid(); layer.Children.Add(open);
        var frame = new Border { Child = layer, Tag = card };
        CollectionsPage.ConfigurePosterImageHover(frame, poster);
        Content = frame;
    }

    private void SetByline(SectionCollectionCard card, string owner)
    {
        if (_byline == null) return;
        _byline.Text = CollectionText.Get("web.collections.posterCard.byOwner", "by " + owner, ("owner", owner));
    }

    private async Task NameOwnerAsync(SectionCollectionCard card, long generation)
    {
        try
        {
            await HomeRowOwnerNames.EnsureAsync();
            if (generation != _generation || !ReferenceEquals(Card, card)) return;
            SetByline(card, HomeRowOwnerNames.NameOf(card.CreatorProfileId!));
        }
        catch (OperationCanceledException) { }
        catch { /* The byline keeps its neutral name when the profile list cannot be read. */ }
    }

    /// <summary>Opens the collection: a server collection in the library its card names, a personal one on its own.</summary>
    internal static void Open(SectionCollectionCard card)
        => App.Services.GetRequiredService<NavigationService>().Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
        {
            CollectionId = card.Id, Title = card.Title, IsUserCollection = card.IsPersonal, LibraryId = card.IsPersonal ? null : card.LibraryId,
            Subtitle = card.IsPersonal ? "Personal collection" : "Server collection",
        });
}

/// <summary>
/// The profile names a "by Name" line needs, read once per signed-in profile and only when a row shows another profile's shared collection.
/// A reply that arrives after the account or profile changed is dropped.
/// </summary>
internal static class HomeRowOwnerNames
{
    private static ApiRequestContext? _context;
    private static Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private static Task? _loading;

    internal const string Neutral = "Another profile";

    internal static bool Known(string profileId) => _context == App.Services.GetRequiredService<SiloApiClient>().CaptureContext() && _names.ContainsKey(profileId);

    internal static string NameOf(string profileId)
        => _context == App.Services.GetRequiredService<SiloApiClient>().CaptureContext() && _names.TryGetValue(profileId, out var name) ? name : Neutral;

    internal static Task EnsureAsync()
    {
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var context = client.CaptureContext();
        if (_context == context && _loading != null) return _loading;
        _context = context; _names = new(StringComparer.Ordinal);
        return _loading = ReadAsync(client, context);
    }

    private static async Task ReadAsync(SiloApiClient client, ApiRequestContext context)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Core.Models.Auth.ProfilesResponse response;
        try { response = await App.Services.GetRequiredService<AuthApi>().GetProfilesAsync(timeout.Token); }
        catch { if (_context == context) _loading = null; throw; }
        if (!client.IsCurrentContext(context) || _context != context) return;
        _names = response.Profiles.GroupBy(profile => profile.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
    }
}
