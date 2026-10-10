using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

// Finite original E05/E06 acceptance. Uses only a fake API and its own Window.
internal static class CollectionsAcceptanceNativeFixture
{
    private static readonly List<string> Failures = [];
    internal static async Task RunAsync(StackPanel unused)
    {
        Failures.Clear();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        await using var artwork = new ArtworkAsset(); Wire.PosterUrl = artwork.Url;
        using var artworkHttp = new HttpClient(new ArtworkOnlyHandler(artwork.Url));
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "collection-image-cache"));
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://collections-acceptance.invalid");
        Check(client.ResolveServerUrl("/artwork/template.png") == "https://collections-acceptance.invalid/artwork/template.png", "relative server artwork URI resolution changed");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("fixture", "fixture", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Primary", IsPrimary = true });
        var catalog = new CatalogApi(client); var collections = new CollectionsApi(client); var settings = new SettingsApi(client);
        var navigation = new NavigationService(); var frame = new Frame { Width = 1280, Height = 900 };
        var window = new Window { Content = frame }; navigation.Frame = frame;
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = () => client, [typeof(AuthService)] = () => auth,
            [typeof(CatalogApi)] = () => catalog, [typeof(CollectionsApi)] = () => collections, [typeof(SettingsApi)] = () => settings,
            [typeof(NavigationService)] = () => navigation, [typeof(ToastService)] = () => new ToastService(),
            [typeof(HttpClient)] = () => artworkHttp, [typeof(ImageService)] = () => images,
            [typeof(CollectionsViewModel)] = () => new CollectionsViewModel(collections, catalog),
            [typeof(CollectionEditorViewModel)] = () => new CollectionEditorViewModel(collections, catalog, settings, auth),
        }));
        try
        {
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1280, 900)); window.AppWindow.Show(false);
            await Task.Delay(160);
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_COLLECTIONS_QUEUED_ARTWORK") == "1")
            {
                var queued = new TaskCompletionSource<ImageSource>(TaskCreationOptions.RunContinuationsAsynchronously);
                frame.DispatcherQueue.TryEnqueue(() =>
                {
                    var context = SynchronizationContext.Current;
                    Program.Log("TRACE dispatcher artwork context=" + (context?.GetType().Name ?? "none"));
                    SynchronizationContext.SetSynchronizationContext(null);
                    try { queued.SetResult((ImageSource)new SiloPlayer.Converters.UrlToImageSourceConverter().Convert(artwork.Url, typeof(ImageSource), null!, "")); }
                    catch (Exception error) { queued.SetException(error); }
                    finally { SynchronizationContext.SetSynchronizationContext(context); }
                });
                var source = await queued.Task;
                var outcome = SiloPlayer.Converters.UrlToImageSourceConverter.GetLoadOutcome(source);
                Check(outcome != null && await outcome.WaitAsync(TimeSpan.FromSeconds(10)),
                    "artwork created by a UI dispatcher callback without a synchronization context failed to decode");
                Check(source is BitmapImage { PixelWidth: > 0 }, "queued decoded artwork has no pixels");
                Program.Log("PASS: dispatched artwork decode retains its owner UI thread.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_COLLECTIONS_DISCOVERY_ONLY") == "1")
            {
                await DiscoveryAsync(frame, window, wire);
                Check(Failures.Count == 0, string.Join("; ", Failures));
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_COLLECTIONS_REORDER_DRAFT") == "1")
            {
                await Viewport(frame, window, 1280);
                var page = new CollectionEditorPage(); frame.Content = page; Set(page, "_editorActive", true);
                await (Task)Call(page, "InitializeNewEditorAsync", "manual")!;
                var writes = Wire.MutationCalls;
                await page.ViewModel.AddManualItemCommand.ExecuteAsync(new() { ContentId = "one", Title = "First", Type = "movie" });
                await page.ViewModel.AddManualItemCommand.ExecuteAsync(new() { ContentId = "two", Title = "Second", Type = "series" });
                await Layout();
                var rows = (StackPanel)page.FindName("ManualItemsPanel");
                Check(All<Button>(rows).Count(b => AutomationProperties.GetName(b).StartsWith("Move ") && b.Visibility == Visibility.Visible) == 2,
                    "new manual draft does not expose both drag handles");
                Check(rows.Children.OfType<Border>().All(b => b.Background is SolidColorBrush),
                    "manual drop rows have no hit-testable background across their empty space");
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_COLLECTIONS_PHYSICAL_ORDER") == "1")
                {
                    foreach (var handle in All<Button>(rows).Where(b => AutomationProperties.GetName(b).StartsWith("Move ")))
                    {
                        handle.GotFocus += (_, _) => Program.Log("PHYSICAL focus " + AutomationProperties.GetName(handle));
                        handle.DragStarting += (_, _) => Program.Log("PHYSICAL drag " + AutomationProperties.GetName(handle));
                        handle.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, args) => Program.Log("PHYSICAL key " + args.Key + " handled=" + args.Handled)), true);
                    }
                    window.Title = "Manual collection physical verification";
                    window.AppWindow.Move(new(400, 160)); window.Activate();
                    var deadline = DateTime.UtcNow.AddSeconds(110);
                    while (page.ViewModel.ManualItems[0].MediaItemId != "two" && DateTime.UtcNow < deadline) await Task.Delay(100);
                    Check(page.ViewModel.ManualItems[0].MediaItemId == "two", "physical keyboard did not reorder the second title");
                    await Layout();
                    foreach (var handle in All<Button>(rows).Where(b => AutomationProperties.GetName(b).StartsWith("Move ")))
                    {
                        handle.DragStarting += (_, args) => Program.Log("PHYSICAL drag-start " + AutomationProperties.GetName(handle) + " allowed=" + args.AllowedOperations);
                        handle.DropCompleted += (_, args) => Program.Log("PHYSICAL drag-result " + args.DropResult);
                        handle.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) => Program.Log("PHYSICAL pointer-down " + AutomationProperties.GetName(handle) + " left=" + args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)), true);
                        var moves = 0;
                        handle.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) => { if (args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed && moves++ < 4) Program.Log("PHYSICAL pointer-move " + AutomationProperties.GetName(handle)); }), true);
                    }
                    foreach (var drop in rows.Children.OfType<Border>().Where(b => b.AllowDrop))
                    { drop.DragOver += (_, _) => Program.Log("PHYSICAL drag-over"); drop.Drop += (_, _) => Program.Log("PHYSICAL drop"); }
                    Program.Log("PHYSICAL keyboard order accepted; drag Second below First.");
                    while (page.ViewModel.ManualItems[0].MediaItemId != "one" && DateTime.UtcNow < deadline) await Task.Delay(100);
                    Check(page.ViewModel.ManualItems[0].MediaItemId == "one" && Wire.MutationCalls == writes, "physical drag did not restore local order or wrote remotely");
                    Program.Log("PASS: physical keyboard and drag manual reorder with no remote mutation."); return;
                }
                await page.ViewModel.MoveManualItemAsync(1, 0); await Layout();
                Check(page.ViewModel.ManualItems[0].MediaItemId == "two" && Wire.MutationCalls == writes, "draft reorder wrote remotely or retained the old order");
                Check(All<TextBlock>(rows).Any(t => t.Text == "Drag, or focus a handle and press Space, then ↑ or ↓."), "manual reorder keyboard help is absent");
                await Capture(frame, "manual-reorder-draft.png");
                Program.Log("PASS: manual draft handles, local order and keyboard help."); return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_COLLECTIONS_LOOK") == "1")
            {
                foreach (var width in new[] { 1280, 460 })
                {
                    var page = await Editor(frame, window, "imported"); await Viewport(frame, window, width); await Layout();
                    var look = (Border)Read(page, "_lookPanel")!; look.StartBringIntoView(); await Layout();
                    var toggle = (Button)Read(page, "_lookToggle")!;
                    Program.Log($"TRACE: Look/{width}: panel={look.ActualWidth}x{look.ActualHeight}, alignment={toggle.HorizontalContentAlignment}.");
                    Probe(Math.Abs(look.ActualHeight - 82) < 1.1 && toggle.HorizontalContentAlignment == HorizontalAlignment.Stretch,
                        $"Look/{width}: collapsed artwork row must retain its82px frame and right-aligned Change action");
                    var sort = (ComboBox)page.FindName("ImportedDefaultSortCombo");
                    Probe((sort.SelectedItem as ComboBoxItem)?.Content as string == "List order", "saved Synced sort label must survive the final option rebuild");
                    await Capture(frame, $"collection-look-{width}.png");
                    frame.Content = null;
                }
                if (Failures.Count != 0) throw new InvalidOperationException(string.Join("; ", Failures));
                Program.Log("PASS: COLLECTION_LOOK_COMPLETED collapsed artwork spacing and saved list order.");
                return;
            }
            await Case("current chooser rendered contract", () => TemplateAsync(frame, window));
            await Case("current chooser capability states", () => ChooserStatesAsync(frame, window, wire));
            await Case("MDBList actual debounce/clear supersession", () => DiscoveryAsync(frame, window, wire));
            await Case("manual draft visible responsive actions", () => ManualDraftAsync(frame, window));
            await Case("manual title search keyboard/recovery", () => ManualSearchAsync(frame, window, wire));
            await Case("smart current rules and live preview", () => SmartDraftAsync(frame, window, wire));
            await Case("manual actual failed add/remove retention", () => ManualAsync(frame, window, wire));
            await Case("imported successful Save stays clean", () => ImportedSaveAsync(frame, window, wire, navigation));
            await Case("locked Discover and Trakt presentation", () => LockedSourcesAsync(frame, window));
            await Case("artwork draft preview and Delete Cancel", () => ArtworkAsync(frame, window, wire));
            if (Failures.Count != 0) throw new InvalidOperationException(string.Join("; ", Failures));
            Program.Log("PASS: finite collection template, discovery, mutation, imported Save and artwork acceptance.");
        }
        finally { wire.SearchGate?.TrySetResult(Reply(new { configured = true, items = Array.Empty<object>() })); wire.PosterGate?.TrySetResult(Reply(new { message = "Fixture teardown" }, HttpStatusCode.UnprocessableEntity)); if (frame.Content is CollectionEditorPage pending) for (var n = 0; n < 40 && pending.ViewModel.IsSaving; n++) await Task.Delay(25); frame.Content = null; navigation.Frame = null; window.Close(); field.SetValue(null, previous); }
    }
    private static async Task Case(string name, Func<Task> action)
    { var previousFailures = Failures.Count; try { await action(); Program.Log((Failures.Count == previousFailures ? "PASS case " : "FAIL checks ") + name); } catch (Exception ex) { Failures.Add(name + ": " + ex.Message); Program.Log("FAIL case " + name + ": " + ex); } }
    private static async Task TemplateAsync(Frame frame, Window window)
    {
        var navigation = SiloPlayer.App.Services.GetRequiredService<NavigationService>();
        Type? destination = null; object? route = null;
        navigation.NavigationRequestHandler = (type, parameter) => { destination = type; route = parameter; return true; };
        try
        {
            foreach (var width in new[] { 1280, 460 })
            foreach (var kind in new[] { "manual", "smart", "synced" })
            {
                await Viewport(frame, window, width); var page = new CollectionsPage(); frame.Content = page; await Layout();
                var pageTitle = (TextBlock)page.FindName("CollectionsTitle");
                var pageSubtitle = (TextBlock)page.FindName("CollectionsSubtitle");
                var titleOrigin = pageTitle.TransformToVisual(page).TransformPoint(new(0, 0));
                var subtitleOrigin = pageSubtitle.TransformToVisual(page).TransformPoint(new(0, 0));
                Program.Log($"TRACE collection header {width}: title={titleOrigin.X},{titleOrigin.Y}/{pageTitle.ActualHeight}, subtitle={subtitleOrigin.X},{subtitleOrigin.Y}/{pageSubtitle.ActualHeight}, header={((Grid)page.FindName("CollectionsHeaderGrid")).ActualHeight}.");
                Probe(Math.Abs(titleOrigin.Y - (width >= 1024 ? 56 : 32)) < 1 && Math.Abs(titleOrigin.X - subtitleOrigin.X) < 1,
                    "collection page title/subtitle must share the left edge and current outer top gutter");
                Probe(Math.Abs(((Grid)page.FindName("CollectionsHeaderGrid")).ActualHeight - (pageTitle.ActualHeight + 12 + pageSubtitle.ActualHeight)) < 1,
                    "collection header reserves space for an unused row");
                destination = null; route = null;
                var initialWrites = Wire.MutationCalls;
                Click(width < 1024 ? All<Button>((DependencyObject)page.FindName("CreateCollectionDock")).Single() : (Button)page.FindName("CreateButton"));
                await Until(() => Read(page, "_creationDialog") is ContentDialog dialog && All<Button>(dialog).Any(button => AutomationProperties.GetName(button) == "Synced list"));
                var dialog = (ContentDialog)Read(page, "_creationDialog")!;
                try
                {
                    await Layout();
                    var shell = All<Border>(dialog).Single(border => border.Name == "BackgroundElement");
                    var dialogTitle = (TextBlock)dialog.Title;
                    var titleInset = dialogTitle.TransformToVisual(shell).TransformPoint(new(0, 0));
                    Program.Log($"TRACE collection chooser {width}: titleInset={titleInset.X},{titleInset.Y}/{dialogTitle.ActualHeight}, shell={shell.ActualWidth}x{shell.ActualHeight}.");
                    Probe(Math.Abs(titleInset.Y - (width < 640 ? 21 : 25)) < 1 && Math.Abs(dialogTitle.ActualHeight - 28) < 1,
                        "collection chooser title must have the current20/24px top inset and28px line box");
                    Probe(Math.Abs(shell.ActualWidth - (width >= 1024 ? Math.Min(1000, width - 48) : width)) < 1 && shell.CornerRadius.TopLeft == 20, "current choice dialog width/corners differ from desktop1000px or narrow full-width source role");
                    var close = All<Button>(dialog).Single(button => AutomationProperties.GetName(button) == "Close");
                    Check(close.Width == (width >= 1024 ? 34 : 44), "current chooser corner close target does not follow its source breakpoint");
                    var buttons = All<Button>(dialog).Where(button => new[] { "Manual", "Smart", "Synced list" }.Contains(AutomationProperties.GetName(button))).ToArray();
                    Check(buttons.Length == 3 && buttons.All(button => button.IsEnabled), "current chooser did not expose all three capabilities-backed destinations");
                    Check(buttons.All(button => button.CornerRadius.TopLeft == 18), "current chooser card18px radius changed");
                    if (width >= 1024)
                        Probe(buttons.Max(button => button.ActualHeight) - buttons.Min(button => button.ActualHeight) < 1,
                            "desktop collection choices must fill one equally tall row");
                    foreach (var button in buttons)
                    {
                        Check(All<TextBlock>(button).Any(text => text.FontSize == 17 && text.FontWeight.Weight == 600), "current chooser title17px semibold missing");
                        Check(All<Border>(button).Any(border => border.Width == 32 && border.Height == 32 && border.CornerRadius.TopLeft == 9), "current chooser icon32px tile missing");
                        var stage = All<Border>(button).Single(border => border.Height == 168);
                        Check(stage.Visibility == (width >= 1024 ? Visibility.Visible : Visibility.Collapsed), "current chooser decorative stage does not follow desktop breakpoint");
                    }
                    if (kind == "manual") await Capture(dialog, $"collections-new-chooser-{width}.png");
                    var chosen = buttons.Single(button => AutomationProperties.GetName(button) == (kind == "manual" ? "Manual" : kind == "smart" ? "Smart" : "Synced list"));
                    Click(chosen); await Until(() => destination != null);
                    Check(Wire.MutationCalls == initialWrites && destination == typeof(CollectionEditorPage) && (kind == "manual" ? route == null : route is CollectionEditorNavigationArgs args && args.Kind == kind), "chooser did not navigate to the selected editor draft");
                }
                finally { dialog.Hide(); await Layout(); frame.Content = null; }
            }
        }
        finally { navigation.NavigationRequestHandler = null; }
    }
    private static async Task ChooserStatesAsync(Frame frame, Window window, Wire wire)
    {
        await Viewport(frame, window, 460); frame.Content = new Grid(); await Layout();
        async Task ShowState(bool reject, string[] sources, Func<ContentDialog, Task> inspect)
        {
            wire.RejectCapabilities = reject; wire.ImportSources = sources;
            var dialog = new SiloPlayer.Controls.NewCollectionDialog(SiloPlayer.App.Services.GetRequiredService<CollectionsApi>(), SiloPlayer.App.Services.GetRequiredService<SiloApiClient>()) { XamlRoot = frame.XamlRoot };
            var showing = dialog.ShowAsync().AsTask();
            try { await inspect(dialog); }
            finally { dialog.Hide(); await showing; }
        }
        try
        {
            await ShowState(false, [], async dialog =>
            {
                await Until(() => All<TextBlock>(dialog).Any(text => text.Text == "Synced lists are off on this server."));
                Check(!All<Button>(dialog).Any(button => AutomationProperties.GetName(button) == "Synced list"), "Synced off card remains an interactive destination");
                Check(All<Border>(dialog).Any(border => AutomationProperties.GetName(border) == "Synced list" && Math.Abs(border.Opacity - .6) < .000001 && border.CornerRadius.TopLeft == 18), "Synced off card does not use the current quiet disabled appearance");
            });
            await ShowState(true, ["mdblist"], async dialog =>
            {
                await Until(() => All<Button>(dialog).Any(button => Equals(button.Content, "Retry")));
                var retry = All<Button>(dialog).Single(button => Equals(button.Content, "Retry"));
                Check(retry.Height == 32 && retry.IsEnabled, "capability failure omitted the source retry action");
                wire.RejectCapabilities = false; Click(retry);
                await Until(() => All<Button>(dialog).Any(button => AutomationProperties.GetName(button) == "Synced list"));
            });
            await ShowState(false, ["future-source"], async dialog =>
            {
                await Until(() => All<Button>(dialog).Any(button => AutomationProperties.GetName(button) == "Synced list"));
                Check(All<Button>(dialog).Single(button => AutomationProperties.GetName(button) == "Synced list").IsEnabled, "chooser incorrectly treats an advertised import source as Synced off");
            });
        }
        finally { wire.RejectCapabilities = false; wire.ImportSources = ["tmdb", "mdblist", "tmdb_list"]; frame.Content = null; }
    }
    private static async Task DiscoveryAsync(Frame frame, Window window, Wire wire)
    {
        await Viewport(frame, window, 460); var page = new CollectionEditorPage(); frame.Content = page;
        Set(page, "_editorActive", true); await page.ViewModel.LoadReferenceDataCommand.ExecuteAsync(null);
        await (Task)Call(page, "ConfigureSyncedCreationAsync")!; await Layout();
        Probe(All<TextBlock>(page).Any(t => t.Text == "The list it follows") && All<TextBlock>(page).Any(t => t.Text == "Titles come from this list and update on its schedule."),
            "Synced editor uses the retired section heading and caption");
        Probe(All<ScrollViewer>(page).Any(s => s.MaxHeight == 420 && ReferenceEquals(s.Content, Read(page, "_syncedChoices"))),
            "Synced popular picks have no bounded420px scroll surface");
        Probe(All<TextBlock>(page).Any(t => t.Text == "Or paste any MDBList link") && All<TextBlock>(page).Any(t => t.Text.StartsWith("Blank takes the whole list, up to 500.")),
            "Synced link and Order omit current visible labels/help");
        var pristine = (Dictionary<string, string>)Call(page, "CaptureDraft")!;
        All<RadioButton>(page).Single(button => Equals(button.Content, "TMDB chart")).IsChecked = true;
        await Layout();
        Probe(All<RadioButton>(page).Single(button => Equals(button.Content, "TMDB chart")).ActualHeight == 29,
            "source tab does not use the current29px selected-tab rhythm");
        var chartPreset = (ComboBox)Read(page, "_chartPreset")!;
        var chartCards = (Grid)Read(page, "_syncedChartCards")!;
        Check(chartCards.Children.OfType<RadioButton>().Count() == 7 && chartCards.ColumnDefinitions.Count == 2,
            "narrow chart selection does not show all seven choices in two columns");
        chartPreset.SelectedIndex = 0;
        Check(Equals(((ComboBox)Read(page, "_chartMedia")!).SelectedItem is ComboBoxItem mediaChoice ? mediaChoice.Tag : null, "all"), "fresh Trending does not default to Both");
        chartPreset.SelectedIndex = 3; await Layout();
        Check(((ComboBoxItem)((ComboBox)Read(page, "_chartMedia")!).SelectedItem).Tag?.ToString() == "movie" &&
            All<RadioButton>((DependencyObject)Read(page, "_syncedChartUi")!).Any(b => AutomationProperties.GetName(b) == "TV shows" && !b.IsEnabled),
            "Now playing retains an invalid Both scope or permits TV");
        chartPreset.SelectedIndex = 0; await Layout();
        Check(((ComboBoxItem)((ComboBox)Read(page, "_chartMedia")!).SelectedItem).Tag?.ToString() == "movie", "chart change discarded the previous valid media selection");
        await Viewport(frame, window, 1280); await Layout();
        Check(chartCards.ColumnDefinitions.Count == 4, "wide chart cards did not adapt to four columns");
        await Viewport(frame, window, 460); await Layout();
        page.ViewModel.SyncSchedule = null;
        var chartDraft = (Dictionary<string, string>)Call(page, "CaptureDraft")!;
        Check(chartDraft["synced_source"] != pristine["synced_source"] && chartDraft["synced_chart"] != pristine["synced_chart"], "source/chart-only changes disappear from the unsaved draft");
        Check(page.ViewModel.Name == "Trending Movies Today" && !page.ViewModel.IsSaving, "chart selection did not fill the current suggested name or unexpectedly created the draft");
        page.ViewModel.Name = "My own name"; page.ViewModel.Description = "My own description"; page.ViewModel.MaxItemsText = "27"; page.ViewModel.SyncSchedule = "weekly";
        chartPreset.SelectedIndex = 1; await Layout();
        Probe(All<RadioButton>(page).Single(button => Equals(button.Content, "TMDB chart")).MinWidth == 0,
            "source tabs retain the native120px minimum instead of fitting their label");
        Probe(All<TextBlock>(page).Any(t => t.Text == "Kept your name and description" && t.Visibility == Visibility.Visible),
            "changing a source preserves editable drafts without the current kept-fields explanation");
        Check(page.ViewModel.Name == "My own name" && page.ViewModel.Description == "My own description" && page.ViewModel.MaxItemsText == "27" && page.ViewModel.SyncSchedule == "weekly",
            "changing chart overwrote user-entered draft fields");
        page.ViewModel.Name = page.ViewModel.Description = ""; page.ViewModel.MaxItemsText = ""; page.ViewModel.SyncSchedule = "";
        All<RadioButton>(page).Single(button => Equals(button.Content, "MDBList")).IsChecked = true;
        var search = (TextBox)Read(page, "_listSearch")!; var choices = (StackPanel)Read(page, "_syncedChoices")!;
        var imports = (CollectionsViewModel)Read(page, "_syncedImports")!;
        var extraGroup = new CollectionTemplateCategory { Category = "isolated", Label = "Isolated theme", Templates = [new() { Id = "other", Title = "Other pick", Source = "mdblist", Mdblist = new() { Url = "https://mdblist.com/lists/fixture/other" } }] };
        imports.TemplateGroups.Add(extraGroup); Call(page, "RenderSyncedSource"); await Layout();
        Click(All<Button>((DependencyObject)Read(page, "_syncedThemes")!).Single(b => Equals(b.Content, "Isolated theme"))); await Layout();
        Check(choices.Children.OfType<RadioButton>().Count() == 1 && All<TextBlock>(choices).Any(t => t.Text == "Other pick") && string.IsNullOrEmpty(page.ViewModel.Name),
            "theme selection did not filter picks or unexpectedly selected a list");
        Click(All<Button>((DependencyObject)Read(page, "_syncedThemes")!).Single(b => Equals(b.Content, "All")));
        imports.TemplateGroups.Remove(extraGroup); Call(page, "RenderSyncedSource"); await Layout();
        var titleLimit = (TextBox)Read(page, "_titleLimit")!; titleLimit.Text = "12"; Call(page, "CommitSyncedLimit"); titleLimit.Text = "1e2"; Call(page, "CommitSyncedLimit");
        Check(page.ViewModel.MaxItemsText == "12" && titleLimit.Text == "12", "invalid Synced limit replaced its previous whole-number value");
        titleLimit.Text = ""; Call(page, "CommitSyncedLimit"); Check(string.IsNullOrEmpty(page.ViewModel.MaxItemsText), "clearing Synced limit did not restore the whole list");
        Check(search.IsEnabled && choices.Children.OfType<RadioButton>().Any(), "capabilities-backed MDBList search/popular picks missing");
        var sourcePick = choices.Children.OfType<RadioButton>().First();
        sourcePick.IsChecked = true; await Layout();
        Program.Log($"TRACE selected template: name={page.ViewModel.Name}, poster={page.ViewModel.CurrentPosterUrl == Wire.PosterUrl}, override={string.IsNullOrWhiteSpace(page.ViewModel.PosterSourceUrl)}, selected={((CollectionTemplate?)Read(page, "_syncedPick"))?.Id}.");
        Check(page.ViewModel.CurrentPosterUrl == Wire.PosterUrl && string.IsNullOrWhiteSpace(page.ViewModel.PosterSourceUrl),
            "selecting a template must preview its suggested artwork without staging a user artwork override");
        var headerPoster = (Image)Read(page, "_headerPoster")!;
        await Until(() => headerPoster.Source is BitmapImage { PixelWidth: > 0, PixelHeight: > 0 });
        Check(headerPoster.ActualWidth > 0 && headerPoster.ActualHeight > 0,
            "decoded suggested header artwork has no visible layout area");
        sourcePick.StartBringIntoView(); await Layout();
        var poster = ((Grid)sourcePick.Content).Children.OfType<Border>().Single(border => border.Width == 30 && border.Height == 45 && border.Child is Image);
        poster.StartBringIntoView(); await Until(() => ((Image)poster.Child).Source is BitmapImage { PixelWidth: > 0, PixelHeight: > 0 });
        var posterClip = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(poster).Clip as Microsoft.UI.Composition.CompositionGeometricClip;
        Check(posterClip?.Geometry is Microsoft.UI.Composition.CompositionRoundedRectangleGeometry geometry && geometry.Size.X == 30 && geometry.Size.Y == 45 && geometry.CornerRadius.X == 5, "current decoded source pick artwork lost its30x45/5px crop");
        var firstCalls = wire.SearchCalls; wire.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously); var stale = wire.SearchGate;
        search.Text = "ab"; await Until(() => wire.SearchCalls == firstCalls + 1);
        Check(wire.SearchQuery == "?q=ab", "two-character typed search did not issue the exact scoped query");
        search.Text = ""; await Layout();
        Check(choices.Children.OfType<RadioButton>().Any(row => All<TextBlock>(row).Any(text => text.Text == "Popular pick")), "clearing search did not restore source popular picks");
        stale.TrySetResult(Reply(new { configured = true, items = new[] { Wire.List("stale", "Stale search") } }));
        await Until(() => !((CollectionsViewModel)Read(page, "_syncedImports")!).IsSearchingMdblist); await Layout();
        Check(!All<TextBlock>(choices).Any(text => text.Text.Contains("Stale search")), "late search replaced the empty-query popular picks");
        wire.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously); search.Text = "xy";
        await Until(() => wire.SearchCalls == firstCalls + 2);
        wire.SearchGate.TrySetResult(Reply(new { configured = true, items = new[] { Wire.List("current", "Current search") } }));
        await Until(() => All<TextBlock>(choices).Any(text => text.Text == "Current search"));
        var pick = choices.Children.OfType<RadioButton>().Single(row => All<TextBlock>(row).Any(text => text.Text == "Current search"));
        ((ISelectionItemProvider)new RadioButtonAutomationPeer(pick).GetPattern(PatternInterface.SelectionItem)).Select();
        Check(page.ViewModel.Name == "Current search" && ((TextBox)Read(page, "_syncedLink")!).Text == "https://mdblist.com/lists/fixture/current/json", "actual discovery selection did not fill current editor draft and source URL");
        await Capture(frame, "collections-current-synced-discovery-460.png"); wire.SearchGate = null; frame.Content = null;
    }
    private static async Task ManualDraftAsync(Frame frame, Window window)
    {
        foreach (var width in new[] { 460, 1920, 2566, 3440, 3840, 5120 })
        {
            await Viewport(frame, window, width);
            var page = new CollectionEditorPage(); frame.Content = page; Set(page, "_editorActive", true);
            await (Task)Call(page, "InitializeNewEditorAsync", "manual")!; await Layout();
            var shell = (FrameworkElement)page.FindName("CollectionEditorShell");
            var shellOrigin = shell.TransformToVisual(page).TransformPoint(new(0, 0));
            var title = (TextBlock)page.FindName("PageTitle");
            var search = (TextBox)page.FindName("SearchBox");
            var section = (Border)page.FindName("ManualItemsSection");
            var save = (Button)page.FindName("SaveButton");
            var saveOrigin = save.TransformToVisual(page).TransformPoint(new(0, 0));
            Program.Log($"TRACE manual draft {width}: page={page.ActualWidth}, shell={shellOrigin.X},{shellOrigin.Y}/{shell.ActualWidth}, title={title.ActualHeight}, search={search.ActualWidth}, save={saveOrigin.Y}/{save.ActualHeight}, enabled={save.IsEnabled}.");
            if (width >= 1024)
            {
                var titleY = title.TransformToVisual(page).TransformPoint(new(0, 0)).Y;
                var basicsY = ((Border)page.FindName("BasicInfoSection")).TransformToVisual(page).TransformPoint(new(0, 0)).Y;
                Program.Log($"TRACE manual rhythm {width}: titleY={titleY}, basicsY={basicsY}, titlesY={section.TransformToVisual(page).TransformPoint(new(0, 0)).Y}, titlesHeight={section.ActualHeight}.");
                Probe(Math.Abs(titleY - 121) < 1 && Math.Abs(basicsY - 181.8) < 1,
                    "manual editor reserves unused header rows or inherited button minimums");
            }
            Probe(Math.Abs(shellOrigin.X - (page.ActualWidth - shell.ActualWidth) / 2) < 1,
                "manual editor column is not centered inside the available page");
            Probe(Math.Abs(shellOrigin.Y - (width >= 1024 ? 56 : 32)) < 1,
                "manual editor does not use the current page-shell top gutter");
            Probe(((TextBlock)page.FindName("PageSubtitle")).Visibility == Visibility.Collapsed,
                "manual create header repeats Not created yet beneath its tag");
            Probe(All<TextBlock>(section).Any(t => t.Text == "Titles") && search.PlaceholderText == "Add a title" && search.MaxWidth == double.PositiveInfinity,
                "manual contents still exposes the legacy Items/search layout");
            Probe(saveOrigin.Y >= 0 && saveOrigin.Y + save.ActualHeight <= page.ActualHeight - 10 && !save.IsEnabled,
                "unnamed create action must be disabled and visible without scrolling");
            Probe(((TextBox)page.FindName("NameTextBox")).PlaceholderText == "" && ((TextBox)page.FindName("DescriptionTextBox")).PlaceholderText == "",
                "manual fields contain legacy placeholder copy");
            var scroll = (ScrollViewer)page.FindName("CollectionEditorScroll");
            var pinnedY = saveOrigin.Y; scroll.ChangeView(null, scroll.ScrollableHeight, null, true); await Layout();
            Probe(Math.Abs(save.TransformToVisual(page).TransformPoint(new(0, 0)).Y - pinnedY) < 1,
                "collection actions move offscreen with the editor contents");
            if (width is 460 or 2566) await Capture(frame, $"collections-manual-draft-{width}.png");
            frame.Content = null;
        }
    }
    private static async Task ManualSearchAsync(Frame frame, Window window, Wire wire)
    {
        await Viewport(frame, window, 460);
        var page = new CollectionEditorPage(); frame.Content = page; Set(page, "_editorActive", true);
        await (Task)Call(page, "InitializeNewEditorAsync", "manual")!; await Layout();
        var writes = Wire.MutationCalls;
        try
        {
            wire.SearchSecond = true;
            await page.ViewModel.SearchItemsCommand.ExecuteAsync("title"); await Layout();
            var panel = (StackPanel)page.FindName("SearchResultsPanel");
            Probe(All<Border>(panel).Count(b => b.Width == 36 && b.Height == 54) == 2 &&
                All<ScrollViewer>(page).Any(s => s.MaxHeight == 320 && ReferenceEquals(s.Content, panel)),
                "manual search results lack current poster rows and bounded scroll viewport");
            await (Task)Call(page, "HandleManualTitleKeyAsync", Windows.System.VirtualKey.Down)!;
            await (Task)Call(page, "HandleManualTitleKeyAsync", Windows.System.VirtualKey.Enter)!; await Layout();
            Check(page.ViewModel.ManualItems.Single().MediaItemId == "second" && Wire.MutationCalls == writes,
                "title keyboard selection chose the wrong row or wrote before collection creation");
            await (Task)Call(page, "HandleManualTitleKeyAsync", Windows.System.VirtualKey.Enter)!;
            Check(page.ViewModel.ManualItems.Count == 1 && Wire.MutationCalls == writes, "repeated title Enter staged a duplicate or wrote remotely");
            await (Task)Call(page, "HandleManualTitleKeyAsync", Windows.System.VirtualKey.Escape)!; await Layout();
            Check(((Border)Read(page, "_manualSearchResults")!).Visibility == Visibility.Collapsed, "Escape retained the title results");
            Set(page, "_manualSearchOpen", true); wire.RejectTitleSearch = true;
            ((TextBox)page.FindName("SearchBox")).Text = "rejected";
            await Until(() => ((StackPanel)Read(page, "_manualSearchProblem")!).Visibility == Visibility.Visible);
            wire.RejectTitleSearch = false;
            Click(All<Button>((DependencyObject)Read(page, "_manualSearchProblem")!).Single());
            await Until(() => page.ViewModel.SearchResults.Count == 2); await Layout();
            Check(((StackPanel)Read(page, "_manualSearchProblem")!).Visibility == Visibility.Collapsed && page.ViewModel.ManualItems.Count == 1,
                "title search Retry retained its error or discarded staged titles");
            wire.EmptyTitleSearch = true; await page.ViewModel.SearchItemsCommand.ExecuteAsync("empty"); await Layout();
            Check(((TextBlock)Read(page, "_manualSearchStatus")!).Text == "No titles match." && ((TextBlock)Read(page, "_manualSearchStatus")!).Visibility == Visibility.Visible,
                "empty title results did not explain the search state");
            wire.EmptyTitleSearch = false; wire.TitleSearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = page.ViewModel.SearchItemsCommand.ExecuteAsync("slow"); await Layout();
            Check(((TextBlock)Read(page, "_manualSearchStatus")!).Text == "Searching…", "pending title search lacks a loading status");
            ((TextBox)page.FindName("SearchBox")).Text = ""; await pending; await Layout();
            Check(page.ViewModel.SearchResults.Count == 0 && ((TextBlock)Read(page, "_manualSearchStatus")!).Visibility == Visibility.Collapsed && page.ViewModel.ManualItems.Count == 1 && Wire.MutationCalls == writes,
                "cleared search exposes obsolete state or loses the staged collection");
        }
        finally { wire.SearchSecond = wire.RejectTitleSearch = wire.EmptyTitleSearch = false; wire.TitleSearchGate = null; frame.Content = null; }
    }
    private static async Task SmartDraftAsync(Frame frame, Window window, Wire wire)
    {
        foreach (var width in new[] { 1280, 460 })
        {
            await Viewport(frame, window, width); var page = new CollectionEditorPage(); frame.Content = page; Set(page, "_editorActive", true);
            var previews = wire.PreviewCalls;
            await (Task)Call(page, "InitializeNewEditorAsync", "smart")!; await Layout();
            Probe(All<ComboBox>(page).Any(c => AutomationProperties.GetName(c) == "Kind of titles") && All<Button>(page).Any(b => AutomationProperties.GetName(b).StartsWith("Libraries:")),
                "Smart rules omit the current scope/library sentence");
            Probe(All<TextBlock>(page).Any(t => t.Text == "Titles that match are in the collection. New matches join on their own.") && All<TextBlock>(page).Any(t => t.Text == "Live preview"),
                "Smart rules and preview use retired page sections");
            await Until(() => wire.PreviewCalls > previews && !page.ViewModel.IsPreviewing);
            Check(page.ViewModel.PreviewItems.Count == 24 && page.ViewModel.PreviewTotal == 80 && page.ViewModel.ErrorMessage == null,
                "empty Smart rules do not render their automatic24-title preview");
            using var body = JsonDocument.Parse(wire.PreviewBody!);
            Check(!body.RootElement.GetProperty("query_definition").TryGetProperty("media_scope", out _), "All titles posts an invalid literal all scope instead of omitting it");
            Check(body.RootElement.GetProperty("limit").GetInt32() == 24 && body.RootElement.GetProperty("query_definition").GetProperty("groups").GetArrayLength() == 0,
                "Smart preview changes an empty query or sends the retired20-title limit");
            Check(body.RootElement.GetProperty("query_definition").GetProperty("sort").GetProperty("field").GetString() == "added_at" &&
                body.RootElement.GetProperty("query_definition").GetProperty("sort").GetProperty("order").GetString() == "desc", "new Smart preview does not use the current Date Added/Newest order");
            var previewPanel = (FrameworkElement)Read(page, "_smartPreviewPanel")!; previewPanel.StartBringIntoView(); await Layout();
            Check(All<Border>(previewPanel).Count(b => b.Tag is SiloPlayer.Core.Models.Collections.CollectionPreviewItem) == 24,
                "live Smart preview did not build all poster tiles");
            Check(All<Border>(previewPanel).Where(b => b.Tag is SiloPlayer.Core.Models.Collections.CollectionPreviewItem).All(b =>
                All<TextBlock>(b).Any(t => t.Text == ((SiloPlayer.Core.Models.Collections.CollectionPreviewItem)b.Tag).Title && t.MaxLines == 2 && t.TextTrimming == TextTrimming.CharacterEllipsis)),
                "preview posters omit their visible two-line title captions, including missing artwork");
            await Capture(frame, $"collections-smart-live-preview-{width}.png");
            wire.RejectPreview = true; await page.ViewModel.PreviewCommand.ExecuteAsync(null); await Layout();
            Check(page.ViewModel.PreviewItems.Count == 24 && page.ViewModel.ErrorMessage == null &&
                All<TextBlock>(page).Any(t => t.Text == "The preview didn't load. You can still save."),
                "a failed preview discards healthy matches or blocks the collection draft");
            wire.RejectPreview = false;
            if (width == 1280)
            {
                var limit = (TextBox)Read(page, "_smartLimit")!; limit.Text = "12"; Call(page, "CommitSmartLimit");
                Check(page.ViewModel.RuleDefinition.Limit == 12, "valid Smart limit was not committed");
                limit.Text = "garbage"; Call(page, "CommitSmartLimit"); Check(page.ViewModel.RuleDefinition.Limit == 12 && limit.Text == "12", "invalid Smart limit coerced the query");
                limit.Text = ""; Call(page, "CommitSmartLimit"); Check(page.ViewModel.RuleDefinition.Limit == null, "clearing the limit does not restore all matches");
                await Task.Delay(300); await Until(() => !page.ViewModel.IsPreviewing);
                wire.PreviewGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var oldItems = page.ViewModel.PreviewItems.ToArray(); var oldCalls = wire.PreviewCalls;
                var pending = page.ViewModel.RefreshPreviewAsync(); await Until(() => wire.PreviewCalls > oldCalls);
                page.ViewModel.InvalidateEditorLoads(); wire.PreviewGate.TrySetResult(Reply(new { total = 0, items = Array.Empty<object>() })); await pending;
                Check(!page.ViewModel.IsPreviewing && page.ViewModel.PreviewItems.SequenceEqual(oldItems), "late preview from an invalidated editor replaced healthy matches"); wire.PreviewGate = null;
                ((DispatcherTimer)Read(page, "_smartPreviewDelay")!).Stop();
            }
            page.ViewModel.AddRuleCommand.Execute(null); await Layout();
            var fieldChoice = All<ComboBox>(page).Single(c => AutomationProperties.GetName(c) == "Rule field");
            Check(fieldChoice.Height == 36, "Smart rules still use compact32px selectors");
            fieldChoice.StartBringIntoView(); await Layout();
            var ruleSelectors = (Grid)fieldChoice.Parent;
            foreach(var control in ruleSelectors.Children.OfType<FrameworkElement>())
            {
                var origin = control.TransformToVisual(ruleSelectors).TransformPoint(new(0,0));
                Check(origin.X >= -.5 && origin.X + control.ActualWidth <= ruleSelectors.ActualWidth + .5, "wrapped Smart rule control extends outside its panel");
            }
            await Capture(frame, $"collections-smart-rules-{width}.png");
            frame.Content = null; page.ViewModel.InvalidateEditorLoads();
        }
    }
    private static async Task<CollectionEditorPage> Editor(Frame frame, Window window, string id)
    {
        await Viewport(frame, window, 460); var page = new CollectionEditorPage(); frame.Content = page;
        Set(page, "_editorActive", true); await (Task)Call(page, "LoadEditorAsync", id)!; await Layout();
        Check(page.ViewModel.ErrorMessage == null && !page.ViewModel.IsReadOnly, "fixture collection did not load editable"); return page;
    }
    private static async Task ManualAsync(Frame frame, Window window, Wire wire)
    {
        var page = await Editor(frame, window, "manual"); var original = page.ViewModel.ManualItems.Single();
        Call(page, "BuildRulesUI"); Call(page, "BuildRulesUI"); await Layout();
        Check(((FrameworkElement)page.FindName("SmartRulesSection")).Visibility == Visibility.Collapsed,
            "rebuilding a hidden Smart editor exposed the wrong collection section");
        await page.ViewModel.SearchItemsCommand.ExecuteAsync("new"); await Layout();
        wire.RejectManual = true; var add = All<Button>((DependencyObject)page.FindName("SearchResultsPanel")).Single(b => Equals(b.Content, "Add"));
        Click(add); await Until(() => wire.AddCalls == 1 && !page.ViewModel.IsManualMutationPending); await Layout();
        Check(page.ViewModel.ErrorMessage != null && page.ViewModel.ManualItems.Single() == original, "failed actual add changed retained manual rows");
        var remove = All<Button>((DependencyObject)page.FindName("ManualItemsPanel")).Single(b => AutomationProperties.GetName(b) == "Remove " + original.Title);
        Click(remove); await Until(() => wire.RemoveCalls == 1 && !page.ViewModel.IsManualMutationPending); await Layout();
        Check(page.ViewModel.ErrorMessage != null && page.ViewModel.ManualItems.Single() == original, "failed actual remove discarded existing manual row");
        await Capture(frame, "collections-manual-failed-add-remove-460.png"); wire.RejectManual = false; frame.Content = null;
    }
    private static async Task ImportedSaveAsync(Frame frame, Window window, Wire wire, NavigationService navigation)
    {
        var seeded = new CollectionEditorPage(); frame.Content = seeded; Set(seeded, "_editorActive", true);
        var route = new CollectionEditorNavigationArgs { CollectionId = "imported", PosterUrl = Wire.PosterUrl };
        route.GetType().GetProperty("PosterIsCollage")?.SetValue(route, true);
        Set(seeded, "_creationRoute", route);
        await (Task)Call(seeded, "LoadEditorAsync", "imported")!; await Layout();
        Check(seeded.ViewModel.CurrentPosterUrl == Wire.PosterUrl && All<TextBlock>(seeded).Any(t => t.Text == "Poster: a collage of its titles"),
            "navigation collage seed must display artwork with its correct attribution");
        await seeded.ViewModel.RemovePosterCommand.ExecuteAsync(null); await Layout();
        Check(((Border)Read(seeded, "_headerCover")!).Visibility == Visibility.Collapsed,
            "staging artwork removal must not resurrect the navigation seed");
        frame.Content = null;
        foreach (var width in new[] { 1280, 460 })
        {
            var initial = await Editor(frame, window, "imported"); await Viewport(frame, window, width);
            var source = (Border)initial.FindName("ImportedSourceSection");
            Probe(All<TextBlock>(source).Any(t => t.Text == "The list it follows") && All<TextBlock>(source).Any(t => t.Text == "Last sync") && All<TextBlock>(source).Any(t => t.Text == "Next sync") && All<TextBlock>(source).Any(t => t.Text == "The list decides"),
                "saved Synced editor omits current source/status/ownership blocks");
            Probe(All<Button>(source).Any(b => AutomationProperties.GetName(b).StartsWith("Match into:")) && All<ComboBox>(source).Any(c => AutomationProperties.GetName(c) == "Default sort"),
                "saved Synced libraries/order are not the current controls");
            var strip = (Grid)Read(initial, "_savedSyncStrip")!;
            Probe(width < 640 || strip.ActualHeight <= 80, "saved Synced status uses unnecessary extra rows or stacked inline labels");
            await Capture(frame, $"collections-imported-initial-{width}.png"); var heading = (TextBlock)initial.FindName("PageTitle"); var name = (TextBox)initial.FindName("NameTextBox"); var fieldGrid = (Grid)initial.FindName("DisplayFilterGrid"); var filters = (ComboBox)initial.FindName("MediaFilterCombo"); var body = initial.FindName("ImportedEditorSurface") as Border; Program.Log($"TRACE imported layout viewport={frame.XamlRoot.Size}, title={heading.FontSize}/{heading.LineHeight}, name={name.ActualWidth}x{name.ActualHeight}, filters=col{Grid.GetColumn(filters)}/row{Grid.GetRow(filters)}, bodyCorner={body?.CornerRadius.TopLeft}, sectionCorner={((Border)initial.FindName("BasicInfoSection")).CornerRadius.TopLeft}, padding={((Border)initial.FindName("BasicInfoSection")).Padding}"); Probe(heading.FontSize == (width < 640 ? 26 : 32) && name.MinHeight == 36 && name.ActualHeight >= 36 && name.HorizontalAlignment == HorizontalAlignment.Stretch && ((FrameworkElement)initial.FindName("CollectionEditorShell")).ActualWidth <= 768.5 && body?.BorderThickness == new Thickness(0) && ((Border)initial.FindName("BasicInfoSection")).CornerRadius.TopLeft == 22, "imported heading/full Name/single divided source panel mismatch"); Probe(Grid.GetRow(filters) == 0 && Grid.GetColumn(filters) == 1 && fieldGrid.ColumnSpacing == 8 && filters.ActualWidth > 100, "current two-column Show only controls clip or have incorrect spacing"); frame.Content = null;
        }
        await Viewport(frame, window, 460); navigation.Navigate<CollectionsPage>(); await Layout(); navigation.Navigate<CollectionEditorPage>("imported");
        await Until(() => frame.Content is CollectionEditorPage p && !p.ViewModel.IsLoading && p.ViewModel.CollectionId == "imported"); await Layout();
        var page = (CollectionEditorPage)frame.Content; ((TextBox)page.FindName("NameTextBox")).Text = "Saved imported name"; await Layout();
        Click((Button)page.FindName("SaveButton")); await Until(() => wire.SaveCalls == 1 && !page.ViewModel.IsSaving); await Layout();
        await Capture(frame, "collections-imported-after-save-460.png");
        Probe(ReferenceEquals(frame.Content, page), "successful imported Save navigated away; pinned source stays in the editor");
        var saved = (Dictionary<string, string>)Read(page, "_draftBaseline")!;
        Probe(!((Dictionary<string, string>)Call(page, "CaptureDraft")!).Any(pair => saved.GetValueOrDefault(pair.Key) != pair.Value) && page.ViewModel.Name == "Saved imported name", "successful imported Save did not establish a clean saved baseline");
        frame.Content = null;
    }

    private static async Task LockedSourcesAsync(Frame frame, Window window)
    {
        foreach (var id in new[] { "discover", "trakt", "stopped-trakt" })
        foreach (var width in new[] { 460, 1280 })
        {
            var page = await Editor(frame, window, id); await Viewport(frame, window, width); await Layout();
            var source = (Border)page.FindName("ImportedSourceSection");
            var texts = All<TextBlock>(source).Select(t => t.Text).ToArray();
            Check(texts.Contains(id == "discover" ? "Made by a starter pack. Its rules can't be changed here." : "New Trakt lists aren't supported. This one keeps its source and libraries.") && !texts.Contains("The list decides"),
                "locked source must show its retained configuration instead of an ordinary chart's ownership cards");
            Check(texts.Contains(id == "discover" ? "Movies, most popular first" : "Trending movies"), "locked source summary lost source criteria");
            Program.Log($"TRACE locked source {id}: match={((Button)Read(page, "_syncedMatchButton")!).IsEnabled}, limit={((TextBox)Read(page, "_titleLimit")!).IsEnabled}, schedule={((ComboBox)Read(page, "_savedSchedule")!).IsEnabled}.");
            Check(((Button)Read(page, "_syncedMatchButton")!).IsEnabled == (id == "discover") && ((TextBox)Read(page, "_titleLimit")!).IsEnabled == (id == "discover"),
                "legacy Trakt source libraries and title limit must stay locked");
            Check(((ComboBox)Read(page, "_savedSchedule")!).IsEnabled == (id != "stopped-trakt"), "legacy schedule editable state is incorrect");
            await Capture(frame, $"collections-locked-{id}-{width}.png"); frame.Content = null;
        }
    }
    private static async Task ArtworkAsync(Frame frame, Window window, Wire wire)
    {
        var page = await Editor(frame, window, "imported");
        Click((Button)Read(page, "_lookToggle")!); await Layout();
        // Local file bytes only. Native file picker chrome is not automated by this test.
        var bytes = ArtworkAsset.Bytes;
        var filePath = Path.Combine(Program.ResultDirectory, "collection-local-poster.png"); await File.WriteAllBytesAsync(filePath, bytes);
        await (Task)Call(page, "ApplyPosterFileAsync", await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath)))!; await Layout();
        var preview = (Image)page.FindName("ImportedPosterPreview"); await Until(() => preview.Source is BitmapImage { PixelWidth: 256 }); preview.StartBringIntoView(); await Layout(); Program.Log($"TRACE artwork initial source={preview.Source?.GetType().Name}, pixels={(preview.Source as BitmapImage)?.PixelWidth}, geometry={preview.ActualWidth}x{preview.ActualHeight}, visibility={preview.Visibility}");
        await Capture(frame, "collections-artwork-draft-460.png");
        var previewFrame = page.FindName("ImportedPosterPreviewFrame") as Border; var previewClip = previewFrame == null ? null : Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(previewFrame).Clip as Microsoft.UI.Composition.CompositionGeometricClip; Program.Log($"TRACE previewFrame={previewFrame?.ActualWidth}x{previewFrame?.ActualHeight}, clip={previewClip?.Geometry?.GetType().Name}"); Probe(previewFrame?.ActualHeight == 128 && previewClip?.Geometry is Microsoft.UI.Composition.CompositionRoundedRectangleGeometry geometry && geometry.Size.Y == 128 && geometry.CornerRadius.X == 12, "picked local artwork is not cropped inside the actual128px rounded draft preview");
        Call(page, "DeleteCollection_Click", page, new RoutedEventArgs());
        await Until(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot).Any(p => All<ContentDialog>(p.Child).Any()));
        var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot).SelectMany(p => All<ContentDialog>(p.Child)).Single();
        Click(All<Button>(dialog).Single(b => b.Name == "CloseButton")); await Layout();
        Check(wire.Deletes == 0 && page.ViewModel.PosterFileBytes?.Length == bytes.Length, "Delete Cancel wrote or discarded artwork draft");
        Click((Button)page.FindName("ImportedRemoveSelectedPoster")); await Layout();
        Check(page.ViewModel.PosterFileBytes == null && ((Image)page.FindName("ImportedPosterPreview")).Source == null && wire.Deletes == 0,
            "Remove selected file deleted saved artwork or retained the local preview");
        var drop = new Windows.ApplicationModel.DataTransfer.DataPackage();
        drop.SetStorageItems(new[] { await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath)) });
        await (Task)Call(page, "ApplyPosterDropAsync", drop.GetView())!; await Layout();
        Check(page.ViewModel.PosterFileBytes?.SequenceEqual(bytes) == true && ((Image)page.FindName("ImportedPosterPreview")).Source is BitmapImage { PixelWidth: 256 },
            "actual storage-item drop callback did not produce the selected local artwork draft");
        preview = (Image)page.FindName("ImportedPosterPreview"); preview.StartBringIntoView(); await Layout();
        await Capture(frame, "collections-artwork-drop-preview-460.png");
        wire.PosterGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = (Button)page.FindName("SaveButton"); Program.Log($"TRACE artwork beforeSave enabled={save.IsEnabled}, readOnly={page.ViewModel.IsReadOnly}, saving={page.ViewModel.IsSaving}"); Click(save); await Layout(); Program.Log($"TRACE artwork afterSave saving={page.ViewModel.IsSaving}, error={page.ViewModel.ErrorMessage}, saveCalls={wire.SaveCalls}, posterCalls={wire.PosterCalls}"); await Until(() => wire.PosterCalls == 1); await Layout();
        Check(!save.IsEnabled && page.ViewModel.PosterFileBytes?.SequenceEqual(bytes) == true, "pending artwork Save permits duplicate writes or discards its draft");
        wire.PosterGate.TrySetResult(Reply(new { message = "Fixture poster rejected" }, HttpStatusCode.UnprocessableEntity));
        await Until(() => !page.ViewModel.IsSaving); await Layout();
        Check(page.ViewModel.ErrorMessage != null && ReferenceEquals(frame.Content, page) && page.ViewModel.PosterFileBytes?.SequenceEqual(bytes) == true && preview.Source != null,
            "rejected poster write lost the editor or selected preview");
        wire.PosterGate = null; Click(save); await Until(() => wire.PosterCalls == 2 && !page.ViewModel.IsSaving && !page.ViewModel.IsLoading); await Layout();
        Check(page.ViewModel.ErrorMessage == null && page.ViewModel.PosterFileBytes == null && ((FrameworkElement)page.FindName("CollectionDirtyDock")).Visibility == Visibility.Collapsed,
            "successful artwork retry did not establish a clean saved editor");
        await (Task)Call(page, "ApplyPosterDropAsync", drop.GetView())!; await Layout();
        Call(page, "Discard_Click", page, new RoutedEventArgs()); await Until(() => !page.ViewModel.IsLoading && page.ViewModel.PosterFileBytes == null); await Layout();
        Check(((Image)page.FindName("ImportedPosterPreview")).Source == null && wire.PosterCalls == 2, "Discard retained selected artwork or wrote another poster");
        frame.Content = null;
    }
    private static async Task Viewport(Frame frame, Window window, int width)
    { window.AppWindow.ResizeClient(new(width, 900)); frame.Width = width; await Layout(); var actual = window.AppWindow.ClientSize; Check(actual.Width == width && actual.Height == 900 && Math.Abs(frame.XamlRoot.Size.Width - width) < 1, "collection fixture viewport is clamped or mislabeled"); }
    private static async Task Layout() { await Task.Delay(250); }
    private static async Task Until(Func<bool> ready) { for (var n = 0; n < 200 && !ready(); n++) await Task.Delay(25); Check(ready(), "native collection operation did not settle"); }
    private static void Probe(bool ok, string message) { if (!ok) { Failures.Add(message); Program.Log("FAIL assertion: " + message); } }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static object? Call(object obj, string method, params object?[] args) => obj.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(obj, args);
    private static object? Read(object obj, string name) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(obj, value);
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject { if (root is T match) yield return match; for (var n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++) foreach (var item in All<T>(VisualTreeHelper.GetChild(root, n))) yield return item; }
    private static async Task Capture(FrameworkElement frame, string name)
    {
        await Layout(); frame.Clip = new RectangleGeometry { Rect = new(0, 0, frame.ActualWidth, frame.ActualHeight) }; var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(frame);
        Program.Log($"TRACE {name} actualFrame={frame.ActualWidth}x{frame.ActualHeight}, bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight}, XamlRoot={frame.XamlRoot.Size}");
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0 && (frame is not Frame || bitmap.PixelWidth == (int)frame.Width && bitmap.PixelHeight == 900), "collection screenshot is not the actual rendered surface/client boundary");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, Func<object>> factories) : IServiceProvider { public object? GetService(Type type) => factories.TryGetValue(type, out var factory) ? factory() : fallback.GetService(type); }
    private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) { var reply = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body)) }; reply.Headers.ETag = new("\"collections-fixture\""); return reply; }
    // An isolated loopback image endpoint exercises the real WinUI BitmapImage pipeline.
    // The controlled PNG is shared with the reference fixture; no production artwork is accessed.
    private sealed class ArtworkOnlyHandler(string allowedUrl) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Check(request.RequestUri?.AbsoluteUri == allowedUrl, "Collection artwork attempted an unowned endpoint");
            return base.SendAsync(request, token);
        }
    }
    private sealed class ArtworkAsset : IAsyncDisposable
    {
        internal static readonly byte[] Bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAQAAAAEACAYAAABccqhmAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAM6SURBVHhe7daxEYJQAARRsRUCHUMicjqmD5ugCA0/+W9h3wuvgJ1bfsd7PMj6bOc8EfKcB6BDACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBs+X/3MY90vK51ngjxACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACDsBoKICZScmj7NAAAAAElFTkSuQmCC");
        private readonly System.Net.Sockets.TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly byte[] _servedBytes;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;
        internal string Url { get; }
        internal ArtworkAsset()
        {
            var realArtwork = Environment.GetEnvironmentVariable("SILO_NATIVE_COLLECTIONS_ARTWORK_FILE");
            _servedBytes = string.IsNullOrEmpty(realArtwork) ? Bytes : File.ReadAllBytes(realArtwork);
            _listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/collection-poster.png";
            _serving = ServeAsync();
        }
        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                    using var stream = connection.GetStream();
                    var request = new byte[8192]; await stream.ReadAsync(request, _stop.Token);
                    var headers = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {(_servedBytes[0] == 255 ? "image/jpeg" : "image/png")}\r\nContent-Length: {_servedBytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, _stop.Token); await stream.WriteAsync(_servedBytes, _stop.Token); await stream.FlushAsync(_stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (System.Net.Sockets.SocketException) when (_stop.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync()
        { _stop.Cancel(); _listener.Stop(); await _serving; _stop.Dispose(); }
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal static string PosterUrl = ""; internal static int MutationCalls;
        internal static readonly CollectionTemplate Template = new() { Id = "fixture", Title = "A current collection template with a long title", Description = "Collection template description", Source = "tmdb", MediaKind = "movie", Icon = "🎬", RequiresProfile = true, DefaultSyncSchedule = "0 0 * * *", DefaultLimit = 100, PosterPath = "/artwork/template.png" };
        internal string[] ImportSources = ["tmdb", "mdblist", "tmdb_list"]; internal bool RejectCapabilities;
        internal int PreviewCalls; internal string? PreviewBody; internal bool RejectPreview;
        internal TaskCompletionSource<HttpResponseMessage>? PreviewGate;
        internal TaskCompletionSource<HttpResponseMessage>? SearchGate, PosterGate, TitleSearchGate; internal int SearchCalls, TopCalls, AddCalls, RemoveCalls, SaveCalls, Deletes, PosterCalls; internal string? SearchQuery; internal bool RejectManual, SearchSecond, RejectTitleSearch, EmptyTitleSearch; internal string ImportedName = "Imported fixture";
        internal static object List(string id, string name) => new { id = id == "top" ? 2 : 1, name, user_name = "Fixture", mediatype = "movie", items = 12, likes = 3, url = "https://mdblist.com/lists/fixture/" + id };
        private object Collection(string id) => new { id, name = id == "manual" ? "Manual fixture" : ImportedName, collection_type = id == "manual" ? "manual" : id == "discover" ? "tmdb" : id.Contains("trakt") ? "trakt" : "mdblist", creator_profile_id = "fixture", source_url = "https://mdblist.com/lists/fixture/top/", source_config = new { limit = 100, mode = id == "discover" ? "tmdb_discover" : "trakt_preset", media_type = "movie", preset = "trending", discover = new { sort_by = "popularity.desc" } }, sync_cadence = id == "stopped-trakt" ? "" : "daily", updated_at = "2026-10-01T00:00:00Z" };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get && request.RequestUri?.AbsolutePath != "/api/v2/collections/preview") MutationCalls++;
            Check(request.RequestUri?.Host == "collections-acceptance.invalid", "collection fixture attempted external network"); var path = request.RequestUri!.AbsolutePath; Program.Log($"TRACE collection wire {request.Method} {path}");
            if (path == "/api/v2/collections/imported/poster")
            {
                PosterCalls++; Check(request.Method == HttpMethod.Put && request.Content is MultipartFormDataContent, "artwork save is not the canonical multipart PUT");
                var poster = ((MultipartFormDataContent)request.Content!).Single();
                Check(poster.Headers.ContentDisposition?.Name?.Trim('"') == "poster" && poster.Headers.ContentType?.MediaType == "image/png" && (await poster.ReadAsByteArrayAsync(ct)).Length > 0,
                    "artwork payload lost its file field, MIME type or bytes");
                return PosterGate is null ? Reply(Collection("imported")) : await PosterGate.Task.WaitAsync(ct);
            }
            if (path.EndsWith("/mdblist/search")) { SearchCalls++; SearchQuery = request.RequestUri.Query; return await SearchGate!.Task.WaitAsync(ct); }
            if (path == "/api/v2/collections/preview") { PreviewCalls++; PreviewBody = await request.Content!.ReadAsStringAsync(ct); if (PreviewGate != null) return await PreviewGate.Task; return RejectPreview ? Reply(new { message = "Fixture preview rejected" }, HttpStatusCode.ServiceUnavailable) : Reply(new { total = 80, items = Enumerable.Range(0, 24).Select(i => new { content_id = "preview-" + i, title = "Preview " + i, type = "movie", poster_url = PosterUrl }).ToArray() }); }
            if (path.EndsWith("/mdblist/top")) { TopCalls++; return Reply(new { configured = true, items = new[] { List("top", "Top list") } }); }
            if (path == "/api/v2/collections/capabilities") return RejectCapabilities ? Reply(new { message = "Fixture capabilities rejected" }, HttpStatusCode.ServiceUnavailable) : Reply(new { item_reorder = false, imports = true, import_sources = ImportSources, mdblist_search = true, artwork = true, sync_schedule_editable = true });
            if (path == "/api/v2/collections/templates") return Reply(new { categories = new[] { new { category = "featured", label = "Featured", templates = new[] { new { id = Template.Id, title = Template.Title, description = Template.Description, source = "tmdb", media_kind = "movie", icon = Template.Icon, requires_profile = true, default_sync_schedule = Template.DefaultSyncSchedule, default_limit = 100, poster_path = PosterUrl, mdblist = new { url = "" } }, new { id = "popular", title = "Popular pick", description = "Source pick", source = "mdblist", media_kind = "movie", icon = Template.Icon, requires_profile = false, default_sync_schedule = "0 0 * * *", default_limit = 100, poster_path = PosterUrl, mdblist = new { url = "https://mdblist.com/lists/fixture/popular/" } } } } } });
            if (path == "/api/v2/collections") return Reply(new { items = new[] { Collection("manual"), Collection("imported") }, groups = Array.Empty<object>() });
            if (path == "/api/v2/collections/manual/items") return Reply(new { items = new[] { new { media_item_id = "one", content_id = "one", title = "Retained title", type = "movie", position = 0 } }, page = new { has_more = false } });
            if (path.StartsWith("/api/v2/collections/manual/items/")) { if (request.Method == HttpMethod.Put) AddCalls++; else if (request.Method == HttpMethod.Delete) RemoveCalls++; return Reply(new { message = "Fixture manual write rejected" }, RejectManual ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK); }
            if (path is "/api/v2/collections/manual" or "/api/v2/collections/imported" or "/api/v2/collections/discover" or "/api/v2/collections/trakt" or "/api/v2/collections/stopped-trakt")
            {
                if (request.Method == HttpMethod.Delete) Deletes++;
                if (request.Method == HttpMethod.Patch) { SaveCalls++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); if (body.RootElement.TryGetProperty("name", out var name)) ImportedName = name.GetString()!; }
                return Reply(Collection(path.Split('/')[^1]));
            }
            if (path == "/api/v2/catalog")
            {
                if (TitleSearchGate != null) return await TitleSearchGate.Task.WaitAsync(ct);
                if (RejectTitleSearch) return Reply(new { message = "Fixture title search rejected" }, HttpStatusCode.ServiceUnavailable);
                var titles = new List<object>();
                if (!EmptyTitleSearch) { titles.Add(new { content_id = "new", title = "New title", type = "movie", year = 2026 }); if (SearchSecond) titles.Add(new { content_id = "second", title = "Second title", type = "episode", year = 2025 }); }
                return Reply(new { items = titles, page = new { has_more = false } });
            }
            return Reply(new { items = Array.Empty<object>(), libraries = Array.Empty<object>(), page = new { has_more = false } });
        }
    }
}
