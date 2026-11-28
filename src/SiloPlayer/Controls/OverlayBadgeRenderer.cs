using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Helpers;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using Windows.Graphics.Imaging;

namespace SiloPlayer.Controls;

/// <summary>
/// The live span of a card's badges, driven by the card (its badge hosts' Loaded and Unloaded) instead of
/// each badge's own: a badge's own Loaded and Unloaded handlers are objects every full garbage collection
/// walks, two or three badges per card.
/// </summary>
internal sealed class BadgeLifetime
{
    private readonly List<(Action Attach, Action Detach)> _badges = new();
    internal bool Attached { get; private set; }

    internal void Add(Action attach, Action detach)
    {
        _badges.Add((attach, detach));
        if (Attached) attach();
    }

    internal void Clear()
    {
        if (Attached) for (var i = 0; i < _badges.Count; i++) _badges[i].Detach();
        _badges.Clear();
    }

    internal void SetAttached(bool attached)
    {
        if (Attached == attached) return;
        Attached = attached;
        for (var i = 0; i < _badges.Count; i++) (attached ? _badges[i].Attach : _badges[i].Detach)();
    }
}

internal static class OverlayBadgeRenderer
{
    private static readonly SemaphoreSlim ImageDecodes = new(2, 2);
    /// <param name="lifetime">A card's badges live while it is attached (and refresh their caption then); without it a badge follows its own Loaded and Unloaded.</param>
    internal static Border Build(string text, string id, OverlayItemConfig config, string preset, double scale,
        OverlayStyle? style = null, string? valueKey = null, CardOverlayService? images = null, Func<string>? display = null, BadgeLifetime? lifetime = null)
    {
        scale = double.IsFinite(scale) && scale > 0 ? Math.Clamp(scale, .1, 8) : 1;
        var accent = config.AccentColor ?? DefaultAccent(id) ?? OverlayRegistry.DataAccent(id, text);
        var configOwner = images?.CaptureImageOwner();
        var look = OverlayAppearance.Resolve(preset, style, accent);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = look.IconGap * scale, VerticalAlignment = VerticalAlignment.Center };
        var foreground = Brush(look.Foreground);
        var badge = new Border
        {
            Background = Brush(look.Background), BorderBrush = Brush(look.Border),
            BorderThickness = new Thickness((look.BorderLeftWidth > 0 ? look.BorderLeftWidth : look.BorderWidth) * scale, look.BorderWidth * scale, look.BorderWidth * scale, look.BorderWidth * scale),
            CornerRadius = new CornerRadius(look.Radius * scale),
            Padding = new Thickness(look.PaddingInline * scale, look.PaddingBlock * scale, look.PaddingInline * scale, look.PaddingBlock * scale),
            Child = content, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false, Tag = id,
        };
        if (preset is not "minimal" and not "band" && config.Position is OverlayPosition.LeftEdge) badge.CornerRadius = new(0, look.Radius * scale, look.Radius * scale, 0);
        if (preset is not "minimal" and not "band" && config.Position is OverlayPosition.RightEdge) badge.CornerRadius = new(look.Radius * scale, 0, 0, look.Radius * scale);
        if (preset is not "minimal" and not "band" && config.Position is OverlayPosition.LeftEdge) badge.BorderThickness = badge.BorderThickness with { Left = 0 };
        if (preset is not "minimal" and not "band" && config.Position is OverlayPosition.RightEdge) badge.BorderThickness = badge.BorderThickness with { Right = 0 };
        if (preset is not "minimal" and not "band" && config.Position is OverlayPosition.LeftEdge or OverlayPosition.RightEdge)
            badge.SizeChanged += (_, _) =>
            {
                if (badge.Child is Image) return;
                var radius = Math.Min(look.Radius * scale, Math.Min(badge.ActualWidth, badge.ActualHeight) / 2);
                badge.CornerRadius = config.Position == OverlayPosition.LeftEdge ? new(0, radius, radius, 0) : new(radius, 0, 0, radius);
            };
        TextBlock Label(string value, bool score = false) => new()
        {
            Text = look.Uppercase ? value.ToUpperInvariant() : value,
            FontSize = look.FontSize * scale, FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)look.FontWeight },
            CharacterSpacing = preset is "minimal" or "square" ? 100 : 25,
            LineHeight = look.FontSize * scale, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextTrimming = score ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            Foreground = foreground, VerticalAlignment = VerticalAlignment.Center,
        };
        string DisplayText()
        {
            if (display != null) return display();
            if (id == "availability") return OverlayRegistry.LocalizeAvailability(text);
            var key = (id, valueKey) switch
            {
                ("status", "pending") => "web.item.card.statusScanning",
                ("status", "unmatched") => "web.item.card.statusUnmatched",
                ("status", "ambiguous") => "web.item.card.statusAmbiguous",
                ("show_status", "returning") => "web.settings.overlays.badge.showStatus.value.returning",
                ("show_status", "ended") => "web.settings.overlays.badge.showStatus.value.ended",
                ("show_status", "cancelled") => "web.settings.overlays.badge.showStatus.value.cancelled",
                ("show_status", "upcoming") => "web.settings.overlays.badge.showStatus.value.upcoming",
                ("upcoming_event", "series_premiere") => "web.calendar.event.badge.seriesPremiere",
                ("upcoming_event", "season_premiere") => "web.calendar.event.badge.seasonPremiere",
                ("upcoming_event", "finale") => "web.calendar.event.badge.finale",
                ("upcoming_event", "digital") => "web.calendar.event.badge.digital",
                ("upcoming_event", "physical") => "web.calendar.event.badge.physical",
                _ => null
            };
            return key == null ? text : SiloPlayer.Core.Localization.AppStrings.Get(key, fallback: text);
        }
        var source = SourceMark(id);
        var score = source is not null ? BareScore(text, source) : null;
        void Plain()
        {
            content.Children.Clear();
            if (look.SourceChips && source is not null)
            {
                var color = OverlayColor.Parse(accent) ?? new OverlayColor(255, 255, 255);
                var mark = Label(source); mark.Foreground = Brush(color.ReadableText);
                content.Children.Add(new Border { Background = Brush(color), CornerRadius = new(look.FontSize * scale * .2), Padding = new(look.FontSize * scale * .35, look.FontSize * scale * .15, look.FontSize * scale * .35, look.FontSize * scale * .15), Child = mark });
            }
            else if (source is null && (config.ShowIcon ?? look.PreferIcon) && Glyph(id, text) is { } glyph)
                content.Children.Add(new FontIcon { Glyph = glyph, FontSize = look.IconSize * scale, Width = look.IconSize * scale, Height = look.IconSize * scale, Foreground = foreground });
            content.Children.Add(Label(source is not null ? look.SourceChips ? score! : source + " " + score : DisplayText(), look.SourceChips && score is not null));
        }
        void Caption()
        {
            Plain();
            AutomationProperties.SetName(badge, source is null ? DisplayText() : source + " " + score);
        }
        AppLocalization.Follower? captions = null;
        if (lifetime is null) MediaItemCardActions.BindText(badge, "Caption", _ => Caption());
        else { captions = new AppLocalization.Follower(Caption, badge.DispatcherQueue); captions.Apply(); }
        // Live: loaded (a badge on its own), or attached (a card's badge, which may not have loaded yet).
        var attached = false;
        bool Live() => lifetime is null ? badge.IsLoaded : attached;
        if (look.BoxShadow || look.TextShadow)
        {
            badge.Shadow = new ThemeShadow();
            badge.Translation = new Vector3(0, 0, (float)((look.BoxShadow ? 8 : 3) * scale));
        }
        var epoch = 0;
        var originalPadding = badge.Padding;
        var originalBackground = badge.Background;
        var originalBorder = badge.BorderThickness;
        var originalShadow = badge.Shadow;
        async Task<BitmapImage?> DecodeAsync(string? reference, int owner, ApiIdentityContext context)
        {
            if (images is null || !SiloApiClient.IsOverlayImageRef(reference)) return null;
            try
            {
                var bytes = await images.GetImageAsync(reference!);
                if (bytes is null || owner != epoch || !Live() || !images.IsCurrentImageOwner(context)) return null;
                await ImageDecodes.WaitAsync();
                try
                {
                    if (owner != epoch || !Live() || !images.IsCurrentImageOwner(context)) return null;
                    using var input = new MemoryStream(bytes, false);
                    var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
                    if (decoder.PixelWidth is 0 or > 600 || decoder.PixelHeight is 0 or > 300) return null;
                    input.Position = 0;
                    var bitmap = new BitmapImage { DecodePixelHeight = Math.Min(300, (int)Math.Ceiling((look.FontSize + 2 * look.PaddingBlock) * scale * 1.6 * 2)) };
                    await bitmap.SetSourceAsync(input.AsRandomAccessStream());
                    return owner == epoch && Live() && images.IsCurrentImageOwner(context) ? bitmap : null;
                }
                finally { ImageDecodes.Release(); }
            }
            catch { return null; }
        }
        async void Load(object sender, RoutedEventArgs args)
        {
            if (images is null || !images.SupportsImages || configOwner is not { } bound || !images.IsCurrentImageOwner(bound)) return;
            var owner = ++epoch;
            var context = images.CaptureImageOwner();
            var valueRef = OverlayRegistry.ResolveValueImage(config, id, valueKey);
            var bitmap = await DecodeAsync(valueRef, owner, context);
            if (owner != epoch || !Live() || !images.IsCurrentImageOwner(context)) return;
            if (bitmap is not null)
            {
                badge.Child = new Image { FlowDirection = FlowDirection.LeftToRight, Source = bitmap, Height = (look.FontSize + 2 * look.PaddingBlock) * scale * 1.6, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                badge.Background = null; badge.BorderThickness = new(0); badge.Padding = new(0); badge.Shadow = null;
                RoundedBadge.SetRadius(badge, 0); badge.CornerRadius = new(0);
                return;
            }
            bitmap = await DecodeAsync(config.Image, owner, context);
            if (bitmap is null || owner != epoch || !Live() || !images.IsCurrentImageOwner(context)) return;
            content.Children.Clear();
            content.Children.Add(new Image { FlowDirection = FlowDirection.LeftToRight, Source = bitmap, Height = look.IconSize * scale, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(Label(score ?? DisplayText(), score is not null));
            badge.InvalidateMeasure();
        }
        void Reset()
        {
            ++epoch; badge.Child = content; badge.Padding = originalPadding; badge.Background = originalBackground; badge.BorderThickness = originalBorder;
            badge.Shadow = originalShadow;
            if (config.Position is not OverlayPosition.LeftEdge and not OverlayPosition.RightEdge || preset is "minimal" or "band")
                RoundedBadge.Apply(badge, look.Radius * scale);
            Plain();
        }
        void Retire() => badge.DispatcherQueue.TryEnqueue(Reset);
        void Reload() => badge.DispatcherQueue.TryEnqueue(() =>
        {
            if (!Live()) return;
            Reset(); Load(badge, new RoutedEventArgs());
        });
        void Unsubscribe() { if (images is not null) { images.ImagesInvalidated -= Retire; images.Changed -= Reload; } }
        void Attach()
        {
            // A repeated Loaded must not stack handlers on the long-lived service.
            Unsubscribe();
            if (images is not null) { images.ImagesInvalidated += Retire; images.Changed += Reload; }
            Load(badge, new RoutedEventArgs());
        }
        if (lifetime is null)
        {
            badge.Loaded += (_, _) => Attach();
            badge.Unloaded += (_, _) =>
            {
                // WinUI can deliver a queued Unloaded after this badge has already been reattached.
                if (badge.IsLoaded) return;
                Unsubscribe(); Reset();
            };
        }
        else lifetime.Add(
            () => { attached = true; captions!.Attach(); Attach(); },
            () => { attached = false; captions!.Detach(); Unsubscribe(); Reset(); });
        if (config.Position is not OverlayPosition.LeftEdge and not OverlayPosition.RightEdge || preset is "minimal" or "band")
            RoundedBadge.Apply(badge, look.Radius * scale);
        return badge;
    }

    // One brush per colour and UI thread, shared by every badge: each brush made is an object full collections walk.
    // Badges only ever assign these, never change them.
    [ThreadStatic] private static Dictionary<uint, SolidColorBrush>? t_brushes;

    internal static SolidColorBrush Brush(OverlayColor color)
    {
        var key = (uint)color.Alpha << 24 | (uint)color.Red << 16 | (uint)color.Green << 8 | color.Blue;
        var brushes = t_brushes ??= new();
        if (!brushes.TryGetValue(key, out var brush))
            brushes[key] = brush = new(Microsoft.UI.ColorHelper.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
        return brush;
    }
    internal static string? DefaultAccent(string id) => id switch { "rating_imdb" => "#f5c518", "rating_tmdb" => "#01b4e4", "rating_rt" => "#fa320a", "rating_rt_audience" => "#fa6400", _ => null };
    internal static string? SourceMark(string id) => id switch { "rating_imdb" => "IMDb", "rating_tmdb" => "TMDB", "rating_rt" => "RT", "rating_rt_audience" => "RT Aud", _ => null };
    private static string BareScore(string text, string mark) => text.StartsWith(mark + " ", StringComparison.OrdinalIgnoreCase) ? text[(mark.Length + 1)..] : text;
    // Availability shows its check only for a complete title, as the WebUI's getIcon does.
    private static string? Glyph(string id, string text) => id == "availability" ? OverlayRegistry.IsAvailabilityComplete(text) ? ((char)0xE73E).ToString() : null : Glyph(id);
    private static string? Glyph(string id) => id switch
    {
        "resolution" or "resolution_hdr" or "resolution_hdr_audio" or "hdr" or "video_codec" => "\uE7F4",
        "audio" or "audio_channels" => "\uE767", "multi_audio" or "original_language" => "\uE8C1", "multi_sub" => "\uED1E",
        "rating_imdb" or "rating_tmdb" or "rating_rt" or "rating_rt_audience" => "\uE734", "runtime" => "\uE823", "year" => "\uE787",
        "studio" or "network" => "\uE80F", "content_rating" => "\uE72E", "show_status" => "\uE735", "container" or "release_type" or "edition" => "\uE8B7", "aspect_ratio" => "\uE740", _ => null,
    };
}

