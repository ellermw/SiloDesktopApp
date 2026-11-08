using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Home;

internal static partial class ArtworkMotionNativeFixture
{
    // The ThumbHash project's published sample; ThumbhashSampleTests checks it decodes.
    internal const string SampleThumbhash = "1QcSHQRnh493V4dIh4eXh1h4kJUI";

    // Library cards show their blurred poster while the poster loads, fade a late poster in over it, and a
    // recycled card clears its old poster at once and shows a cached blur in the same frame.
    private static async Task LibraryPlaceholderCasesAsync(StackPanel parent, Wire wire)
    {
        var card = new LibraryGridCard(); parent.Children.Add(card);
        var heldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recycledGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Until(() => card.IsLoaded);
            var image = CardImage(card);
            var fallback = (DefaultArtwork)typeof(LibraryGridCard).GetField("_artworkFallback", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(card)!;
            var blur = (Image)typeof(DefaultArtwork).GetField("_thumbhash", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fallback)!;
            var reveal = (Grid)typeof(LibraryGridCard).GetField("_posterReveal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(card)!;
            var glow = (Grid)typeof(DefaultArtwork).GetField("_glow", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fallback)!;
            void BindHashed(string id) => card.Bind(new MediaItem
            {
                ContentId = id, Title = id, Type = "movie", PosterThumbhash = SampleThumbhash,
                PosterUrl = "https://artwork-motion.invalid/art/" + id,
            }, "title");

            // Hold the shared blur result so unloading always happens before it is available.
            var cache = typeof(LibraryGridCard).Assembly.GetType("SiloPlayer.Helpers.ThumbhashBitmaps")!;
            var pending = (Dictionary<(string Hash, bool Dim), Task<ImageSource?>>)cache.GetField("Pending", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            const string reloadHash = "2QcSHQRnh493V4dIh4eXh1h4kJUI";
            var blurReady = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add((reloadHash, false), blurReady.Task);
            try
            {
                wire.Gates["/art/placeholder-reload"] = heldGate;
                card.Bind(new MediaItem
                {
                    ContentId = "placeholder-reload", Title = "placeholder-reload", Type = "movie", PosterThumbhash = reloadHash,
                    PosterUrl = "https://artwork-motion.invalid/art/placeholder-reload",
                }, "title");
                Check(blur.Source == null && glow.Visibility == Visibility.Collapsed,
                    "library blur is still pending before unloading");
                var generation = typeof(DefaultArtwork).GetField("_generation", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var beforeUnload = (int)generation.GetValue(fallback)!;
                parent.Children.Remove(card);
                await Until(() => !card.IsLoaded && (int)generation.GetValue(fallback)! != beforeUnload);
                parent.Children.Add(card);
                await Until(() => card.IsLoaded);
                var picture = new WriteableBitmap(1, 1);
                blurReady.TrySetResult(picture);
                await Until(() => ReferenceEquals(blur.Source, picture));
                Check(fallback.Visibility == Visibility.Visible && blur.Visibility == Visibility.Visible && image.Source == null,
                    "reloaded library card restores a pending blur without waiting for its poster");
            }
            finally
            {
                pending.Remove((reloadHash, false));
                blurReady.TrySetResult(null);
            }

            wire.Gates["/art/placeholder-held"] = heldGate;
            BindHashed("placeholder-held");
            Check(glow.Visibility == Visibility.Collapsed, "library loading card hides the type mark behind its blur");
            await Until(() => blur.Source != null);
            Check(fallback.Visibility == Visibility.Visible && blur.Visibility == Visibility.Visible && image.Opacity == 0,
                "library loading card shows its thumbhash blur instead of an empty box");

            await Task.Delay(150); heldGate.TrySetResult();
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 } && image.Opacity >= 1);
            var animations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            Check(reveal.Opacity >= 1 && (animations ? reveal.OpacityTransition is not null : reveal.OpacityTransition is null),
                "a late poster fades in over its blur only when system animations are on");
            await Until(() => fallback.Visibility == Visibility.Collapsed);

            wire.Gates["/art/placeholder-recycled"] = recycledGate;
            BindHashed("placeholder-recycled");
            Check(image.Source == null && image.Opacity == 0 && reveal.Opacity == 0 && reveal.OpacityTransition is null,
                "a recycled card clears the previous poster at once, without fading it out");
            Check(blur.Source != null && fallback.Visibility == Visibility.Visible,
                "a recycled card shows a cached blur in the same frame as the bind");
            var cached = (int)cache.GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var capacity = (int)cache.GetField("Capacity", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Check(cached > 0 && cached <= capacity, "the shared blur cache holds the blur and stays bounded");
            recycledGate.TrySetResult();
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 } && image.Opacity >= 1);
            // An invalid hash while loading keeps the type mark rather than an empty box.
            var invalidGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.Gates["/art/placeholder-invalid"] = invalidGate;
            card.Bind(new MediaItem
            {
                ContentId = "placeholder-invalid", Title = "placeholder-invalid", Type = "movie", PosterThumbhash = "not base64",
                PosterUrl = "https://artwork-motion.invalid/art/placeholder-invalid",
            }, "title");
            await Until(() => glow.Visibility == Visibility.Visible);
            Check(blur.Source == null && fallback.Visibility == Visibility.Visible, "an invalid loading thumbhash falls back to the type mark");
            invalidGate.TrySetResult();
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 } && image.Opacity >= 1);
            Program.Log("PASS library poster placeholder: blur while loading, fade when late, instant clear on recycle, type mark for an invalid hash.");
        }
        finally
        {
            heldGate.TrySetResult(); recycledGate.TrySetResult();
            parent.Children.Remove(card);
        }
    }
}
