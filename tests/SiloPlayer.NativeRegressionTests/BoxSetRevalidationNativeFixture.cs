using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static partial class BoxSetNativeFixture
{
    private static async Task VerifyRevalidationReceiptAsync(StackPanel parent, CatalogApi catalog, SiloApiClient api, Wire wire)
    {
        foreach (var change in new[] { "refresh", "access", "language" })
        {
            wire.Hold = false; wire.Played = 1;
            var page = new BoxSetPage(catalog, api) { Width = 960, Height = 700 };
            parent.Children.Add(page);
            await page.ViewModel.LoadAsync("7", "12"); await DrainAsync(parent);
            var detail = page.ViewModel.Detail;
            var cards = Descendants(page).OfType<PosterCard>().ToArray();
            api.InvalidateAccessContext(); wire.Hold = true;
            wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var oldRelease = wire.Release; var calls = 0;
            var play = page.ViewModel.PlayAsync((_, _, _, _) => { calls++; return Task.CompletedTask; });
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (change == "access") api.InvalidateAccessContext();
            if (change == "language") api.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
            wire.Played = 3;
            wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var successor = page.ViewModel.RefreshAsync();
            try
            {
                await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                oldRelease.TrySetResult();
                Exception? decline = null;
                try { await play.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (InvalidOperationException ex) { decline = ex; }
                Check(decline != null && calls == 0 && page.ViewModel.IsLoading && ReferenceEquals(detail, page.ViewModel.Detail),
                    "superseded Play borrowed its unfinished successor and started the retained old target: " + change);
                Check(Descendants(page).OfType<PosterCard>().SequenceEqual(cards), "held revalidation replaced mounted member controls");
                wire.Release.TrySetResult(); await successor.WaitAsync(TimeSpan.FromSeconds(2));
                await page.ViewModel.PlayAsync((id, library, owner, ct) =>
                {
                    calls++; Check(id == "first" && library == 12 && api.IsCurrentContext(owner) && !ct.IsCancellationRequested,
                        "retry did not use its actual successfully validated target/authority");
                    return Task.CompletedTask;
                });
                Check(calls == 1, "retry must execute once after the current read completes");
                Program.Log("PASS: mounted box-set Play requires its own completed revalidation across " + change + ".");
            }
            finally
            {
                oldRelease.TrySetResult(); wire.Release.TrySetResult(); await successor;
                parent.Children.Remove(page); page.ViewModel.Cancel();
            }
        }
    }

    private static async Task VerifyMembershipResponseAsync(StackPanel parent)
    {
        using var wire = new MembershipResponseWire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://membership-native.invalid"); client.SetProfile("fixture");
        var vm = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        vm.Item = new MediaItemDetail { ContentId = "film", Type = "movie", Title = "Requested film" };
        var services = (NavigationServices)SiloPlayer.App.Services;
        var previous = services.ItemDetail; services.ItemDetail = vm;
        var page = new ItemDetailPage { Width = 960, Height = 700 };
        // This isolated mounted page does not enter a Frame route. Attach the
        // same production notification handler that OnNavigatedTo subscribes.
        var changed = typeof(ItemDetailPage).GetMethod("OnViewModelPropertyChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        System.ComponentModel.PropertyChangedEventHandler observe = (sender, args) => changed.Invoke(page, [sender, args]);
        vm.PropertyChanged += observe;
        parent.Children.Add(page);
        try
        {
            foreach (var (content, type) in new[] { ("other", "movie"), ("film", "episode"), ("film", "box_set") })
            {
                wire.ContentId = "film"; wire.Type = "movie";
                await vm.RefreshBoxSetAsync(); await DrainAsync(parent);
                var section = (FrameworkElement)page.FindName("BoxSetSection");
                Check(vm.BoxSet != null && section.Visibility == Visibility.Visible, "valid requested-movie membership must mount");
                wire.ContentId = content; wire.Type = type;
                await vm.RefreshBoxSetAsync(); await DrainAsync(parent);
                Check(vm.BoxSet == null && section.Visibility == Visibility.Collapsed &&
                    ((Panel)page.FindName("BoxSetMovies")).Children.Count == 0 && vm.Item.ContentId == "film" && vm.ErrorMessage == null,
                    "successful unrelated/non-movie membership published a wrong visible row");
            }
            Program.Log("PASS: mounted movie membership rejects unrelated and non-movie responses while preserving the movie.");
        }
        finally { vm.PropertyChanged -= observe; vm.CancelPendingLoads(); parent.Children.Remove(page); services.ItemDetail = previous; }
    }

    private sealed class MembershipResponseWire : HttpMessageHandler
    {
        internal string ContentId = "film", Type = "movie";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object body = request.RequestUri!.AbsolutePath switch
            {
                var path when path.EndsWith("/capabilities") => new { box_sets = true },
                var path when path.EndsWith("/libraries") => new { items = new[] { new { id = 12, box_sets_enabled = true } } },
                _ => new { box_set = new { id = "7", library_id = "12", title = "Membership", movie_count = 1,
                    items = new[] { new { content_id = ContentId, type = Type, title = "Response movie" } } } }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body)) });
        }
    }
}
