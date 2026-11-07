using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Helpers;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static partial class EpisodeQuickPlayNativeFixture
{
    internal static async Task VerifyMenuAdmissionAsync(StackPanel parent, bool controls = false)
    {
        foreach (var boundary in controls ? new[] { "accepted-unload", "active-room", "ordinary-queue", "ordinary-close" }
            : new[] { "authority", "request", "watch", "settings", "capability", "access-watch" })
        {
            using var fixture = new Fixture(); EnsureNativeResolver();
            using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null");
            SetField(fixture.Player, "_mpv", mpv);
            var servicesField = typeof(SiloPlayer.App).GetField("_services", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var coordinator = new WatchTogetherCoordinator(fixture.Player);
            servicesField.SetValue(null, new Services(SiloPlayer.App.Services,
                new() { [typeof(WatchTogetherCoordinator)] = coordinator }, () => new(fixture.Catalog, fixture.Cache)));
            var authorityGate = (SemaphoreSlim)GetField(fixture.Player, "_authoritySwitchGate")!;
            var requestGate = (SemaphoreSlim)GetField(fixture.Player, "_playRequestGate")!;
            SemaphoreSlim? held = null; Task? accepted = null;
            try
            {
                if (boundary is "authority" or "ordinary-close") { await authorityGate.WaitAsync(); held = authorityGate; }
                if (boundary == "request") { await requestGate.WaitAsync(); held = requestGate; }
                if (boundary is "watch" or "access-watch" or "accepted-unload") fixture.Wire.HoldWatch = "restart-film";
                if (boundary == "settings") fixture.Wire.HoldSettings = true;
                if (boundary == "capability") fixture.Wire.HoldCapability = true;
                if (boundary == "ordinary-queue")
                {
                    fixture.Wire.HoldWatch = "accepted-scoped";
                    accepted = fixture.Player.PlayAsync("accepted-scoped", libraryId: 73, expectedIdentityContext: fixture.Client.CaptureIdentityContext());
                    await fixture.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                }
                WatchTogetherRoomViewModel? room = null;
                if (boundary == "active-room")
                {
                    room = new(new PlaybackApi(fixture.Client), fixture.Client) { RoomId = "menu-room", RoomToken = "synthetic-proof",
                        Room = new() { RoomId = "menu-room", Phase = "lobby", SelectedContentId = "other-film", SelfCanManageRoom = true, SelectionMode = "host_pick" } };
                    coordinator.SetActiveRoom(room);
                }
                var card = new PosterCard { MediaItem = new MediaItem { ContentId = "restart-film", Type = "movie", Title = "Restart film", UserState = new() { Played = true } },
                    Tag = new ItemDetailNavigationArgs("restart-film", 73) };
                parent.Children.Add(card); await Task.Delay(60); card.UpdateLayout();
                Invoke((Button)card.FindName("MoreButton"));
                MenuFlyoutItem? restart = null;
                await Until(() => (restart = VisualTreeHelper.GetOpenPopupsForXamlRoot(card.XamlRoot)
                    .SelectMany(popup => Descendants<MenuFlyoutItem>(popup.Child)).SingleOrDefault(x => x.Text == "Play from Beginning")) != null);
                var clicked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                restart!.Click += (_, _) => clicked.TrySetResult();
                var scopedIntent = GetField(fixture.Player, "_scopedViewerPlayCts") as CancellationTokenSource;
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(restart).GetPattern(PatternInterface.Invoke)).Invoke();
                await clicked.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (boundary == "request") await Until(() => GetField(fixture.Player, "_playRequestCts") != null);
                if (boundary is "watch" or "access-watch" or "accepted-unload") await fixture.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (boundary is "settings" or "capability") await fixture.Wire.BoundaryStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (boundary == "ordinary-queue")
                {
                    await Task.Delay(60);
                    Check(scopedIntent is { IsCancellationRequested: false } && ReferenceEquals(scopedIntent, GetField(fixture.Player, "_scopedViewerPlayCts"))
                        && fixture.Wire.Watches.Count == 1 && fixture.Wire.Starts.Count == 0 && !restart.IsEnabled,
                        "An ordinary menu restart superseded accepted scoped preparation instead of queuing.");
                }
                else if (boundary == "accepted-unload")
                { parent.Children.Remove(card); await Until(() => !card.IsLoaded); }
                else if (boundary == "access-watch") fixture.Client.InvalidateAccessContext();
                else if (!controls) fixture.Auth.SelectProfile("replacement", profile: new() { Id = "replacement", Name = "Replacement viewer" });
                if (boundary == "ordinary-close") await fixture.Player.CloseAsync();
                held?.Release(); held = null; fixture.Wire.ReleaseWatch.TrySetResult(); fixture.Wire.ReleaseBoundary.TrySetResult();
                if (accepted != null) await accepted.WaitAsync(TimeSpan.FromSeconds(3));
                await Until(() => restart.IsEnabled);
                if (!controls && boundary != "access-watch")
                {
                    Check(fixture.Wire.Starts.Count == 0 && fixture.Wire.Watches.Count == (boundary is "authority" or "request" ? 0 : 1),
                        $"The {boundary} menu continuation prepared or started playback for a departed viewer.");
                    lock (fixture.Wire.Requests) Check(fixture.Wire.Requests.All(x => x.Profile != "replacement"), "Menu preparation recaptured the replacement viewer.");
                    Check(!fixture.Player.IsLoading && !(bool)GetField(fixture.Player, "_switchingContent")!, "Retired menu request left loading state behind.");
                }
                else
                {
                    Check(fixture.Wire.Starts.Count == (boundary == "ordinary-queue" ? 2 : 1), $"Ordinary menu {boundary} did not retain its accepted request.");
                    Check(fixture.Wire.Watches.All(x => x.Query == "?library_id=73") && fixture.Wire.Starts.Last().GetProperty("start_position").GetDouble() == 0,
                        "Accepted menu request lost library or zero restart.");
                    if (room != null) Check(ReferenceEquals(coordinator.ActiveRoom, room) && fixture.Wire.Stages.Count == 0 && fixture.Wire.Suggestions == 0,
                        "Ordinary restart changed its existing active-room entry policy.");
                    if (boundary == "ordinary-queue") Check(fixture.Wire.Watches.Select(x => x.Segments.Last()).SequenceEqual(new[] { "accepted-scoped", "restart-film" }), "Ordinary restart reordered the accepted scoped action.");
                }
                Program.Log("PASS: actual menu admission/preparation control " + boundary + ".");
            }
            finally
            {
                held?.Release(); fixture.Wire.ReleaseWatch.TrySetResult(); fixture.Wire.ReleaseBoundary.TrySetResult();
                if (accepted != null) await accepted;
                coordinator.ClearActiveRoom(); SetField(fixture.Player, "_mpv", null);
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot)) popup.IsOpen = false;
                parent.Children.Clear(); await Task.Delay(60);
            }
        }
    }
}
