using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Helpers;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static partial class EpisodeQuickPlayNativeFixture
{
    internal static async Task VerifyBoxSetMenuScopeAsync(StackPanel parent, bool retiredOwnerChecks = false)
    {
        foreach (var surface in retiredOwnerChecks ? new[] { "recycled", "scope-change", "departed", "profile", "access-refresh" } : new[] { "dedicated", "membership", "unscoped" })
        {
            using var fixture = new Fixture();
            EnsureNativeResolver();
            using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null");
            typeof(PlayerService).GetField("_mpv", Private)!.SetValue(fixture.Player, mpv);
            var movie = new MediaItem { ContentId = "restart-film", Type = "movie", Title = "Restart film", PositionSeconds = 91, UserState = new() { Played = true } };
            var set = new BoxSetDetail { Id = "7", LibraryId = "73", Title = "Restart collection", MovieCount = 1, Items = [movie] };
            try
            {
                PosterCard card;
                if (surface == "dedicated")
                {
                    var page = new BoxSetPage(fixture.Catalog, fixture.Client) { Width = 900, Height = 700 };
                    parent.Children.Add(page); await Task.Delay(60);
                    typeof(BoxSetViewModel).GetProperty("Detail")!.SetValue(page.ViewModel, set);
                    page.UpdateLayout(); await Task.Delay(60);
                    card = Descendants<PosterCard>(page).Single();
                }
                else if (surface == "membership")
                {
                    var page = new ItemDetailPage { Width = 900, Height = 700 };
                    parent.Children.Add(page); await Task.Delay(60);
                    page.ViewModel.Item = new() { ContentId = movie.ContentId, Type = "movie", Title = movie.Title };
                    typeof(ItemDetailViewModel).GetProperty("BoxSet")!.SetValue(page.ViewModel, set);
                    typeof(ItemDetailPage).GetMethod("BuildBoxSetRow", Private)!.Invoke(page, null);
                    page.UpdateLayout(); await Task.Delay(60);
                    card = Descendants<PosterCard>((DependencyObject)page.FindName("BoxSetMovies")).Single();
                }
                else { card = new PosterCard { MediaItem = movie, Tag = retiredOwnerChecks ? new ItemDetailNavigationArgs(movie.ContentId, 73) : null }; parent.Children.Add(card); await Task.Delay(60); }
                Check(MediaNavigationContext.LibraryId(card) == (surface == "unscoped" ? null : 73), "Native menu fixture lost its actual member card scope.");
                // Invoke the actual card's More button and its real popup menu.
                var more = (Button)card.FindName("MoreButton");
                more.Focus(FocusState.Keyboard); Invoke(more);
                MenuFlyoutItem? restart = null;
                await Until(() => (restart = VisualTreeHelper.GetOpenPopupsForXamlRoot(card.XamlRoot)
                    .SelectMany(popup => Descendants<MenuFlyoutItem>(popup.Child)).SingleOrDefault(x => x.Text == "Play from Beginning")) != null);
                if (retiredOwnerChecks && surface != "access-refresh")
                {
                    if (surface == "recycled") card.MediaItem = new MediaItem { ContentId = "replacement", Type = "movie", Title = "Replacement" };
                    if (surface is "recycled" or "scope-change") card.Tag = new ItemDetailNavigationArgs(card.MediaItem!.ContentId, 74);
                    if (surface == "departed") { parent.Children.Remove(card); await Until(() => !card.IsLoaded); }
                    if (surface == "profile") fixture.Auth.SelectProfile("other", profile: new() { Id = "other", Name = "Other fixture" });
                    var clicked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    restart!.Click += (_, _) => clicked.TrySetResult();
                    ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(restart).GetPattern(PatternInterface.Invoke)).Invoke();
                    await clicked.Task.WaitAsync(TimeSpan.FromSeconds(2)); await Until(() => restart.IsEnabled);
                    Check(fixture.Wire.Watches.Count == 0 && fixture.Wire.Starts.Count == 0, $"Retained {surface} menu prepared playback for a departed owner.");
                    Program.Log($"PASS: retained {surface} restart menu does not prepare or start old movie playback.");
                    continue;
                }
                if (surface == "access-refresh") fixture.Client.InvalidateAccessContext();
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(restart!).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => fixture.Wire.Starts.Count == 1 && restart!.IsEnabled);
                var expectedQuery = surface == "unscoped" ? "" : "?library_id=73";
                Check(fixture.Wire.Watches.Single().Query == expectedQuery,
                    $"Actual {surface} restart menu lost library: expected '{expectedQuery}', actual '{fixture.Wire.Watches.Single().Query}'.");
                var start = fixture.Wire.Starts.Single();
                Check(start.GetProperty("file_id").GetString() == "42" && start.GetProperty("profile_id").GetString() == "fixture"
                    && start.GetProperty("start_position").GetDouble() == 0,
                    "Restart menu changed the normal authorized file/profile or resumed instead of starting at zero.");
                Check(fixture.Wire.Stages.Count == 0 && fixture.Wire.Suggestions == 0, "Restart menu unexpectedly changed room entry policy.");
                Program.Log($"PASS: actual {surface} PosterCard More/restart popup preserves library query '{expectedQuery}', authorized file/profile and explicit zero restart.");
            }
            finally
            {
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot)) popup.IsOpen = false;
                typeof(PlayerService).GetField("_mpv", Private)!.SetValue(fixture.Player, null);
                parent.Children.Clear(); await Task.Delay(60);
            }
        }
    }
}
