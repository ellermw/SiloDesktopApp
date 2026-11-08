using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

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

    // Home and Search cards show the same blur while loading, fade a late poster in over it, and clear
    // a recycled card at once.
    private static async Task CardPlaceholderCasesAsync(StackPanel parent, Wire wire, string kind)
    {
        var card = NewCard(kind); parent.Children.Add(card);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recycledGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Until(() => card.IsLoaded);
            var image = CardImage(card);
            var fallback = (DefaultArtwork)card.FindName("ArtworkFallback");
            var reveal = (Grid)card.FindName(kind == "poster" ? "PosterReveal" : "BackdropReveal");
            var blur = (Image)typeof(DefaultArtwork).GetField("_thumbhash", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fallback)!;
            void BindHashed(string id, string hash = SampleThumbhash)
            {
                var item = new MediaItem
                {
                    ContentId = id, Title = id, Type = "movie", PosterThumbhash = hash, BackdropThumbhash = hash,
                    PosterUrl = "https://artwork-motion.invalid/art/" + id, BackdropUrl = "https://artwork-motion.invalid/art/" + id,
                };
                switch (card) { case PosterCard poster: poster.MediaItem = item; break; case LandscapeCard landscape: landscape.MediaItem = item; break; }
            }

            // Detaching invalidates DefaultArtwork's pending generation. The retained binding must
            // request the blur again on attachment, before its real artwork is allowed through.
            var cache = typeof(LibraryGridCard).Assembly.GetType("SiloPlayer.Helpers.ThumbhashBitmaps")!;
            var pending = (Dictionary<(string Hash, bool Dim), Task<ImageSource?>>)cache.GetField("Pending", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var reloadHash = kind == "poster" ? "3QcSHQRnh493V4dIh4eXh1h4kJUI" : "4QcSHQRnh493V4dIh4eXh1h4kJUI";
            var blurReady = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add((reloadHash, false), blurReady.Task);
            try
            {
                var reload = kind + "-placeholder-reload-" + Guid.NewGuid().ToString("N");
                wire.Gates["/art/" + reload] = gate;
                BindHashed(reload, reloadHash);
                Check(blur.Source == null && fallback.Visibility == Visibility.Visible,
                    kind + " blur is still pending before unloading");
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
                    kind + " reloaded card restores a pending blur without waiting for its artwork");
            }
            finally
            {
                pending.Remove((reloadHash, false));
                blurReady.TrySetResult(null);
            }

            var held = kind + "-placeholder-held-" + Guid.NewGuid().ToString("N");
            wire.Gates["/art/" + held] = gate;
            BindHashed(held);
            await Until(() => blur.Source != null);
            Check(fallback.Visibility == Visibility.Visible && image.Opacity == 0 && reveal.Opacity == 0,
                kind + " loading card shows its thumbhash blur instead of an empty frame");
            await Until(() => wire.Requests.ContainsKey("/art/" + held));
            await Task.Delay(150); gate.TrySetResult();
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 } && image.Opacity >= 1 && reveal.Opacity >= 1);
            var animations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            Check(animations ? reveal.OpacityTransition is not null : reveal.OpacityTransition is null,
                kind + " late artwork fades in over its blur only when system animations are on");
            await Until(() => fallback.Visibility == Visibility.Collapsed);

            var recycled = kind + "-placeholder-recycled-" + Guid.NewGuid().ToString("N");
            wire.Gates["/art/" + recycled] = recycledGate;
            BindHashed(recycled);
            Check(image.Source == null && image.Opacity == 0 && reveal.Opacity == 0 && reveal.OpacityTransition is null,
                kind + " recycled card clears the previous artwork at once, without fading it out");
            Check(fallback.Visibility == Visibility.Visible, kind + " recycled card shows its blur while the new artwork loads");
            recycledGate.TrySetResult();
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 } && image.Opacity >= 1 && reveal.Opacity >= 1);
            // A disk hit still goes through the native decoder, but neither the intentional grace
            // delay nor the initial viewport wait should force this quick artwork to fade.
            var fast = kind + "-placeholder-fast-" + Guid.NewGuid().ToString("N");
            var services = SiloPlayer.App.Services;
            var images = (ImageService)services.GetService(typeof(ImageService))!;
            var http = (HttpClient)services.GetService(typeof(HttpClient))!;
            await images.GetImageDiskPathAsync(fast, kind == "poster" ? "poster" : "backdrop",
                "https://artwork-motion.invalid/art/" + fast, http, CancellationToken.None);
            var fastOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler onFastOpened = (_, _) => fastOpened.TrySetResult();
            image.ImageOpened += onFastOpened;
            try
            {
                BindHashed(fast);
                await fastOpened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { image.ImageOpened -= onFastOpened; }
            Check(reveal.OpacityTransition is null && fallback.Visibility == Visibility.Collapsed &&
                image.Opacity >= 1 && reveal.Opacity >= 1,
                kind + " fast disk-cached artwork reveals at once and immediately removes its blur");
            Check(wire.Requests.GetValueOrDefault("/art/" + fast) == 1,
                kind + " instant reveal used the disk cache rather than another download");

            // Drive ImageOpened explicitly with an undecoded source: no native decode timing can
            // accidentally hide a fade that started (and finished) before the image could draw.
            var controller = card.GetType().GetField("_reveal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(card)!;
            void InvokeReveal(string method, params object[] args)
                => controller.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, args);
            InvokeReveal("Hide");
            image.Source = new BitmapImage();
            fallback.Visibility = Visibility.Visible;
            InvokeReveal("Begin");
            await Task.Delay(150);
            InvokeReveal("Reveal", false);
            await Task.Delay(300);
            Check(image.Opacity == 0 && reveal.Opacity == 0 && reveal.OpacityTransition is null &&
                fallback.Visibility == Visibility.Visible,
                kind + " pending native decode cannot consume the reveal fade");
            InvokeReveal("Opened");
            Check(image.Opacity >= 1 && reveal.Opacity >= 1 &&
                (animations ? reveal.OpacityTransition is not null && fallback.Visibility == Visibility.Visible
                    : reveal.OpacityTransition is null && fallback.Visibility == Visibility.Collapsed),
                kind + " late reveal starts only when ImageOpened makes artwork drawable");
            await Until(() => fallback.Visibility == Visibility.Collapsed);

            // Already-decoded sources may open synchronously during Source assignment, before Reveal.
            InvokeReveal("Hide");
            image.Source = new BitmapImage();
            fallback.Visibility = Visibility.Visible;
            InvokeReveal("Begin");
            InvokeReveal("Opened");
            InvokeReveal("Reveal", false);
            Check(reveal.OpacityTransition is null && fallback.Visibility == Visibility.Collapsed && reveal.Opacity >= 1,
                kind + " quick synchronous ImageOpened also reveals immediately");
            Program.Log("PASS " + kind + " placeholder: pending blur restored on reload, fade after decode when late, instant reveal when quick, instant clear on recycle.");
        }
        finally
        {
            gate.TrySetResult(); recycledGate.TrySetResult();
            parent.Children.Remove(card);
        }
    }
}
