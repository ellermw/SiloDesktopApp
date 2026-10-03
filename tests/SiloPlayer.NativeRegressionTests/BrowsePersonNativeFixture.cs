using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
using SiloPlayer.Views.Dialogs;

internal static class BrowsePersonNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://browse-person.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "admin" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture", IsPrimary = true });
        var people = new PeopleApi(client); var catalog = new CatalogApi(client); var settings = new SettingsApi(client);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "person-images"));
        var frame = new Frame(); var owner = new Grid(); owner.Children.Add(frame);
        var navigation = new NavigationService { Frame = frame };
        var window = new Window { Content = owner }; ContentDialog? active = null;
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(AuthService)] = auth, [typeof(SiloApiClient)] = client, [typeof(PeopleApi)] = people,
            [typeof(CatalogApi)] = catalog, [typeof(HttpClient)] = http, [typeof(ImageService)] = images,
            [typeof(UICustomizationService)] = new UICustomizationService(settings), [typeof(NavigationService)] = navigation,
            [typeof(ToastService)] = new ToastService(),
        }, () => new PersonDetailViewModel(people, catalog, client)));
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280, 900)); window.AppWindow.Show(false); await Task.Delay(100);
            PersonDetailPage Page() => (PersonDetailPage)frame.Content;
            async Task<PersonDetailPage> Open(string id)
            {
                navigation.Navigate<PersonDetailPage>(id);
                await Until(() => frame.Content is PersonDetailPage page && !page.ViewModel.IsLoading && (page.ViewModel.Person != null || page.ViewModel.ErrorMessage != null));
                await Task.Delay(80); return Page();
            }
            wire.PersonStatus = HttpStatusCode.NotFound;
            var missing = await Open("missing");
            Check(((StackPanel)missing.FindName("PersonUnavailableState")).Visibility == Visibility.Visible && ((TextBlock)missing.FindName("PersonUnavailableTitle")).Text == "This person isn't available" && ((Button)missing.FindName("PersonRetryButton")).Visibility == Visibility.Collapsed,
                "Actual person404 must show its unavailable state without a misleading filmography failure.");
            wire.PersonStatus = HttpStatusCode.ServiceUnavailable;
            var retry = await Open("person-retry");
            var retryButton = (Button)retry.FindName("PersonRetryButton");
            Check(retryButton.Visibility == Visibility.Visible && retryButton.IsEnabled, "Actual person503 did not expose an enabled Retry.");
            wire.PersonStatus = HttpStatusCode.OK; Invoke(retryButton);
            await Until(() => retry.ViewModel.Person?.Id == "person-retry" && retry.ViewModel.ErrorMessage == null && !retry.ViewModel.IsLoading && ((ScrollViewer)retry.FindName("ContentScroll")).Visibility == Visibility.Visible);
            Check(((ScrollViewer)retry.FindName("ContentScroll")).Visibility == Visibility.Visible, "Actual person Retry did not restore visible metadata.");
            wire.FilmographyStatus = HttpStatusCode.NotFound;
            var film = await Open("film-retry");
            Check(film.ViewModel.Person?.Name == wire.Name && film.ViewModel.ErrorMessage?.Contains("filmography") == true && ((ScrollViewer)film.FindName("ContentScroll")).Visibility == Visibility.Visible,
                "Filmography-only404 hid the successfully read person.");
            var filmRetry = Descendants<Button>((ScrollViewer)film.FindName("ContentScroll")).Single(button => button.Content?.ToString() == "Retry");
            wire.FilmographyStatus = HttpStatusCode.OK; Invoke(filmRetry);
            await Until(() => film.ViewModel.ErrorMessage == null && film.ViewModel.Filmography.Count == 1 && !film.ViewModel.IsLoading && ((ScrollViewer)film.FindName("ContentScroll")).Visibility == Visibility.Visible);
            Program.Log("PASS: actual person404 unavailable/person503 Retry/filmography-only404 Retry retaining visible metadata.");

            wire.RefreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var refresh = (Button)film.FindName("RefreshPersonButton"); Invoke(refresh);
            await Until(() => wire.Refreshes == 1 && !refresh.IsEnabled);
            Check(film.ViewModel.Person?.Name == wire.Name && ((TextBlock)film.FindName("BioText")).Text == "A cached biography for actual admin actions." && !film.ViewModel.IsLoading,
                "Pending admin refresh discarded its cached biography or blocked the whole page.");
            wire.RefreshGate.SetResult(new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"fixture refresh unavailable\"}") });
            await Until(() => refresh.IsEnabled && !film.ViewModel.IsRefreshing);
            Check(film.ViewModel.Person?.Name == wire.Name, "Failed admin refresh replaced cached metadata.");
            var reads = wire.PersonReads;
            Invoke((Button)film.FindName("EditPersonButton"));
            await Until(() => Dialogs(owner.XamlRoot).OfType<EditPersonDialog>().Any());
            var dialog = Dialogs(owner.XamlRoot).OfType<EditPersonDialog>().Single(); active = dialog;
            var inputs = (Dictionary<string, TextBox>)typeof(EditPersonDialog).GetField("_inputs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
            inputs["name"].Text = "Saved through the actual person page";
            Invoke(Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton"));
            await Until(() => dialog.HasSaved && film.ViewModel.Person?.Name == wire.Name && ((TextBlock)film.FindName("PersonName")).Text == wire.Name);
            active = null;
            Check(wire.Writes == 1 && wire.PersonReads == reads + 1, $"Actual person Save must issue one write and one reread; writes={wire.Writes}, read delta={wire.PersonReads - reads}.");
            Program.Log("PASS: acting-admin mounted Person pending/failure retains cached bio; actual Edit Person Save rereads once and updates visible title.");
        }
        finally
        {
            active?.Hide();
            if (frame.Content is PersonDetailPage page) page.ViewModel.Cancel();
            frame.Content = null; await Task.Delay(100);
            field.SetValue(null, previous); window.Close();
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var attempt = 0; attempt < 120 && !ready(); attempt++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Actual Person interaction did not settle."); }
    private static IEnumerable<ContentDialog> Dialogs(XamlRoot root)
    {
        var found = new HashSet<ContentDialog>();
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            foreach (var dialog in new[] { popup.Child }.Concat(Descendants<DependencyObject>(popup.Child)).OfType<ContentDialog>()) found.Add(dialog);
            for (DependencyObject? ancestor = popup.Child; ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor)) if (ancestor is ContentDialog dialog) found.Add(dialog);
        }
        return found;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T typed) yield return typed; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> overrides, Func<PersonDetailViewModel> person) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(PersonDetailViewModel) ? person() : overrides.TryGetValue(type, out var value) ? value : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal HttpStatusCode PersonStatus = HttpStatusCode.OK, FilmographyStatus = HttpStatusCode.OK;
        internal int PersonReads, Refreshes, Writes; internal string Name = "Native Person";
        internal TaskCompletionSource<HttpResponseMessage>? RefreshGate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "browse-person.invalid") throw new InvalidOperationException("Actual Person fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath; object body = new { items = Array.Empty<object>(), page = new { has_more = false } }; var status = HttpStatusCode.OK;
            if (path == "/artwork.png") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j8uoAAAAASUVORK5CYII=")) };
            if (path.StartsWith("/api/v2/catalog/people/")) { PersonReads++; status = PersonStatus; body = Person(path.Split('/').Last()); }
            if (path.StartsWith("/api/v2/admin/people/") && path.EndsWith("/refresh")) { Refreshes++; return await RefreshGate!.Task.WaitAsync(ct); }
            if (path.StartsWith("/api/v2/admin/people/") && request.Method == HttpMethod.Patch)
            { Writes++; using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Name = json.RootElement.GetProperty("name").GetString()!; body = Person(path.Split('/').Last()); }
            if (path == "/api/v2/catalog" && request.RequestUri.Query.Contains("source=person"))
            { status = FilmographyStatus; body = new { items = new[] { new { content_id = "film-one", type = "movie", title = "Recovered Film" } }, total = 1, page = new { has_more = false } }; }
            if (status != HttpStatusCode.OK) body = new { message = "isolated unavailable response" };
            return new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        }
        private object Person(string id) => new { id, name = Name, bio = "A cached biography for actual admin actions.", birth_date = "1970-01-02", photo_url = "https://browse-person.invalid/artwork.png", tmdb_id = "7" };
    }
}
