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
using SiloPlayer.Messaging;
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
            wire.OwnOverview = "A new-language episode plot."; wire.TargetLanguage = "de";
            client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
            Call(page, "ApplyEpisodeSpoilers");
            Check(vm.OverviewSource == null && overview.Text == "An episode-specific plot." && overview.Visibility == Visibility.Collapsed &&
                ((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Visible && button.Visibility == Visibility.Collapsed,
                "A retired language scope preserves display text and spoiler protection without allowing stale translation");
            await Until(() => vm.OverviewSource != null && overview.Text == "A new-language episode plot.");
            Check(wire.Paths.Count(path => path == "/api/v2/catalog/items/episode?library_id=42") == 2 &&
                vm.OverviewSource!.PendingTranslationLanguage == "de", "Retired own overview is re-read in the captured library");
            Check(overview.Visibility == Visibility.Collapsed && button.Visibility == Visibility.Collapsed,
                "Re-resolving the same episode after language retirement keeps its own plot hidden");
            ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("RevealEpisodeOverviewButton")).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => button.Visibility == Visibility.Visible);
            Check(overview.Visibility == Visibility.Visible && overview.Text == "A new-language episode plot.", "Reveal displays current-language own overview");
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => wire.Posts.Count == 1 && button.Visibility == Visibility.Collapsed);
            Check(wire.Posts.Single() == "/api/v2/catalog/items/episode/translate-description|de", "Own overview translation remains episode-owned and uses the new target");
            Set(page, "_revealedEpisodeOverviewId", null!);

            foreach (var change in new[] { "watched", "progress" })
            {
                wire.Reset(); wire.OwnOverview = "An episode-specific plot.";
                await Load();
                await (Task)Call(page, "ConfigureOnViewTranslationAsync", vm.Item!)!;
                var owner = vm.Item!;
                Check(overview.Visibility == Visibility.Collapsed && button.Visibility == Visibility.Collapsed,
                    "An unwatched own overview starts with its translation action hidden");
                if (change == "watched") vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "episode"));
                else vm.Receive(new PlaybackProgressUpdated("episode", 60, 1200, false));
                Call(page, "ApplyEpisodeSpoilers");
                await Until(() => button.Visibility == Visibility.Visible && button.IsEnabled);
                Check(ReferenceEquals(vm.Item, owner) && overview.Visibility == Visibility.Visible &&
                    ((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Collapsed &&
                    Field<string?>(page, "_revealedEpisodeOverviewId") == null && wire.Posts.Count == 0,
                    $"A {change} change restores manual translation without reveal, reload or automatic translation");
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => wire.Posts.Count == 1 && button.Visibility == Visibility.Collapsed);
                Check(wire.Posts.Single() == "/api/v2/catalog/items/episode/translate-description|fr",
                    "The restored action translates the current episode overview");
            }
            Program.Log("PASS: watched/progress spoiler transitions restore own-overview translation without Reveal or reload.");

            foreach (var mode in new[] { "button", "auto" })
            foreach (var change in new[] { "access", "same-profile" })
            {
                wire.Reset(); wire.OwnOverview = "An unrevealed episode plot."; wire.OnView = mode;
                await vm.LoadInLibraryAsync("episode", 42); Call(page, "UpdateUI");
                await (Task)Call(page, "ConfigureOnViewTranslationAsync", vm.Item!)!;
                Check(overview.Visibility == Visibility.Collapsed && button.Visibility == Visibility.Collapsed && wire.Posts.Count == 0,
                    "An unrevealed own overview starts protected before access retirement");
                wire.OwnOverview = "A refreshed unrevealed plot."; wire.HoldSpoilers = true;
                if (change == "access") client.InvalidateAccessContext();
                else client.SetProfile("fixture");
                Call(page, "RefreshRetiredOverviewScope");
                await wire.SpoilersEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!spoilers.IsLoadedForCurrentContext && !spoilers.HideOverviews,
                    "The held replacement preference is unknown in the new access context");
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    Call(page, "RefreshRetiredOverviewScope"); Call(page, "ApplyOverviewSpoilers");
                    Check(overview.Visibility == Visibility.Collapsed &&
                        ((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Visible &&
                        button.Visibility == Visibility.Collapsed && wire.Posts.Count == 0,
                        "Access retirement must not reveal or translate a plot while preferences are loading");
                    await Task.Delay(550);
                }
                wire.HoldSpoilers = false; wire.ReleaseSpoilers.TrySetResult();
                await Until(() => spoilers.IsLoadedForCurrentContext && spoilers.HideOverviews &&
                    vm.OverviewSource != null && overview.Text == "A refreshed unrevealed plot.");
                await Task.Delay(550);
                Check(overview.Visibility == Visibility.Collapsed &&
                    ((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Visible &&
                    button.Visibility == Visibility.Collapsed && wire.Posts.Count == 0 &&
                    !wire.Paths.Contains("/api/v2/capabilities/metadata-ai"),
                    "Reloaded spoiler preferences keep the own plot hidden and block manual/automatic translation");
            }
            Program.Log("PASS: access and same-profile retirement preserve unrevealed own-overview spoilers during and after preference reload.");

            foreach (var missing in new[] { true, false })
            {
                wire.Reset(); wire.SeriesId = missing ? null : "series"; wire.FailSeries = !missing;
                await Load();
                Check(overview.Text == "" && button.Visibility == Visibility.Collapsed && vm.ErrorMessage == null && vm.Item!.ContentId == "episode",
                    "Missing/failed fallback leaves episode usable");
            }
            Program.Log("PASS: own episode plot preserves spoiler/reveal and episode translation; absent/failed series is optional.");

            foreach (var mode in new[] { "button", "auto" })
            {
                wire.Reset(); wire.StatusFailuresRemaining = 2; wire.OnView = mode;
                await vm.LoadInLibraryAsync("episode", 42); Call(page, "UpdateUI");
                await Until(() => wire.Paths.Count(path => path == "/api/v2/capabilities/metadata-ai") == 1 &&
                    Field<DateTimeOffset?>(page, "_overviewRetryAt") != null);
                Check(overview.Text == Wire.SeriesText && overview.Visibility == Visibility.Visible &&
                    button.Visibility == Visibility.Collapsed && wire.Posts.Count == 0,
                    "A capability failure retains the series description and schedules recovery without translating");
                Set(page, "_overviewRetryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
                Call(page, "RefreshRetiredOverviewScope");
                await Until(() => wire.Paths.Count(path => path == "/api/v2/capabilities/metadata-ai") == 2 &&
                    Field<DateTimeOffset?>(page, "_overviewRetryAt") != null);
                Check(Field<TimeSpan>(page, "_overviewRetryDelay") == TimeSpan.FromSeconds(4),
                    "Repeated capability failures retain the same owner's exponential backoff");
                Set(page, "_overviewRetryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
                Call(page, "RefreshRetiredOverviewScope");
                await Until(() => wire.Paths.Count(path => path == "/api/v2/capabilities/metadata-ai") == 3 &&
                    overview.Text == (mode == "auto" ? Wire.TranslatedText : Wire.SeriesText) &&
                    button.Visibility == (mode == "auto" ? Visibility.Collapsed : Visibility.Visible));
                Check(mode == "auto" ? wire.Posts.Single() == "/api/v2/catalog/items/series/translate-description|fr" : button.IsEnabled,
                    "A successful capability retry restores automatic or manual series translation on the same page");
            }
            Program.Log("PASS: metadata-AI capability failures retry with backoff and recover manual/automatic series translation.");

            foreach (var stage in new[] { "post", "poll" })
            {
                wire.Reset(); wire.OnView = "auto";
                wire.PostFailuresRemaining = stage == "post" ? 1 : 0;
                wire.PollFailuresRemaining = stage == "poll" ? 1 : 0;
                await vm.LoadInLibraryAsync("episode", 42); Call(page, "UpdateUI");
                await Until(() => wire.Posts.Count == 1 && button.Visibility == Visibility.Visible && button.IsEnabled && overview.Opacity == 1);
                Check(overview.Text == Wire.SeriesText && vm.OverviewSource!.PendingTranslationLanguage == "fr" &&
                    (string)button.Content == "Translate description" && Field<bool>(page, "_translateButtonMode"),
                    $"A failed automatic {stage} retains the description and offers a manual retry");
                await Task.Delay(600);
                Check(wire.Posts.Count == 1 && button.Visibility == Visibility.Visible && button.IsEnabled,
                    "The scope watcher preserves the recovery action without repeating automatic writes");
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => wire.Posts.Count == 2 && overview.Text == Wire.TranslatedText && button.Visibility == Visibility.Collapsed);
                Check(wire.Posts.All(post => post == "/api/v2/catalog/items/series/translate-description|fr") &&
                    vm.Item!.ContentId == "episode" && string.IsNullOrWhiteSpace(vm.Item.Overview),
                    "Manual recovery after an automatic failure still translates only the scoped series source");
            }
            Program.Log("PASS: automatic translation POST/poll failures expose a working manual recovery action.");

            foreach (var mode in new[] { "button", "auto" })
            {
                wire.Reset(); wire.SeriesFailuresRemaining = 1; wire.OnView = mode;
                await Load();
                await Until(() => overview.Text == (mode == "auto" ? Wire.TranslatedText : Wire.SeriesText) &&
                    button.Visibility == (mode == "auto" ? Visibility.Collapsed : Visibility.Visible));
                Check(wire.Paths.Count(path => path == "/api/v2/catalog/items/series?library_id=42") >= 2 &&
                    vm.Item!.ContentId == "episode" && vm.ErrorMessage == null,
                    "A transient series failure recovers on the same page without a scope change");
                Check(mode == "auto" ? wire.Posts.Single() == "/api/v2/catalog/items/series/translate-description|fr" : button.IsEnabled,
                    "Recovered series restores automatic or manual translation");
            }
            wire.Reset(); wire.FailSeries = true;
            await Load();
            await Until(() => wire.Paths.Count(path => path == "/api/v2/catalog/items/series?library_id=42") == 2);
            await Task.Delay(1200);
            Check(wire.Paths.Count(path => path == "/api/v2/catalog/items/series?library_id=42") == 2 && overview.Text == "" &&
                button.Visibility == Visibility.Collapsed && vm.ErrorMessage == null, "Persistent optional failures back off instead of issuing rapid retries");
            Check(Field<DateTimeOffset?>(page, "_overviewRetryAt") != null && Field<TimeSpan>(page, "_overviewRetryDelay") <= TimeSpan.FromSeconds(30),
                "Persistent failures re-arm a retry with a capped delay");
            Set(page, "_overviewRetryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
            Call(page, "RefreshRetiredOverviewScope");
            await Until(() => wire.Paths.Count(path => path == "/api/v2/catalog/items/series?library_id=42") == 3 &&
                Field<DateTimeOffset?>(page, "_overviewRetryAt") != null);
            Check(overview.Text == "" && button.Visibility == Visibility.Collapsed && vm.ErrorMessage == null,
                "A later failed optional retry still leaves the episode usable");
            Program.Log("PASS: transient series reads recover text and translation on the same page; persistent failures retain capped backoff retries.");

            foreach (var change in new[] { "language", "access" })
            {
                wire.Reset(); wire.SeriesOverview = string.Concat(Enumerable.Repeat(Wire.SeriesText + " ", 30));
                await Load();
                var source = vm.OverviewSource;
                var visibleText = overview.Text;
                ((IInvokeProvider)new ButtonAutomationPeer((Button)overview.Children[1]).GetPattern(PatternInterface.Invoke)).Invoke();
                Check(((TextBlock)overview.Children[0]).MaxLines == 0, "Series description is expanded before scope retirement");
                wire.FailSeries = true; wire.SeriesOverview = "Recovered series description";
                if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
                else client.InvalidateAccessContext();
                Call(page, "RefreshRetiredOverviewScope");
                await Until(() => vm.OverviewResolutionFailed && Field<DateTimeOffset?>(page, "_overviewRetryAt") != null);
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    Call(page, "ApplyEpisodeSpoilers");
                    Check(vm.OverviewSource == null && ReferenceEquals(source, vm.OverviewDisplaySource) && overview.Text == visibleText &&
                        overview.Visibility == Visibility.Visible && ((TextBlock)overview.Children[0]).MaxLines == 0 &&
                        ((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Collapsed && button.Visibility == Visibility.Collapsed,
                        "Failed replacement reads preserve expanded series text under episode spoilers without stale translation");
                    Call(page, "TranslateOverviewButton_Click", button, new RoutedEventArgs());
                    Check(wire.Posts.Count == 0, "A retired series display is never a translation target");
                    var reads = wire.Paths.Count(path => path == "/api/v2/catalog/items/series?library_id=42");
                    Set(page, "_overviewRetryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
                    Call(page, "RefreshRetiredOverviewScope");
                    await Until(() => wire.Paths.Count(path => path == "/api/v2/catalog/items/series?library_id=42") == reads + 1 &&
                        Field<DateTimeOffset?>(page, "_overviewRetryAt") != null);
                }
                wire.FailSeries = false;
                Set(page, "_overviewRetryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
                Call(page, "RefreshRetiredOverviewScope");
                await Until(() => overview.Text == "Recovered series description" && vm.OverviewSource != null && button.Visibility == Visibility.Visible);
                Check(!ReferenceEquals(source, vm.OverviewSource) && vm.Item!.ContentId == "episode" && string.IsNullOrWhiteSpace(vm.Item.Overview),
                    "Successful retry replaces the series display without replacing episode identity");
            }
            Program.Log("PASS: language/access retirement and repeated series failures preserve expanded fallback text until a successful retry.");

            foreach (var type in new[] { "movie", "episode" })
            foreach (var change in new[] { "language", "access" })
            {
                wire.Reset(); wire.OwnOverview = "Already visible plot";
                Set(page, "_currentContentId", type); Set(page, "_revealedEpisodeOverviewId", type);
                await vm.LoadInLibraryAsync(type, 42); Call(page, "UpdateUI");
                await Until(() => overview.Text == "Already visible plot" && button.Visibility == Visibility.Visible);
                var configuredOwner = Field<object>(page, "_overviewConfiguredOwner");
                wire.OwnOverview = "Recovered plot"; wire.TargetLanguage = "de";
                wire.OwnMachineTranslatedFields = ["overview"]; wire.FailItem = true;
                if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
                else client.InvalidateAccessContext();
                Call(page, "RefreshRetiredOverviewScope");
                await Until(() => vm.OverviewResolutionFailed && Field<DateTimeOffset?>(page, "_overviewRetryAt") != null);
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    Check(vm.OverviewSource == null && overview.Text == "Already visible plot" && overview.Visibility == Visibility.Visible &&
                        button.Visibility == Visibility.Collapsed && wire.Posts.Count == 0 &&
                        ReferenceEquals(configuredOwner, Field<object>(page, "_overviewConfiguredOwner")),
                        "Failed own reads retain visible text, retire translation and do not commit the replacement owner");
                    Check(Field<TimeSpan>(page, "_overviewRetryDelay") <= TimeSpan.FromSeconds(30) &&
                        Field<DateTimeOffset?>(page, "_overviewRetryAt") <= DateTimeOffset.UtcNow.AddSeconds(30),
                        "Repeated failures never grow the retry backoff beyond its cap");
                    Call(page, "TranslateOverviewButton_Click", button, new RoutedEventArgs());
                    var reads = wire.Paths.Count(path => path == $"/api/v2/catalog/items/{type}?library_id=42");
                    Set(page, "_overviewRetryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
                    Call(page, "RefreshRetiredOverviewScope");
                    await Until(() => wire.Paths.Count(path => path == $"/api/v2/catalog/items/{type}?library_id=42") == reads + 1 &&
                        Field<DateTimeOffset?>(page, "_overviewRetryAt") != null);
                }
                wire.FailItem = false;
                Set(page, "_overviewRetryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
                Call(page, "RefreshRetiredOverviewScope");
                await Until(() => overview.Text == "Recovered plot" && vm.OverviewSource != null && button.Visibility == Visibility.Visible && button.IsEnabled);
                Check(vm.OverviewSource!.PendingTranslationLanguage == "de" && wire.Posts.Count == 0 &&
                    ((FrameworkElement)page.FindName("OverviewTranslationLabel")).Visibility == Visibility.Visible &&
                    Field<DateTimeOffset?>(page, "_overviewRetryAt") == null,
                    "A later successful retry atomically publishes the replacement text, provenance and translation target");
            }
            Set(page, "_revealedEpisodeOverviewId", null!);
            Program.Log("PASS: repeated own-overview failures after language/access retirement preserve visible text and recover on a later same-page retry.");

            foreach (var type in new[] { "movie", "episode" })
            foreach (var mode in new[] { "button", "auto" })
            {
                wire.Reset(); wire.OwnOverview = "Old-language plot"; wire.OnView = mode;
                wire.KeepPending = true; wire.HoldPoll = mode == "auto";
                Set(page, "_currentContentId", type); Set(page, "_revealedEpisodeOverviewId", type);
                await vm.LoadInLibraryAsync(type, 42);
                var initialConfiguration = (Task)Call(page, "ConfigureOnViewTranslationAsync", vm.Item!)!;
                Call(page, "UpdateUI");
                var owner = vm.Item;
                if (mode == "auto") await wire.PollEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                else await Until(() => button.Visibility == Visibility.Visible && button.IsEnabled);
                var oldPosts = wire.Posts.Count;
                wire.OwnOverview = "New-language plot"; wire.TargetLanguage = "de";
                wire.OwnMachineTranslatedFields = ["overview"]; wire.HoldItem = true;
                var ownReadsBeforeRetirement = wire.Paths.Count(path => path == $"/api/v2/catalog/items/{type}?library_id=42");
                client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
                await wire.ItemReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(vm.OverviewSource == null && overview.Text == "Old-language plot" && button.Visibility == Visibility.Collapsed && wire.Posts.Count == oldPosts,
                    "A retired own source retains display text but stays unavailable for translation until its new-language read succeeds");
                Call(page, "RefreshRetiredOverviewScope"); Call(page, "UpdateUI");
                Check(wire.Paths.Count(path => path == $"/api/v2/catalog/items/{type}?library_id=42") == ownReadsBeforeRetirement + 1,
                    "Timer and enrichment paints do not overlap a held replacement read");
                wire.HoldItem = false; wire.ReleaseItemRead.TrySetResult();
                await Until(() => overview.Text == "New-language plot" &&
                    (mode == "auto" ? wire.Posts.Count == oldPosts + 1 : button.Visibility == Visibility.Visible && button.IsEnabled));
                Check(ReferenceEquals(vm.Item, owner) && vm.OverviewSource!.PendingTranslationLanguage == "de" &&
                    ((FrameworkElement)page.FindName("OverviewTranslationLabel")).Visibility == Visibility.Visible,
                    "New-scope own text, target and provenance publish without replacing item identity");
                if (mode == "button")
                {
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Until(() => wire.Posts.Count == 1);
                }
                Check(wire.Posts.Last() == $"/api/v2/catalog/items/{type}/translate-description|de" &&
                    wire.Posts.Skip(oldPosts).All(post => post.EndsWith("|de")), "Retirement never POSTs the stale target in button or auto mode");
                Field<CancellationTokenSource>(page, "_translationCts").Cancel();
                wire.ReleasePoll.TrySetResult();
                await Until(() => overview.Opacity == 1 && button.IsEnabled);
                await initialConfiguration;
                Check(overview.Text == "New-language plot" && vm.Item!.Overview == "New-language plot",
                    "The retired automatic poll cannot overwrite the new own overview");
            }
            Set(page, "_revealedEpisodeOverviewId", null!);
            Program.Log("PASS: movie and own-episode language retirement re-reads text/provenance and targets the new language in manual and automatic modes, including an active poll.");

            foreach (var change in new[] { "item", "library", "profile", "language", "page" })
            {
                wire.Reset(); wire.HoldStatus = true; wire.OnView = "auto";
                Set(page, "_currentContentId", "episode");
                await vm.LoadInLibraryAsync("episode", 42); Call(page, "UpdateUI");
                var stale = (Task)Call(page, "ConfigureOnViewTranslationAsync", vm.Item!)!;
                await wire.StatusEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // Replacement configurations may read status, but only the held old response says auto.
                wire.OnView = "off";
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

            foreach (var stage in new[] { "post", "poll", "idle" })
            foreach (var change in new[] { "language", "access" })
            {
                wire.Reset(); wire.HoldPost = stage == "post"; wire.HoldPoll = stage == "poll";
                await Load();
                var episode = vm.Item;
                var oldSource = vm.OverviewSource!;
                var retiredTranslation = stage == "idle" ? Task.CompletedTask : (Task)Call(page, "TranslateOverviewAsync", oldSource)!;
                if (stage != "idle")
                {
                    await (stage == "post" ? wire.PostEntered.Task : wire.PollEntered.Task).WaitAsync(TimeSpan.FromSeconds(5));
                    Check(overview.Opacity == 0.55 && !button.IsEnabled && (string)button.Content == "Translating…",
                        "Held translation must own the dimmed controls before scope retirement");
                }
                // The new scope still needs translation; a held old read may contain translated text.
                wire.Translated = false;
                if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
                else client.InvalidateAccessContext();
                if (stage == "idle") Call(page, "TranslateOverviewButton_Click", button, new RoutedEventArgs());
                await Until(() => vm.OverviewSource is { } current && !ReferenceEquals(current, oldSource) &&
                    overview.Opacity == 1 && button.Visibility == Visibility.Visible && button.IsEnabled &&
                    (string)button.Content == "Translate description");
                var currentSource = vm.OverviewSource;
                Check(ReferenceEquals(vm.Item, episode) && (stage != "idle" || wire.Posts.Count == 0),
                    "Scope retirement keeps the displayed episode and idle clicks reconfigure before translating");
                wire.ReleasePost.TrySetResult(); wire.ReleasePoll.TrySetResult();
                await retiredTranslation;
                Check(ReferenceEquals(vm.OverviewSource, currentSource) && overview.Text == Wire.SeriesText && overview.Opacity == 1 &&
                    button.IsEnabled && (string)button.Content == "Translate description" && oldSource.Overview == Wire.SeriesText,
                    $"Retired {change} {stage} cannot publish old results or leave same-item controls stuck");
            }
            Program.Log("PASS: language/access retirement during POST, polling and idle button mode re-resolves the same item's overview and restores controls.");

            wire.Reset(); wire.KeepPending = true;
            wire.SeriesOverview = string.Concat(Enumerable.Repeat(Wire.SeriesText + " ", 30));
            await Load();
            page.Width = 460; Call(page, "UpdateResponsiveLayout", 460d);
            var siblings = Field<List<Episode>>(page, "_siblingEpisodes");
            siblings.Clear();
            for (var index = 0; index < 8; index++)
                siblings.Add(new Episode { ContentId = index == 4 ? "episode" : $"sibling-{index}", Title = $"Episode {index + 1}", EpisodeNumber = index + 1 });
            Call(page, "BuildSiblingEpisodeCards");
            ((FrameworkElement)page.FindName("SiblingEpisodesSection")).Visibility = Visibility.Visible;
            ((FrameworkElement)page.FindName("SiblingEpisodesLoadingSkeleton")).Visibility = Visibility.Collapsed;
            var rail = (ScrollViewer)page.FindName("SiblingEpisodesScrollViewer");
            rail.Visibility = Visibility.Visible;
            page.UpdateLayout();
            await Until(() => rail.ScrollableWidth > 160);
            rail.ChangeView(160, null, null, true);
            await Until(() => rail.HorizontalOffset > 0);
            var offset = rail.HorizontalOffset;
            var cards = ((StackPanel)page.FindName("SiblingEpisodesPanel")).Children.ToArray();
            ((IInvokeProvider)new ButtonAutomationPeer((Button)overview.Children[1]).GetPattern(PatternInterface.Invoke)).Invoke();
            Check(((TextBlock)overview.Children[0]).MaxLines == 0, "Long description must be expanded before polling");
            var polling = (Task)Call(page, "TranslateOverviewAsync", vm.OverviewSource!)!;
            await Until(() => wire.PollReads >= 2);
            Check(cards.SequenceEqual(((StackPanel)page.FindName("SiblingEpisodesPanel")).Children) &&
                Math.Abs(rail.HorizontalOffset - offset) < 1 && ((TextBlock)overview.Children[0]).MaxLines == 0,
                "Unchanged translation polls preserve sibling card instances, rail offset and expanded description");
            Field<CancellationTokenSource>(page, "_translationCts").Cancel();
            await polling;
            Program.Log("PASS: repeated pending translation polls do not rebuild the sibling rail or collapse expanded unchanged text.");
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
    private static T Field<T>(object owner, string field) => (T)owner.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
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
        internal string OnView = "button", TargetLanguage = "fr";
        internal string[] OwnMachineTranslatedFields = [];
        internal string SeriesOverview = SeriesText;
        internal bool FailSeries, FailItem, Translated, HoldStatus, HoldPost, HoldPoll, KeepPending, HoldItem, HoldSpoilers;
        internal int PollReads, SeriesFailuresRemaining, StatusFailuresRemaining, PostFailuresRemaining, PollFailuresRemaining;
        internal TaskCompletionSource StatusEntered = NewGate(), ReleaseStatus = NewGate(), PostEntered = NewGate(), ReleasePost = NewGate(),
            PollEntered = NewGate(), ReleasePoll = NewGate(), ItemReadEntered = NewGate(), ReleaseItemRead = NewGate(),
            SpoilersEntered = NewGate(), ReleaseSpoilers = NewGate();
        private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Reset()
        {
            Paths.Clear(); Posts.Clear(); OwnOverview = " \t "; SeriesId = "series"; OnView = "button";
            SeriesOverview = SeriesText; PollReads = SeriesFailuresRemaining = StatusFailuresRemaining = PostFailuresRemaining = PollFailuresRemaining = 0; TargetLanguage = "fr"; OwnMachineTranslatedFields = [];
            FailSeries = FailItem = Translated = HoldStatus = HoldPost = HoldPoll = KeepPending = HoldItem = HoldSpoilers = false;
            StatusEntered = NewGate(); ReleaseStatus = NewGate(); PostEntered = NewGate(); ReleasePost = NewGate();
            PollEntered = NewGate(); ReleasePoll = NewGate(); ItemReadEntered = NewGate(); ReleaseItemRead = NewGate();
            SpoilersEntered = NewGate(); ReleaseSpoilers = NewGate();
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "episode-overview.fixture.invalid") throw new InvalidOperationException("Unexpected external request");
            var path = request.RequestUri!.PathAndQuery; Paths.Add(path);
            var onView = OnView;
            var translated = Translated;
            var ownOverview = OwnOverview;
            var targetLanguage = TargetLanguage;
            var ownFields = OwnMachineTranslatedFields;
            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/translate-description"))
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Posts.Add(path + "|" + payload.RootElement.GetProperty("target_language").GetString());
                if (PostFailuresRemaining > 0) { --PostFailuresRemaining; return new(HttpStatusCode.ServiceUnavailable); }
                if (HoldPost) { PostEntered.TrySetResult(); await ReleasePost.Task; }
                Translated = !KeepPending; return new(HttpStatusCode.NoContent);
            }
            if ((request.RequestUri.AbsolutePath is "/api/v2/catalog/items/series" or "/api/v2/catalog/items/episode" or "/api/v2/catalog/items/movie") && Posts.Count > 0)
            {
                Interlocked.Increment(ref PollReads);
                if (PollFailuresRemaining > 0) { --PollFailuresRemaining; return new(HttpStatusCode.ServiceUnavailable); }
                if (HoldPoll) { HoldPoll = false; PollEntered.TrySetResult(); await ReleasePoll.Task; }
            }
            if ((request.RequestUri.AbsolutePath is "/api/v2/catalog/items/episode" or "/api/v2/catalog/items/movie") && HoldItem)
            { ItemReadEntered.TrySetResult(); await ReleaseItemRead.Task; }
            if ((request.RequestUri.AbsolutePath is "/api/v2/catalog/items/episode" or "/api/v2/catalog/items/movie") && FailItem)
                return new(HttpStatusCode.InternalServerError);
            if (request.RequestUri.AbsolutePath == "/api/v2/capabilities/metadata-ai" && HoldStatus)
            { StatusEntered.TrySetResult(); await ReleaseStatus.Task; }
            if (request.RequestUri.AbsolutePath == "/api/v2/capabilities/metadata-ai" && StatusFailuresRemaining > 0)
            { --StatusFailuresRemaining; return new(HttpStatusCode.ServiceUnavailable); }
            if (request.RequestUri.AbsolutePath == "/api/v2/catalog/items/series" && SeriesFailuresRemaining > 0)
            { --SeriesFailuresRemaining; return new(HttpStatusCode.InternalServerError); }
            if (request.RequestUri.AbsolutePath == "/api/v2/catalog/items/series" && FailSeries) return new(HttpStatusCode.NotFound);
            if (path.Contains("effective") && HoldSpoilers)
            { SpoilersEntered.TrySetResult(); await ReleaseSpoilers.Task.WaitAsync(ct); }
            object value = request.RequestUri.AbsolutePath switch
            {
                "/api/v2/catalog/items/episode" => new { content_id = "episode", type = "episode", title = "Episode", series_id = SeriesId, overview = ownOverview, pending_translation_language = translated ? null : targetLanguage, machine_translated_fields = ownFields, user_state = new { }, user_data = new { played = false } },
                "/api/v2/catalog/items/movie" => new { content_id = "movie", type = "movie", title = "Movie", overview = ownOverview, pending_translation_language = translated ? null : targetLanguage, machine_translated_fields = ownFields, user_state = new { } },
                "/api/v2/catalog/items/series" => new { content_id = "series", type = "series", overview = translated ? TranslatedText : SeriesOverview, machine_translated_fields = new[] { "overview" }, pending_translation_language = translated ? null : "fr" },
                "/api/v2/capabilities/metadata-ai" => new { state = "available", allowed = true, on_view = onView },
                "/api/v2/settings/contract/capabilities" => new { api_version = 1, manifest_revision = 17, supports_batched_effective = true },
                _ when path.Contains("effective") => new { items = new[] { new { key = "catalog.hide_unwatched_episode_overviews", value = true } } },
                _ => new { }
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
        }
    }
}
