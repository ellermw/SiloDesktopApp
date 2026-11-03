using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Views;

internal static partial class ArtworkMotionNativeFixture
{
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Tab(LibraryPage page, string name) =>
        ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName(name + "Tab")).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task RecommendedCasesAsync(LibraryPage page, Frame frame, Wire wire, SiloApiClient client)
    {
        Tab(page, "Recommended");
        var panel = (Panel)page.FindName("RecommendedSectionsPanel");
        await Until(() => panel.Children.OfType<SectionRow>().Count(row => row.Section?.LoadCompleted == true) == 2);
        await Task.Delay(700);
        foreach (var kind in new[] { "poster", "landscape" })
        {
            var row = panel.Children.OfType<SectionRow>().Single(row => row.Section!.Id == kind);
            var rail = Descendants<ScrollViewer>(row).Single();
            var card = Descendants<FrameworkElement>(row).First(c => kind == "poster" ? c is PosterCard : c is LandscapeCard);
            var image = CardImage(card);
            async Task Move()
            {
                var target = rail.HorizontalOffset + 350;
                rail.ChangeView(target, null, null, true);
                await Until(() => Math.Abs(rail.HorizontalOffset - target) < 1);
            }
            await Move(); Bind(card, "recommended-hidden-" + kind);
            Tab(page, "Library"); await Task.Delay(600);
            Check(!wire.Requests.ContainsKey("/art/recommended-hidden-" + kind) && image.Source == null,
                kind + " completed Recommended tab cancels deferred artwork while retained hidden");
            Tab(page, "Recommended");
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 });
            Check(ReferenceEquals(row, panel.Children.OfType<SectionRow>().Single(r => r.Section!.Id == kind)), kind + " completed Recommended row retained on return");
            await Move(); Bind(card, "recommended-depart-" + kind);
            frame.Navigate(typeof(Page)); await Task.Delay(400);
            Check(!wire.Requests.ContainsKey("/art/recommended-depart-" + kind), kind + " completed Recommended departure cancels current work");
            frame.GoBack(); await Until(() => ReferenceEquals(frame.Content, page) && page.IsLoaded);
            Tab(page, "Recommended");
            await Until(() => Descendants<FrameworkElement>(row).Any(c => (c is PosterCard or LandscapeCard) && CardImage(c).Source is BitmapImage { PixelWidth: > 0 }));
            Tab(page, "Library");
            Bind(card, "recommended-fresh-hidden-" + kind); await Task.Delay(400);
            Check(!wire.Requests.ContainsKey("/art/recommended-fresh-hidden-" + kind), kind + " explicit bind while Recommended hidden stays suspended");
            Tab(page, "Recommended"); await Until(() => image.Source is BitmapImage { PixelWidth: > 0 });
        }
        await RecommendedReturnWhileSettingsPendingAsync(page, frame, wire, client, changeAccessContext: false);
        await RecommendedReturnWhileSettingsPendingAsync(page, frame, wire, client, changeAccessContext: true);
    }

    private static async Task RecommendedReturnWhileSettingsPendingAsync(LibraryPage page, Frame frame, Wire wire,
        SiloApiClient client, bool changeAccessContext)
    {
        var panel = (Panel)page.FindName("RecommendedSectionsPanel");
        var cards = panel.Children.OfType<SectionRow>()
            .Select(row => Descendants<FrameworkElement>(row).First(card => card is PosterCard or LandscapeCard)).ToArray();
        Check(cards.Length == 2, "Retained Recommended return covers both poster and landscape rows.");
        var currentTab = typeof(LibraryPage).GetField("_currentTab", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await Until(() => cards.All(card => CardImage(card).Source is BitmapImage { PixelWidth: > 0 }));
        var requests = cards.Select(card =>
        {
            var item = card is PosterCard poster ? poster.MediaItem! : ((LandscapeCard)card).MediaItem!;
            var path = new Uri(item.PosterUrl!).AbsolutePath;
            return (Path: path, Count: wire.Requests.GetValueOrDefault(path));
        }).ToArray();
        frame.Navigate(typeof(Page));
        await Until(() => !page.IsLoaded && cards.All(card => CardImage(card).Source == null));
        await Task.Delay(200); // Drain departure state writes before holding the return read.
        var heldReads = wire.HeldLibraryStateReads;
        var stateGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.LibraryStateReadGate = stateGate;
        try
        {
            frame.GoBack();
            await Until(() => ReferenceEquals(frame.Content, page) && page.IsLoaded && wire.HeldLibraryStateReads > heldReads);
            if (changeAccessContext) client.InvalidateAccessContext();
            // Deliberately do not click a tab: Loaded must recover these retained
            // poster and landscape binds using decoded/disk cache while settings wait.
            await Until(() => cards.All(card => CardImage(card).Source is BitmapImage { PixelWidth: > 0 }));
            Check(!stateGate.Task.IsCompleted && requests.All(request => wire.Requests.GetValueOrDefault(request.Path) == request.Count),
                $"Cached Recommended artwork resumes without network before settings complete (access race={changeAccessContext}).");
        }
        finally { wire.LibraryStateReadGate = null; stateGate.TrySetResult(); }
        await Task.Delay(300); // Let restoration reach ShowTab or its stale-context early exit.
        if (changeAccessContext)
        {
            foreach (var card in cards)
                Bind(card, "recommended-after-access-refresh-" + card.GetType().Name);
            await Until(() => cards.All(card => CardImage(card).Source is BitmapImage { PixelWidth: > 0 }));
            Check((string)currentTab.GetValue(page)! == "Recommended",
                "Recommended activity remains live after state restoration exits on an access-context change.");
        }
        else
        {
            // The original navigation parameter requests Library once restoration
            // completes; wait for that correction before starting the next return.
            await Until(() => (string)currentTab.GetValue(page)! == "Library");
            Tab(page, "Recommended");
            await Until(() => cards.All(card => CardImage(card).Source is BitmapImage { PixelWidth: > 0 }));
        }
    }

    private static async Task FirstMountCasesAsync(StackPanel parent, Wire wire, SiloApiClient client, EpisodeSpoilerPreferences spoilers)
    {
        await OverlayFirstMountCasesAsync(parent);
        await spoilers.EnsureLoadedAsync();
        foreach (var kind in new[] { "poster", "landscape" })
        foreach (var variant in new[] { "movie", "blur", "cached" })
        {
            var id = $"first-mount-{kind}-{variant}";
            var card = NewCard(kind);
            BitmapImage? cached = null;
            if (variant == "cached")
            {
                var seed = NewCard(kind); parent.Children.Insert(0, seed); await Until(() => seed.IsLoaded);
                Bind(seed, id); await Until(() => CardImage(seed).Source is BitmapImage { PixelWidth: > 0 });
                cached = (BitmapImage)CardImage(seed).Source;
                parent.Children.Remove(seed); await Until(() => !seed.IsLoaded);
            }
            if (variant == "blur")
            {
                var episode = new MediaItem { ContentId = id, Title = id, Type = "episode", PosterIsEpisodeStill = true, BackdropIsEpisodeStill = true,
                    PosterUrl = "https://artwork-motion.invalid/art/" + id, BackdropUrl = "https://artwork-motion.invalid/art/" + id };
                if (card is PosterCard poster) poster.MediaItem = episode; else ((LandscapeCard)card).MediaItem = episode;
            }
            else Bind(card, id);
            await Task.Delay(650);
            Check(!card.IsLoaded, id + " bind deliberately outlives initial artwork grace before first mount");
            if (cached != null) Check(ReferenceEquals(CardImage(card).Source, cached), id + " cheap decoded reuse remains immediate");
            parent.Children.Insert(0, card); await Until(() => card.IsLoaded); await Task.Delay(800);
            Check(CardImage(card).Source is BitmapImage { PixelWidth: > 0 }, id + " valid current bind presents after delayed first mount");
            parent.Children.Remove(card); await Until(() => !card.IsLoaded);
        }
        var stale = new PosterCard(); Bind(stale, "first-mount-old-profile"); await Task.Delay(650);
        client.SetProfile("first-mount-successor"); parent.Children.Insert(0, stale); await Until(() => stale.IsLoaded); await Task.Delay(300);
        Check(CardImage(stale).Source == null, "Delayed first mount does not reauthorize an outgoing profile DTO");
        Bind(stale, "first-mount-authorized-profile"); await Until(() => CardImage(stale).Source is BitmapImage { PixelWidth: > 0 });
        Check(CardImage(stale).Source != null, "Explicit authorized successor bind loads after first mount");
        parent.Children.Remove(stale); await Until(() => !stale.IsLoaded);
        await Task.Delay(300); // Allow the owned queued Unloaded handlers to finish before service teardown.
    }

    // Hold only this fixture's async continuations; dispatch every callback on its original UI context.
    private sealed class StageContext(SynchronizationContext ui, int holdStage) : SynchronizationContext
    {
        internal readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Held = new();
        private readonly bool _scopedStages = typeof(PosterCard).Assembly.GetType("SiloPlayer.Helpers.ArtworkDecodeGate") != null;
        internal bool HoldNext = true;
        internal int CanvasCompletions, BitmapCompletions;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            var target = state is Delegate action ? action.Target : state;
            var type = target?.GetType();
            var machine = type?.GetField("StateMachine", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
            var machineType = machine?.GetType();
            var name = machineType?.FullName ?? type?.FullName ?? "unknown";
            var step = machineType?.GetField("<>1__state", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(machine);
            // Frozen implementation awaits native operations directly. Corrected implementation
            // uses scoped stage helpers; recognize the corresponding actual native completions.
            var canvasLoaded = step is int load && (_scopedStages ? name.Contains("LoadEpisodeBitmapAsync") && load == 1 : name.Contains("BlurEpisodeStillAsync") && load == 0);
            var transformed = step is int render && (_scopedStages ? name.Contains("RenderEpisodeBlurAsync") && render == 1 : name.Contains("BlurEpisodeStillAsync") && render == 1);
            var bitmapLoaded = step is int bitmap && (_scopedStages ? name.Contains("EpisodeSpoilerArtwork+<SetSourceAsync>") && bitmap == 1 : name.Contains("EpisodeSpoilerArtwork+<LoadAsync>") && bitmap == 2);
            if (transformed) Interlocked.Increment(ref CanvasCompletions);
            if (bitmapLoaded) Interlocked.Increment(ref BitmapCompletions);
            if (HoldNext && (holdStage == 0 ? canvasLoaded : transformed))
            {
                HoldNext = false;
                Program.Log($"TRACE holding actual blur stage={holdStage} {name} state={step}");
                Held.Enqueue((callback, state)); return;
            }
            Dispatch(callback, state);
        }
        private void Dispatch(SendOrPostCallback callback, object? state) => ui.Post(_ =>
        {
            var previous = Current; SetSynchronizationContext(this);
            try { callback(state); } finally { SetSynchronizationContext(previous); }
        }, null);
        internal void Release() { while (Held.TryDequeue(out var work)) Dispatch(work.Callback, work.State); }
    }
    private static async Task BlurStageCasesAsync(StackPanel parent, SiloApiClient client, EpisodeSpoilerPreferences spoilers)
    {
        foreach (var kind in new[] { "poster", "landscape" })
        foreach (var stage in new[] { 0, 1 })
        foreach (var outcome in new[] { "motion", "rebind", "profile", "unload" })
        {
            await spoilers.EnsureLoadedAsync();
            var id = $"blur-{kind}-{stage}-{outcome}";
            var card = NewCard(kind); var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(card); row.Children.Add(new Border { Width = 10000, Height = 400 });
            var scroll = new ScrollViewer { Width = 500, Height = 400, HorizontalScrollMode = ScrollMode.Enabled, Content = row };
            var rail = new CarouselRail(scroll); parent.Children.Add(rail);
            var other = NewCard(kind); parent.Children.Add(other);
            await Until(() => card.IsLoaded && scroll.ScrollableWidth > 1000);
            var context = new StageContext(SynchronizationContext.Current!, stage);
            var episode = new MediaItem { ContentId = id, Type = "episode", Title = "Hidden", PosterIsEpisodeStill = true, BackdropIsEpisodeStill = true,
                PosterUrl = "https://artwork-motion.invalid/art/" + id, BackdropUrl = "https://artwork-motion.invalid/art/" + id };
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try { if (card is PosterCard poster) poster.MediaItem = episode; else ((LandscapeCard)card).MediaItem = episode; }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            try
            {
                await Until(() => !context.Held.IsEmpty);
                var transformed = context.CanvasCompletions; var bitmap = context.BitmapCompletions;
                scroll.ChangeView(400, null, null, true); await Until(() => scroll.HorizontalOffset == 400);
                if (outcome == "rebind") Bind(card, id + "-current");
                if (outcome == "profile") client.SetProfile(id + "-successor");
                if (outcome == "unload") { row.Children.Remove(card); await Until(() => !card.IsLoaded); }
                context.Release();
                Bind(other, id + "-independent");
                for (var i = 2; i < 11; i++) { scroll.ChangeView(i * 400, null, null, true); await Task.Delay(50); }
                Check(context.CanvasCompletions == transformed && context.BitmapCompletions == bitmap,
                    $"{id} no subsequent native transform/Bitmap completion during motion or stale ownership");
                Check(CardImage(card).Source == null, id + " pending/stale source is not presented");
                Check(CardImage(other).Source is BitmapImage { PixelWidth: > 0 }, id + " no Poster lease retained during motion wait");
                if (outcome == "profile")
                {
                    await Task.Delay(180);
                    Check(CardImage(card).Source == null && context.BitmapCompletions == bitmap, id + " retired profile stays canceled after settling");
                    Bind(card, id + "-authorized-current");
                }
                if (outcome == "unload") { row.Children.Insert(0, card); await Until(() => card.IsLoaded); }
                await Until(() => CardImage(card).Source is BitmapImage { PixelWidth: > 0 });
                Check(CardImage(card).Source != null, id + " current authorized bind resumes");
            }
            finally { context.HoldNext = false; context.Release(); parent.Children.Remove(rail); parent.Children.Remove(other); await Task.Delay(150); }
        }
    }
}
