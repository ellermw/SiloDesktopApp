using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

internal static partial class TitleAllowancesNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://title-allowances.fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("fixture-access", "fixture-refresh", 3600);
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user", Username = "Fixture" });
        auth.SelectProfile("primary", "fixture-proof", new() { Id = "primary", Name = "Primary", IsPrimary = true, HasPin = true });
        var services = new Services(original, client, auth); slot.SetValue(null, services);
        try
        {
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "title-allowances-header")
            {
                await CollectionEditor(parent, wire); return;
            }
            await Detail(parent, wire, client, auth);
            await Collections(parent, wire, client, auth);
            await CollectionEditor(parent, wire);
            await ProfileAccess(parent, wire, client);
            await AccessPagingRaces(parent, wire, client, auth);
            Program.Log("PASS title allowances: actual detail/collection menu entrypoints, five states, immediate writes, copy, profile Access paging/search/removal, error/retry and retired ownership.");
        }
        finally { wire.Gate?.TrySetResult(); slot.SetValue(null, original); }
    }

    private static async Task Detail(StackPanel parent, Wire wire, SiloApiClient client, AuthService auth)
    {
        var page = new ItemDetailPage { Width = 900, Height = 720 };
        using var navigation = new CancellationTokenSource();
        parent.Children.Add(page);
        page.ViewModel.Item = new MediaItemDetail { ContentId = "movie:fixture", Type = "movie", Title = "Fixture movie" };
        Set(page, "_navigationCts", navigation); Set(page, "_currentContentId", "movie:fixture");
        try
        {
            await Task.Delay(80);
            await CallTask(page, "LoadTitleAllowanceActionsAsync", page.ViewModel.Item, navigation.Token);
            var menu = (MenuFlyout)page.FindName("MoreFlyout");
            var action = Actions(menu.Items).Single(item => item.Text.StartsWith("Allowed for "));
            Check(action.Text.Contains("Explicit") && action.Text.Contains("Collection"), "Detail lacks allowed names.");
            menu.ShowAt(page); await Task.Delay(50); Invoke(action);
            var dialog = await FindDialog<AllowTitleDialog>(parent);
            await Until(() => Descendants(dialog).OfType<CheckBox>().Count() == 4);
            var checks = Descendants(dialog).OfType<CheckBox>().ToDictionary(check => (string)check.Tag);
            Check(!checks.ContainsKey("within"), "within_limits was offered.");
            Check(checks["collection"].IsChecked == true && !checks["collection"].IsEnabled, "Collection allowance checkbox differs.");
            Check(checks["no-library"].IsChecked == false && !checks["no-library"].IsEnabled, "No-library-access checkbox differs.");
            Check(checks["explicit"].IsChecked == true && checks["explicit"].IsEnabled, "Explicit allowance cannot be revoked.");
            wire.FailMutation = true;
            Toggle(checks["above"]);
            await Until(() => Descendants(dialog).OfType<TextBlock>().Any(text => text.Text.StartsWith("Couldn’t save")));
            Check(Descendants(dialog).OfType<CheckBox>().Single(check => Equals(check.Tag, "above")).IsChecked == false, "Refused title write changed authoritative checkbox.");
            wire.FailMutation = false;
            Click(Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "Retry")));
            await Until(() => Descendants(dialog).OfType<TextBlock>().All(text => !text.Text.StartsWith("Couldn’t save")));
            checks = Descendants(dialog).OfType<CheckBox>().ToDictionary(check => (string)check.Tag);
            Toggle(checks["above"]);
            await Until(() => wire.Writes.Contains("PUT /api/v2/profiles/above/allowed-titles/movie%3Afixture"));
            await Until(() => Descendants(dialog).OfType<CheckBox>().Any(check => Equals(check.Tag, "above") && check.IsChecked == true));
            Toggle(Descendants(dialog).OfType<CheckBox>().Single(check => Equals(check.Tag, "explicit")));
            await Until(() => wire.Writes.Contains("DELETE /api/v2/profiles/explicit/allowed-titles/movie%3Afixture"));
            await Until(() => Descendants(dialog).OfType<CheckBox>().Any(check => Equals(check.Tag, "explicit") && check.IsChecked == false));
            await MediaParityNativeFixture.CaptureAsync(dialog, "title-allowances-dialog.png");
            // A visible retained checkbox must not mutate after access retirement.
            var retained = Descendants(dialog).OfType<CheckBox>().Single(check => Equals(check.Tag, "above"));
            var before = wire.Writes.Count; client.InvalidateAccessContext(); Toggle(retained); await Task.Delay(80);
            Check(wire.Writes.Count == before, "Retired dialog wrote under refreshed access.");
            dialog.Hide(); await Task.Delay(80);

            page.ViewModel.Item = new MediaItemDetail { ContentId = "series:fixture", Type = "series", Title = "Fixture series" };
            Set(page, "_currentContentId", "series:fixture");
            await CallTask(page, "LoadTitleAllowanceActionsAsync", page.ViewModel.Item, navigation.Token);
            menu.ShowAt(page); await Task.Delay(40); Invoke(Actions(menu.Items).Single(item => item.Text.StartsWith("Allowed for ")));
            var seriesDialog = await FindDialog<AllowTitleDialog>(parent);
            await Until(() => Descendants(seriesDialog).OfType<CheckBox>().Any());
            Toggle(Descendants(seriesDialog).OfType<CheckBox>().Single(check => Equals(check.Tag, "explicit")));
            await Until(() => wire.Writes.Contains("PUT /api/v2/profiles/explicit/allowed-titles/series%3Afixture"));
            seriesDialog.Hide(); await Task.Delay(60);
            page.ViewModel.Item = new MediaItemDetail { ContentId = "movie:fixture", Type = "movie", Title = "Fixture movie" };
            Set(page, "_currentContentId", "movie:fixture");

            wire.FailTitleStates = true;
            await CallTask(page, "LoadTitleAllowanceActionsAsync", page.ViewModel.Item, navigation.Token);
            var retry = Actions(menu.Items).Single(item => item.Text == "Retry title allowances");
            wire.FailTitleStates = false; Invoke(retry);
            await Until(() => Actions(menu.Items).Any(item => item.Text.StartsWith("Allowed for ")));
            wire.Capability = "unsupported";
            await CallTask(page, "LoadTitleAllowanceActionsAsync", page.ViewModel.Item, navigation.Token);
            Check(!Actions(menu.Items).Any(item => item.Text.StartsWith("Allow")), "Unsupported capability still offers allowances.");
            wire.Capability = "available";
            auth.SelectProfile("above", profile: new() { Id = "above", Name = "Limited", MaxContentRating = "PG" });
            var reads = wire.CapabilityReads;
            await CallTask(page, "LoadTitleAllowanceActionsAsync", page.ViewModel.Item, navigation.Token); Call(page, "BuildMoreFlyout");
            Check(!Actions(menu.Items).Any(item => item.Text.StartsWith("Allow")) && wire.CapabilityReads == reads, "Non-primary offered management/probed capability.");
            auth.SelectProfile("primary", "fixture-proof", new() { Id = "primary", Name = "Primary", IsPrimary = true });
            wire.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.HoldStates = true;
            var load = CallTask(page, "LoadTitleAllowanceActionsAsync", page.ViewModel.Item, navigation.Token);
            await Until(() => wire.HeldReads > 0);
            Set(page, "_currentContentId", "replacement"); wire.Gate.TrySetResult(); await load;
            Check(!Actions(menu.Items).Any(item => item.Text.StartsWith("Allow")), "Old navigation published title action.");
            wire.HoldStates = false; wire.Gate = null;
            Program.Log("PASS native movie detail: reached Allow dialog via More; PUT/DELETE, collection/no-library disabled, within hidden, access/nav guards, unavailable/nonprimary and retry.");
        }
        finally { wire.Gate?.TrySetResult(); parent.Children.Remove(page); }
    }

    private static async Task Collections(StackPanel parent, Wire wire, SiloApiClient client, AuthService auth)
    {
        var page = new CollectionsPage { Width = 900, Height = 700 }; parent.Children.Add(page); await Task.Delay(80);
        try
        {
            foreach (var kind in new[] { "manual", "smart", "synced" })
            {
                var collection = new Collection { Id = "c-" + kind, Name = "Family " + kind, CreatorProfileId = "primary", CollectionType = kind };
                var card = (Border)Call(page, "BuildCollectionCard", collection)!;
                parent.Children.Add(card); await Task.Delay(40);
                var menu = (MenuFlyout)card.ContextFlyout; menu.ShowAt(card);
                await Until(() => menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Allow for…" && item.Visibility == Visibility.Visible));
                Invoke(menu.Items.OfType<MenuFlyoutItem>().Single(item => item.Text == "Allow for…"));
                var dialog = await FindDialog<AllowCollectionDialog>(parent);
                if (kind == "manual")
                {
                    await Until(() => Descendants(dialog).OfType<CheckBox>().Any());
                    Toggle(Descendants(dialog).OfType<CheckBox>().Single(check => Equals(check.Tag, "above")));
                    await Until(() => wire.Writes.Contains("PUT /api/v2/profiles/above/allowed-collections/c-manual"));
                    await Until(() => Descendants(dialog).OfType<CheckBox>().Any(check => Equals(check.Tag, "above") && check.IsChecked == true));
                    Toggle(Descendants(dialog).OfType<CheckBox>().Single(check => Equals(check.Tag, "above")));
                    await Until(() => wire.Writes.Contains("DELETE /api/v2/profiles/above/allowed-collections/c-manual"));
                }
                else
                {
                    await Until(() => Descendants(dialog).OfType<Button>().Any(button => Equals(button.Content, "Allow current titles")));
                    if (kind == "smart")
                    {
                        wire.FailCopy = true; var attempts = wire.CopyBodies.Count;
                        Click(Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "Allow current titles") && Equals(button.Tag, "above")));
                        await Until(() => Descendants(dialog).OfType<TextBlock>().Any(text => text.Text.StartsWith("Couldn’t confirm")));
                        Check(wire.CopyBodies.Count == attempts + 1 && wire.Reads.Any(path => path.StartsWith("/api/v2/profiles/above/allowed-titles")), "Uncertain copy was retried without a title-list read.");
                        Click(Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "Retry")));
                        await Task.Delay(80); Check(wire.CopyBodies.Count == attempts + 1, "Retry automatically repeated copy.");
                        wire.FailCopy = false;
                    }
                    var before = wire.CopyBodies.Count;
                    Click(Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "Allow current titles") && Equals(button.Tag, "above")));
                    await Until(() => wire.CopyBodies.Count == before + 1);
                    Check(wire.CopyBodies.Last().GetProperty("collection_id").GetString() == collection.Id, "Copy body differs from collection.");
                    await Until(() => Descendants(dialog).OfType<TextBlock>().Any(text => text.Text == "2 titles newly allowed"));
                }
                await MediaParityNativeFixture.CaptureAsync(dialog, "collection-allowance-" + kind + ".png");
                dialog.Hide(); await Task.Delay(60); parent.Children.Remove(card);
            }
            var shared = (Border)Call(page, "BuildCollectionCard", new Collection { Id = "shared", CreatorProfileId = "other", Name = "Shared" })!;
            Check(!((MenuFlyout)shared.ContextFlyout).Items.OfType<MenuFlyoutItem>().Any(item => item.Text.StartsWith("Allow")), "Shared collection offers owned-only allowance.");
            var retired = (Border)Call(page, "BuildCollectionCard", new Collection { Id = "retired", CreatorProfileId = "primary", Name = "Retired" })!;
            parent.Children.Add(retired); await Task.Delay(40);
            var retiredMenu = (MenuFlyout)retired.ContextFlyout; retiredMenu.ShowAt(retired);
            await Until(() => retiredMenu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Allow for…" && item.Visibility == Visibility.Visible));
            var retiredAction = retiredMenu.Items.OfType<MenuFlyoutItem>().Single(item => item.Text == "Allow for…");
            retiredMenu.Hide(); parent.Children.Remove(retired); await Task.Delay(40); var retiredReads = wire.Reads.Count;
            Invoke(retiredAction); await Task.Delay(60); Check(wire.Reads.Count == retiredReads, "Detached collection card reopened management.");
            Program.Log("PASS native owned collection card menus: live manual PUT/DELETE, smart and synced copy bodies/counts; shared menu excluded.");
        }
        finally { parent.Children.Remove(page); }
    }

    private static async Task ProfileAccess(StackPanel parent, Wire wire, SiloApiClient client)
    {
        wire.EmptyFirstTitlePage = true; wire.FailCollections = true;
        var profile = new Profile { Id = "above", Name = "Limited", MaxContentRating = "PG", MaxAdvisoryAge = 10 };
        var editor = typeof(ProfileEditorDialog).GetConstructors(Private).Single().Invoke([parent.XamlRoot, profile,
            Array.Empty<Library>(), false, new AuthApi(client), true, true, false]);
        foreach (var image in Descendants(Field<Grid>(editor, "_avatarPresetGrid")).OfType<Image>()) image.Source = null;
        var dialog = Field<ContentDialog>(editor, "_dialog"); var showing = dialog.ShowAsync();
        try
        {
            await Until(() => Descendants(dialog).OfType<AllowedTitlesSection>().Any());
            var section = Descendants(dialog).OfType<AllowedTitlesSection>().Single();
            await Until(() => Descendants(section).OfType<Button>().Any(button => Equals(button.Content, "Show more") && button.Visibility == Visibility.Visible));
            Check(!Descendants(section).OfType<TextBlock>().Any(text => text.Text == "No titles allowed individually."), "Empty filtered page incorrectly declared empty list.");
            Click(Descendants(section).OfType<Button>().Single(button => Equals(button.Content, "Show more")));
            await Until(() => Descendants(section).OfType<Button>().Any(button => Equals(button.Content, "Stop allowing") && Equals(button.Tag, "movie:listed")));
            Check(wire.Reads.Any(path => path.Contains("cursor=next%2Bpage")), "Opaque allowance cursor changed.");
            await Until(() => Descendants(section).OfType<Button>().Any(button => Equals(button.Content, "Retry collections") && button.Visibility == Visibility.Visible));
            wire.FailCollections = false;
            Click(Descendants(section).OfType<Button>().Single(button => Equals(button.Content, "Retry collections")));
            await Until(() => Descendants(section).OfType<TextBlock>().Any(text => text.Text.Contains("Paused")));
            wire.EmptyFirstTitlePage = false;
            Click(Descendants(section).OfType<Button>().Single(button => Equals(button.Content, "Stop allowing") && Equals(button.Tag, "movie:listed")));
            await Until(() => wire.Writes.Contains("DELETE /api/v2/profiles/above/allowed-titles/movie%3Alisted"));
            await Task.Delay(100);
            Click(Descendants(section).OfType<Button>().Single(button => Equals(button.Content, "Stop allowing") && Equals(button.Tag, "paused")));
            await Until(() => wire.Writes.Contains("DELETE /api/v2/profiles/above/allowed-collections/paused"));
            wire.FailSearch = true;
            var search = Descendants(section).OfType<TextBox>().Single(); search.Text = "Fixture";
            await Until(() => Descendants(section).OfType<Button>().Any(button => Equals(button.Content, "Retry search") && button.Visibility == Visibility.Visible));
            wire.FailSearch = false;
            Click(Descendants(section).OfType<Button>().Single(button => Equals(button.Content, "Retry search")));
            await Until(() => Descendants(section).OfType<Button>().Any(button => Equals(button.Content, "Allow") && Equals(button.Tag, "series:search")));
            Check(!Descendants(section).OfType<Button>().Any(button => Equals(button.Tag, "episode:search")), "Search offered episode allowance.");
            Click(Descendants(section).OfType<Button>().Single(button => Equals(button.Content, "Allow") && Equals(button.Tag, "series:search")));
            await Until(() => wire.Writes.Contains("PUT /api/v2/profiles/above/allowed-titles/series%3Asearch"));
            await Task.Delay(100);
            var before = wire.Writes.Count; client.SetProfile("other");
            var retained = Descendants(section).OfType<Button>().First(button => Equals(button.Content, "Stop allowing")); Click(retained); await Task.Delay(60);
            Check(wire.Writes.Count == before, "Profile Access used replacement owner.");
            Program.Log("PASS native profile editor Access: empty cursor page, Show more, failed collections/retry, paused row, remove title/collection, search/add series, episode exclusion and stale owner.");
        }
        finally { dialog.Hide(); await showing; }
    }

    private static async Task CollectionEditor(StackPanel parent, Wire wire)
    {
        var page = new CollectionEditorPage { Width = 900, Height = 700 }; parent.Children.Add(page);
        try
        {
            var collection = new Collection { Id = "editor-manual", Name = "Editor family", CollectionType = "manual", CreatorProfileId = "primary" };
            page.ViewModel.IsEditing = true; page.ViewModel.CollectionId = collection.Id; page.ViewModel.Name = collection.Name;
            typeof(CollectionEditorViewModel).GetProperty("LoadedCollection")!.SetValue(page.ViewModel, collection);
            Set(page, "_editorActive", true); Set(page, "_lastHeaderState", null!);
            Call(page, "UpdateHeaderActions", false); await Task.Delay(80);
            var more = Field<StackPanel>(page, "_headerActions").Children.OfType<Button>().Single(button => button.Flyout is MenuFlyout);
            var menu = (MenuFlyout)more.Flyout; menu.ShowAt(more);
            await Until(() => menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Allow for…" && item.Visibility == Visibility.Visible));
            var action = menu.Items.OfType<MenuFlyoutItem>().Single(item => item.Text == "Allow for…"); Invoke(action);
            var dialog = await FindDialog<AllowCollectionDialog>(parent);
            await Until(() => Descendants(dialog).OfType<CheckBox>().Any());
            Toggle(Descendants(dialog).OfType<CheckBox>().Single(check => Equals(check.Tag, "above")));
            await Until(() => wire.Writes.Contains("PUT /api/v2/profiles/above/allowed-collections/editor-manual"));
            dialog.Hide(); await Task.Delay(60);
            typeof(CollectionEditorViewModel).GetProperty("LoadedCollection")!.SetValue(page.ViewModel,
                new Collection { Id = collection.Id, Name = collection.Name, CollectionType = "manual", CreatorProfileId = "primary" });
            Call(page, "UpdateHeaderActions", false);
            more = Field<StackPanel>(page, "_headerActions").Children.OfType<Button>().Single(button => button.Flyout is MenuFlyout);
            menu = (MenuFlyout)more.Flyout; menu.ShowAt(more); await Task.Delay(150);
            Check(menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Allow for…" && item.Visibility == Visibility.Visible), "Refreshing the same saved collection retired its Allow action without rebuilding it.");
            action = menu.Items.OfType<MenuFlyoutItem>().Single(item => item.Text == "Allow for…"); menu.Hide();
            var before = wire.Reads.Count;
            Set(page, "_editorLoadGeneration", Field<int>(page, "_editorLoadGeneration") + 1);
            Invoke(action); await Task.Delay(60);
            Check(wire.Reads.Count == before, "A prior editor visit reopened allowances.");
            Program.Log("PASS native collection editor header More: live allowance command and retired visit guard.");
        }
        finally { Set(page, "_editorActive", false); parent.Children.Remove(page); }
    }

    private static IEnumerable<MenuFlyoutItem> Actions(IEnumerable<MenuFlyoutItemBase> items)
        => items.OfType<MenuFlyoutItem>().Concat(items.OfType<MenuFlyoutSubItem>().SelectMany(item => Actions(item.Items)));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); ++i) { var child = VisualTreeHelper.GetChild(owner, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private static async Task<T> FindDialog<T>(FrameworkElement host) where T : ContentDialog
    {
        T? Find() => VisualTreeHelper.GetOpenPopupsForXamlRoot(host.XamlRoot).SelectMany(popup => Descendants(popup.Child).Prepend(popup.Child)).OfType<T>().FirstOrDefault();
        await Until(() => Find() != null); return Find()!;
    }
    private static object? Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);
    private static Task CallTask(object owner, string name, params object[] args) => (Task)Call(owner, name, args)!;
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private)!.SetValue(owner, value);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Invoke(MenuFlyoutItem item) => ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Toggle(CheckBox check) => ((IToggleProvider)new CheckBoxAutomationPeer(check).GetPattern(PatternInterface.Toggle)).Toggle();
    private static void Check(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    private static async Task Until(Func<bool> condition) { for (var i = 0; i < 250 && !condition(); ++i) await Task.Delay(20); Check(condition(), "Native title allowance operation did not settle."); }

    private sealed class Services : IServiceProvider
    {
        private readonly IServiceProvider _original;
        private readonly Dictionary<Type, object> _values;
        internal Services(IServiceProvider original, SiloApiClient client, AuthService auth)
        {
            _original = original; var catalog = new CatalogApi(client); var collections = new CollectionsApi(client);
            var cache = new ItemDetailPrefetchCache(catalog, auth);
            _values = new() { [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth, [typeof(AuthApi)] = new AuthApi(client),
                [typeof(CatalogApi)] = catalog, [typeof(CollectionsApi)] = collections, [typeof(ItemDetailPrefetchCache)] = cache,
                [typeof(UICustomizationService)] = new UICustomizationService(new SettingsApi(client)),
                [typeof(CollectionEditorViewModel)] = new CollectionEditorViewModel(collections, catalog, new SettingsApi(client), auth),
                [typeof(CollectionsViewModel)] = new CollectionsViewModel(collections, catalog, new AuthApi(client), client) };
        }
        public object? GetService(Type type) => type == typeof(ItemDetailViewModel)
            ? new ItemDetailViewModel((CatalogApi)_values[typeof(CatalogApi)], (ItemDetailPrefetchCache)_values[typeof(ItemDetailPrefetchCache)])
            : _values.GetValueOrDefault(type) ?? _original.GetService(type);
    }
    private sealed partial class Wire : HttpMessageHandler
    {
        internal string Capability = "available";
        internal bool FailTitleStates, FailCollections, EmptyFirstTitlePage, HoldStates, FailMutation, FailCopy, FailSearch;
        internal int CapabilityReads, HeldReads; internal TaskCompletionSource? Gate;
        internal List<string> Writes = [], Reads = [];
        internal List<JsonElement> CopyBodies = [];
        private readonly HashSet<string> _titles = ["explicit"], _pausedRemoved = [];
        private readonly HashSet<(string Profile, string Id)> _collections = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "title-allowances.fixture.invalid") throw new InvalidOperationException("External request blocked.");
            var path = request.RequestUri.AbsolutePath; var query = request.RequestUri.Query;
            if (RaceMode) return await RaceResponseAsync(request);
            if (request.Method != HttpMethod.Get)
            {
                Writes.Add(request.Method + " " + path);
                Check(request.Headers.GetValues("X-Profile-Id").Single() == "primary", "Mutation lost primary header.");
                Check(request.Headers.GetValues("X-Profile-Token").Single() == "fixture-proof", "Mutation lost primary PIN proof.");
                if (path.EndsWith("copy-collection"))
                {
                    CopyBodies.Add(JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)));
                    if (FailCopy) return Json(new { detail = "Synthetic uncertain copy" }, HttpStatusCode.InternalServerError);
                    return Json(new { added = 2 });
                }
                Check(request.Content == null, "Bodiless allowance write acquired a body.");
                if (FailMutation) return Json(new { detail = "Synthetic no_library_access", code = "no_library_access" }, HttpStatusCode.UnprocessableEntity);
                var profile = path.Split('/')[4];
                if (path.Contains("allowed-collections"))
                {
                    var id = Uri.UnescapeDataString(path.Split('/')[6]);
                    if (request.Method == HttpMethod.Put) _collections.Add((profile, id));
                    else { _collections.Remove((profile, id)); if (id == "paused") _pausedRemoved.Add(profile); }
                }
                else if (request.Method == HttpMethod.Put) _titles.Add(profile); else _titles.Remove(profile);
                return new(HttpStatusCode.NoContent);
            }
            Reads.Add(path + query);
            if (path == "/api/v2/capabilities/title-allowances") { ++CapabilityReads; return Json(new { state = Capability }); }
            if (path == "/api/v2/profiles") return Json(new { items = new object[] { new { id = "primary", name = "Primary", is_primary = true }, new { id = "above", name = "Limited", max_content_rating = "PG" } } });
            if (path.EndsWith("title-allowances"))
            {
                if (HoldStates && Gate != null) { ++HeldReads; await Gate.Task; }
                if (FailTitleStates) return Json(new { detail = "Synthetic failed standing" }, HttpStatusCode.InternalServerError);
                return Json(new { items = new[] { State("within", "Within", "within_limits"), State("above", "Above", _titles.Contains("above") ? "allowed" : "above_limit"),
                    State("explicit", "Explicit", _titles.Contains("explicit") ? "allowed" : "above_limit"), State("collection", "Collection", "allowed_by_collection"), State("no-library", "No library", "no_library_access") } });
            }
            if (path.EndsWith("allowed-collections"))
            {
                if (FailCollections) return Json(new { detail = "Synthetic failed collections" }, HttpStatusCode.InternalServerError);
                var profile = path.Split('/')[4];
                var items = new List<object>();
                if (!_pausedRemoved.Contains(profile)) items.Add(new { collection_id = "paused", name = "Paused list", item_count = 5, live = false });
                foreach (var allowance in _collections.Where(value => value.Profile == profile))
                    items.Add(new { collection_id = allowance.Id, name = "Family " + allowance.Id, item_count = 2, live = true });
                return Json(new { items });
            }
            if (path.EndsWith("allowed-titles")) return Json(new { items = EmptyFirstTitlePage && query.IndexOf("cursor=", StringComparison.Ordinal) < 0 ? Array.Empty<object>() : new object[] { new { content_id = "movie:listed", title = "Listed title", type = "movie", year = 2000 } }, page = new { has_more = EmptyFirstTitlePage && !query.Contains("cursor="), next_cursor = "next+page" } });
            if (path == "/api/v2/catalog") return FailSearch ? Json(new { detail = "Synthetic failed search" }, HttpStatusCode.InternalServerError) : Json(new { items = new[] { new { content_id = "series:search", title = "Found series", type = "series", year = 2020 }, new { content_id = "episode:search", title = "Found episode", type = "episode", year = 2020 } } });
            return Json(new { items = Array.Empty<object>() });
        }
        private static object State(string id, string name, string state) => new { profile_id = id, name, max_content_rating = "PG", max_advisory_age = 10, state };
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
