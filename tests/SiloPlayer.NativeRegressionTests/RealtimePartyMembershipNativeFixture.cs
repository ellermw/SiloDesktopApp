using System.Net;
using System.Reflection;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Views;

internal static partial class RealtimeSeriesStateNativeFixture
{
    private static async Task VerifyPartyMembershipAsync(StackPanel parent, Wire wire)
    {
        var failures = new List<string>();
        foreach (var phase in new[] { "capabilities", "target", "episode-list", "primary", "stable", "viewer-progress", "season-primary", "season-progress", "season-initial" })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            HttpResponseMessage? held = null;
            var created = 0;
            wire.EmptyEpisodes = wire.FailEpisodes = false;
            wire.EpisodeResponseStatus = HttpStatusCode.OK; wire.EpisodeSeasonNumber = 1;
            wire.HeldCompanion = wire.CompanionFailure = wire.HeldTitleArt = null;
            wire.PartyResponse = request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/api/v2/watch-together/rooms")
                {
                    Check(request.Method == HttpMethod.Post, "Only synthetic room creation is authorized.");
                    created++;
                    // No room access proof: stop before persistence, room navigation or staging.
                    return Task.FromResult(Json("{\"room\":{\"room_id\":\"synthetic-party\"}}"));
                }
                var capabilities = path == "/api/v2/watch-together/capabilities";
                var shouldHold = phase == "capabilities" && capabilities
                    || phase == "target" && path == "/api/v2/catalog/items/season/episodes"
                    || phase == "episode-list" && path == "/api/v2/catalog/series/series/seasons/1/episodes";
                if (shouldHold)
                {
                    held = capabilities ? Json("{\"allowed\":true,\"state\":\"available\",\"staged_selection\":true}")
                        : Json("{\"items\":[{\"content_id\":\"old-episode\",\"season_number\":1,\"episode_number\":1,\"title\":\"Old episode\",\"user_data\":{}}]}");
                    entered.TrySetResult(); return release.Task;
                }
                return capabilities ? Task.FromResult(Json("{\"allowed\":true,\"state\":\"available\",\"staged_selection\":true}")) : null;
            };
            using var navigation = new CancellationTokenSource();
            var page = new ItemDetailPage { Width = 900, Height = 720 };
            var vm = page.ViewModel;
            var seasonScope = phase.StartsWith("season-", StringComparison.Ordinal);
            var content = seasonScope ? "season" : "series";
            vm.Item = new() { ContentId = content, Type = content, Title = "Synthetic party scope", Overview = "Synthetic description.", SeasonNumber = 1 };
            if (phase is not ("target" or "season-initial")) vm.Item.PlayContentId = "old-episode";
            vm.IsSeries = !seasonScope;
            vm.Seasons.Add(new() { ContentId = "season", SeasonNumber = 1, EpisodeCount = 6, UserData = new() { UnplayedCount = 6 } });
            if (seasonScope && phase != "season-initial")
            {
                vm.Episodes.Add(new() { ContentId = "old-episode", EpisodeNumber = 1, UserData = new() });
                vm.EpisodesSeasonContentId = "season";
            }
            Set(page, "_currentContentId", content); Set(page, "_navigationCts", navigation);
            parent.Children.Add(page);
            Task? opening = null;
            try
            {
                await UntilAsync(() => page.IsLoaded && page.XamlRoot != null);
                opening = (Task)typeof(ItemDetailPage).GetMethod("ShowStartPartyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
                if (phase is "capabilities" or "target" or "episode-list")
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    vm.Seasons.Clear(); vm.Seasons.Add(new() { ContentId = "new-season", SeasonNumber = 2, EpisodeCount = 6 });
                    release.SetResult(held!);
                    await UntilAsync(() => opening.IsCompleted || PartyDialog(page) != null);
                    Check(opening.IsCompleted && PartyDialog(page) == null && created == 0,
                        "Retired season membership reached the party picker after " + phase + ".");
                }
                else
                {
                    await UntilAsync(() => PartyDialog(page) != null);
                    var dialog = PartyDialog(page)!;
                    if (phase == "primary") vm.Seasons[0].SeasonNumber = 2;
                    if (phase == "viewer-progress") vm.Seasons[0].UserData!.WatchedCount = 1;
                    if (phase == "season-primary") vm.Episodes[0].EpisodeNumber = 2;
                    if (phase == "season-progress") vm.Episodes[0].UserData!.PositionSeconds = 100;
                    var primary = Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton");
                    ((IInvokeProvider)new ButtonAutomationPeer(primary).GetPattern(PatternInterface.Invoke)).Invoke();
                    await UntilAsync(() => created > 0 || dialog.IsPrimaryButtonEnabled);
                    Check(created == (phase is "primary" or "season-primary" ? 0 : 1),
                        "Party primary action did not preserve its exact membership ownership: " + phase + ".");
                }
                Program.Log("PASS: actual native party membership " + phase + ".");
            }
            catch (Exception ex) { failures.Add(phase + ": " + ex.Message); }
            finally
            {
                if (held != null) release.TrySetResult(held);
                PartyDialog(page)?.Hide();
                try { if (opening != null) await opening.WaitAsync(TimeSpan.FromSeconds(5)); }
                finally
                {
                    vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(vm);
                    RemovePage(parent, page, navigation); wire.PartyResponse = null;
                }
            }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(" | ", failures));
    }

    private static ContentDialog? PartyDialog(ItemDetailPage page) =>
        VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(popup => Descendants<ContentDialog>(popup.Child))
            .SingleOrDefault(dialog => dialog.Title as string == "Start a party with this");

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
