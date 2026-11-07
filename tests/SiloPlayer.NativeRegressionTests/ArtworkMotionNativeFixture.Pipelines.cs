using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

internal static partial class ArtworkMotionNativeFixture
{
    private static async Task BlurPipelineCasesAsync(StackPanel parent, EpisodeSpoilerPreferences spoilers)
    {
        await spoilers.EnsureLoadedAsync();
        const int count = 4;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var cards = Enumerable.Range(0, count).Select(_ => new PosterCard()).ToArray();
        foreach (var card in cards) row.Children.Add(card);
        parent.Children.Insert(0, row);
        var contexts = new StageContext[count];
        var registrations = new List<(Image Image, long Token)>();
        var sync = new object();
        var active = 0;
        var peak = 0;
        var events = new List<(string Kind, int Card)>();
        try
        {
            await Until(() => cards.All(card => card.IsLoaded));
            for (var i = 0; i < count; i++)
            {
                var index = i;
                // Count native-decoded pipelines still awaiting presentation,
                // including their later transform/BitmapImage stages. Hold each
                // decode continuation so batch ordering is deterministic.
                contexts[i] = new StageContext(SynchronizationContext.Current!, 0)
                {
                    CanvasLoaded = () =>
                    {
                        lock (sync)
                        {
                            peak = Math.Max(peak, ++active);
                            events.Add(("decode", index));
                        }
                    },
                };
                var image = CardImage(cards[i]);
                var token = image.RegisterPropertyChangedCallback(Image.SourceProperty, (_, _) =>
                {
                    if (image.Source == null) return;
                    lock (sync)
                    {
                        active--;
                        events.Add(("present", index));
                    }
                });
                registrations.Add((image, token));
                var id = "blur-pipeline-batch-" + i;
                var previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(contexts[i]);
                try
                {
                    cards[i].MediaItem = new MediaItem
                    {
                        ContentId = id, Title = id, Type = "episode", PosterIsEpisodeStill = true,
                        PosterUrl = "https://artwork-motion.invalid/art/" + id,
                    };
                }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
            }
            for (var i = 0; i < count; i++)
            {
                await Until(() => contexts.Any(context => !context.Held.IsEmpty));
                var index = Array.FindIndex(contexts, context => !context.Held.IsEmpty);
                contexts[index].Release();
                await Until(() => CardImage(cards[index]).Source != null || contexts.Any(context => !context.Held.IsEmpty));
                Check(CardImage(cards[index]).Source != null,
                    "A blurred PosterCard presents before the next queued native decode completes.");
            }
            await Until(() => cards.All(card => CardImage(card).Source is BitmapImage { PixelWidth: > 0 }));
            lock (sync)
            {
                Check(peak == 1 && active == 0,
                    $"Queued hidden PosterCards keep one live decoded pipeline through presentation (peak={peak}, remaining={active}).");
                Check(events.Count(item => item.Kind == "decode") == count &&
                    events.Count(item => item.Kind == "present") == count,
                    "Every hidden PosterCard performs one native decode and one presentation.");
                Check(events.FindIndex(item => item.Kind == "present") < events.FindLastIndex(item => item.Kind == "decode"),
                    "The first blurred poster presents before the last poster decodes.");
            }
        }
        finally
        {
            foreach (var context in contexts)
                if (context != null) { context.HoldNext = false; context.Release(); }
            foreach (var registration in registrations)
                registration.Image.UnregisterPropertyChangedCallback(Image.SourceProperty, registration.Token);
            parent.Children.Remove(row);
            await Until(() => cards.All(card => !card.IsLoaded));
            await Task.Delay(150);
        }
    }
}
