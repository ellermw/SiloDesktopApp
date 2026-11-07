using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static partial class BoxSetNativeFixture
{
    private static async Task VerifyInitialMovieMembershipProgressAsync(StackPanel parent)
    {
        var failures = new List<string>();
        foreach (var initialFinishesFirst in new[] { true, false })
        {
            try { await CheckInitialMovieMembershipProgressAsync(parent, initialFinishesFirst); }
            catch (Exception ex) { failures.Add(initialFinishesFirst + ": " + ex.Message); Program.Log("CASE FAIL: initial movie membership progress " + initialFinishesFirst + ": " + ex); }
        }
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
    }

    private static async Task CheckInitialMovieMembershipProgressAsync(StackPanel parent, bool initialFinishesFirst)
    {
        using var wire = new InitialProgressWire(); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://initial-membership-native.invalid"); api.SetProfile("fixture");
        var vm = new ItemDetailViewModel(new CatalogApi(api), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        typeof(ItemDetailViewModel).GetProperty(nameof(vm.LibraryId))!.SetValue(vm, 12);
        vm.Item = new MediaItemDetail { ContentId = "film", Type = "movie", Title = "Current film" };
        var services = (NavigationServices)SiloPlayer.App.Services;
        var previous = services.ItemDetail; services.ItemDetail = vm;
        var page = new ItemDetailPage { Width = 960, Height = 700 };
        var changed = typeof(ItemDetailPage).GetMethod("OnViewModelPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        System.ComponentModel.PropertyChangedEventHandler observe = (sender, args) => changed.Invoke(page, [sender, args]);
        vm.PropertyChanged += observe;
        parent.Children.Add(page);
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? initial = null;
        try
        {
            initial = vm.RefreshBoxSetAsync();
            await wire.InitialStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(vm.BoxSet == null && ((FrameworkElement)page.FindName("BoxSetSection")).Visibility == Visibility.Collapsed,
                "Initial-progress fixture did not reach its unknown mounted membership boundary.");
            vm.Receive(new PlaybackProgressUpdated("sibling", 100, 100, true) { Persisted = saved.Task });
            PosterCard[]? originalCards = null;
            if (initialFinishesFirst)
            {
                wire.InitialRelease.TrySetResult(); await initial; await DrainAsync(parent);
                originalCards = Descendants((DependencyObject)page.FindName("BoxSetMovies")).OfType<PosterCard>().ToArray();
                Check(vm.BoxSet?.PlayedCount == 0 && vm.BoxSet.PlayTarget?.ContentId == "sibling" && originalCards.Length == 2 &&
                    originalCards.Single(card => card.MediaItem?.ContentId == "sibling").MediaItem!.UserState?.Played == false,
                    "Initial-progress fixture did not mount its actual pre-save sibling and target.");
            }
            Check(wire.Reads == 1, "A sibling progress event refreshed membership before its final save.");
            wire.Played = true; saved.TrySetResult();
            await Task.WhenAny(wire.PostSaveRead.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            Check(wire.Reads == 2, $"Unknown initial membership discarded sibling progress: expected2 actual{wire.Reads}, initialFirst={initialFinishesFirst}.");
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (vm.BoxSet?.PlayedCount != 1 && DateTime.UtcNow < deadline) await Task.Delay(20);
            Check(vm.BoxSet?.PlayedCount == 1, "The post-save membership did not publish inside its mounted callback bound.");
            await DrainAsync(parent);
            var currentCards = Descendants((DependencyObject)page.FindName("BoxSetMovies")).OfType<PosterCard>().ToArray();
            Check(vm.BoxSet!.PlayTarget?.ContentId == "film" && currentCards.Length == 2 &&
                currentCards.Single(card => card.MediaItem?.ContentId == "sibling").MediaItem!.UserState?.Played == true &&
                ((FrameworkElement)page.FindName("BoxSetSection")).Visibility == Visibility.Visible && vm.Item.ContentId == "film",
                "The actual membership controls did not apply the saved sibling state/current target.");
            if (originalCards != null) Check(currentCards.SequenceEqual(originalCards), "Post-save membership state replaced stable mounted movie cards.");
            wire.InitialRelease.TrySetResult(); await initial; await DrainAsync(parent);
            Check(vm.BoxSet.PlayedCount == 1 && vm.BoxSet.PlayTarget?.ContentId == "film" && wire.Reads == 2,
                "The late initial response overwrote the current post-save membership.");
            Program.Log("PASS: actual mounted movie membership retains initial sibling progress with initialFirst=" + initialFinishesFirst + ".");
        }
        finally
        {
            saved.TrySetResult(); wire.InitialRelease.TrySetResult();
            if (initial != null) await initial;
            vm.PropertyChanged -= observe; vm.CancelPendingLoads(); parent.Children.Remove(page); services.ItemDetail = previous;
        }
    }

    private sealed class InitialProgressWire : HttpMessageHandler
    {
        internal readonly TaskCompletionSource InitialStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource InitialRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource PostSaveRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Played;
        internal int Reads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/capabilities")) return Json(new { box_sets = true });
            if (path.EndsWith("/libraries")) return Json(new { items = new[] { new { id = 12, box_sets_enabled = true } } });
            if (!path.EndsWith("/box-set")) throw new InvalidOperationException("Unexpected initial-membership fixture route.");
            var read = Interlocked.Increment(ref Reads);
            var response = Json(new { box_set = new {
                id = "7", library_id = "12", title = "Fixture set", movie_count = 2, played_count = Played ? 1 : 0,
                items = new[] {
                    new { content_id = "sibling", type = "movie", title = "Sibling film", user_state = new { played = Played } },
                    new { content_id = "film", type = "movie", title = "Current film", user_state = new { played = false } },
                },
            } });
            if (read == 1) { InitialStarted.TrySetResult(); await InitialRelease.Task; }
            else PostSaveRead.TrySetResult();
            return response;
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
