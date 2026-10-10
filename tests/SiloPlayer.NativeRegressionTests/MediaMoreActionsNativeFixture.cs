using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class MediaMoreActionsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!; var original = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = new SiloApiClient(http); client.SetBaseUrl("https://media-more-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetCurrentUser(new UserInfo { Id = "42", Role = "admin" });
        var catalog = new CatalogApi(client); var cache = new ItemDetailPrefetchCache(catalog, auth); var fixture = new Services(original, client, auth, catalog, cache); slot.SetValue(null, fixture);
        var page = new ItemDetailPage { Width = 900, Height = 720 }; using var lifetime = new CancellationTokenSource();
        try
        {
            parent.Children.Add(page); page.ViewModel.Item = new MediaItemDetail { ContentId = "fixture-movie", Title = "Fixture movie", Type = "movie", Versions = [new FileVersion { FileId = 1 }] };
            Set(page, "_navigationCts", lifetime); Set(page, "_currentContentId", "fixture-movie"); Set(page, "_selectedVersion", page.ViewModel.Item.Versions[0]); page.UpdateLayout(); await Task.Delay(60);
            Call(page, "BuildMoreFlyout");
            var menu = (MenuFlyout)page.FindName("MoreFlyout"); var labels = menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToArray();
            foreach (var label in new[] { "View Play History", "Edit Markers", "Add Subtitles" })
                if (!labels.Contains(label)) throw new InvalidOperationException("Current detail overflow is missing " + label);
            if (Array.IndexOf(labels, "Edit Metadata") < Array.IndexOf(labels, "Refresh Metadata")) throw new InvalidOperationException("Metadata actions precede ordinary overflow actions.");
            page.ViewModel.Item.Versions.Add(new FileVersion { FileId = 2 }); Set(page, "_mediaActionCapabilitiesId", "fixture-movie"); Set(page, "_offerSeekPreviews", true); Set(page, "_offerMarkerDetection", true); Call(page, "BuildMoreFlyout"); labels = menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToArray();
            foreach (var label in new[] { "Split Versions", "Seek Previews", "Re-detect Credits" }) if (!labels.Contains(label)) throw new InvalidOperationException("Advertised movie action missing " + label);
            foreach (var (label, mark) in new[] { ("Edit Markers", "tags"), ("Seek Previews", "gallery-horizontal") })
            {
                var icon = (ImageIcon)menu.Items.OfType<MenuFlyoutItem>().Single(entry => entry.Text == label).Icon;
                if (((Microsoft.UI.Xaml.Media.Imaging.SvgImageSource)icon.Source).UriSource.AbsolutePath != "/Assets/Icons/" + mark + ".svg")
                    throw new InvalidOperationException("Current detail action icon differs for " + label);
            }
            page.ViewModel.Item.Type = "episode"; Call(page, "BuildMoreFlyout"); labels = menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToArray();
            if (labels.Contains("Split Versions") || labels.Contains("Add to Collection") || !labels.Contains("Re-detect Markers")) throw new InvalidOperationException("Episode action policy differs from current source.");
            Set(page, "_offerMarkerDetection", false); Call(page, "BuildMoreFlyout"); labels = menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToArray(); if (!labels.Contains("Re-detect Intro Markers")) throw new InvalidOperationException("Legacy node lacks the episode-intro action.");
            var staleAdminAction = menu.Items.OfType<MenuFlyoutItem>().Single(entry => entry.Text == "Re-detect Intro Markers");
            auth.SetCurrentUser(new UserInfo { Id = "42", Role = "user" });
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(staleAdminAction).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            if (wire.Writes != 0) throw new InvalidOperationException("An already-created administrator action wrote after role revocation.");
            page.ViewModel.Item.Type = "movie";
            auth.SetCurrentUser(new UserInfo { Id = "42", Role = "user" }); Call(page, "BuildMoreFlyout"); labels = menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToArray();
            if (labels.Any(label => label is "Media Info" or "View Play History" or "Edit Markers" or "Edit Metadata")) throw new InvalidOperationException("Regular viewer sees privileged current detail actions.");
            auth.SetCurrentUser(new UserInfo { Id = "42", Role = "user", Permissions = ["marker_edit"] }); Call(page, "BuildMoreFlyout"); labels = menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToArray();
            if (!labels.Contains("Edit Markers") || labels.Contains("View Play History")) throw new InvalidOperationException("Marker permission is conflated with administrator access.");
            Program.Log("PASS: current detail menu exposes assigned marker/history actions in source order and excludes them for unprivileged viewers.");
        }
        finally { parent.Children.Remove(page); slot.SetValue(null, original); }
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private sealed class Services(IServiceProvider original, SiloApiClient client, AuthService auth, CatalogApi catalog, ItemDetailPrefetchCache cache) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(SiloApiClient) ? client : type == typeof(AuthService) ? auth : type == typeof(CatalogApi) ? catalog : type == typeof(ItemDetailViewModel) ? new ItemDetailViewModel(catalog, cache) : type == typeof(UICustomizationService) ? new UICustomizationService(new SettingsApi(client)) : original.GetService(type);
    }
    private sealed class Wire : HttpMessageHandler
    {
        public int Writes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { if (request.Method != HttpMethod.Get) Writes++; return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") }); }
    }
}
