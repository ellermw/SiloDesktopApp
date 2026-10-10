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
using SiloPlayer.Controls;
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
        var localSettings = new SettingsService(Path.Combine(Program.ResultDirectory, "person-settings"));
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
            [typeof(SettingsService)] = localSettings,
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
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_PERSON_LOADING") == "1")
            {
                var differences = new List<string>();
                foreach (var width in new[] { 1280, 900, 500 })
                {
                    var scale = owner.XamlRoot.RasterizationScale;
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
                    wire.PersonGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pendingReads = wire.PersonReads;
                    navigation.Navigate<PersonDetailPage>("loading-" + width);
                    await Until(() => wire.PersonReads > pendingReads && Page().ViewModel.IsLoading);
                    await Task.Delay(100); Page().UpdateLayout();
                    var skeleton = (StackPanel)Page().FindName("PersonSkeletonShell");
                    var boxes = Descendants<SkeletonBox>(skeleton).ToArray();
                    var photo = boxes.SingleOrDefault(box => box.Height == 270 || box.Height == 210);
                    var bio = boxes.SingleOrDefault(box => box.Height == 64);
                    if (boxes.Length != 5 || photo?.ActualWidth != (width < 640 ? 140 : 180) || photo?.CornerRadius.TopLeft != 16 || bio == null)
                        differences.Add($"{width}: pending person must render only five current hero placeholders with responsive portrait, name, two facts and biography.");
                    else
                    {
                        var photoPoint = photo.TransformToVisual(skeleton).TransformPoint(new());
                        var bioPoint = bio.TransformToVisual(skeleton).TransformPoint(new());
                        if (width < 1024 ? bioPoint.Y <= photoPoint.Y + photo.ActualHeight : bioPoint.X < photoPoint.X + photo.ActualWidth + 31)
                            differences.Add($"{width}: pending hero must use the same stacked/desktop layout as the WebUI.");
                    }
                    wire.PersonGate.TrySetResult(true); wire.PersonGate = null;
                    await Until(() => !Page().ViewModel.IsLoading);
                }
                wire.FilmographyGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var filmReads = wire.FilmographyReads;
                navigation.Navigate<PersonDetailPage>("filmography-pending");
                await Until(() => wire.FilmographyReads > filmReads);
                await Task.Delay(150);
                if (Page().ViewModel.Person?.Id != "filmography-pending" || Page().ViewModel.IsLoading || ((ScrollViewer)Page().FindName("ContentScroll")).Visibility != Visibility.Visible)
                    differences.Add("Independent filmography latency hides the already available person header.");
                wire.FilmographyGate.TrySetResult(true); wire.FilmographyGate = null;
                await Until(() => !Page().ViewModel.IsLoading && Page().ViewModel.Filmography.Count == 1);
                if (differences.Count > 0) throw new InvalidOperationException(string.Join("\n", differences));
                Program.Log("PASS: PERSON_LOADING_COMPLETED responsive pending hero and visible person before independent filmography completes.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_PERSON_LAYOUT") == "1")
            {
                var differences = new List<string>();
                foreach (var width in new[] { 1280, 900, 500 })
                {
                    var scale = owner.XamlRoot.RasterizationScale;
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
                    var preferences = localSettings.Load(); preferences.UiDateFormat = width == 900 ? "YYYY-MM-DD" : width == 500 ? "DD/MM/YYYY" : "MM/DD/YYYY"; localSettings.Save(preferences);
                    var person = await Open("layout-" + width); person.UpdateLayout();
                    var photo = (Border)person.FindName("PersonPhotoBorder");
                    var filter = (Button)person.FindName("FilterAllButton");
                    var personRefresh = (Button)person.FindName("RefreshPersonButton");
                    var badge = (Border)person.FindName("BirthDateBadge"); var label = (TextBlock)badge.Child;
                    var bio = (TextBlock)person.FindName("BioText");
                    var viewport = (FrameworkElement)((ScrollViewer)person.FindName("ContentScroll")).Content;
                    var photoPoint = photo.TransformToVisual(viewport).TransformPoint(new());
                    var name = (TextBlock)person.FindName("PersonName");
                    var bioPoint = bio.TransformToVisual(viewport).TransformPoint(new());
                    Program.Log($"TRACE person rhythm {width}: name={name.ActualHeight}, badge={badge.ActualHeight}, bioY={bioPoint.Y}, photoY={photoPoint.Y}");
                    if (width >= 1024 && Math.Abs(bioPoint.Y - photoPoint.Y - 94) > 1)
                        differences.Add($"{width}: biography starts{bioPoint.Y-photoPoint.Y}px below the portrait; current WebUI requires94px after its36px name,26px facts and spacing.");
                    var expectedHeroTop = width >= 1024 ? 120 : width >= 640 ? 104 : 88;
                    if (Math.Abs(photoPoint.Y - expectedHeroTop) > 1)
                        differences.Add($"{width}: person hero starts at {photoPoint.Y}, expected {expectedHeroTop} with outer page padding.");
                    if (bio.Text.Contains('\n') || bio.Text.Contains("  ", StringComparison.Ordinal) || !bio.Text.Contains("Second paragraph", StringComparison.Ordinal))
                        differences.Add($"{width}: biography must follow WebUI normal whitespace without extra paragraph gaps or lost text.");
                    if (Math.Abs(badge.ActualHeight - 26) > 1)
                        differences.Add($"{width}: person metadata line box gives {badge.ActualHeight}px badge instead of26px.");
                    if (photo.CornerRadius.TopLeft != 16 || photo.ActualWidth != (width < 640 ? 140 : 180))
                        differences.Add($"{width}: current person portrait requires16px corners and responsive140/180 width.");
                    if (filter.CornerRadius.TopLeft != 10 || filter.FontWeight.Weight != 500 || ((StackPanel)filter.Parent).Spacing != 6)
                        differences.Add($"{width}: filmography tabs require10px corners, Medium weight and6px gaps.");
                    if (personRefresh.FontSize != 14 || personRefresh.Padding.Left != 10 || Descendants<TextBlock>(personRefresh).Any(text => text.FontSize != 14))
                        differences.Add($"{width}: current small icon action requires14px labels and10px horizontal padding.");
                    if (badge.CornerRadius.TopLeft < 13 || badge.Padding.Top != 4 || Math.Abs(badge.Padding.Left - 9.6) > .01 || badge.BorderThickness.Top != 1 ||
                        Math.Abs(label.FontSize - 11.2) > .01 || label.LineHeight != 16 || label.FontWeight.Weight != 600 || label.CharacterSpacing != 40 || label.Text != label.Text.ToUpperInvariant())
                        differences.Add($"{width}: actual person facts do not use the shared uppercase metadata-badge typography/pill/padding/border.");
                    var expectedDate = width == 900 ? "BORN 1970-01-02" : width == 500 ? "BORN 2 JAN 1970" : "BORN JAN 2, 1970";
                    if (label.Text != expectedDate) differences.Add($"{width}: visible birth facts ignore preference-aware medium date formatting: {label.Text}.");
                    wire.EmptyFilmography = true;
                    var emptyPerson = await Open("empty-" + width); await Until(() => !emptyPerson.ViewModel.IsLoadingFilmography);
                    var empty = (TextBlock)emptyPerson.FindName("FilmographyEmptyText");
                    if (empty.Visibility != Visibility.Visible || empty.FontSize != 16 || empty.Margin.Top != 48 || empty.Margin.Bottom != 48)
                        differences.Add($"{width}: empty filmography differs from current16px text/48px vertical padding.");
                    wire.EmptyFilmography = false;
                    await MediaParityNativeFixture.CaptureAsync(owner, $"person-source-layout-{width}.png");
                }
                if (differences.Count > 0) throw new InvalidOperationException(string.Join("\n", differences));
                Program.Log("PASS: PERSON_SOURCE_LAYOUT_COMPLETED actual responsive portrait, metadata badges, icon actions and filmography tabs.");
                return;
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
            wire.PersonGate?.TrySetResult(true); wire.FilmographyGate?.TrySetResult(true);
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
        internal TaskCompletionSource<bool>? PersonGate, FilmographyGate;
        internal int FilmographyReads;
        internal bool EmptyFilmography;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "browse-person.invalid") throw new InvalidOperationException("Actual Person fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath; object body = new { items = Array.Empty<object>(), page = new { has_more = false } }; var status = HttpStatusCode.OK;
            if (path == "/artwork.png") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j8uoAAAAASUVORK5CYII=")) };
            if (path.StartsWith("/api/v2/catalog/people/")) { PersonReads++; if (PersonGate != null) await PersonGate.Task.WaitAsync(ct); status = PersonStatus; body = Person(path.Split('/').Last()); }
            if (path.StartsWith("/api/v2/admin/people/") && path.EndsWith("/refresh")) { Refreshes++; return await RefreshGate!.Task.WaitAsync(ct); }
            if (path.StartsWith("/api/v2/admin/people/") && request.Method == HttpMethod.Patch)
            { Writes++; using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Name = json.RootElement.GetProperty("name").GetString()!; body = Person(path.Split('/').Last()); }
            if (path == "/api/v2/catalog" && request.RequestUri.Query.Contains("source=person"))
            { FilmographyReads++; if (FilmographyGate != null) await FilmographyGate.Task.WaitAsync(ct); status = FilmographyStatus; body = new { items = EmptyFilmography ? [] : new[] { new { content_id = "film-one", type = "movie", title = "Recovered Film" } }, total = EmptyFilmography ? 0 : 1, page = new { has_more = false } }; }
            if (status != HttpStatusCode.OK) body = new { message = "isolated unavailable response" };
            return new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        }
        private object Person(string id) => new { id, name = Name, bio = id.StartsWith("layout-") ? "A cached biography.\n\nSecond paragraph  remains readable." : "A cached biography for actual admin actions.", birth_date = "1970-01-02", photo_url = "https://browse-person.invalid/artwork.png", tmdb_id = "7" };
    }
}
