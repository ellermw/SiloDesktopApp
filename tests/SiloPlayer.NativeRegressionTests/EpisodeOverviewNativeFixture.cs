using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
        wire.HoldSpoilers = true; wire.OwnOverview = "First-paint episode plot.";
        var vm = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException("Unexpected unscoped read")));
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "episode-overview-images"));
        var toastService = new ToastService();
        var toasts = new ToastContainer();
        slot.SetValue(null, new Services(original, client, api, vm, spoilers, http, images, toastService));
        ItemDetailPage? page = null;
        using var navigation = new CancellationTokenSource();
        try
        {
            page = new ItemDetailPage { Width = 960, Height = 700 };
            parent.Children.Add(page); await Task.Delay(100);
            Set(page, "_navigationCts", navigation); Set(page, "_currentContentId", "episode");
            await vm.LoadInLibraryAsync("episode", 42);
            var firstSiblings = Field<List<Episode>>(page, "_siblingEpisodes");
            firstSiblings.Add(new Episode { ContentId = "first-sibling", Title = "Sibling", Overview = "First-paint sibling plot." });
            Call(page, "UpdateUI");
            await wire.SpoilersEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Call(page, "BuildSiblingEpisodeCards");
            ((FrameworkElement)page.FindName("SiblingEpisodesSection")).Visibility = Visibility.Visible;
            ((ScrollViewer)page.FindName("SiblingEpisodesScrollViewer")).Visibility = Visibility.Visible;
            page.UpdateLayout();
            var overview = (ExpandableDescription)page.FindName("OverviewText");
            var firstRail = (StackPanel)page.FindName("SiblingEpisodesPanel");
            await Until(() => Descendants(firstRail).OfType<EpisodeCardCaption>().Count() == 1);
            Check(!spoilers.IsLoadedForCurrentContext && spoilers.ShouldHideOverviews && overview.Text == wire.OwnOverview &&
                overview.Visibility == Visibility.Collapsed &&
                ((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Visible &&
                ((Button)page.FindName("TranslateOverviewButton")).Visibility == Visibility.Collapsed && wire.Posts.Count == 0 &&
                Descendants(firstRail).OfType<EpisodeCardCaption>().Single().Presentation.Overview == "First-paint sibling plot.",
                "First paint protects the own episode plot and its translation while preferences are unknown, without changing sibling captions");
            wire.HoldSpoilers = false; wire.ReleaseSpoilers.TrySetResult();
            await spoilers.EnsureLoadedAsync();
            Check(spoilers.HideOverviews, "Fixture must enable actual episode spoiler preferences");
            await Until(() => overview.Visibility == Visibility.Collapsed && vm.OverviewSource != null &&
                Descendants(firstRail).OfType<EpisodeCardCaption>().SingleOrDefault()?.Presentation.Overview == "");
            firstSiblings.Clear(); Call(page, "BuildSiblingEpisodeCards");
            wire.Reset();
            Program.Log("PASS: first-paint own plot is protected while preferences are unknown; sibling captions retain their existing policy and both honor the loaded preference.");

            await vm.LoadInLibraryAsync("episode", 42);
            Call(page, "UpdateUI");
            await (Task)Call(page, "ConfigureOnViewTranslationAsync", vm.Item!)!;
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

            foreach (var stage in new[] { "post", "poll" })
            foreach (var change in new[] { "watched", "progress", "reveal" })
            {
                wire.Reset(); wire.OwnOverview = "An automatically translated episode plot."; wire.OnView = "auto";
                wire.HoldPost = stage == "post"; wire.HoldPoll = stage == "poll";
                Set(page, "_revealedEpisodeOverviewId", null);
                await vm.LoadInLibraryAsync("episode", 42);
                vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "episode"));
                var configuration = (Task)Call(page, "ConfigureOnViewTranslationAsync", vm.Item!)!;
                Call(page, "UpdateUI");
                await (stage == "post" ? wire.PostEntered.Task : wire.PollEntered.Task).WaitAsync(TimeSpan.FromSeconds(5));
                var translation = Field<CancellationTokenSource>(page, "_translationCts");
                var revision = Field<long>(page, "_overviewConfigurationRevision");

                vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedCleared, "episode"));
                Call(page, "ApplyEpisodeSpoilers");
                Check(overview.Visibility == Visibility.Collapsed && button.Visibility == Visibility.Collapsed &&
                    !translation.IsCancellationRequested && ReferenceEquals(translation, Field<CancellationTokenSource?>(page, "_translationCts")) &&
                    Field<long>(page, "_overviewConfigurationRevision") == revision && wire.Posts.Count == 1,
                    "Hiding an own plot during automatic translation changes only presentation, not its POST/poll owner");

                if (change == "watched") vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "episode"));
                else if (change == "progress") vm.Receive(new PlaybackProgressUpdated("episode", 60, 1200, false));
                else Call(page, "RevealEpisodeOverviewButton_Click", button, new RoutedEventArgs());
                Call(page, "ApplyEpisodeSpoilers"); Call(page, "RefreshRetiredOverviewScope");
                await Task.Delay(550);
                Check(overview.Visibility == Visibility.Visible && button.Visibility == Visibility.Visible && !button.IsEnabled &&
                    (string)button.Content == "Translating…" && overview.Opacity == 0.55 &&
                    !translation.IsCancellationRequested && ReferenceEquals(translation, Field<CancellationTokenSource?>(page, "_translationCts")) &&
                    Field<long>(page, "_overviewConfigurationRevision") == revision && wire.Posts.Count == 1,
                    $"A {change} transition during a held automatic {stage} retains the original translation and restores its busy presentation");

                wire.ReleasePost.TrySetResult(); wire.ReleasePoll.TrySetResult();
                await configuration;
                Check(wire.Posts.Single() == "/api/v2/catalog/items/episode/translate-description|fr" && wire.PollReads > 0 &&
                    vm.OverviewSource!.PendingTranslationLanguage == null && overview.Opacity == 1 &&
                    button.Visibility == Visibility.Collapsed && Field<CancellationTokenSource?>(page, "_translationCts") == null,
                    "The original automatic translation finishes polling without a duplicate POST after spoiler transitions");
            }
            Set(page, "_revealedEpisodeOverviewId", null);
            Program.Log("PASS: watched/progress/reveal transitions preserve an in-flight automatic POST and poll without duplicate translations.");

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
                Check(!spoilers.IsLoadedForCurrentContext && spoilers.HideOverviews && spoilers.HideImages,
                    "An access-only refresh retains both last-known card spoiler preferences");
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

            foreach (var change in new[] { "access", "same-profile" })
            {
                wire.Reset(); wire.OwnOverview = "Protected own plot.";
                await Load();
                page.Width = 1280; Call(page, "UpdateResponsiveLayout", 1280d);
                var protectedSiblings = Field<List<Episode>>(page, "_siblingEpisodes");
                protectedSiblings.Clear(); protectedSiblings.Add(new Episode { ContentId = $"protected-{change}", Title = "Unwatched sibling",
                    Overview = "An unwatched sibling plot", StillUrl = "https://episode-overview.fixture.invalid/still.png", StillIsEpisodeStill = true });
                Call(page, "ApplyEpisodeSpoilers"); Call(page, "BuildSiblingEpisodeCards");
                ((FrameworkElement)page.FindName("SiblingEpisodesSection")).Visibility = Visibility.Visible;
                ((ScrollViewer)page.FindName("SiblingEpisodesScrollViewer")).Visibility = Visibility.Visible;
                var protectedRail = (StackPanel)page.FindName("SiblingEpisodesPanel");
                Image? Still() => Descendants(protectedRail).OfType<Image>().FirstOrDefault(image => image.Source is BitmapImage { PixelWidth: > 0 });
                await Until(() => Still() != null); page.UpdateLayout();
                var protectedPixels = await Pixels(Still()!);
                wire.SpoilerFailure = "503"; wire.FailSpoilerEffective = true;
                if (change == "access") client.InvalidateAccessContext(); else client.SetProfile("fixture");
                Call(page, "RefreshRetiredOverviewScope");
                await spoilers.EnsureLoadedAsync(); await (Task)Call(page, "EnsureEpisodeSpoilersAsync")!;
                Check(!spoilers.IsLoadedForCurrentContext && spoilers.HideImages && spoilers.HideOverviews &&
                    Descendants(protectedRail).OfType<EpisodeCardCaption>().All(caption => caption.Presentation.Overview == ""),
                    "A failed access-only reload preserves sibling captions and both protection preferences");
                // Also exercise a fresh card/still created after the replacement read has failed.
                Call(page, "BuildSiblingEpisodeCards");
                await Until(() => Still() != null); page.UpdateLayout();
                Check(Descendants(protectedRail).OfType<EpisodeCardCaption>().Single().OverviewText.Visibility == Visibility.Collapsed &&
                    protectedPixels.SequenceEqual(await Pixels(Still()!)),
                    "Sibling captions remain hidden and freshly loaded still pixels retain the same spoiler blur after a failed reload");
                wire.SpoilerFailure = null; Set(spoilers, "_retryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
                await spoilers.EnsureLoadedAsync();
            }
            Field<List<Episode>>(page, "_siblingEpisodes").Clear(); Call(page, "BuildSiblingEpisodeCards");
            Program.Log("PASS: failed access-only reloads preserve sibling captions and actual blurred stills, including freshly rebuilt cards.");

            foreach (var failure in new[] { "404", "503", "timeout", "network", "malformed", "null" })
            foreach (var effective in new[] { false, true })
            foreach (var hide in new[] { false, true })
            {
                wire.Reset(); wire.OwnOverview = "An episode-specific plot.";
                wire.SpoilerFailure = failure; wire.FailSpoilerEffective = effective; wire.HideOverviews = hide;
                client.SetProfile($"failed-{failure}-{effective}-{hide}");
                await Load();
                await Until(() => !spoilers.IsLoadedForCurrentContext && !spoilers.ShouldHideOverviews &&
                    overview.Text == wire.OwnOverview && overview.Visibility == Visibility.Visible && button.Visibility == Visibility.Visible);
                Check(((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Collapsed && wire.Posts.Count == 0,
                    "A failed optional preference read keeps the base visible overview and manual translation action");
                var reads = wire.Paths.Count(path => path == "/api/v2/settings/contract/capabilities");
                Call(page, "RefreshRetiredOverviewScope"); Call(page, "RefreshRetiredOverviewScope");
                Check(wire.Paths.Count(path => path == "/api/v2/settings/contract/capabilities") == reads,
                    "The scope watcher respects preference failure backoff");
                wire.SpoilerFailure = null; wire.HoldSpoilers = true;
                Set(spoilers, "_retryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
                Call(page, "RefreshRetiredOverviewScope");
                await wire.SpoilersEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(overview.Visibility == Visibility.Visible && button.Visibility == Visibility.Visible,
                    "A retry in flight preserves the visible default and its translation action");
                wire.HoldSpoilers = false; wire.ReleaseSpoilers.TrySetResult();
                await Until(() => spoilers.IsLoadedForCurrentContext && spoilers.HideOverviews == hide &&
                    overview.Visibility == (hide ? Visibility.Collapsed : Visibility.Visible) &&
                    button.Visibility == (hide ? Visibility.Collapsed : Visibility.Visible));
                Check(wire.Posts.Count == 0 && vm.OverviewSource == vm.Item,
                    "Preference recovery on the same page restores the hide/translate policy without changing the overview owner");
            }
            Program.Log("PASS: failed capability/effective reads (404, 503, timeout, network, malformed and null bodies) retain the visible default and recover both hide values with backoff.");

            foreach (var state in new[] { "active", "manual-recovery" })
            {
                wire.Reset(); wire.OwnOverview = "A visible episode plot."; wire.OnView = "auto";
                wire.SpoilerFailure = "503"; wire.FailSpoilerEffective = true;
                wire.HoldPost = state == "active"; wire.PostFailuresRemaining = state == "manual-recovery" ? 1 : 0;
                client.SetProfile($"retry-{state}");
                await spoilers.EnsureLoadedAsync();
                await vm.LoadInLibraryAsync("episode", 42);
                var configuration = (Task)Call(page, "ConfigureOnViewTranslationAsync", vm.Item!)!;
                Call(page, "UpdateUI");
                await Until(() => wire.Posts.Count == 1 && (state == "active" ? overview.Opacity == 0.55 :
                    button.Visibility == Visibility.Visible && button.IsEnabled && Field<bool>(page, "_translateButtonMode")));
                var translation = Field<CancellationTokenSource?>(page, "_translationCts");
                var configurationRevision = Field<long>(page, "_overviewConfigurationRevision");
                var retrySiblings = Field<List<Episode>>(page, "_siblingEpisodes");
                retrySiblings.Clear(); retrySiblings.Add(new Episode { ContentId = "retry-sibling", Title = "Sibling", Overview = "Sibling plot" });
                Call(page, "ApplyEpisodeSpoilers"); Call(page, "BuildSiblingEpisodeCards");
                var retryCards = ((StackPanel)page.FindName("SiblingEpisodesPanel")).Children.ToArray();
                var spoilerRevision = Field<int>(page, "_episodeSpoilerRevision");
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    wire.HoldSpoilers = true;
                    wire.SpoilersEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wire.ReleaseSpoilers = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Set(spoilers, "_retryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
                    Call(page, "RefreshRetiredOverviewScope");
                    await wire.SpoilersEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(overview.Visibility == Visibility.Visible &&
                        ((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Collapsed &&
                        Field<long>(page, "_overviewConfigurationRevision") == configurationRevision &&
                        (translation == null || !translation.IsCancellationRequested),
                        "A held failing preference retry neither hides the plot nor cancels/reconfigures translation");
                    wire.HoldSpoilers = false; wire.ReleaseSpoilers.TrySetResult();
                    await spoilers.EnsureLoadedAsync(); await (Task)Call(page, "EnsureEpisodeSpoilersAsync")!;
                    await Task.Delay(550);
                    Check(wire.Posts.Count == 1 && retryCards.SequenceEqual(((StackPanel)page.FindName("SiblingEpisodesPanel")).Children) &&
                        Field<int>(page, "_episodeSpoilerRevision") == spoilerRevision &&
                        Field<long>(page, "_overviewConfigurationRevision") == configurationRevision &&
                        (translation == null ? Field<bool>(page, "_translateButtonMode") && button.IsEnabled :
                            ReferenceEquals(translation, Field<CancellationTokenSource?>(page, "_translationCts")) && !translation.IsCancellationRequested),
                        "Failed retries preserve cards, still-load revision, the active translation or manual recovery, and never repeat POSTs");
                }
                translation?.Cancel(); wire.ReleasePost.TrySetResult(); await configuration;
            }
            Field<List<Episode>>(page, "_siblingEpisodes").Clear(); Call(page, "BuildSiblingEpisodeCards");
            wire.Reset(); client.SetProfile("fixture"); await spoilers.EnsureLoadedAsync();
            Program.Log("PASS: failing preference retries preserve visible plot, active/manual-recovery translation, sibling instances and still-load ownership without repeated POSTs.");

            // A failed reload after disabling hiding keeps known protection only until recovery.
            wire.Reset(); wire.OwnOverview = "An episode-specific plot.";
            await spoilers.EnsureLoadedAsync(); // The fixture's known preference enables hiding.
            wire.HideOverviews = false; wire.SpoilerFailure = "503"; spoilers.Invalidate();
            await spoilers.EnsureLoadedAsync(); Call(page, "ApplyEpisodeSpoilers");
            Check(overview.Visibility == Visibility.Collapsed && button.Visibility == Visibility.Collapsed,
                "A failed setting reload preserves the known hide preference");
            wire.SpoilerFailure = null; Set(spoilers, "_retryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
            Call(page, "RefreshRetiredOverviewScope");
            await Until(() => spoilers.IsLoadedForCurrentContext && !spoilers.HideOverviews &&
                overview.Visibility == Visibility.Visible && button.Visibility == Visibility.Visible);

            foreach (var mode in new[] { "button", "auto" })
            foreach (var hide in new[] { false, true })
            {
                wire.Reset(); wire.OnView = mode; wire.HideOverviews = hide;
                wire.HoldSpoilers = true; wire.SpoilerFailure = "malformed"; wire.FailSpoilerEffective = true;
                client.SetProfile($"fallback-{mode}-{hide}");
                await vm.LoadInLibraryAsync("episode", 42); Call(page, "UpdateUI");
                await wire.SpoilersEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Until(() => overview.Text == (mode == "auto" ? Wire.TranslatedText : Wire.SeriesText) &&
                    button.Visibility == (mode == "auto" ? Visibility.Collapsed : Visibility.Visible));
                Check(!spoilers.IsLoadedForCurrentContext && overview.Visibility == Visibility.Visible &&
                    ((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Collapsed,
                    "Series text and manual/automatic translation never wait for held optional spoiler preferences");
                wire.HoldSpoilers = false; wire.ReleaseSpoilers.TrySetResult();
                await Until(() => !spoilers.ShouldHideOverviews);
                Check(vm.OverviewSource?.ContentId == "series" && overview.Visibility == Visibility.Visible,
                    "A malformed optional preference response cannot block or clear series fallback");
                wire.SpoilerFailure = null; Set(spoilers, "_retryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
                Call(page, "RefreshRetiredOverviewScope");
                await Until(() => spoilers.IsLoadedForCurrentContext && spoilers.HideOverviews == hide);
                Check(overview.Visibility == Visibility.Visible && wire.Posts.All(post => post.Contains("/series/translate-description")),
                    "Recovering either spoiler value keeps the series source visible and translation series-owned");
            }
            wire.Reset(); client.SetProfile("fixture"); await spoilers.EnsureLoadedAsync();
            Program.Log("PASS: failed disable-hide reload recovers; held/malformed preferences never delay series fallback or its manual/automatic translation.");

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

            parent.Children.Add(toasts);
            toastService.Register(toasts, page.DispatcherQueue);
            foreach (var mode in new[] { "button", "auto" })
            foreach (var failure in new[] { "503", "timeout" })
            foreach (var stage in new[] { "post", "poll" })
            {
                ((StackPanel)toasts.FindName("ToastStack")).Children.Clear();
                wire.Reset(); wire.OnView = mode; wire.TranslationTimeout = failure == "timeout";
                wire.PostFailuresRemaining = stage == "post" ? 1 : 0;
                wire.PollFailuresRemaining = stage == "poll" ? 1 : 0;
                await vm.LoadInLibraryAsync("episode", 42); Call(page, "UpdateUI");
                if (mode == "button")
                {
                    await Until(() => button.Visibility == Visibility.Visible && button.IsEnabled);
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                }
                await Until(() => wire.Posts.Count == 1 && button.Visibility == Visibility.Visible && button.IsEnabled && overview.Opacity == 1);
                Check(overview.Text == Wire.SeriesText && vm.OverviewSource!.PendingTranslationLanguage == "fr" &&
                    (string)button.Content == "Translate description" && Field<bool>(page, "_translateButtonMode"),
                    $"A {mode} {stage} {failure} retains the description and offers a manual retry");
                if (failure == "timeout")
                    await Until(() => Descendants(toasts).OfType<TextBlock>().Any(text =>
                        text.Text == "Description translation timed out. Try again."));
                await Task.Delay(600);
                Check(wire.Posts.Count == 1 && button.Visibility == Visibility.Visible && button.IsEnabled,
                    "The scope watcher preserves the recovery action without repeating automatic writes");
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => wire.Posts.Count == 2 && overview.Text == Wire.TranslatedText && button.Visibility == Visibility.Collapsed);
                Check(wire.Posts.All(post => post == "/api/v2/catalog/items/series/translate-description|fr") &&
                    vm.Item!.ContentId == "episode" && string.IsNullOrWhiteSpace(vm.Item.Overview),
                    "Manual recovery after a translation failure still translates only the scoped series source");
            }
            toastService.Unregister(); parent.Children.Remove(toasts);
            Program.Log("PASS: manual/automatic translation POST/poll failures, including uncancelled-token transport timeouts, show readable feedback and a working manual recovery action.");

            wire.Reset(); wire.OnView = "auto"; wire.HoldPost = wire.KeepPending = true;
            await vm.LoadInLibraryAsync("episode", 42);
            var timedTranslation = (Task)Call(page, "ConfigureOnViewTranslationAsync", vm.Item!)!;
            Call(page, "UpdateUI");
            await wire.PostEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var deadline = Field<CancellationTokenSource>(page, "_translationCts");
            // Shorten the real CancelAfter deadline; do not cancel navigation or change ownership.
            deadline.CancelAfter(TimeSpan.FromMilliseconds(1));
            await Until(() => deadline.IsCancellationRequested);
            wire.HoldPost = false; wire.ReleasePost.TrySetResult(); await timedTranslation;
            Check(Field<bool>(page, "_translateButtonMode") && button.Visibility == Visibility.Visible && button.IsEnabled &&
                overview.Opacity == 1 && (string)button.Content == "Translate description",
                "An automatic translation deadline restores an enabled manual recovery action and undims the description");
            await Task.Delay(600);
            Check(wire.Posts.Count == 1, "The deadline recovery action does not repeat automatic POSTs");
            wire.KeepPending = false;
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => wire.Posts.Count == 2 && overview.Text == Wire.TranslatedText && button.Visibility == Visibility.Collapsed);

            using (var departed = new CancellationTokenSource())
            {
                Set(page, "_navigationCts", departed);
                wire.Reset(); wire.OnView = "auto"; wire.HoldPost = wire.KeepPending = true;
                await vm.LoadInLibraryAsync("episode", 42);
                var retired = (Task)Call(page, "ConfigureOnViewTranslationAsync", vm.Item!)!;
                await wire.PostEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                departed.Cancel(); wire.ReleasePost.TrySetResult(); await retired;
                Check(!Field<bool>(page, "_translateButtonMode") && !button.IsEnabled &&
                    Field<CancellationTokenSource?>(page, "_translationCts") == null,
                    "Navigation cancellation silently retires automatic translation without exposing deadline recovery");
                Set(page, "_navigationCts", navigation);
            }
            Program.Log("PASS: automatic translation deadlines expose a working manual retry; navigation cancellation remains retired.");

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
            foreach (var stage in new[] { "series", "status" })
            {
                wire.Reset(); wire.FailSeries = stage == "series";
                wire.StatusFailuresRemaining = stage == "status" ? 100 : 0;
                await Load();
                var path = stage == "series" ? "/api/v2/catalog/items/series?library_id=42" : "/api/v2/capabilities/metadata-ai";
                for (var attempt = 1; attempt <= 8; attempt++)
                {
                    var expected = attempt;
                    await Until(() => wire.Paths.Count(value => value == path) == expected &&
                        (expected == 8 ? Field<DateTimeOffset?>(page, "_overviewRetryAt") == null :
                            Field<DateTimeOffset?>(page, "_overviewRetryAt") != null));
                    if (attempt < 8)
                    {
                        Set(page, "_overviewRetryAt", DateTimeOffset.UtcNow.AddSeconds(-1));
                        Call(page, "RefreshRetiredOverviewScope");
                    }
                }
                var reads = wire.Paths.Count;
                for (var tick = 0; tick < 3; tick++)
                {
                    Call(page, "RefreshRetiredOverviewScope"); Call(page, "ConfigureOverviewIfNeeded");
                    await Task.Delay(550);
                }
                Check(wire.Paths.Count == reads && wire.Posts.Count == 0 && vm.ErrorMessage == null,
                    "Exhausted optional reads stop for this owner without blocking the page or writing translations");
                wire.FailSeries = false; wire.StatusFailuresRemaining = 0;
                client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
                Call(page, "RefreshRetiredOverviewScope");
                await Until(() => overview.Text == Wire.SeriesText && button.Visibility == Visibility.Visible && button.IsEnabled);
                Check(Field<int>(page, "_overviewConfigurationAttempts") == 1,
                    "A new metadata scope gets a fresh optional-read budget");
            }
            Program.Log("PASS: transient optional reads recover; permanent series/capability failures exhaust a bounded budget until scope changes.");

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

            wire.Reset(); wire.OwnOverview = "An unrevealed episode plot.";
            client.SetProfile("fixture"); await spoilers.EnsureLoadedAsync();
            Microsoft.UI.Xaml.Navigation.NavigationEventArgs? routeEvent = null;
            var eventSource = new Frame();
            eventSource.Navigated += (_, e) => routeEvent = e;
            eventSource.Navigate(typeof(Page));
            Check(routeEvent != null, "Fixture needs real navigation event arguments");
            // Exercise route departure on the same page instance without starting unrelated watch/enrichment reads.
            Set(page, "_navigationCts", new CancellationTokenSource());
            Call(page, "OnNavigatedFrom", routeEvent!);
            Set(page, "_navigationCts", new CancellationTokenSource());
            var firstRoute = true;
            foreach (var id in new[] { "episode", "episode-b", "episode" })
            {
                Set(page, "_currentContentId", id);
                await vm.LoadInLibraryAsync(id, 42); Call(page, "UpdateUI");
                await Until(() => overview.Visibility == Visibility.Collapsed &&
                    ((Button)page.FindName("RevealEpisodeOverviewButton")).Visibility == Visibility.Visible);
                Check(Field<string?>(page, "_revealedEpisodeOverviewId") == null && button.Visibility == Visibility.Collapsed,
                    "Every newly entered episode route, including a return to A, requires a fresh Reveal");
                if (firstRoute)
                {
                    Call(page, "RevealEpisodeOverviewButton_Click", button, new RoutedEventArgs());
                    await Until(() => overview.Visibility == Visibility.Visible && button.Visibility == Visibility.Visible);
                    firstRoute = false;
                }
                Call(page, "OnNavigatedFrom", routeEvent!);
                Set(page, "_navigationCts", new CancellationTokenSource());
            }
            Field<CancellationTokenSource>(page, "_navigationCts").Dispose();
            Set(page, "_navigationCts", navigation);
            Program.Log("PASS: revealing A, departing to B and returning to A on the same page does not retain the reveal.");
        }
        finally
        {
            navigation.Cancel(); vm.CancelPendingLoads();
            toastService.Unregister(); parent.Children.Remove(toasts);
            if (page != null) parent.Children.Remove(page);
            slot.SetValue(null, original);
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static async Task<byte[]> Pixels(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        Check(pixels.Length > 0 && pixels.Any(value => value != 0), "Spoiler still must actually render before comparing pixels");
        return pixels;
    }
    private static object? Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args);
    private static void Set(object owner, string field, object? value) => owner.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, value);
    private static T Field<T>(object owner, string field) => (T)owner.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var n = 0; n < 240 && !ready(); n++) await Task.Delay(25); Check(ready(), "Native overview state did not settle"); }
    private sealed class Services(IServiceProvider original, SiloApiClient client, CatalogApi api, ItemDetailViewModel vm,
        EpisodeSpoilerPreferences spoilers, HttpClient http, ImageService images, ToastService toasts) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(SiloApiClient) ? client : type == typeof(CatalogApi) ? api :
            type == typeof(ItemDetailViewModel) ? vm : type == typeof(EpisodeSpoilerPreferences) ? spoilers :
            type == typeof(HttpClient) ? http : type == typeof(ImageService) ? images : type == typeof(ToastService) ? toasts :
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
        internal bool FailSeries, FailItem, Translated, HoldStatus, HoldPost, HoldPoll, KeepPending, HoldItem, HoldSpoilers, TranslationTimeout;
        internal bool HideOverviews = true, HideImages = true, FailSpoilerEffective;
        internal string? SpoilerFailure;
        internal int PollReads, SeriesFailuresRemaining, StatusFailuresRemaining, PostFailuresRemaining, PollFailuresRemaining;
        internal TaskCompletionSource StatusEntered = NewGate(), ReleaseStatus = NewGate(), PostEntered = NewGate(), ReleasePost = NewGate(),
            PollEntered = NewGate(), ReleasePoll = NewGate(), ItemReadEntered = NewGate(), ReleaseItemRead = NewGate(),
            SpoilersEntered = NewGate(), ReleaseSpoilers = NewGate();
        private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Reset()
        {
            Paths.Clear(); Posts.Clear(); OwnOverview = " \t "; SeriesId = "series"; OnView = "button";
            SeriesOverview = SeriesText; PollReads = SeriesFailuresRemaining = StatusFailuresRemaining = PostFailuresRemaining = PollFailuresRemaining = 0; TargetLanguage = "fr"; OwnMachineTranslatedFields = [];
            FailSeries = FailItem = Translated = HoldStatus = HoldPost = HoldPoll = KeepPending = HoldItem = HoldSpoilers = TranslationTimeout = false;
            HideOverviews = HideImages = true; FailSpoilerEffective = false; SpoilerFailure = null;
            StatusEntered = NewGate(); ReleaseStatus = NewGate(); PostEntered = NewGate(); ReleasePost = NewGate();
            PollEntered = NewGate(); ReleasePoll = NewGate(); ItemReadEntered = NewGate(); ReleaseItemRead = NewGate();
            SpoilersEntered = NewGate(); ReleaseSpoilers = NewGate();
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "episode-overview.fixture.invalid") throw new InvalidOperationException("Unexpected external request");
            var path = request.RequestUri!.PathAndQuery; Paths.Add(path);
            if (request.RequestUri.AbsolutePath == "/still.png")
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(
                    Path.Combine(Program.AppDirectory, "Assets", "silo-icon-1024.png"), ct)) };
            var onView = OnView;
            var translated = Translated;
            var ownOverview = OwnOverview;
            var targetLanguage = TargetLanguage;
            var ownFields = OwnMachineTranslatedFields;
            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/translate-description"))
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Posts.Add(path + "|" + payload.RootElement.GetProperty("target_language").GetString());
                if (PostFailuresRemaining > 0)
                {
                    --PostFailuresRemaining;
                    if (TranslationTimeout)
                    {
                        Check(!ct.IsCancellationRequested, "A transport timeout must not cancel the request token");
                        throw new TaskCanceledException("Synthetic POST connect timeout", new TimeoutException(), ct);
                    }
                    return new(HttpStatusCode.ServiceUnavailable);
                }
                if (HoldPost) { PostEntered.TrySetResult(); await ReleasePost.Task; }
                Translated = !KeepPending; return new(HttpStatusCode.NoContent);
            }
            if ((request.RequestUri.AbsolutePath is "/api/v2/catalog/items/series" or "/api/v2/catalog/items/episode" or "/api/v2/catalog/items/movie") && Posts.Count > 0)
            {
                Interlocked.Increment(ref PollReads);
                if (PollFailuresRemaining > 0)
                {
                    --PollFailuresRemaining;
                    if (TranslationTimeout)
                    {
                        Check(!ct.IsCancellationRequested, "A transport timeout must not cancel the request token");
                        throw new TaskCanceledException("Synthetic poll connect timeout", new TimeoutException(), ct);
                    }
                    return new(HttpStatusCode.ServiceUnavailable);
                }
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
            if (SpoilerFailure != null && (FailSpoilerEffective ? path.Contains("effective") :
                request.RequestUri.AbsolutePath == "/api/v2/settings/contract/capabilities"))
                return SpoilerFailure switch
                {
                    "404" => new(HttpStatusCode.NotFound),
                    "503" => new(HttpStatusCode.ServiceUnavailable),
                    "timeout" => throw new TaskCanceledException("Synthetic settings timeout"),
                    "network" => throw new HttpRequestException("Synthetic settings network failure"),
                    _ => new(HttpStatusCode.OK) { Content = new StringContent(SpoilerFailure == "null" ? "null" : "{") }
                };
            object value = request.RequestUri.AbsolutePath switch
            {
                "/api/v2/catalog/items/episode" => new { content_id = "episode", type = "episode", title = "Episode", series_id = SeriesId, overview = ownOverview, pending_translation_language = translated ? null : targetLanguage, machine_translated_fields = ownFields, user_state = new { }, user_data = new { played = false } },
                "/api/v2/catalog/items/episode-b" => new { content_id = "episode-b", type = "episode", title = "Episode B", overview = ownOverview, pending_translation_language = targetLanguage, user_state = new { }, user_data = new { played = false } },
                "/api/v2/catalog/items/movie" => new { content_id = "movie", type = "movie", title = "Movie", overview = ownOverview, pending_translation_language = translated ? null : targetLanguage, machine_translated_fields = ownFields, user_state = new { } },
                "/api/v2/catalog/items/series" => new { content_id = "series", type = "series", overview = translated ? TranslatedText : SeriesOverview, machine_translated_fields = new[] { "overview" }, pending_translation_language = translated ? null : "fr" },
                "/api/v2/capabilities/metadata-ai" => new { state = "available", allowed = true, on_view = onView },
                "/api/v2/settings/contract/capabilities" => new { api_version = 1, manifest_revision = 17, supports_batched_effective = true },
                _ when path.Contains("effective") => new { items = new[] {
                    new { key = "catalog.hide_unwatched_episode_overviews", value = HideOverviews },
                    new { key = "catalog.hide_unwatched_episode_images", value = HideImages } } },
                _ => new { }
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
        }
    }
}
