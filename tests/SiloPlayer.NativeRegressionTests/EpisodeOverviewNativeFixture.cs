using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class EpisodeOverviewNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://episode-overview.fixture.invalid"); client.SetProfile("fixture");
        var api = new CatalogApi(client);
        using var spoilers = new EpisodeSpoilerPreferences(new SettingsApi(client));
        await spoilers.EnsureLoadedAsync();
        Check(spoilers.HideOverviews, "Fixture must enable actual episode spoiler preferences");
        var vm = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException("Unexpected unscoped read")));
        slot.SetValue(null, new Services(original, client, api, vm, spoilers));
        ItemDetailPage? page = null;
        using var navigation = new CancellationTokenSource();
        try
        {
            page = new ItemDetailPage { Width = 960, Height = 700 };
            parent.Children.Add(page); await Task.Delay(100);
            Set(page, "_navigationCts", navigation); Set(page, "_currentContentId", "episode");
            await vm.LoadInLibraryAsync("episode", 42);
            Call(page, "UpdateUI");
            await (Task)Call(page, "ConfigureOnViewTranslationAsync", vm.Item!)!;
            var overview = (ExpandableDescription)page.FindName("OverviewText");
            Check(overview.Text == Wire.SeriesText, "Whitespace episode description must display the series overview");
            Check(wire.Paths.Contains("/api/v2/catalog/items/series?library_id=42"), "Series fallback must carry library scope");
            Check(overview.Visibility == Visibility.Visible && ((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Collapsed,
                "Series fallback remains visible under episode spoilers");
            Check(((FrameworkElement)page.FindName("OverviewTranslationLabel")).Visibility == Visibility.Visible,
                "Series machine-translation label follows the overview source");
            Program.Log("PASS: episode blank overview displays scoped series source and translation label under spoilers.");

            await vm.ReloadAsync("episode"); Call(page, "UpdateUI");
            await Until(() => overview.Text == Wire.SeriesText);
            Check(((FrameworkElement)page.FindName("OverviewTranslationLabel")).Visibility == Visibility.Visible,
                "Reloaded episode keeps its series description and provenance");

            var button = (Button)page.FindName("TranslateOverviewButton");
            Check(button.Visibility == Visibility.Visible && button.IsEnabled && button.IsTabStop, "Series translation is keyboard accessible");
            foreach (var width in new[] { 460d, 1280d })
            {
                page.Width = width; Call(page, "UpdateResponsiveLayout", width);
                page.UpdateLayout(); await Task.Delay(80);
                var body = (FrameworkElement)page.FindName("HeroInfoPanel");
                var buttonTop = button.TransformToVisual(body).TransformPoint(new(0, 0));
                var overviewTop = overview.TransformToVisual(body).TransformPoint(new(0, 0));
                var label = (FrameworkElement)page.FindName("OverviewTranslationLabel");
                var labelTop = label.TransformToVisual(body).TransformPoint(new(0, 0));
                await MediaParityNativeFixture.CaptureAsync(page, $"episode-overview-{width}.png");
                Check(overview.ActualWidth > 0 && overview.ActualWidth <= width && button.ActualWidth > 0 &&
                    buttonTop.X >= 0 && buttonTop.X + button.ActualWidth <= body.ActualWidth + 1 &&
                    buttonTop.Y >= overviewTop.Y + overview.ActualHeight,
                    $"Actual overview/translation controls fit without overlap at {width}px");
                Check(labelTop.Y >= overviewTop.Y + overview.ActualHeight && labelTop.Y + label.ActualHeight <= buttonTop.Y + 1,
                    $"Translation provenance remains beside overview and action at {width}px");
            }
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => wire.Posts.Count == 1 && overview.Text == Wire.TranslatedText);
            Check(wire.Posts.Single() == "/api/v2/catalog/items/series/translate-description|fr", "Manual action translates source series and target language");
            Check(vm.Item!.ContentId == "episode" && string.IsNullOrWhiteSpace(vm.Item.Overview) && button.Visibility == Visibility.Collapsed,
                "Translated fallback never overwrites episode and completed action disappears");

            async Task Load()
            {
                Set(page, "_currentContentId", "episode");
                await vm.LoadInLibraryAsync("episode", 42); Call(page, "UpdateUI");
                await Task.Delay(80);
                if (wire.OnView == "auto")
                    await Until(() => wire.Posts.Count == 1 && overview.Text == Wire.TranslatedText && button.Visibility == Visibility.Collapsed);
            }
            wire.Reset(); wire.OnView = "auto";
            await Load();
            Check(wire.Posts.Single() == "/api/v2/catalog/items/series/translate-description|fr" && overview.Text == Wire.TranslatedText,
                "Automatic translation targets series and paints its refreshed text");
            Program.Log("PASS: native manual/automatic actions translate series and poll scoped series detail; compact/wide geometry and keyboard action.");

            wire.Reset(); wire.OwnOverview = "An episode-specific plot.";
            await Load();
            Check(overview.Visibility == Visibility.Collapsed && ((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Visible &&
                button.Visibility == Visibility.Collapsed && !wire.Paths.Any(path => path.StartsWith("/api/v2/catalog/items/series")),
                "Own episode overview wins, avoids fallback and retains spoiler reveal");
            ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("RevealEpisodeOverviewButton")).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => button.Visibility == Visibility.Visible);
            Check(overview.Visibility == Visibility.Visible && overview.Text == "An episode-specific plot.", "Reveal displays own overview");
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => wire.Posts.Count == 1 && button.Visibility == Visibility.Collapsed);
            Check(wire.Posts.Single() == "/api/v2/catalog/items/episode/translate-description|fr", "Own overview translation remains episode-owned");
            Set(page, "_revealedEpisodeOverviewId", null!);

            foreach (var missing in new[] { true, false })
            {
                wire.Reset(); wire.SeriesId = missing ? null : "series"; wire.FailSeries = !missing;
                await Load();
                Check(overview.Text == "" && button.Visibility == Visibility.Collapsed && vm.ErrorMessage == null && vm.Item!.ContentId == "episode",
                    "Missing/failed fallback leaves episode usable");
            }
            Program.Log("PASS: own episode plot preserves spoiler/reveal and episode translation; absent/failed series is optional.");

            foreach (var change in new[] { "item", "library", "profile", "language", "page" })
            {
                wire.Reset(); wire.HoldStatus = true; wire.OnView = "auto";
                Set(page, "_currentContentId", "episode");
                await vm.LoadInLibraryAsync("episode", 42); Call(page, "UpdateUI");
                var stale = (Task)Call(page, "ConfigureOnViewTranslationAsync", vm.Item!)!;
                await wire.StatusEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (change == "profile") client.SetProfile("new-profile");
                else if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
                else if (change == "library") await vm.LoadInLibraryAsync("episode", 99);
                else if (change == "page") { vm.CancelPendingLoads(); Set(page, "_currentContentId", "departed"); }
                else vm.Item = new() { ContentId = "new-episode", Type = "episode", Overview = "New page" };
                wire.ReleaseStatus.TrySetResult(); await stale;
                Check(wire.Posts.Count == 0 && button.Visibility == Visibility.Collapsed, $"Retired {change} status cannot translate or show action");
            }
            wire.Reset(); wire.HoldPost = true;
            await Load();
            var retiredItem = vm.Item!;
            var oldTranslation = (Task)Call(page, "TranslateOverviewAsync", vm.OverviewSource!)!;
            await wire.PostEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            wire.HoldPost = false; wire.OwnOverview = "A newer page description.";
            await Load();
            // Reveal the new owner while an old server write is still pending.
            Call(page, "RevealEpisodeOverviewButton_Click", button, new RoutedEventArgs());
            await Until(() => button.Visibility == Visibility.Visible && button.IsEnabled);
            wire.ReleasePost.TrySetResult(); await oldTranslation;
            Check(overview.Text == "A newer page description." && overview.Opacity == 1 && button.IsEnabled && (string)button.Content == "Translate description",
                "Old translation completion cannot repaint or disable the new owner's controls");
            await (Task)Call(page, "ConfigureOnViewTranslationAsync", retiredItem)!;
            Check(button.Visibility == Visibility.Visible && button.IsEnabled && overview.Text == "A newer page description.",
                "Retired configuration callback cannot hide the current source action");
            wire.Reset(); wire.HoldPost = true;
            await Load();
            var canceledTranslation = (Task)Call(page, "TranslateOverviewAsync", vm.OverviewSource!)!;
            await wire.PostEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ((CancellationTokenSource)typeof(ItemDetailPage).GetField("_translationCts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!).Cancel();
            wire.ReleasePost.TrySetResult(); await canceledTranslation;
            Check(overview.Opacity == 1 && button.Visibility == Visibility.Visible && button.IsEnabled,
                "Expired/canceled translation restores its still-current source controls");
            Program.Log("PASS: stale item/library/profile/language/page status and old translation completion cannot alter current native controls.");
        }
        finally
        {
            navigation.Cancel(); vm.CancelPendingLoads();
            if (page != null) parent.Children.Remove(page);
            slot.SetValue(null, original);
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }
    private static object? Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args);
    private static void Set(object owner, string field, object? value) => owner.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, value);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var n = 0; n < 240 && !ready(); n++) await Task.Delay(25); Check(ready(), "Native overview state did not settle"); }
    private sealed class Services(IServiceProvider original, SiloApiClient client, CatalogApi api, ItemDetailViewModel vm, EpisodeSpoilerPreferences spoilers) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(SiloApiClient) ? client : type == typeof(CatalogApi) ? api :
            type == typeof(ItemDetailViewModel) ? vm : type == typeof(EpisodeSpoilerPreferences) ? spoilers :
            type == typeof(UICustomizationService) ? new UICustomizationService(new SettingsApi(client)) : original.GetService(type);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal const string SeriesText = "A series description without episode spoilers.";
        internal const string TranslatedText = "Une description traduite de la série.";
        internal readonly List<string> Paths = [];
        internal readonly List<string> Posts = [];
        internal string OwnOverview = " \t ";
        internal string? SeriesId = "series";
        internal string OnView = "button";
        internal bool FailSeries, Translated, HoldStatus, HoldPost;
        internal TaskCompletionSource StatusEntered = NewGate(), ReleaseStatus = NewGate(), PostEntered = NewGate(), ReleasePost = NewGate();
        private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Reset()
        {
            Paths.Clear(); Posts.Clear(); OwnOverview = " \t "; SeriesId = "series"; OnView = "button";
            FailSeries = Translated = HoldStatus = HoldPost = false;
            StatusEntered = NewGate(); ReleaseStatus = NewGate(); PostEntered = NewGate(); ReleasePost = NewGate();
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "episode-overview.fixture.invalid") throw new InvalidOperationException("Unexpected external request");
            var path = request.RequestUri!.PathAndQuery; Paths.Add(path);
            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/translate-description"))
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Posts.Add(path + "|" + payload.RootElement.GetProperty("target_language").GetString());
                if (HoldPost) { PostEntered.TrySetResult(); await ReleasePost.Task; }
                Translated = true; return new(HttpStatusCode.NoContent);
            }
            if (request.RequestUri.AbsolutePath == "/api/v2/capabilities/metadata-ai" && HoldStatus)
            { StatusEntered.TrySetResult(); await ReleaseStatus.Task; }
            if (request.RequestUri.AbsolutePath == "/api/v2/catalog/items/series" && FailSeries) return new(HttpStatusCode.NotFound);
            object value = request.RequestUri.AbsolutePath switch
            {
                "/api/v2/catalog/items/episode" => new { content_id = "episode", type = "episode", title = "Episode", series_id = SeriesId, overview = OwnOverview, pending_translation_language = Translated ? null : "fr", user_state = new { }, user_data = new { played = false } },
                "/api/v2/catalog/items/series" => new { content_id = "series", type = "series", overview = Translated ? TranslatedText : SeriesText, machine_translated_fields = new[] { "overview" }, pending_translation_language = Translated ? null : "fr" },
                "/api/v2/capabilities/metadata-ai" => new { state = "available", allowed = true, on_view = OnView },
                "/api/v2/settings/contract/capabilities" => new { api_version = 1, manifest_revision = 17, supports_batched_effective = true },
                _ when path.Contains("effective") => new { items = new[] { new { key = "catalog.hide_unwatched_episode_overviews", value = true } } },
                _ => new { }
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
        }
    }
}
