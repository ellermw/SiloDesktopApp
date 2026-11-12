using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

internal static class ProfilesMetadataNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(StackPanel parent)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://profiles-metadata.fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("fixture-access", "fixture-refresh", 3600);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "admin" });
        auth.SelectProfile("primary", profile: new() { Id = "primary", Name = "Primary", IsPrimary = true });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "profiles-metadata-local"));
        var prefs = local.Load(); prefs.SiloCastEnabled = false; local.Save(prefs);
        await using var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(http).AddSingleton(auth).AddSingleton(local)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<MediaMaintenanceApi>().AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>()
            .AddSingleton<HistoryImportApi>().AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>().AddSingleton<CollectionsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>().AddSingleton<ProfileSelectViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "profiles-metadata-images")))
            .AddSingleton<ItemDetailPrefetchCache>().BuildServiceProvider();
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = slot.GetValue(null); slot.SetValue(null, services);
        try
        {
            await Limits(parent, wire, services);
            await ProfileEditor(parent, wire, services.GetRequiredService<AuthApi>(), primary: true, first: false);
            await ProfileEditor(parent, wire, services.GetRequiredService<AuthApi>(), primary: false, first: true);
            await ProfileEditor(parent, wire, services.GetRequiredService<AuthApi>(), primary: false, first: false);
            foreach (var type in new[] { "season", "episode" })
            {
                await ChildEditor(parent, wire, type, []);
                await ChildEditor(parent, wire, type, [2, 10, 99]);
            }
            await Hints(parent, wire, auth);
            Program.Log("PASS profiles/metadata native: all assigned controls, bodies and delayed read ownership checks.");
        }
        finally { wire.HintsGate?.TrySetResult(); slot.SetValue(null, previous); }
    }

    private static async Task Limits(StackPanel parent, Wire wire, IServiceProvider services)
    {
        var vm = services.GetRequiredService<ProfileSelectViewModel>();
        var picker = new ProfileSelectPage(); parent.Children.Add(picker);
        await vm.LoadProfilesCommand.ExecuteAsync(null);
        Check(!vm.PickerCards.Any(card => card.IsAdd), "Picker offers Add at limit.");
        var settings = new SettingsPage(); parent.Children.Add(settings);
        await CallTask(settings, "LoadProfilesAsync");
        Check(((Button)settings.FindName("AddProfileButton")).Visibility == Visibility.Collapsed, "Settings offers New at limit.");
        Check(((TextBlock)settings.FindName("ProfileLimitText")).Text == ProfileLimits.Message(1), "Settings omits limit sentence.");
        var household = new HouseholdSetupPage(); parent.Children.Add(household);
        await CallTask(household, "ReloadProfilesAsync", CancellationToken.None);
        Check(!household.ProfileTiles.Any(tile => tile.IsAdd), "Household offers Add at limit.");
        wire.Maximum = null;
        await vm.LoadProfilesCommand.ExecuteAsync(null); await CallTask(settings, "LoadProfilesAsync"); await CallTask(household, "ReloadProfilesAsync", CancellationToken.None);
        Check(vm.PickerCards.Any(card => card.IsAdd) && household.ProfileTiles.Any(tile => tile.IsAdd) && ((Button)settings.FindName("AddProfileButton")).Visibility == Visibility.Visible, "Missing max did not restore create attempts.");
        wire.Maximum = 1;
        Click((Button)settings.FindName("AddProfileButton"));
        await Until(() => ((Button)settings.FindName("AddProfileButton")).Visibility == Visibility.Collapsed);
        Check(((TextBlock)settings.FindName("ProfileLimitText")).Text == ProfileLimits.Message(1), "A freshly refused create left the stale Add action instead of the limit sentence.");
        try
        {
            foreach (var failLibraries in new[] { false, true })
            {
                wire.FailProfiles = !failLibraries; wire.FailLibraries = failLibraries;
                services.GetRequiredService<CatalogApi>().InvalidateLibraryCache();
                settings.ViewModel.ErrorMessage = null;
                typeof(SettingsPage).GetMethod("AddProfileButton_Click", Private)!.Invoke(settings, [settings, new RoutedEventArgs()]);
                await Until(() => settings.ViewModel.ErrorMessage?.StartsWith("Couldn't open the new profile form:") == true);
                vm.Profiles.Clear(); vm.ErrorMessage = null;
                services.GetRequiredService<CatalogApi>().InvalidateLibraryCache();
                typeof(ProfileSelectPage).GetMethod("AddProfileButton_Click", Private)!.Invoke(picker, [picker, new RoutedEventArgs()]);
                await Until(() => vm.ErrorMessage?.StartsWith("Couldn't open the new profile form:") == true);
            }
        }
        finally { wire.FailProfiles = wire.FailLibraries = false; }
        parent.Children.Remove(household); parent.Children.Remove(settings); parent.Children.Remove(picker); vm.CancelProfileLoad();
        Program.Log("PASS native picker, Settings limit sentence, household Add and omitted-limit refresh.");
    }

    private static async Task ProfileEditor(StackPanel parent, Wire wire, AuthApi api, bool primary, bool first)
    {
        var profile = first ? null : new Profile { Id = "primary", Name = "Before", IsPrimary = primary, LibraryRestrictionsEnabled = true, AllowedLibraryIds = [7] };
        var form = typeof(ProfileEditorDialog).GetConstructors(Private).Single().Invoke([parent.XamlRoot, profile,
            new[] { new Library { Id = 7, Name = "Movies", Type = "movie" } }, false, api, false, false, first]);
        // Avoid remote avatar preview requests; these checks own only access and form submission.
        foreach (var image in Descendants(Field<Grid>(form, "_avatarPresetGrid")).OfType<Image>()) image.Source = null;
        var dialog = Field<ContentDialog>(form, "_dialog"); var showing = dialog.ShowAsync();
        try
        {
            await Until(() => Descendants(dialog).OfType<Button>().Any(b => b.Name == "PrimaryButton"));
            var isPrimary = primary || first;
            Check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "Restrict libraries") != isPrimary, "Primary restriction controls were not suppressed, or secondary lost them.");
            Check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.StartsWith("This profile sees every library")) == isPrimary, "Inherited-access guidance differs from primary state.");
            Field<TextBox>(form, "_nameBox").Text = "Entered name";
            Field<PasswordBox>(form, "_pinBox").Password = "1234";
            Field<ToggleSwitch>(form, "_kidsToggle").IsOn = true;
            if (isPrimary) Check(!Field<ToggleSwitch>(form, "_restrictLibrariesToggle").IsOn, "Primary Kids preset enabled restriction.");
            var scroll = (ScrollViewer)dialog.Content;
            var accessText = Descendants(dialog).OfType<TextBlock>().Single(t => isPrimary ? t.Text.StartsWith("This profile sees every library") : t.Text == "Restrict libraries");
            Field<ToggleSwitch>(form, "_kidsToggle").Focus(FocusState.Programmatic);
            await Task.Delay(100); scroll.UpdateLayout();
            scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
            await Task.Delay(100);
            var accessY = accessText.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point()).Y;
            Program.Log($"Access scroll: primary={isPrimary}, y={accessY}, textHeight={accessText.ActualHeight}, viewport={scroll.ActualHeight}, offset={scroll.VerticalOffset}, max={scroll.ScrollableHeight}");
            Check(accessY >= 0 && accessY + accessText.ActualHeight <= scroll.ActualHeight + 1, "Access guidance could not be scrolled into the form viewport.");
            await MediaParityNativeFixture.CaptureAsync(dialog, $"profiles-metadata-editor-{primary}-{first}.png");
            // Reproduce an old retained restricted draft at the last submission boundary.
            Field<ToggleSwitch>(form, "_restrictLibrariesToggle").IsOn = true;
            foreach (var toggle in Field<Dictionary<int, ToggleSwitch>>(form, "_libraryChecks").Values) toggle.IsOn = true;
            Click(Descendants(dialog).OfType<Button>().Single(b => b.Name == "PrimaryButton"));
            await Until(() => form.GetType().GetProperty("SavedProfile")!.GetValue(form) != null);
            Check(wire.ProfileBody.GetProperty("library_restrictions_enabled").GetBoolean() != isPrimary, "Profile restriction wire differs from primary state.");
            Check(wire.ProfileBody.GetProperty("allowed_library_ids").GetArrayLength() == (isPrimary ? 0 : 1), "Stale draft leaked library ids or secondary lost ids.");
            Check(wire.ProfileBody.GetProperty("name").GetString() == "Entered name" && wire.ProfileBody.GetProperty("pin").GetString() == "1234", "Entered name/PIN lost.");
            await showing;
        }
        finally { dialog.Hide(); }
        Program.Log($"PASS native profile primary={primary}, first={first}: controls, Kids, stale draft and exact body.");
    }

    private static async Task ChildEditor(StackPanel parent, Wire wire, string type, int[] preservedLocks)
    {
        var dialog = new EditMetadataDialog(new MediaItemDetail { ContentId = "child", Type = type, Title = "Before", LockedFields = preservedLocks.ToList() }) { XamlRoot = parent.XamlRoot };
        var showing = dialog.ShowAsync();
        try
        {
            await Until(() => Descendants(dialog).OfType<Button>().Any(b => b.Name == "PrimaryButton"));
            var locks = Field<List<(string, Button)>>(dialog, "_lockButtons");
            Check(locks.Select(pair => pair.Item1).Order().SequenceEqual((type == "episode" ? new[] { "title", "overview", "air_date", "runtime" } : new[] { "title", "overview", "air_date" }).Order()), "Child lock controls contain wrong fields.");
            var inputs = Field<Dictionary<string, TextBox>>(dialog, "_textInputs"); inputs["title"].Text = "Child edit";
            await Until(() => AutomationProperties.GetName(locks.Single(p => p.Item1 == "title").Item2) == "Unlock title");
            Click(locks.Single(p => p.Item1 == "title").Item2);
            await Until(() => AutomationProperties.GetName(locks.Single(p => p.Item1 == "title").Item2) == "Lock title");
            Click(locks.Single(p => p.Item1 == "title").Item2);
            inputs["air_date"].Text = "2026-01-02";
            if (type == "episode") Field<Dictionary<string, NumberBox>>(dialog, "_numberInputs")["runtime"].Value = 42;
            var reset = Field<Button>(dialog, "_reset"); Check(reset.Visibility == Visibility.Visible, "Child Reset hidden.");
            await Until(() => reset.IsEnabled);
            var resetText = Descendants((DependencyObject)((Flyout)reset.Flyout).Content).OfType<TextBlock>().Select(t => t.Text);
            Check(resetText.Any(t => t.Contains("legacy unlock and quick refresh") && t.Contains("other locks are preserved")), "Child legacy reset explanation wrong.");
            await Until(() => Field<TextBlock>(dialog, "_lockedCount").Text == $"{(type == "episode" ? 3 : 2) + preservedLocks.Length} locked");
            await MediaParityNativeFixture.CaptureAsync(dialog, $"profiles-metadata-{type}.png");
            Click(Descendants(dialog).OfType<Button>().Single(b => b.Name == "PrimaryButton"));
            await Until(() => dialog.HasSaved); await showing;
            Check(wire.MetadataBody.GetProperty("locked_fields").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual((type == "episode" ? new[] { 0, 7, 13 } : new[] { 0, 13 }).Concat(preservedLocks).Order()), "Child metadata PATCH lacks exact locks.");
        }
        finally { dialog.Hide(); await showing; await Task.Delay(80); }
        wire.MetadataOrder.Clear();
        var resetting = new EditMetadataDialog(new MediaItemDetail { ContentId = "child", Type = type, Title = "Locked", LockedFields = (type == "episode" ? new[] { 0, 1, 7, 13 } : new[] { 0, 1, 13 }).Concat(preservedLocks).ToList() }) { XamlRoot = parent.XamlRoot };
        var resetShowing = resetting.ShowAsync();
        try
        {
            await Until(() => Descendants(resetting).OfType<Button>().Any(b => b.Name == "PrimaryButton"));
            var reset = Field<Button>(resetting, "_reset"); await Until(() => reset.IsEnabled); Click(reset);
            var confirmation = (StackPanel)((Flyout)reset.Flyout).Content;
            await Until(() => confirmation.IsLoaded);
            Click(Descendants(confirmation).OfType<Button>().Single(b => Equals(b.Content, "Reset")));
            await Until(() => resetting.HasSaved); await resetShowing;
            Check(wire.MetadataOrder.SequenceEqual(new[] { "PATCH:" + string.Join(",", preservedLocks), "POST:quick" }), "Child Reset did not preserve unrelated locks while unlocking before quick refresh.");
        }
        finally { resetting.Hide(); await resetShowing; await Task.Delay(80); }
        Program.Log("PASS native " + type + " edits, lock toggle/count, reset explanation, serialized PATCH and actual Reset unlock/refresh.");
    }

    private static async Task Hints(StackPanel parent, Wire wire, AuthService auth)
    {
        async Task WithDialog(Func<MatchItemDialog, Task> run, int? library = 7)
        {
            var dialog = new MatchItemDialog("item", "Wrong match", 2020, "movie", library, null, null) { XamlRoot = parent.XamlRoot };
            var showing = dialog.ShowAsync();
            try { if (library == null) {
                var scope = Field<object>(dialog, "_scope");
                await Until(() => Descendants((DependencyObject)scope).OfType<ComboBox>().Any(b => b.Items.Count == 2));
                var picker = Descendants((DependencyObject)scope).OfType<ComboBox>().Single();
                picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, 7));
            } await Until(() => wire.HintReads > 0 || !wire.SupportsHints && ((Button)dialog.FindName("SearchButton")).IsEnabled); await run(dialog); }
            finally { wire.HintsGate?.TrySetResult(); dialog.Hide(); await showing; wire.HintsGate = null; wire.HintReads = 0; }
        }
        wire.SupportsHints = true; wire.DenyAdminCapabilities = true;
        await WithDialog(async dialog =>
        {
            await Until(() => ((TextBox)dialog.FindName("TitleBox")).Text == "Folder 7");
            Check(wire.HintReads == 1, "A curator could not load permission-gated hints when the admin-only capability endpoint refused access.");
        });
        wire.DenyAdminCapabilities = false;
        wire.SupportsHints = true; wire.HintsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await WithDialog(async dialog =>
        {
            Check(!((Button)dialog.FindName("SearchButton")).IsEnabled, "Initial Search enabled while folder hints pending.");
            var search = CallTask(dialog, "DoSearchAsync"); await Task.Delay(60);
            Check(wire.Searches == 0, "Fast Search dispatched stale match.");
            wire.HintsGate!.TrySetResult(); await search;
            Check(wire.SearchBody.GetProperty("title").GetString() == "Folder 7" && wire.SearchBody.GetProperty("year").GetInt32() == 1997, "Fast Search did not use hints.");
            Check(wire.SearchBody.GetProperty("imdb_id").GetString() == "tt7", "Provider hint missing from search.");
        });
        wire.HintsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await WithDialog(async dialog =>
        {
            ((TextBox)dialog.FindName("TitleBox")).Text = "Entered title"; ((TextBox)dialog.FindName("YearBox")).Text = "1984";
            ((TextBox)dialog.FindName("ImdbIdBox")).Text = "ttEntered";
            // Model a native notification still in the dispatcher queue at merge.
            Field<HashSet<TextBox>>(dialog, "_editedHintFields").Clear();
            var held = wire.HintsGate!; wire.HintsGate = null;
            var scope = (ItemLibraryScope)Field<object>(dialog, "_scope").GetType().GetProperty("State")!.GetValue(Field<object>(dialog, "_scope"))!;
            await CallTask(dialog, "LoadHintsAsync", scope.Capture(), Field<int>(dialog, "_hintsRevision"));
            Check(((TextBox)dialog.FindName("TitleBox")).Text == "Entered title" && ((TextBox)dialog.FindName("YearBox")).Text == "1984", "Hints overwrote the visible draft before queued change notification.");
            held.TrySetResult(); await Until(() => ((Button)dialog.FindName("SearchButton")).IsEnabled);
            Click((Button)dialog.FindName("SearchButton")); await Until(() => wire.SearchBody.GetProperty("title").GetString() == "Entered title");
            Check(wire.SearchBody.GetProperty("year").GetInt32() == 1984 && wire.SearchBody.GetProperty("imdb_id").GetString() == "ttEntered", "Late hints overwrote entered fields.");
        });
        wire.NullHintProviders = true;
        await WithDialog(async dialog =>
        {
            await Until(() => ((Button)dialog.FindName("SearchButton")).IsEnabled);
            await Field<Task>(dialog, "_hintsTask");
            Check(new[] { "ImdbIdBox", "TmdbIdBox", "TvdbIdBox" }.All(name => ((TextBox)dialog.FindName(name)).Text == ""), "Null provider hints did not behave as an empty map.");
            var searches = wire.Searches;
            await CallTask(dialog, "DoSearchAsync");
            Check(wire.Searches == searches + 1 && wire.SearchBody.GetProperty("title").GetString() == "Folder 7", "Null provider hints prevented search.");
            typeof(MatchItemDialog).GetField("_hintsTask", Private)!.SetValue(dialog, Task.FromException(new InvalidOperationException("Synthetic optional hint failure")));
            ((TextBox)dialog.FindName("TitleBox")).Text = "Manual title";
            await CallTask(dialog, "DoSearchAsync");
            Check(wire.Searches == searches + 2 && wire.SearchBody.GetProperty("title").GetString() == "Manual title", "A faulted optional hints task prevented manual search.");
        });
        wire.NullHintProviders = false;
        wire.FailHints = true;
        await WithDialog(async dialog => { await Until(() => ((Button)dialog.FindName("SearchButton")).IsEnabled); Check(((TextBox)dialog.FindName("TitleBox")).Text == "Wrong match", "Failed optional hint lost fallback."); });
        wire.FailHints = false; wire.SupportsHints = false;
        await WithDialog(async dialog => { await Until(() => ((Button)dialog.FindName("SearchButton")).IsEnabled); Check(wire.HintReads == 1 && ((TextBox)dialog.FindName("TitleBox")).Text == "Wrong match", "Old-server 404 did not preserve current-item fallback."); });
        wire.SupportsHints = true; wire.HintsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await WithDialog(async dialog =>
        {
            var scope = Field<object>(dialog, "_scope"); var picker = (ComboBox)Descendants((DependencyObject)scope).First(v => v is ComboBox);
            picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, 8));
            await Until(() => wire.HintReads >= 2); wire.HintsGate!.TrySetResult();
            await Until(() => ((Button)dialog.FindName("SearchButton")).IsEnabled);
            Check(((TextBox)dialog.FindName("TitleBox")).Text == "Folder 8", "Late old library hints replaced current scope.");
        }, library: null);
        wire.HintsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await WithDialog(async dialog =>
        {
            auth.SelectProfile("other", profile: new() { Id = "other", IsPrimary = true });
            wire.HintsGate!.TrySetResult(); await Task.Delay(80);
            Check(((TextBox)dialog.FindName("TitleBox")).Text == "Wrong match" && !((Button)dialog.FindName("SearchButton")).IsEnabled, "Departed owner published hints.");
        });
        Program.Log("PASS native hints: fast Search, edits, failure, old-server 404 fallback, scope and owner retirement.");
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static Task CallTask(object owner, string name, params object[] args) => (Task)owner.GetType().GetMethod(name, Private)!.Invoke(owner, args)!;
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 240 && !ready(); i++) await Task.Delay(20); Check(ready(), "Native profiles/metadata operation did not settle."); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); i++) { var child = VisualTreeHelper.GetChild(owner, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }

    private sealed class Wire : HttpMessageHandler
    {
        internal int? Maximum = 1;
        internal bool SupportsHints, FailHints, DenyAdminCapabilities, NullHintProviders, FailProfiles, FailLibraries;
        internal int HintReads, Searches;
        internal TaskCompletionSource? HintsGate;
        internal JsonElement ProfileBody, MetadataBody, SearchBody;
        internal List<string> MetadataOrder = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "profiles-metadata.fixture.invalid") throw new InvalidOperationException("External request blocked.");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/profiles" && FailProfiles || path.EndsWith("/libraries") && FailLibraries)
                return Json(new { detail = "Fixture profile form read failed" }, HttpStatusCode.BadRequest);
            if (path == "/api/v2/admin/catalog/capabilities" && DenyAdminCapabilities)
                return Json(new { detail = "Admin primary profile required" }, HttpStatusCode.Forbidden);
            if (request.Content != null)
            {
                var body = JsonSerializer.Deserialize<JsonElement>(await request.Content.ReadAsStringAsync(ct));
                if (path.StartsWith("/api/v2/profiles")) { ProfileBody = body; return Json(new { id = "primary", name = "Entered name", is_primary = true }); }
                if (path.EndsWith("/metadata")) { MetadataBody = body; MetadataOrder.Add("PATCH:" + string.Join(",", body.GetProperty("locked_fields").EnumerateArray().Select(v => v.GetInt32()))); return Json(new { content_id = "child", type = "episode", title = "Child edit" }); }
                if (path.EndsWith("/refresh-metadata")) { MetadataOrder.Add("POST:" + body.GetProperty("mode").GetString()); return Json(new { id = "fixture-job", state = "queued" }); }
                if (path.EndsWith("/match/search")) { SearchBody = body; Searches++; return Json(new { candidates = Array.Empty<object>() }); }
            }
            if (path.EndsWith("/match/hints"))
            {
                HintReads++; var library = request.RequestUri.Query.Contains("library_id=8") ? 8 : 7;
                if (!SupportsHints) return Json(new { detail = "Not found" }, HttpStatusCode.NotFound);
                if (HintsGate != null) await HintsGate.Task;
                if (FailHints) return Json(new { detail = "Fixture lookup failed" }, HttpStatusCode.InternalServerError);
                return Json(new { title = $"Folder {library}", year = 1997, provider_ids = NullHintProviders ? null : new { imdb = $"tt{library}" } });
            }
            return Json(path switch
            {
                "/api/v2/profiles" => new { items = new[] { new { id = "primary", name = "Primary", is_primary = true } }, max_profiles = Maximum },
                "/api/v2/libraries" => new { items = new[] { new { id = "7", name = "Movies", type = "movie" } } },
                "/api/v2/admin/catalog/capabilities" => new { item_match_hints = SupportsHints },
                "/api/v2/admin/items/item/files" => new { items = new[] { new { id = "1", library_id = "7" }, new { id = "2", library_id = "8" } }, page = new { has_more = false } },
                _ => (object)new { items = Array.Empty<object>(), state = "unavailable", sections = Array.Empty<object>(), devices = Array.Empty<object>(), providers = Array.Empty<object>() },
            });
        }
        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
    }
}
