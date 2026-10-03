using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class BrowseSearchRecoveryNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://search-recovery.invalid");
        var catalog = new CatalogApi(client); var people = new PeopleApi(client);
        var requests = new RequestsApi(client); var settings = new SettingsApi(client);
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("search-recovery", profile: new() { Id = "search-recovery", Name = "Fixture" });
        var model = new SearchViewModel(catalog, people, requests, settings);
        var navigation = new NavigationService();
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth,
            [typeof(CatalogApi)] = catalog, [typeof(PeopleApi)] = people,
            [typeof(RequestsApi)] = requests, [typeof(SettingsApi)] = settings,
            [typeof(SearchViewModel)] = model, [typeof(NavigationService)] = navigation,
            [typeof(UICustomizationService)] = new UICustomizationService(settings),
        }));
        var frame = new Frame { Width = 460, Height = 900 };
        parent.Children.Add(frame); navigation.Frame = frame;
        try
        {
            navigation.Navigate<SearchPage>(); await Task.Delay(100);
            var page = (SearchPage)frame.Content;
            var initialization = typeof(SearchPage).GetField("_initializationTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page) as Task;
            if (initialization != null) await initialization;
            model.Query = "Recover Person"; await model.SearchCommand.ExecuteAsync(null);
            await Until(() => model.Results.Count == 1 && model.PeopleError != null && ((FrameworkElement)page.FindName("PeopleSection")).Visibility == Visibility.Visible);
            page.UpdateLayout(); await Task.Delay(80);
            var retained = model.Results.Single(); var catalogReads = wire.CatalogReads;
            var retry = Descendants<Button>(page).Single(button => button.Content?.ToString() == "Retry people");
            if (!retry.IsEnabled || retry.ActualHeight == 0) throw new InvalidOperationException("Failed independent people lookup did not expose an actual rendered Retry people control.");
            wire.FailPeople = false;
            ((IInvokeProvider)new ButtonAutomationPeer(retry).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => !model.IsPeopleLoading && model.PeopleError == null && model.PeopleResults.Count == 1);
            await Task.Delay(80);
            if (wire.CatalogReads != catalogReads || model.Results.Count != 1 || !ReferenceEquals(model.Results[0], retained) || model.PeopleResults[0].Id != "recovered-person")
                throw new InvalidOperationException("Actual people retry reloaded/discarded catalog results or did not publish the independently recovered person.");
            Program.Log("PASS: actual rendered Search people503→Retry invokes only the scoped people read while retaining successful catalog results and their object identity.");
        }
        finally
        {
            model.CancelPendingSearch(); frame.Content = null; navigation.Frame = null;
            await Task.Delay(100); parent.Children.Remove(frame); field.SetValue(null, previous);
        }
    }
    private static async Task Until(Func<bool> ready) { for (var attempt = 0; attempt < 120 && !ready(); attempt++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Actual Search people recovery did not settle."); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T typed) yield return typed; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values) : IServiceProvider
    { public object? GetService(Type type) => values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool FailPeople = true; internal int CatalogReads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "search-recovery.invalid") throw new InvalidOperationException("Search recovery fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath; var status = HttpStatusCode.OK;
            var body = "{\"items\":[],\"page\":{\"has_more\":false}}";
            if (path == "/api/v2/catalog/search/capabilities") body = "{\"people_media_scope\":true}";
            if (path == "/api/v2/requests/status") body = "{\"requests_enabled\":false}";
            if (path == "/api/v2/catalog") { CatalogReads++; body = "{\"items\":[{\"content_id\":\"retained-movie\",\"type\":\"movie\",\"title\":\"Retained result\"}],\"total\":1,\"page\":{\"has_more\":false}}"; }
            if (path == "/api/v2/catalog/people")
            {
                status = FailPeople ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
                body = FailPeople ? "{\"message\":\"isolated people failure\"}" : "{\"items\":[{\"id\":\"recovered-person\",\"name\":\"Recovered Person\"}]}";
            }
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
