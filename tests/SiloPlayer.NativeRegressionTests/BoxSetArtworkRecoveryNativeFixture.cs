using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Converters;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Views;

internal static partial class BoxSetNativeFixture
{
    private static async Task VerifyFailedArtworkRenewalAsync(StackPanel parent, CatalogApi catalog,
        SiloApiClient api, Wire wire, string scenario)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = SiloPlayer.App.Services;
        using var handler = new RenewedArtworkWire();
        using var http = new HttpClient(handler);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "renewed-artwork-cache"));
        servicesField.SetValue(null, new ArtworkRecoveryServices(original, http, images));
        try
        {
            if (scenario == "artwork-recovery-aggregate")
            {
                var card = new BoxSetCard(); parent.Children.Add(card);
                try
                {
                    card.SetSummary(new() { Id = "renewal", LibraryId = "12", Title = "Synthetic renewal", MovieCount = 3,
                        PosterUrl = "https://artwork-renewal.invalid/aggregate.png?X-Amz-Signature=expired" });
                    await handler.Expired.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    await ArtworkUntil(() => typeof(BoxSetCard).GetField("_artworkLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(card) == null);
                    var image = (Image)typeof(BoxSetCard).GetField("_poster", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(card)!;
                    var content = card.Content;
                    Check(image.Source == null && card.Focus(FocusState.Keyboard), "expired aggregate request actually failed before renewal");
                    card.SetSummary(new() { Id = "renewal", LibraryId = "12", Title = "Synthetic renewal", MovieCount = 3, PlayedCount = 2,
                        PosterUrl = "https://artwork-renewal.invalid/aggregate.png?X-Amz-Signature=renewed" });
                    await handler.Renewed.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    await ArtworkUntil(() => image.Source is BitmapImage bitmap && bitmap.PixelWidth > 0);
                    Check(ReferenceEquals(content, card.Content) && card.FocusState == FocusState.Keyboard && handler.ExpiredReads == 1,
                        "renewed aggregate image must decode without rebuilding controls, losing focus or retrying the expired URL");
                    Program.Log("PASS: failed aggregate artwork retries the renewed signed URL and decodes in the retained focused control.");
                }
                finally { parent.Children.Remove(card); }
            }
            else
            {
                wire.ArtworkSignature = "expired";
                var page = new BoxSetPage(catalog, api) { Width = 960, Height = 700 }; parent.Children.Add(page);
                try
                {
                    await page.ViewModel.LoadAsync("7", "12"); await DrainAsync(parent); page.UpdateLayout();
                    var hero = (Grid)page.Body.Children[0];
                    var poster = hero.Children.OfType<Grid>().Single().Children.OfType<Image>().Single();
                    var backdrop = hero.Children.OfType<Image>().Single();
                    var oldPoster = poster.Source; var oldBackdrop = backdrop.Source;
                    Check(!await UrlToImageSourceConverter.GetLoadOutcome(oldPoster)!.WaitAsync(TimeSpan.FromSeconds(4)) &&
                        !await UrlToImageSourceConverter.GetLoadOutcome(oldBackdrop)!.WaitAsync(TimeSpan.FromSeconds(4)),
                        "both expired hero requests must finish as actual load failures before renewal");
                    var play = Descendants(page).OfType<Button>().Single(button => button.Name == "BoxSetPlay");
                    var movies = ((ItemsControl)page.Body.Children[2]).Items.OfType<PosterCard>().ToArray();
                    Check(movies.Length == 3 && play.Focus(FocusState.Keyboard), "hero renewal begins with real member controls and focused Play");
                    wire.ArtworkSignature = "renewed";
                    await page.ViewModel.RefreshAsync(); await DrainAsync(parent);
                    await handler.Renewed.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    Check(!ReferenceEquals(oldPoster, poster.Source) && !ReferenceEquals(oldBackdrop, backdrop.Source),
                        "failed hero images must be replaced by renewed URL operations");
                    Check(await UrlToImageSourceConverter.GetLoadOutcome(poster.Source)!.WaitAsync(TimeSpan.FromSeconds(4)) &&
                        await UrlToImageSourceConverter.GetLoadOutcome(backdrop.Source)!.WaitAsync(TimeSpan.FromSeconds(4)),
                        "renewed hero poster and backdrop must actually decode");
                    Check(ReferenceEquals(hero, page.Body.Children[0]) && ReferenceEquals(play, Descendants(page).OfType<Button>().Single(button => button.Name == "BoxSetPlay")) &&
                        ((ItemsControl)page.Body.Children[2]).Items.OfType<PosterCard>().SequenceEqual(movies) && play.FocusState == FocusState.Keyboard,
                        "hero renewal must retain mounted hero, member cards, Play control and keyboard focus");
                    Program.Log("PASS: failed hero poster/backdrop recover through renewed URL decode while retaining controls and focus.");
                }
                finally { wire.ArtworkSignature = null; page.ViewModel.Cancel(); parent.Children.Remove(page); }
            }
        }
        finally { servicesField.SetValue(null, original); }
    }

    private static async Task ArtworkUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Check(ready(), "artwork operation did not reach its bounded native completion");
    }

    private sealed class ArtworkRecoveryServices(IServiceProvider original, HttpClient http, ImageService images) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(HttpClient) ? http : type == typeof(ImageService) ? images : original.GetService(type);
    }

    private sealed class RenewedArtworkWire : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Expired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Renewed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ExpiredReads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Query.Contains("expired", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref ExpiredReads); Expired.TrySetResult();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            }
            Renewed.TrySetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j8uoAAAAASUVORK5CYII=")) });
        }
    }
}
