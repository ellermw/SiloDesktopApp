using System.Collections.ObjectModel;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

/// <summary>
/// Actual-control checks for the Nov 10 viewer-surface parity items: Continue Watching / Next Up removal on the
/// dedicated page (G1), complete smart-collection rules in the editor and wizard (G4), the Trending worldwide
/// Show selector (G5), Calendar digital/physical badges (G9), the failed Sync now refresh (G11) and the episode
/// link library scope (G12). The sidebar library icons (G10) are checked in the sidebar-publication route.
/// The fixture only touches members that exist in the earlier Beta, so it can also run against that app to
/// reproduce each failure.
/// </summary>
internal static class ViewerActionsNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync(StackPanel parent)
    {
        var only = Environment.GetEnvironmentVariable("SILO_NATIVE_VIEWER_ACTIONS") ?? "";
        bool Wants(string name) => only.Length == 0 || only.Split(',').Contains(name);
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://viewer-actions.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("fixture", "fixture", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Primary", IsPrimary = true });
        using var events = new EventChannelClient(client, auth);
        var catalog = new CatalogApi(client); var collections = new CollectionsApi(client); var settings = new SettingsApi(client);
        var home = new HomeApi(client); var overlays = new CardOverlayService(settings);
        var localSettings = new SettingsService(Path.Combine(Program.ResultDirectory, "viewer-actions-settings"));
        var toast = new ToastService(); var customization = new UICustomizationService(settings);
        var prefetch = new ItemDetailPrefetchCache((_, _) => new TaskCompletionSource<MediaItemDetail>().Task);
        var frame = new Frame { Width = 1100, Height = 800 };
        var navigation = new NavigationService { Frame = frame };
        var calendarModel = new CalendarViewModel(catalog, localSettings) { HasLoaded = true };
        var window = new Window { Content = frame };
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = () => client, [typeof(AuthService)] = () => auth, [typeof(EventChannelClient)] = () => events,
            [typeof(CatalogApi)] = () => catalog, [typeof(CollectionsApi)] = () => collections, [typeof(SettingsApi)] = () => settings,
            [typeof(HomeApi)] = () => home, [typeof(ToastService)] = () => toast, [typeof(SettingsService)] = () => localSettings,
            [typeof(CardOverlayService)] = () => overlays, [typeof(UICustomizationService)] = () => customization,
            [typeof(NavigationService)] = () => navigation, [typeof(ItemDetailPrefetchCache)] = () => prefetch,
            [typeof(LibraryViewModel)] = () => new LibraryViewModel(catalog),
            [typeof(CalendarViewModel)] = () => calendarModel,
            [typeof(CollectionEditorViewModel)] = () => new CollectionEditorViewModel(collections, catalog, settings, auth),
            [typeof(SmartCollectionWizardViewModel)] = () => new SmartCollectionWizardViewModel(catalog, collections, new AuthApi(client), auth),
        }));
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1100, 800)); window.AppWindow.Show(false);
            await Task.Delay(200);
            if (Wants("dismissal")) await DedicatedPageRemovalAsync(frame, wire);
            if (Wants("paging")) await DismissalPagingAsync(frame, wire);
            if (Wants("smart-editor")) await SmartEditorAsync(frame, wire);
            if (Wants("smart-wizard")) await SmartWizardAsync(frame, wire);
            if (Wants("trending")) await TrendingShowAsync(parent);
            if (Wants("calendar")) await CalendarBadgesAsync(frame, calendarModel);
            if (Wants("sync")) await FailedSyncRefreshAsync(client, collections, catalog, settings, auth, wire);
            if (Wants("episode-scope")) await EpisodeLinkScopeAsync(navigation, frame);
        }
        finally
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot)) popup.IsOpen = false;
            frame.Content = null; await Task.Delay(100);
            field.SetValue(null, previous);
            window.Close();
        }
    }

    // ----- G1 -----

    private static async Task DedicatedPageRemovalAsync(Frame frame, Wire wire)
    {
        async Task<CatalogPage> OpenAsync(string section, string title)
        {
            frame.Navigate(typeof(CatalogPage), new CatalogNavigation("section", title, null, null, section));
            var opened = (CatalogPage)frame.Content;
            await Until(() => !Field<bool>(opened, "_loading") && Items(opened).Count > 0, "dedicated page cards");
            await Task.Delay(300); opened.UpdateLayout();
            return opened;
        }
        static ObservableCollection<MediaItem> Items(CatalogPage page) => Field<ObservableCollection<MediaItem>>(page, "_items");
        async Task<PosterCard> CardAsync(CatalogPage page, string id)
        {
            PosterCard? found = null;
            await Until(() => (found = Descendants<PosterCard>(page).FirstOrDefault(card => card.MediaItem?.ContentId == id)) != null, "realized card " + id);
            return found!;
        }
        async Task<List<string>> MenuAsync(PosterCard card)
        {
            Invoke((Button)card.FindName("MoreButton"));
            List<string> entries = [];
            await Until(() => (entries = OpenMenuItems(card).Select(item => item.Text).ToList()).Count > 0, "card menu");
            return entries;
        }
        void CloseMenus(UIElement root) { foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)) popup.IsOpen = false; }
        string Count(CatalogPage page) => ((TextBlock)page.FindName("CountText")).Text;

        // Continue Watching page
        var page = await OpenAsync("cw", "Continue Watching");
        Check(Items(page).Select(item => item.ContentId).SequenceEqual(["film", "show-a", "show-b", "no-stamp"]), "Continue Watching page lists its four cards");
        Check(Count(page) == "4", "Continue Watching count starts at 4");
        var filmMenu = await MenuAsync(await CardAsync(page, "film"));
        Check(filmMenu.Contains("Remove from Continue Watching"), "a Continue Watching card with progress offers Remove from Continue Watching: " + string.Join("|", filmMenu));
        var remove = OpenMenuItems(await CardAsync(page, "film")).Single(item => item.Text == "Remove from Continue Watching");
        Invoke(remove);
        await Until(() => wire.Dismissals.Count == 1 && !Field<bool>(page, "_loading") && Items(page).All(item => item.ContentId != "film"), "movie dismissal");
        Check(wire.Dismissals[0].Path == "/api/v2/home/dismissals/continue_watching/film"
            && wire.Dismissals[0].Body == """{"progress_updated_at":"2026-11-09T10:00:00Z"}""", "movie removal sends the progress timestamp: " + wire.Dismissals[0]);
        Check(Count(page) == "3", "the removed movie leaves the count: " + Count(page));
        CloseMenus(page);

        var stampless = await MenuAsync(await CardAsync(page, "no-stamp"));
        Check(!stampless.Any(text => text.StartsWith("Remove from")), "a card without progress_updated_at offers no removal: " + string.Join("|", stampless));
        CloseMenus(page);

        await MenuAsync(await CardAsync(page, "show-a"));
        Invoke(OpenMenuItems(await CardAsync(page, "show-a")).Single(item => item.Text == "Remove from Continue Watching"));
        await Until(() => wire.Dismissals.Count == 2 && !Field<bool>(page, "_loading") && Items(page).All(item => item.SeriesId != "show"), "episode dismissal drops the whole show");
        Check(wire.Dismissals[1].Path == "/api/v2/home/dismissals/continue_watching/show-a"
            && wire.Dismissals[1].Body == """{"progress_updated_at":"2026-11-09T11:00:00Z"}""", "episode removal sends its progress timestamp: " + wire.Dismissals[1]);
        Check(Items(page).Select(item => item.ContentId).SequenceEqual(["no-stamp"]) && Count(page) == "1",
            "both cards of the dismissed show leave the page and the count: " + string.Join(",", Items(page).Select(item => item.ContentId)) + "/" + Count(page));
        CloseMenus(page);
        Program.Log("PASS: dedicated Continue Watching page removes movies/episodes with the progress timestamp, drops the whole show and its count, and offers nothing without a timestamp.");

        // Next Up page
        page = await OpenAsync("nu", "Next Up");
        var nextMenu = await MenuAsync(await CardAsync(page, "nu-ep"));
        Check(nextMenu.Contains("Remove from Next Up") && !nextMenu.Contains("Remove from Continue Watching"), "a Next Up card with a series offers Remove from Next Up: " + string.Join("|", nextMenu));
        Invoke(OpenMenuItems(await CardAsync(page, "nu-ep")).Single(item => item.Text == "Remove from Next Up"));
        await Until(() => wire.Dismissals.Count == 3 && Items(page).All(item => item.ContentId != "nu-ep"), "next up dismissal");
        Check(wire.Dismissals[2].Path == "/api/v2/home/dismissals/next_up/nu-ep" && wire.Dismissals[2].Body == """{"series_id":"nu-show"}""",
            "Next Up removal sends the series identity: " + wire.Dismissals[2]);
        CloseMenus(page);
        var orphanMenu = await MenuAsync(await CardAsync(page, "nu-orphan"));
        Check(!orphanMenu.Any(text => text.StartsWith("Remove from")), "a Next Up card without a series offers no removal: " + string.Join("|", orphanMenu));
        CloseMenus(page);
        Program.Log("PASS: dedicated Next Up page removes by series identity and offers nothing for a card without one.");

        // Rows without a removable source keep their menu.
        page = await OpenAsync("plain", "Recently added");
        var plain = await MenuAsync(await CardAsync(page, "plain-film"));
        Check(!plain.Any(text => text.StartsWith("Remove from")), "an ordinary section card offers no Continue Watching removal: " + string.Join("|", plain));
        CloseMenus(page);
        Program.Log("PASS: ordinary section pages keep their existing menu.");

        foreach (var type in new[] { "audiobook", "ebook", "manga" })
        {
            page = await OpenAsync("books-" + type, "Continue Reading");
            var firstId = type + "-one";
            await MenuAsync(await CardAsync(page, firstId));
            var removals = wire.Dismissals.Count;
            Invoke(OpenMenuItems(await CardAsync(page, firstId)).Single(item => item.Text.StartsWith("Remove from Continue")));
            await Until(() => wire.Dismissals.Count == removals + 1 && !Field<bool>(page, "_loading")
                && Items(page).All(item => item.ContentId != firstId), type + " dismissal");
            Check(Items(page).Select(item => item.ContentId).SequenceEqual([type + "-two"]) && Count(page) == "1",
                type + " dismissal keeps the sibling with the same SeriesId");
            CloseMenus(page);
        }
    }

    private static async Task DismissalPagingAsync(Frame frame, Wire wire)
    {
        foreach (var before in new[] { true, false })
        {
            var section = before ? "race-before" : "race-after";
            wire.ContinuationHeld = false; wire.ReadBeforeDismissal = before;
            wire.ReleaseContinuation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            frame.Navigate(typeof(CatalogPage), new CatalogNavigation("section", "Continue Watching", null, null, section));
            var page = (CatalogPage)frame.Content;
            ObservableCollection<MediaItem> Items() => Field<ObservableCollection<MediaItem>>(page, "_items");
            await Until(() => !Field<bool>(page, "_loading") && Items().Count == 60, "paging first sixty");
            var continuation = (Task)Call(page, "LoadAsync", false)!;
            try
            {
                await Until(() => wire.ContinuationHeld, "held seek sixty");
                PosterCard? first = null;
                await Until(() => (first = Descendants<PosterCard>(page).FirstOrDefault(card => card.MediaItem?.ContentId == section + "-001")) != null, "paging first card");
                Invoke((Button)first!.FindName("MoreButton"));
                await Until(() => OpenMenuItems(first).Any(item => item.Text == "Remove from Continue Watching"), "paging removal menu");
                Invoke(OpenMenuItems(first).Single(item => item.Text == "Remove from Continue Watching"));
                await Until(() => Items().All(item => item.ContentId != section + "-001"), "paging dismissal");
            }
            finally { wire.ReleaseContinuation.TrySetResult(); }
            await continuation;
            await Until(() => !Field<bool>(page, "_loading"), "paging replacement settled");
            for (var i = 0; i < 5 && Field<bool>(page, "_hasMore"); i++)
            {
                await (Task)Call(page, "LoadAsync", false)!;
                await Until(() => !Field<bool>(page, "_loading"), "paging settled");
            }
            var actual = Items().Select(item => item.ContentId).ToArray();
            var expected = Enumerable.Range(2, 129).Select(index => $"{section}-{index:000}").ToArray();
            Check(actual.Length == 129 && actual.Distinct().Count() == 129 && !expected.Except(actual).Any(),
                $"{section} complete unique window: rows={actual.Length}, unique={actual.Distinct().Count()}, missing={string.Join(',', expected.Except(actual))}");
            Check(((TextBlock)page.FindName("CountText")).Text == "129", "paging total after dismissal");
            Program.Log($"PASS: {section} all 129 remaining titles appear exactly once.");
        }

        foreach (var surface in new[] { "cw", "nu" })
        {
            var section = "off-prefix-" + surface;
            frame.Navigate(typeof(CatalogPage), new CatalogNavigation("section", "Progress", null, null, section));
            var page = (CatalogPage)frame.Content;
            ObservableCollection<MediaItem> Items() => Field<ObservableCollection<MediaItem>>(page, "_items");
            await Until(() => !Field<bool>(page, "_loading") && Items().Count == 60, "show first sixty");
            Check(((TextBlock)page.FindName("CountText")).Text == "130", "show total before dismissal");
            PosterCard? first = null;
            await Until(() => (first = Descendants<PosterCard>(page).FirstOrDefault(card => card.MediaItem?.ContentId == section + "-001")) != null, "show first card");
            Invoke((Button)first!.FindName("MoreButton"));
            await Until(() => OpenMenuItems(first).Any(item => item.Text.StartsWith("Remove from")), "show removal menu");
            Invoke(OpenMenuItems(first).Single(item => item.Text.StartsWith("Remove from")));
            // No scroll or explicit continuation: the second episode was at position 90.
            await Until(() => Items().All(item => item.ContentId != section + "-001") && !Field<bool>(page, "_loading"), "show total refresh");
            Check(((TextBlock)page.FindName("CountText")).Text == "128", "server total includes the unloaded dismissed episode");
            Check(Field<int>(page, "_offset") == Items().Count, "only loaded removals adjust the prefix offset");
            for (var i = 0; i < 5 && Field<bool>(page, "_hasMore"); i++)
                await (Task)Call(page, "LoadAsync", false)!;
            var expected = Enumerable.Range(1, 130).Where(index => index is not (1 or 90)).Select(index => $"{section}-{index:000}");
            Check(Items().Select(item => item.ContentId).SequenceEqual(expected), "show dismissal keeps every remaining title exactly once");
        }
    }

    // ----- G4 -----

    private static async Task SmartEditorAsync(Frame frame, Wire wire)
    {
        frame.BackStack.Clear(); // Saving leaves the editor through GoBack, which must not reopen an earlier scenario page.
        var page = new CollectionEditorPage(); frame.Content = page; Set(page, "_editorActive", true);
        await (Task)Call(page, "InitializeNewEditorAsync", "smart")!;
        await Task.Delay(900);
        var vm = page.ViewModel; var save = (Button)page.FindName("SaveButton");
        var footer = Field<TextBlock>(page, "_editorFooterStatus"); var status = Field<TextBlock>(page, "_smartPreviewStatus");
        vm.Name = "Smart fixture"; await Task.Delay(300);

        Check(wire.Previews.Count == 0, "a new smart collection sends no preview before a rule is complete: " + wire.Previews.Count);
        Check(!save.IsEnabled, "Create collection stays off without a complete rule");
        Check(footer.Text.Contains("Add a rule"), "the footer says a rule is needed: " + footer.Text);
        Check(status.Text.Contains("Add a rule to see what it matches"), "the preview pane asks for a rule: " + status.Text);

        vm.AddRuleCommand.Execute(null); await Task.Delay(400);
        Check(wire.Previews.Count == 0 && !save.IsEnabled, "a blank rule is not a rule: previews " + wire.Previews.Count);
        var rule = vm.RuleDefinition.Groups[0].Rules[0];

        async Task Fill(string field, string op, object? value, string label)
        {
            rule.Field = field; rule.Op = op; rule.Value = value;
            var editor = Field<QueryRulesEditor>(page, "_rulesEditor");
            ((Action?)typeof(QueryRulesEditor).GetField("Changed", Private)!.GetValue(editor))?.Invoke();
            await Task.Delay(900);
            Program.Log("TRACE smart editor " + label + " previews=" + wire.Previews.Count + " save=" + save.IsEnabled);
        }

        await Fill("genre", "is", "Drama", "text");
        Check(wire.Previews.Count == 1 && wire.Previews[0].Contains("Drama"), "a complete text rule sends one preview with the rule: " + string.Join("|", wire.Previews));
        Check(save.IsEnabled && !footer.Text.Contains("Add a rule") && !status.Text.Contains("Add a rule"), "Create is available and the hint clears");
        await Fill("year", "is", 0, "zero");
        Check(wire.Previews.Count == 2 && save.IsEnabled, "zero is a value: previews " + wire.Previews.Count);
        await Fill("watched", "is", false, "false");
        Check(wire.Previews.Count == 3 && save.IsEnabled, "false is a value: previews " + wire.Previews.Count);
        await Fill("year", "between", new object[] { 1990, "" }, "open range");
        Check(wire.Previews.Count == 3 && !save.IsEnabled && status.Text.Contains("Add a rule"), "an open range is incomplete and sends nothing: previews " + wire.Previews.Count);
        await Fill("year", "between", new object[] { 1990, 1999 }, "range");
        Check(wire.Previews.Count == 4 && save.IsEnabled, "a closed range is complete: previews " + wire.Previews.Count);

        // Incomplete saves never leave the editor.
        await Fill("genre", "is", "  ", "blank");
        var posts = wire.Creates.Count;
        await vm.SaveCommand.ExecuteAsync(null);
        Check(wire.Creates.Count == posts && vm.ErrorMessage == "Add at least one rule.", "an incomplete smart collection is not posted: " + vm.ErrorMessage);
        await Fill("genre", "is", "Drama", "restored");
        vm.RuleDefinition.Groups[0].Rules.Add(new() { Field = "year", Op = "gte", Value = "" });
        vm.RuleDefinition.Groups[0].Rules.Add(new() { Field = "year", Op = "between", Value = new object[] { 1990, "" } });
        await vm.RefreshPreviewAsync();
        using (var preview = JsonDocument.Parse(wire.Previews[^1]))
            CheckOnlyRule(preview.RootElement.GetProperty("query_definition").GetProperty("groups"), "genre", "is", "Drama");
        Check(vm.RuleDefinition.Groups[0].Rules.Count == 3, "preview sanitization keeps the editor draft intact");
        await vm.SaveCommand.ExecuteAsync(null);
        Check(wire.Creates.Count == posts + 1 && wire.Creates[^1].Contains("\"collection_type\":\"smart\"") && wire.Creates[^1].Contains("Drama"),
            "a complete smart collection is created with its rule: " + (wire.Creates.Count > posts ? wire.Creates[^1] : "(none)"));
        using (var created = JsonDocument.Parse(wire.Creates[^1]))
            CheckOnlyRule(created.RootElement.GetProperty("query_definition").GetProperty("groups"), "genre", "is", "Drama");
        Program.Log("PASS: smart editor sends no preview or save until a rule is complete; zero, false and closed ranges count, blank and open ranges do not.");
    }

    private static async Task SmartWizardAsync(Frame frame, Wire wire)
    {
        await Task.Delay(600); // the editor save above leaves through a queued GoBack
        frame.BackStack.Clear();
        Check(frame.Navigate(typeof(SmartCollectionWizardPage), new SmartCollectionWizardNavigationArgs()), "the wizard route was rejected");
        await Until(() => frame.Content is SmartCollectionWizardPage, "wizard page");
        var page = (SmartCollectionWizardPage)frame.Content;
        await Until(() => Field<bool>(page, "_loaded") && !page.ViewModel.IsLoading && page.ViewModel.Rules.Count > 0, "wizard load");
        await Task.Delay(900);
        var vm = page.ViewModel; var queries = () => wire.CatalogQueries.Count;
        var save = (Button)page.FindName("SaveButton");
        var next = (Button)page.FindName("ContinueButton");
        var ruleNeeded = (TextBlock)page.FindName("RuleNeededText");
        vm.Title = "Wizard fixture"; await Task.Delay(100);
        Check(queries() == 0, "an untouched wizard sends no catalog preview: " + queries());
        Check(!page.CanSave, "Create Collection is not available without a rule");
        Check(!next.IsEnabled, "Next: Details is unavailable without a rule");
        Check(!save.IsEnabled && ruleNeeded.Visibility == Visibility.Visible
            && ruleNeeded.Text == SmartCollectionWizardViewModel.RuleNeededMessage,
            "the disabled Save control explains the missing rule");
        var empty = (TextBlock)page.FindName("PreviewEmptyTitle");
        Check(empty.Text == "Add a rule to see what it matches", "the wizard asks for a rule: " + empty.Text);

        async Task Preview(string field, string op, object? value)
        {
            var rule = vm.Rules[0]; rule.Field = field; rule.Op = op; rule.Value = value;
            var editor = Field<QueryFilterEditor>(page, "_rulesEditor");
            ((Action?)typeof(QueryFilterEditor).GetField("Changed", Private)!.GetValue(editor))?.Invoke();
            Field<DispatcherTimer?>(page, "_previewDebounceTimer")?.Stop();
            await (Task)Call(page, "RunPreviewAsync", false)!; await Task.Delay(200);
            Check(save.IsEnabled == page.CanSave, "the actual Save control follows rule validity");
        }
        async Task ReachDetails(string scenario)
        {
            Check(next.Visibility == Visibility.Visible && next.IsEnabled, scenario + " permits Next: Details");
            Invoke(next);
            await Until(() => save.Visibility == Visibility.Visible && ((FrameworkElement)page.FindName("DetailsStepPanel")).Visibility == Visibility.Visible,
                scenario + " reaches Details");
            Check(save.IsEnabled, scenario + " exposes an enabled Save control");
        }
        await Preview("genre", "contains", "Drama");
        Check(queries() == 1 && page.CanSave, "a complete rule previews once and enables Create: queries " + queries());
        Check(save.IsEnabled && ruleNeeded.Visibility == Visibility.Collapsed, "a complete rule enables the Save control and clears its guidance");
        Check(vm.PreviewTotal == 0 && !vm.IsPreviewing && vm.ErrorMessage == null, "the successful preview has zero matches");
        await ReachDetails("a zero-match preview");
        Invoke((Button)page.FindName("BackToFiltersButton"));
        await Until(() => next.Visibility == Visibility.Visible, "back to Filters");
        vm.Title = "";
        Check(!save.IsEnabled, "clearing the title disables the Save control");
        vm.Title = "Wizard fixture";
        Check(save.IsEnabled, "restoring the title enables the Save control");
        vm.IsSaving = true;
        Check(!save.IsEnabled, "saving disables the Save control");
        vm.IsSaving = false;
        await Preview("year", "gte", 0);
        Check(queries() == 2 && page.CanSave, "zero is a value in the wizard: queries " + queries());
        await Preview("watched", "is", false);
        Check(queries() == 3 && page.CanSave, "false is a value in the wizard: queries " + queries());
        await Preview("year", "between", new object[] { 1990, "" });
        Check(queries() == 3 && !page.CanSave, "an open range sends nothing and blocks Create: queries " + queries());
        Check(!next.IsEnabled, "an incomplete range blocks Next: Details");
        await Preview("genre", "contains", "");
        Check(queries() == 3 && !page.CanSave, "a blank rule sends nothing: queries " + queries());
        Check(!next.IsEnabled, "a blank rule blocks Next: Details");

        var posts = wire.Creates.Count;
        await vm.SaveAsync();
        Check(wire.Creates.Count == posts && !string.IsNullOrEmpty(vm.ErrorMessage), "saving without a rule posts nothing: " + vm.ErrorMessage);
        // Guided filters for a different field leave the wizard's initial empty genre row behind.
        vm.RuleDefinition.Groups[0].Rules.Add(new() { Field = "genre", Op = "contains", Value = "" });
        await Preview("year", "gte", 2000);
        var groupsJson = Uri.UnescapeDataString(wire.CatalogQueries[^1].Split("&groups=", 2)[1].Split('&')[0]);
        using (var preview = JsonDocument.Parse(groupsJson))
            CheckOnlyRule(preview.RootElement, "year", "gte", "2000");
        Check(vm.RuleDefinition.Groups[0].Rules.Count == 2, "preview sanitization keeps the wizard draft intact");

        wire.FailCatalogPreview = true;
        try { await Preview("genre", "contains", "Drama"); }
        finally { wire.FailCatalogPreview = false; }
        Check(vm.PreviewTotal == 0 && !vm.IsPreviewing && vm.ErrorMessage?.StartsWith("Preview failed:") == true,
            "the preview failure leaves zero matches and an error");
        await ReachDetails("a failed preview");
        Invoke(save);
        await Until(() => wire.Creates.Count == posts + 1 && !vm.IsSaving, "wizard collection created from Details");
        Check(wire.Creates.Count == posts + 1 && wire.Creates[^1].Contains("Drama"), "a complete wizard rule is posted: " + (wire.Creates.Count > posts ? wire.Creates[^1] : "(none)"));
        using (var created = JsonDocument.Parse(wire.Creates[^1]))
            CheckOnlyRule(created.RootElement.GetProperty("query_definition").GetProperty("groups"), "genre", "contains", "Drama");
        Program.Log("PASS: smart wizard previews and saves only with a complete rule (zero/false valid, blank and open ranges invalid).");
    }

    // ----- G5 -----

    private static async Task TrendingShowAsync(StackPanel parent)
    {
        var config = new Dictionary<string, object> { ["source"] = "tmdb", ["window"] = "week" };
        var host = new StackPanel { Width = 600 }; var validation = new TextBlock();
        var build = typeof(RecipeGalleryDialog).GetMethod("BuildParameterFieldsAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        await (Task)build.Invoke(null, [new RecipeDefinition { Type = "trending_discover" }, config, host, validation])!;
        parent.Children.Add(host); await Task.Delay(100);
        try
        {
            var show = Descendants<ComboBox>(host).Single(combo => combo.Header?.ToString() == "Show");
            Check(show.Items.Select(item => item.ToString()).SequenceEqual(["Both", "Movies", "TV shows"]), "Show offers Both, Movies and TV shows");
            Check(show.SelectedIndex == 0 && !config.ContainsKey("filter_type"), "Both is selected and leaves filter_type unset");
            show.SelectedIndex = 1;
            Check(config["filter_type"].ToString() == "movie", "Movies saves filter_type=movie");
            show.SelectedIndex = 2;
            Check(config["filter_type"].ToString() == "series", "TV shows saves filter_type=series");
            show.SelectedIndex = 0;
            Check(!config.ContainsKey("filter_type") && config["window"].ToString() == "week" && config["source"].ToString() == "tmdb", "Both removes filter_type and keeps the period");

            var stored = new Dictionary<string, object> { ["window"] = "day", ["filter_type"] = JsonSerializer.Deserialize<JsonElement>("\"series\"") };
            var storedHost = new StackPanel { Width = 600 };
            await (Task)build.Invoke(null, [new RecipeDefinition { Type = "trending_discover" }, stored, storedHost, validation])!;
            parent.Children.Add(storedHost); await Task.Delay(100);
            var storedShow = Descendants<ComboBox>(storedHost).Single(combo => combo.Header?.ToString() == "Show");
            Check(storedShow.SelectedIndex == 2 && stored.ContainsKey("filter_type"), "a saved TV shows row opens on TV shows without rewriting it");
            parent.Children.Remove(storedHost);
        }
        finally { parent.Children.Remove(host); }
        Program.Log("PASS: Trending worldwide shows the Show selector, writes/clears filter_type and opens a saved value untouched.");
    }

    // ----- G9 -----

    private static async Task CalendarBadgesAsync(Frame frame, CalendarViewModel model)
    {
        model.Days.Clear();
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        model.Days.Add(new() { Date = today, Items = [
            new() { ContentId = "digital-film", Type = "movie", Title = "Digital Feature", Badges = ["digital"] },
            new() { ContentId = "physical-film", Type = "movie", Title = "Physical Feature", Badges = ["physical"] },
            new() { ContentId = "finale-ep", Type = "episode", Title = "Finale Show", Badges = ["finale"] },
            new() { ContentId = "premiere-ep", Type = "episode", Title = "Premiere Show", Badges = ["series_premiere"] }] });
        frame.Navigate(typeof(CalendarPage)); var page = (CalendarPage)frame.Content;
        await Until(() => Descendants<TextBlock>(page).Any(text => text.Text == "PHYSICAL"), "calendar badges"); await Task.Delay(200);

        (Border Pill, TextBlock Label) Pill(string label)
        {
            var text = Descendants<TextBlock>(page).First(candidate => candidate.Text == label);
            return ((Border)VisualTreeHelper.GetParent(text), text);
        }
        static string Rgba(Brush brush) => brush is SolidColorBrush { Color: var c } ? $"{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}" : "?";
        void Expect(string label, string background, string outline, string foreground)
        {
            var (pill, text) = Pill(label);
            Check(Rgba(pill.Background) == background && Rgba(pill.BorderBrush) == outline && Rgba(text.Foreground) == foreground,
                $"{label} pill uses the WebUI palette: bg={Rgba(pill.Background)} border={Rgba(pill.BorderBrush)} text={Rgba(text.Foreground)}");
        }
        Expect("DIGITAL", "E6082F49", "8C075985", "FFF0F9FF");
        Expect("PHYSICAL", "E6022C22", "8C065F46", "FFECFDF5");
        Expect("FINALE", "E6431407", "8C9A3412", "FFFFF7ED");
        var premiere = Pill("PREMIERE");
        Check(Rgba(premiere.Pill.Background) != "E6082F49", "premiere keeps its accent style");
        Program.Log("PASS: Calendar draws labelled Digital and Physical release badges in the WebUI sky and emerald palettes, keeping Finale and Premiere.");
    }

    // ----- G11 -----

    private static async Task FailedSyncRefreshAsync(SiloApiClient client, CollectionsApi collections, CatalogApi catalog, SettingsApi settings, AuthService auth, Wire wire)
    {
        CollectionEditorViewModel NewViewModel() => new(collections, catalog, settings, auth);
        async Task<CollectionEditorViewModel> LoadedAsync()
        {
            wire.SyncState = "ok"; wire.FailSync = true; wire.HoldCollectionRead = null;
            var loaded = NewViewModel(); await loaded.LoadExistingCommand.ExecuteAsync("synced");
            Check(loaded.LastSyncSummary == "ok · Matched 12 titles", "the saved status loads: " + loaded.LastSyncSummary);
            return loaded;
        }

        // The recorded failure appears after a failed Sync now, and unsaved edits remain.
        var vm = await LoadedAsync();
        vm.Name = "Edited while syncing";
        wire.SyncState = "failed";
        await vm.SyncNowCommand.ExecuteAsync(null);
        Check(vm.ErrorMessage?.StartsWith("Sync failed") == true, "the failed sync is reported: " + vm.ErrorMessage);
        Check(vm.LastSyncSummary == "failed · The MDBList list is private", "the saved status shows the recorded failure: " + vm.LastSyncSummary);
        Check(vm.SourceLastNote == "The MDBList list is private" && vm.Name == "Edited while syncing", "the note refreshes and the unsaved name is kept");

        // A read that returns after the editor left the collection is dropped.
        vm = await LoadedAsync(); wire.SyncState = "failed"; wire.HoldCollectionRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var departed = vm.SyncNowCommand.ExecuteAsync(null);
        await Until(() => wire.CollectionReadsHeld > 0, "held re-read");
        vm.InvalidateEditorLoads();
        wire.HoldCollectionRead.SetResult(); await departed;
        Check(vm.LastSyncSummary == "ok · Matched 12 titles", "a re-read that returns after the editor moved on is ignored: " + vm.LastSyncSummary);

        // So is one that returns after the account or profile authority changed.
        vm = await LoadedAsync(); wire.SyncState = "failed"; wire.HoldCollectionRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var retired = vm.SyncNowCommand.ExecuteAsync(null);
        await Until(() => wire.CollectionReadsHeld > 1, "second held re-read");
        client.InvalidateAccessContext();
        wire.HoldCollectionRead.SetResult(); await retired;
        Check(vm.LastSyncSummary == "ok · Matched 12 titles", "a re-read that returns after the access context changed is ignored: " + vm.LastSyncSummary);
        wire.HoldCollectionRead = null;
        Program.Log("PASS: a failed Sync now re-reads the recorded status without discarding unsaved edits, and stale re-reads are dropped.");
    }

    // ----- G12 -----

    private static async Task EpisodeLinkScopeAsync(NavigationService navigation, Frame frame)
    {
        var page = new ItemDetailPage { Width = 900, Height = 700 };
        var root = new Grid(); var cards = new Grid { Width = 400, Height = 400, HorizontalAlignment = HorizontalAlignment.Right };
        root.Children.Add(page); root.Children.Add(cards); frame.Content = root; await Task.Delay(150);
        page.ViewModel.Item = new() { ContentId = "show", Type = "series", Title = "Show" };
        typeof(ItemDetailViewModel).GetProperty("LibraryId")!.SetValue(page.ViewModel, 7);
        Type? destination = null; object? arguments = null;
        navigation.NavigationRequestHandler = (type, args) => { destination = type; arguments = args; return true; };
        try
        {
            async Task<object?> ClickAsync(Episode episode)
            {
                destination = null; arguments = null;
                var card = (FrameworkElement)typeof(ItemDetailPage).GetMethod("CreateEpisodeCard", Private)!.Invoke(page, [episode, false])!;
                cards.Children.Clear(); cards.Children.Add(card);
                await Task.Delay(150);
                Invoke(Descendants<Button>(card).First(button => button.Tag?.ToString() == episode.ContentId));
                await Until(() => destination != null, "episode navigation");
                return arguments;
            }
            var withFile = await ClickAsync(new Episode { ContentId = "ep-file", SeasonNumber = 1, EpisodeNumber = 1, Title = "Has a file", Files = [new EpisodeFile { FileId = 4 }] });
            Check(withFile is ItemDetailNavigationArgs { ContentId: "ep-file", LibraryId: 7 }, "an episode with a file opens inside the library: " + Describe(withFile));
            var withoutFile = await ClickAsync(new Episode { ContentId = "ep-bare", SeasonNumber = 1, EpisodeNumber = 2, Title = "No file here", Files = [] });
            Check(withoutFile is string { Length: > 0 } id && id == "ep-bare", "an episode with no file opens without the library: " + Describe(withoutFile));
        }
        finally { navigation.NavigationRequestHandler = null; }
        Program.Log("PASS: episode cards keep the library only when the list entry has a file.");
    }

    // ----- helpers -----

    private static void CheckOnlyRule(JsonElement groups, string field, string op, string value)
    {
        Check(groups.GetArrayLength() == 1, "only the populated group is sent");
        var rules = groups[0].GetProperty("rules");
        Check(rules.GetArrayLength() == 1, "blank and incomplete rows are not sent alongside a complete rule");
        Check(rules[0].GetProperty("field").GetString() == field && rules[0].GetProperty("op").GetString() == op
            && rules[0].GetProperty("value").ToString() == value, "the filled rule survives sanitization");
    }

    private static string Describe(object? value) => value switch { null => "null", string text => "'" + text + "'", _ => value.ToString() ?? "?" };
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("Viewer actions: " + message); }
    private static async Task Until(Func<bool> ready, string what)
    {
        for (var attempt = 0; attempt < 240 && !ready(); attempt++) await Task.Delay(25);
        if (!ready()) throw new InvalidOperationException("Viewer actions did not reach: " + what);
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Invoke(MenuFlyoutItem item) => ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<MenuFlyoutItem> OpenMenuItems(UIElement owner)
        => VisualTreeHelper.GetOpenPopupsForXamlRoot(owner.XamlRoot).SelectMany(popup => Descendants<MenuFlyoutItem>(popup.Child));
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, Func<object>> factories) : IServiceProvider
    {
        public object? GetService(Type type) => factories.TryGetValue(type, out var factory) ? factory() : fallback.GetService(type);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal bool ContinuationHeld, ReadBeforeDismissal;
        internal bool FailCatalogPreview;
        internal TaskCompletionSource ReleaseContinuation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<(string Path, string Body)> Dismissals = [];
        internal readonly List<string> Previews = [], Creates = [], CatalogQueries = [];
        internal string SyncState = "ok"; internal bool FailSync = true;
        internal TaskCompletionSource? HoldCollectionRead; internal int CollectionReadsHeld;
        private readonly HashSet<string> _dismissed = [];
        private static readonly JsonSerializerOptions Snake = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

        private static object Card(string id, string type, string title, string? series = null, string? source = null, string? stamp = null)
            => new { content_id = id, type, title, series_id = series, series_title = series == null ? null : "Show", item_source = source, progress_updated_at = stamp, user_state = new { played = false } };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "viewer-actions.invalid") throw new InvalidOperationException("Unexpected external network.");
            var path = request.RequestUri.AbsolutePath; var query = request.RequestUri.Query;
            var status = HttpStatusCode.OK; object body = new { items = Array.Empty<object>(), page = new { has_more = false } };
            string? text = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);

            if (path.StartsWith("/api/v2/home/dismissals/"))
            {
                Dismissals.Add((path, text ?? ""));
                var itemId = path[(path.LastIndexOf('/') + 1)..]; _dismissed.Add(itemId);
                if (itemId.StartsWith("show-")) { _dismissed.Add("show-a"); _dismissed.Add("show-b"); }
                if (itemId.StartsWith("off-prefix-") && itemId.EndsWith("-001"))
                    _dismissed.Add(itemId[..^3] + "090");
                return new(HttpStatusCode.NoContent);
            }
            if (path == "/api/v2/catalog")
            {
                if (query.Contains("source=query"))
                {
                    CatalogQueries.Add(query);
                    if (FailCatalogPreview) return new(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("{\"code\":\"dependency_unavailable\",\"message\":\"Preview unavailable\"}")
                    };
                }
                var section = Query(query, "section_id");
                if (section.StartsWith("race-") || section.StartsWith("off-prefix-"))
                {
                    var seek = int.Parse(Query(query, "seek")); var limit = int.Parse(Query(query, "limit"));
                    string[] Row() => Enumerable.Range(1, 130).Select(index => $"{section}-{index:000}").Where(id => !_dismissed.Contains(id)).ToArray();
                    var row = Row();
                    if (section.StartsWith("race-") && seek == 60 && !ContinuationHeld)
                    {
                        ContinuationHeld = true;
                        // Intentionally ignore cancellation so acceptance must retire the generation too.
                        await ReleaseContinuation.Task;
                        if (!ReadBeforeDismissal) row = Row();
                    }
                    var show = section.StartsWith("off-prefix-");
                    var window = row.Skip(seek).Take(limit).Select(id => Card(id, show ? "episode" : "movie", id,
                        show ? (id.EndsWith("-001") || id.EndsWith("-090") ? section + "-show" : id + "-show") : null,
                        section.EndsWith("-nu") ? "next_up" : "in_progress", "2026-11-09T10:00:00Z")).ToArray();
                    return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items = window, total = row.Length, has_more = seek + window.Length < row.Length, total_exact = true, snapshot = "fixture" }, Snake)) };
                }
                object[] items = section switch
                {
                    "cw" => [Card("film", "movie", "Resume film", null, "in_progress", "2026-11-09T10:00:00Z"),
                        Card("show-a", "episode", "Show A", "show", "in_progress", "2026-11-09T11:00:00Z"),
                        Card("show-b", "episode", "Show B", "show", "in_progress", "2026-11-09T12:00:00Z"),
                        Card("no-stamp", "movie", "No stamp", null, "in_progress", null)],
                    "nu" => [Card("nu-ep", "episode", "Next episode", "nu-show", "next_up"), Card("nu-orphan", "episode", "Orphan episode", null, "next_up")],
                    "plain" => [Card("plain-film", "movie", "Plain film")],
                    _ when section.StartsWith("books-") => [
                        Card(section[6..] + "-one", section[6..], "First book", "books", "in_progress", "2026-11-09T10:00:00Z"),
                        Card(section[6..] + "-two", section[6..], "Second book", "books", "in_progress", "2026-11-09T10:00:00Z")],
                    _ => [],
                };
                items = items.Where(item => !_dismissed.Contains(JsonSerializer.SerializeToElement(item, Snake).GetProperty("content_id").GetString()!)).ToArray();
                var total = items.Length;
                var offset = int.TryParse(Query(query, "seek"), out var parsedOffset) ? parsedOffset : 0;
                var pageSize = int.TryParse(Query(query, "limit"), out var parsedLimit) ? parsedLimit : 60;
                items = items.Skip(offset).Take(pageSize).ToArray();
                body = new { items, total, has_more = offset + items.Length < total, total_exact = true, snapshot = "fixture" };
            }
            else if (path == "/api/v2/collections/preview") { Previews.Add(text ?? ""); body = new { items = Array.Empty<object>(), total = 0 }; }
            else if (path == "/api/v2/collections" && request.Method == HttpMethod.Post)
            {
                Creates.Add(text ?? "");
                body = new { id = "created", name = "Created", collection_type = "smart", creator_profile_id = "fixture" };
            }
            else if (path == "/api/v2/collections/capabilities") body = new { imports = true, import_sources = new[] { "mdblist" }, item_reorder = true };
            else if (path == "/api/v2/collections/synced/sync")
            {
                if (FailSync) { status = HttpStatusCode.ServiceUnavailable; body = new { code = "dependency_unavailable", message = "The MDBList list is private" }; }
                else body = new { status = "ok", message = "Matched", items_matched = 12 };
            }
            else if (path == "/api/v2/collections/synced")
            {
                if (HoldCollectionRead != null) { CollectionReadsHeld++; await HoldCollectionRead.Task; }
                var failed = SyncState == "failed";
                body = new
                {
                    id = "synced", name = "Synced fixture", collection_type = "mdblist", creator_profile_id = "fixture",
                    source_url = "https://mdblist.com/lists/fixture/list/", source_config = new { max_items = 100 }, sync_schedule = "daily",
                    last_sync_at = failed ? "2026-11-10T09:00:00Z" : "2026-11-09T09:00:00Z",
                    last_sync_status = failed ? "failed" : "ok", last_sync_message = failed ? "The MDBList list is private" : "Matched 12 titles",
                    item_count = 12, updated_at = "2026-11-10T00:00:00Z",
                };
            }
            else if (path == "/api/v2/profiles") body = new { items = new[] { new { id = "fixture", name = "Primary", is_primary = true } } };

            var reply = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body, Snake)) };
            reply.Headers.ETag = new("\"viewer-actions\"");
            return reply;
        }

        private static string Query(string query, string key)
            => query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).Where(pair => pair[0] == key).Select(pair => Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : "")).FirstOrDefault() ?? "";
    }
}
