using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

// Actual CollectionsPage and CollectionBrowsePage controls over synthetic HTTP: the one poster wall with Show pills,
// the viewer's poster size, loaded-only empty lines and incremental server pages; the collection page hero with its
// description, backdrop, byline and owner/Home actions, and its More actions menu (Sync now, Delete… with confirmation, library pages for a read-only viewer). Screenshots are written to the result directory.
internal static class CollectionsPresentationNativeFixture
{
    private const string SampleThumbhash = "1QcSHQRnh493V4dIh4eXh1h4kJUI";

    internal static async Task RunAsync(StackPanel parent)
    {
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)serviceField.GetValue(null)!;
        using var wire = new Wire(await PngAsync(1280, 720, 0x1F, 0x4F, 0xC8), await PngAsync(400, 600, 0x2A, 0x6A, 0x3C));
        using var http = new HttpClient(wire); var client = new SiloApiClient(http); client.SetBaseUrl("https://collections-presentation.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("me", profile: new() { Id = "me", Name = "Fixture" });
        var catalog = new CatalogApi(client); var collections = new CollectionsApi(client); var settings = new SettingsApi(client);
        var presentation = new UICustomizationService(settings); var overlays = new CardOverlayService(settings); var navigation = new NavigationService();
        using var events = new EventChannelClient(client, auth); using var images = new ImageService(Path.Combine(Program.ResultDirectory, "collections-images"));
        serviceField.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = () => client, [typeof(AuthService)] = () => auth, [typeof(CatalogApi)] = () => catalog, [typeof(CollectionsApi)] = () => collections,
            [typeof(AuthApi)] = () => new AuthApi(client), [typeof(SettingsApi)] = () => settings, [typeof(NavigationService)] = () => navigation,
            [typeof(SettingsService)] = () => new SettingsService(Path.Combine(Program.ResultDirectory, "collections-settings")),
            [typeof(UICustomizationService)] = () => presentation, [typeof(CardOverlayService)] = () => overlays, [typeof(EventChannelClient)] = () => events,
            [typeof(ImageService)] = () => images, [typeof(HttpClient)] = () => http,
            [typeof(CollectionsViewModel)] = () => new CollectionsViewModel(collections, catalog, new AuthApi(client), client),
        }));
        var fixture = new Run(parent, wire, presentation, navigation, catalog);
        try
        {
            await fixture.WallFullWidthAndPosterSizeAsync();
            await fixture.ShowPillsAsync();
            await fixture.IncludedPersonalCollectionsAsync();
            await fixture.EmptyStatesAsync();
            await fixture.GroupsSurviveRebuildAsync();
            await fixture.ServerPaginationAsync();
            await fixture.HeroAsync();
            await fixture.HeroMoreActionsAsync();
            await fixture.CollectionPickerPartialFailuresAsync();
        }
        finally { parent.Children.Clear(); await Task.Delay(100); serviceField.SetValue(null, previous); }
    }

    private sealed class Run(StackPanel parent, Wire wire, UICustomizationService presentation, NavigationService navigation, CatalogApi catalog)
    {
        // ===== The wall =====
        internal async Task WallFullWidthAndPosterSizeAsync()
        {
            wire.Reset(own: 14, shared: 1, libraries: 2);
            foreach (var (size, width) in new[] { ("standard", 460), ("standard", 900), ("standard", 1280), ("standard", 1920), ("compact", 1280), ("large", 1280), ("large", 460) })
            {
                await presentation.SaveCardPresentationAsync(new() { PosterSize = size, Caption = "title_metadata" });
                var (frame, page) = await OpenWall(width, 900);
                var board = (Grid)((StackPanel)page.FindName("CollectionsGrid")).Children.Single();
                await Until(() => OpenButtons(board).Count() == 14 + 1 + 3);
                await Task.Delay(150);
                var gutter = width < 640 ? 16 : width < 1024 ? 24 : 40; var contentWidth = width - gutter * 2;
                var columns = presentation.GetPosterColumnCount(contentWidth); var gap = size == "large" ? 16d : 12d;
                Check(Math.Abs(board.ActualWidth - contentWidth) < 2, $"Collections wall {size}/{width} is {board.ActualWidth}px wide, not the full {contentWidth}px (the 1000px shell cap must be gone).");
                var cards = board.Children.OfType<FrameworkElement>().ToArray();
                var firstRow = cards.Where(card => Grid.GetRow(card) == 0).ToArray();
                Check(firstRow.Length == columns && board.ColumnDefinitions.Count == columns, $"Collections wall {size}/{width} has {firstRow.Length} columns, not the poster-size setting's {columns}.");
                var expected = (contentWidth - gap * (columns - 1)) / columns;
                Check(cards.All(card => Math.Abs(card.ActualWidth - expected) < 1.5), $"Collections wall {size}/{width} card width {cards[0].ActualWidth} is not {expected}.");
                Check(Math.Abs(board.ColumnSpacing - gap) < .1, $"Collections wall {size} gap is {board.ColumnSpacing}, not {gap}.");
                Check(Descendants<TextBlock>(page).All(text => text.Text != "Your collections" && text.Text != "Server collections"), "Stacked section headings must not return.");
                if (size == "standard" && width is 460 or 1280 or 1920 || size == "large" && width == 1280 || size == "compact") await Capture(page, $"collections-wall-{size}-{width}.png");
                Log($"PASS: wall {size}/{width}px is full width ({board.ActualWidth:0}px) with {columns} columns of {expected:0.#}px and {gap}px gaps.");
                parent.Children.Remove(frame);
            }
            await presentation.SaveCardPresentationAsync(new() { PosterSize = "standard", Caption = "title_metadata" });
        }

        internal async Task ShowPillsAsync()
        {
            wire.Reset(own: 3, shared: 1, libraries: 2);
            var (frame, page) = await OpenWall(1280, 900);
            try
            {
                var names = () => Names(page);
                await Until(() => Pills(page).Length == 6 && OpenButtons(Board(page)).Count() == 3 + 1 + 3);
                Check(Pills(page).Select(Auto.GetName).SequenceEqual(["All", "Mine", "Shared", "Server", "Movies", "Shows"]), "Show pills are All, Mine, Shared, Server, then one per library.");
                Check(PillCount(page, "All") == null && PillCount(page, "Server") == null && PillCount(page, "Mine") == "3" && PillCount(page, "Shared") == "1" && PillCount(page, "Movies") == "3" && PillCount(page, "Shows") == "1",
                    "Pills count only what they can know: no combined count for several libraries.");
                Check(Auto.GetItemStatus(Pill(page, "All")) == "Selected", "All starts selected.");
                Check(names().Distinct().Count() == 7 && names().Contains("Family movie night") && names().Contains("Weekend picks 1") && names().Contains("Criterion"), "All shows yours, shared and the server's in one grid.");
                await Capture(page, "collections-wall-pills-all-1280.png");

                await Press(Pill(page, "Mine"));
                Check(names().Count == 3 && names().All(n => n.StartsWith("Weekend")), "Mine shows only the profile's own collections.");
                Check(Auto.GetName((StackPanel)page.FindName("CollectionsWallPanel")) == "Your collections" && Auto.GetItemStatus(Pill(page, "Mine")) == "Selected", "The wall is named for Mine and Mine is selected.");
                Check(!OpenButtons(Board(page)).Any(b => Auto.GetName(b).Contains("Family movie night")) && ((TextBlock)page.FindName("CollectionsEmptyLine")).Visibility == Visibility.Collapsed, "Mine leaves shared and server cards out and says nothing is empty.");

                await Press(Pill(page, "Shared"));
                Check(names().Count == 1 && names()[0] == "Family movie night", "Shared shows other profiles' collections.");
                Check(Descendants<TextBlock>(Board(page)).Any(t => t.Text == "by Other profile"), "A shared card names its owner under the title.");
                Check(!Descendants<Button>(Board(page)).Any(b => Auto.GetName(b).StartsWith("More for ")), "A shared (read-only) card offers no owner menu.");

                await Press(Pill(page, "Server"));
                Check(names().SequenceEqual(["Criterion", "A24", "Anime"]), "Server shows every library's collections, the first of each before any rest.");
                Check(ServerRows(page).Any(b => Auto.GetName(b) == "Load more server collections"), "The default server view offers Load more.");
                Check(wire.TabReads(1) == 0, "Opening the default server view reads only the capped answer.");

                await Press(Pill(page, "Movies"));
                await Until(() => wire.TabReads(1) == 1 && names().Count == 3, () => $"tabReads={wire.TabReads(1)} names=[{string.Join("; ", names())}] paths={string.Join(" , ", wire.Paths.Where(p => p.Contains("library")).ToArray())}");
                Check(names().SequenceEqual(["Criterion", "A24", "Studio Ghibli"]), "A library pill shows that library's collections including the rest read from its Collections tab.");
                Check(Auto.GetName((StackPanel)page.FindName("CollectionsWallPanel")) == "Movies collections", "The wall is named for the picked library.");
                Check(!ServerRows(page).Any(b => Auto.GetName(b).StartsWith("Load")), "A picked library loads its rest straight away: no Load more.");
                await Capture(page, "collections-wall-pills-library-1280.png");

                await Press(Pill(page, "All"));
                Check(names().Count == 3 + 1 + 4 && names().Contains("Studio Ghibli"), "All now includes the library's loaded rest.");
                Check(wire.TabReads(1) == 1, "Returning to All does not read the library again.");
                Log("PASS: Show pills filter one wall (All/Mine/Shared/Server/library) with counts, names, ownership and incremental server reads.");
            }
            finally { parent.Children.Remove(frame); }

            // A one-profile account with no libraries has nothing to filter between: no pills, no Shared.
            wire.Reset(own: 2, shared: 0, libraries: 0, singleProfile: true);
            var (soloFrame, solo) = await OpenWall(900, 800);
            try
            {
                await Until(() => OpenButtons(Board(solo)).Count() == 2);
                Check(((ScrollViewer)solo.FindName("CollectionsFilterScroll")).Visibility == Visibility.Collapsed, "No pills when there is only Mine.");
                Check(((TextBlock)solo.FindName("CollectionsSubtitle")).Text == "Yours and the server's.", "A one-profile account keeps the short subtitle.");
            }
            finally { parent.Children.Remove(soloFrame); }

            // A saved filter the account has nothing for falls back to All; one it needs loading is kept meanwhile.
            wire.Reset(own: 2, shared: 0, libraries: 1, singleProfile: true);
            var (fallbackFrame, fallback) = await OpenWall(900, 800, new CollectionsNavigationArgs(Show: "shared"));
            try
            {
                await Until(() => OpenButtons(Board(fallback)).Count() == 2 + 3);
                Check(Auto.GetItemStatus(Pill(fallback, "All")) == "Selected", "A Shared filter with nobody sharing falls back to All.");
                Check(Pills(fallback).Select(Auto.GetName).SequenceEqual(["All", "Mine", "Server"]), "One library gives Server but no pill of its own.");
            }
            finally { parent.Children.Remove(fallbackFrame); }

            wire.Reset(own: 1, shared: 1, libraries: 2);
            var (libraryFrame, byLibrary) = await OpenWall(900, 800, new CollectionsNavigationArgs(LibraryId: 2));
            try
            {
                await Until(() => Pills(byLibrary).Length == 6 && Names(byLibrary).Count == 1);
                Check(Names(byLibrary).SequenceEqual(["Anime"]) && Auto.GetItemStatus(Pill(byLibrary, "Shows")) == "Selected", "Navigating with a library shows that library's collections.");
                Log("PASS: unavailable filters fall back to All; a navigation-supplied library is honoured.");
            }
            finally { parent.Children.Remove(libraryFrame); }
        }

        internal async Task IncludedPersonalCollectionsAsync()
        {
            wire.Reset(own: 1, shared: 1, libraries: 0);
            var (frame, page) = await OpenWall(1280, 900);
            try
            {
                await Until(() => page.ViewModel.PersonalLoaded && !page.ViewModel.LoadCollectionsCommand.IsRunning);
                page.ViewModel.Collections.Single(collection => collection.Id == "own-1").IncludeInServerCollections = true;
                page.ViewModel.ServerLibraries.Add(new()
                {
                    LibraryId = 1, LibraryName = "Movies", TotalCount = 3,
                    Collections = [new() { Id = "own-1", Title = "Included own" }, new() { Id = "sh-1", Title = "Included shared" },
                        new() { Id = "server-only", Title = "Server only" }],
                });
                var grid = (StackPanel)page.FindName("CollectionsGrid");
                await Until(() => OpenButtons(grid).Count() == 3 && PillCount(page, "All") == "3");
                Check(OpenButtons(grid).Select(Auto.GetName).SequenceEqual(["Open Weekend picks 1", "Open Family movie night", "Open Server only"]),
                    "All keeps the personal cards instead of their server duplicates.");
                Check(Descendants<Button>(grid).Any(button => Auto.GetName(button) == "More for Weekend picks 1"), "The included own collection keeps its owner actions.");
                Check(PillCount(page, "Mine") == "1" && PillCount(page, "Shared") == "1" && PillCount(page, "Server") == "3", "Only All merges personal and server identities.");
                await Press(Pill(page, "Server"));
                Check(Names(page).SequenceEqual(["Included own", "Included shared", "Server only"]), "Server still shows included personal collections when their personal cards are not rendered.");
                await Press(Pill(page, "All"));
                page.ViewModel.Groups.Add(new() { Id = "group", Name = "Group" });
                await Until(() => grid.Children.OfType<StackPanel>().Any());
                Check(OpenButtons(grid).Count() == 3 && PillCount(page, "All") == "3", "Grouped personal posters also suppress server duplicates and count once.");
            }
            finally { parent.Children.Remove(frame); }
            Log("PASS: included own/shared collections appear and count once in All, preserve personal actions and groups, and remain available in Server.");
        }

        internal async Task EmptyStatesAsync()
        {
            // Nothing at all: the line appears only once personal, profiles and server have loaded, and there is no New collection tile.
            wire.Reset(own: 0, shared: 0, libraries: 0, singleProfile: true); wire.PersonalGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var (frame, page) = await OpenWall(1280, 800);
            try
            {
                var line = (TextBlock)page.FindName("CollectionsEmptyLine");
                await Until(() => wire.PersonalReads == 1 && wire.ServerReads >= 1);
                await Task.Delay(300);
                Check(line.Visibility == Visibility.Collapsed, "The empty line flashed before the personal read finished.");
                Check(((StackPanel)page.FindName("CollectionsLoadingShell")).Visibility == Visibility.Visible && ((StackPanel)page.FindName("CollectionsLoadingShell")).Children.Count > 0, "The personal skeleton shows while it loads.");
                wire.PersonalGate.SetResult(true);
                await Until(() => line.Visibility == Visibility.Visible);
                Check(line.Text == "There are no collections yet.", $"All says '{line.Text}'.");
                Check(((StackPanel)page.FindName("CollectionsLoadingShell")).Visibility == Visibility.Collapsed && OpenButtons(Board(page)).Count() == 0, "No skeleton or tile remains once loaded.");
                Check(!Descendants<TextBlock>(page).Any(t => t.Text == "New collection" && !IsInside(t, (FrameworkElement)page.FindName("CollectionsHeaderActions")) && !IsInside(t, (FrameworkElement)page.FindName("CreateCollectionDock")))
                    && !Descendants<Microsoft.UI.Xaml.Shapes.Rectangle>(Board(page)).Any(), "The dashed New collection tile is gone: the header button is the one create action.");
                Check(((ScrollViewer)page.FindName("CollectionsFilterScroll")).Visibility == Visibility.Collapsed, "An empty account has no pills.");
                await Capture(page, "collections-wall-empty-1280.png");
            }
            finally { parent.Children.Remove(frame); }

            var (mineFrame, mine) = await OpenWall(460, 800, new CollectionsNavigationArgs(Show: "mine"));
            try
            {
                var line = (TextBlock)mine.FindName("CollectionsEmptyLine");
                await Until(() => line.Visibility == Visibility.Visible);
                Check(line.Text == "You haven't made any collections yet.", $"Mine says '{line.Text}'.");
                await Capture(mine, "collections-wall-empty-mine-460.png");
            }
            finally { parent.Children.Remove(mineFrame); }

            // A failed personal read is not "none".
            wire.Reset(own: 0, shared: 0, libraries: 0, singleProfile: true); wire.FailPersonal = true;
            var (failedFrame, failed) = await OpenWall(900, 700, new CollectionsNavigationArgs(Show: "mine"));
            try
            {
                var viewModel = failed.ViewModel;
                await Until(() => viewModel.ErrorMessage != null && !viewModel.IsLoading);
                await Task.Delay(200);
                Check(((TextBlock)failed.FindName("CollectionsEmptyLine")).Visibility == Visibility.Collapsed, "A failed personal read must not say the profile has no collections.");
            }
            finally { parent.Children.Remove(failedFrame); }

            // A failed server read leaves All unknown but Mine accurate.
            wire.Reset(own: 0, shared: 0, libraries: 0, singleProfile: true); wire.FailServer = true;
            var (serverFailFrame, serverFail) = await OpenWall(900, 700);
            try
            {
                var viewModel = serverFail.ViewModel;
                await Until(() => viewModel.PersonalLoaded && !viewModel.IsLoadingServerCollections);
                await Task.Delay(200);
                Check(((TextBlock)serverFail.FindName("CollectionsEmptyLine")).Visibility == Visibility.Collapsed, "A failed server read must not say there are no collections at all.");
                var status = (StackPanel)serverFail.FindName("ServerCollectionsRows");
                await Until(() => ServerRows(serverFail).Any(button => Auto.GetName(button) == "Retry server collections"));
                Check(Descendants<TextBlock>(status).Any(text => text.Text.StartsWith("Couldn't load server collections:")), "A first server-read failure must explain the blank wall.");
                wire.FailServer = false;
                await Press(ServerRows(serverFail).Single(button => Auto.GetName(button) == "Retry server collections"));
                await Until(() => viewModel.ServerLoaded && ((TextBlock)serverFail.FindName("CollectionsEmptyLine")).Visibility == Visibility.Visible);
                Check(!ServerRows(serverFail).Any(button => Auto.GetName(button) == "Retry server collections") && viewModel.ServerCollectionsErrorMessage == null,
                    "Retry recovers the aggregate read and removes its error.");
            }
            finally { parent.Children.Remove(serverFailFrame); }

            // Server cards do not wait for the personal read, and the personal ones do not wait for the server.
            wire.Reset(own: 2, shared: 0, libraries: 2, singleProfile: true); wire.PersonalGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var (serverFirstFrame, serverFirst) = await OpenWall(1280, 800);
            try
            {
                await Until(() => OpenButtons(Board(serverFirst)).Count() == 3);
                Check(Names(serverFirst).SequenceEqual(["Criterion", "A24", "Anime"]) && ((StackPanel)serverFirst.FindName("CollectionsLoadingShell")).Visibility == Visibility.Visible,
                    "The server's cards show while the personal ones are still loading.");
                wire.PersonalGate.SetResult(true);
                await Until(() => OpenButtons(Board(serverFirst)).Count() == 5);
                Check(Names(serverFirst).Take(2).All(name => name.StartsWith("Weekend")), "Personal cards come first once they arrive.");
            }
            finally { parent.Children.Remove(serverFirstFrame); }

            wire.Reset(own: 2, shared: 0, libraries: 2, singleProfile: true); wire.ServerGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var (personalFirstFrame, personalFirst) = await OpenWall(1280, 800);
            try
            {
                await Until(() => OpenButtons(Board(personalFirst)).Count() == 2);
                Check(((StackPanel)personalFirst.FindName("ServerCollectionsLoadingRows")).Visibility == Visibility.Visible && ((TextBlock)personalFirst.FindName("CollectionsEmptyLine")).Visibility == Visibility.Collapsed,
                    "Personal cards show with a server skeleton while the server's read is pending.");
                wire.ServerGate.SetResult(true);
                await Until(() => OpenButtons(Board(personalFirst)).Count() == 5 && ((StackPanel)personalFirst.FindName("ServerCollectionsLoadingRows")).Visibility == Visibility.Collapsed);
            }
            finally { parent.Children.Remove(personalFirstFrame); }
            Log("PASS: empty lines appear only after their reads succeed; no dashed New collection tile; personal and server cards load independently.");
        }

        internal async Task GroupsSurviveRebuildAsync()
        {
            wire.Reset(own: 1, shared: 1, libraries: 2);
            var (frame, page) = await OpenWall(1280, 900);
            try
            {
                await Until(() => page.ViewModel.PersonalLoaded && !page.ViewModel.LoadCollectionsCommand.IsRunning);
                var add = (Button)page.FindName("AddGroupButton");
                var editor = (StackPanel)page.FindName("AddGroupEditor");
                Check(add.Visibility == Visibility.Visible, "The wall must offer Add group.");
                await Press(add);
                var input = (TextBox)page.FindName("AddGroupNameBox");
                input.Text = "Weekend groups";
                await page.ViewModel.LoadCollectionsCommand.ExecuteAsync(null);
                await Task.Delay(150);
                Check(editor.Visibility == Visibility.Visible && input.Text == "Weekend groups", "A wall rebuild must not discard the open group editor.");
                await Press(Descendants<Button>(editor).Single(button => Equals(button.Content, "Add")));
                var grid = (StackPanel)page.FindName("CollectionsGrid");
                await Until(() => page.ViewModel.Groups.Count == 1 && grid.Children.OfType<StackPanel>().Any(child => Auto.GetName(child) == "Weekend groups"));
                await page.ViewModel.LoadCollectionsCommand.ExecuteAsync(null);
                await Layout(frame, 1280, 900);
                var section = grid.Children.OfType<StackPanel>().Single(child => Auto.GetName(child) == "Weekend groups");
                Check(Descendants<Button>(section).Any(button => Auto.GetName(button) == "Rename group")
                    && Descendants<Button>(section).Any(button => Auto.GetName(button) == "Delete group"), "A rebuilt group exposes its editing controls.");
                Check(OpenButtons(grid).Count() == 1 + 1 + 3, "Group sections must not hide or duplicate own, shared or server posters.");
                var own = Descendants<Border>(grid).First(border => border.Tag is SiloPlayer.Core.Models.Collections.Collection { Id: "own-1" });
                Check(((MenuFlyout)own.ContextFlyout).Items.OfType<MenuFlyoutSubItem>().Any(menu => menu.Text == "Move to group" && menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Weekend groups")),
                    "The owner card must offer the created group as a move target.");
                await Press(Pill(page, "Shared"));
                Check(add.Visibility == Visibility.Collapsed && !grid.Children.OfType<StackPanel>().Any(child => Auto.GetName(child) == "Weekend groups"), "Shared must not show the viewer's editable groups.");
                await Press(Pill(page, "Mine"));
                Check(add.Visibility == Visibility.Visible && grid.Children.OfType<StackPanel>().Any(child => Auto.GetName(child) == "Weekend groups"), "Mine restores group sections and Add group.");
            }
            finally { parent.Children.Remove(frame); }
            Log("PASS: Add group creates a persistent section; rebuilds preserve the editor, group controls and owner move targets; filters preserve ownership.");
        }

        internal async Task ServerPaginationAsync()
        {
            wire.Reset(own: 1, shared: 0, libraries: 2, singleProfile: true);
            var (frame, page) = await OpenWall(1280, 900);
            try
            {
                await Until(() => Pills(page).Length == 5 && OpenButtons(Board(page)).Count() == 1 + 3);
                await Press(Pill(page, "Server"));
                wire.FailLibraryTab = true;
                await Press(ServerRows(page).Single(b => Auto.GetName(b) == "Load more server collections"));
                await Until(() => ServerRows(page).Any(b => Auto.GetName(b) == "Retry Movies"));
                Check(Names(page).SequenceEqual(["Criterion", "A24", "Anime"]), "A failed library list keeps the first cards.");
                Check(Descendants<TextBlock>((StackPanel)page.FindName("ServerCollectionsRows")).Any(t => t.Text.Contains("Couldn't load all Movies collections")), "The failure is stated for that library.");
                var seeAll = ServerRows(page).Single(b => Auto.GetName(b) == "See all 3 Movies collections");
                Type? target = null; object? route = null; navigation.Frame = new Frame(); navigation.NavigationRequestHandler = (type, args) => { target = type; route = args; return true; };
                Invoke(seeAll);
                Check(target == typeof(LibraryPage) && route is LibraryPage.NavigationArgs { InitialTab: "Collections" } args && args.Library.Id == 1, "See all opens that library's Collections tab.");
                navigation.NavigationRequestHandler = null;
                wire.FailLibraryTab = false;
                await Press(ServerRows(page).Single(b => Auto.GetName(b) == "Retry Movies"));
                await Until(() => Names(page).Count == 4);
                await Until(() => FocusManager.GetFocusedElement(page.XamlRoot) is Button focused && Auto.GetName(focused) == "Open Studio Ghibli");
                Check(wire.TabReads(1) == 2, "Retry read the library list once more.");
                Log("PASS: server pages stay incremental: Load more, per-library failure with Retry and See all, and focus on the first added card.");
            }
            finally { parent.Children.Remove(frame); navigation.NavigationRequestHandler = null; }
        }

        // ===== The collection page hero =====
        internal async Task HeroAsync()
        {
            foreach (var width in new[] { 1280, 460 })
            {
                // Server collection: backdrop (thumbhash first), description, count, Add to my Home.
                wire.Reset(own: 1, shared: 1, libraries: 2); wire.BackdropGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var id = "srv-hero-" + width; wire.Described(id, libraryId: 1, description: "A hand-picked set.\nSecond line stays on its own line.\nThird line.\nFourth line pushes past three.", backdrop: true);
                var (frame, page) = await OpenCollection(width, 900, new CollectionBrowsePage.NavArgs { CollectionId = id, Title = "Server picks", IsUserCollection = false, LibraryId = 1 });
                try
                {
                    var backdrop = (CollectionHeroBackdrop)page.FindName("HeroBackdrop"); var description = (ExpandableDescription)page.FindName("CollectionDescription");
                    await Until(() => backdrop.Visibility == Visibility.Visible && backdrop.ShowsPlaceholder);
                    Check(!backdrop.ShowsArtwork && backdrop.BackdropThumbhash == SampleThumbhash, "The thumbhash shows before the real backdrop.");
                    wire.BackdropGate.SetResult(true);
                    await Until(() => backdrop.ShowsArtwork);
                    Check(backdrop.BackdropUrl == $"https://collections-presentation.invalid/backdrop/{id}.png?sig=wide" && wire.BackdropRequests.Contains($"/backdrop/{id}.png?sig=wide"), "The backdrop is fetched from exactly the URL the Collections tab returned.");
                    await Until(() => description.Visibility == Visibility.Visible);
                    var text = Descendants<TextBlock>(description).First();
                    Check(text.Text.Contains("\n") && text.Text.StartsWith("A hand-picked set.") && text.MaxLines == 3, "The description keeps its line breaks and clamps to three lines.");
                    await Until(() => Descendants<Button>(description).Any(b => Equals(b.Content, "More") && b.Visibility == Visibility.Visible));
                    Check(((TextBlock)page.FindName("CollectionEyebrow")).Visibility == (width < 640 ? Visibility.Collapsed : Visibility.Visible) && ((TextBlock)page.FindName("CollectionEyebrow")).Text == "COLLECTION", "The Collection eyebrow shows from tablet width.");
                    Check(!Descendants<TextBlock>(page).Any(t => t.Text.Contains("Refine the archive")), "The generic subtitle is gone.");
                    await Until(() => ((TextBlock)page.FindName("LoadedCountText")).Visibility == Visibility.Visible);
                    Check(((TextBlock)page.FindName("LoadedCountText")).Text == "6 titles", $"The count moved into the filter bar: '{((TextBlock)page.FindName("LoadedCountText")).Text}'.");
                    var home = (Button)page.FindName("CollectionAddToHomeButton"); var edit = (Button)page.FindName("CollectionEditButton"); var shuffle = (Button)page.FindName("CollectionShuffleButton");
                    Check(home.Visibility == Visibility.Visible && edit.Visibility == Visibility.Collapsed && shuffle.Visibility == Visibility.Visible, "A server collection offers Shuffle and Add to my Home, not Edit.");
                    Check(((TextBlock)page.FindName("CollectionByline")).Visibility == Visibility.Collapsed, "A server collection has no owner byline.");
                    var header = (FrameworkElement)page.FindName("CollectionHeaderGrid");
                    if (width >= 1024) Check(header.ActualHeight >= 207, $"A backdrop hero keeps its minimum height (was {header.ActualHeight}).");
                    foreach (var control in new[] { shuffle, home })
                    {
                        var bounds = control.TransformToVisual(page).TransformBounds(new(0, 0, control.ActualWidth, control.ActualHeight));
                        Check(bounds.Left >= 0 && bounds.Right <= width + .5 && bounds.Width > 0, $"{Auto.GetName(control)} is clipped at {width}px (right edge {bounds.Right}).");
                    }
                    await Task.Delay(300);
                    await Capture(page, $"collections-hero-server-{width}.png");

                    Type? destination = null; object? arguments = null; navigation.Frame = new Frame(); navigation.NavigationRequestHandler = (type, args) => { destination = type; arguments = args; return true; };
                    Invoke(home);
                    var routed = arguments as CustomizeHomeNavigationArgs;
                    Check(destination == typeof(CustomizeHomePage) && routed is { ServerCollection: true } && routed.CollectionId == id, "Add to my Home names the server collection.");
                    navigation.NavigationRequestHandler = null;
                }
                finally { parent.Children.Remove(frame); }
            }
            await HeroOwnerAndFallbacksAsync();
            Log("PASS: collection page hero at 1280/460: thumbhash then backdrop, description clamp, eyebrow, count in filter bar, actions and plain fallbacks.");
        }

        private async Task HeroOwnerAndFallbacksAsync()
        {
            // Own personal collection: description only (no backdrop), Edit.
            wire.Reset(own: 1, shared: 1, libraries: 1);
            wire.PersonalSnapshot = new { id = "own-1", creator_profile_id = "me", profile_id = "me", name = "Mine", description = "  Only mine.  ", collection_type = "manual" };
            var (frame, page) = await OpenCollection(1280, 900, new CollectionBrowsePage.NavArgs { CollectionId = "own-1", Title = "Mine", IsUserCollection = true });
            try
            {
                var description = (ExpandableDescription)page.FindName("CollectionDescription"); var edit = (Button)page.FindName("CollectionEditButton");
                await Until(() => edit.Visibility == Visibility.Visible && description.Visibility == Visibility.Visible);
                Check(Descendants<TextBlock>(description).First().Text == "Only mine." && ((CollectionHeroBackdrop)page.FindName("HeroBackdrop")).Visibility == Visibility.Collapsed, "A personal collection shows its description over the plain page.");
                Check(((Button)page.FindName("CollectionAddToHomeButton")).Visibility == Visibility.Collapsed && ((TextBlock)page.FindName("CollectionByline")).Visibility == Visibility.Collapsed, "The owner edits; no read-only byline or Home pill.");
                Check(((FrameworkElement)page.FindName("CollectionHeaderGrid")).MinHeight == 0, "Without a backdrop the header is the plain layout.");
                await Task.Delay(300); await Capture(page, "collections-hero-plain-1280.png");
                Type? destination = null; object? arguments = null; navigation.Frame = new Frame(); navigation.NavigationRequestHandler = (type, args) => { destination = type; arguments = args; return true; };
                Invoke(edit);
                Check(destination == typeof(CollectionEditorPage) && arguments is CollectionEditorNavigationArgs { CollectionId: "own-1" }, "Edit opens the collection's editor.");
                navigation.NavigationRequestHandler = null;
            }
            finally { parent.Children.Remove(frame); }

            // Someone else's shared collection: byline, Add to my Home (their id, not a server collection).
            wire.PersonalSnapshot = new { id = "sh-1", creator_profile_id = "other", name = "Theirs", is_shared = true, collection_type = "manual" };
            var (sharedFrame, shared) = await OpenCollection(900, 800, new CollectionBrowsePage.NavArgs { CollectionId = "sh-1", Title = "Theirs", IsUserCollection = true });
            try
            {
                var byline = (TextBlock)shared.FindName("CollectionByline");
                await Until(() => byline.Visibility == Visibility.Visible);
                Check(string.Concat(byline.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().Select(run => run.Text)) == "by Other profile · Read-only", "The byline names the owner and says Read-only.");
                Check(((Button)shared.FindName("CollectionAddToHomeButton")).Visibility == Visibility.Visible && ((Button)shared.FindName("CollectionEditButton")).Visibility == Visibility.Collapsed && ((ExpandableDescription)shared.FindName("CollectionDescription")).Visibility == Visibility.Collapsed,
                    "A read-only collection offers Add to my Home and no Edit; no description means no empty line.");
                Type? destination = null; object? arguments = null; navigation.Frame = new Frame(); navigation.NavigationRequestHandler = (type, args) => { destination = type; arguments = args; return true; };
                Invoke((Button)shared.FindName("CollectionAddToHomeButton"));
                Check(destination == typeof(CustomizeHomePage) && arguments is CustomizeHomeNavigationArgs { CollectionId: "sh-1", ServerCollection: false }, "Add to my Home for a personal collection names a user collection.");
                navigation.NavigationRequestHandler = null;
            }
            finally { parent.Children.Remove(sharedFrame); }

            // An unreadable Collections tab keeps the plain header and the page still loads.
            wire.Reset(own: 0, shared: 0, libraries: 1); wire.FailLibraryTab = true;
            var (failedFrame, failed) = await OpenCollection(900, 800, new CollectionBrowsePage.NavArgs { CollectionId = "srv-x", Title = "Plain", IsUserCollection = false, LibraryId = 1 });
            try
            {
                await Until(() => ((TextBlock)failed.FindName("LoadedCountText")).Visibility == Visibility.Visible);
                await Task.Delay(250);
                Check(((CollectionHeroBackdrop)failed.FindName("HeroBackdrop")).Visibility == Visibility.Collapsed && ((ExpandableDescription)failed.FindName("CollectionDescription")).Visibility == Visibility.Collapsed, "A failed Collections-tab read keeps the plain header.");
                Check(((Button)failed.FindName("CollectionAddToHomeButton")).Visibility == Visibility.Visible, "Add to my Home still works for a server collection.");
            }
            finally { parent.Children.Remove(failedFrame); }

            // A header that arrives after the viewer left its page cannot repaint that page, and the next page keeps its own.
            wire.Reset(own: 0, shared: 0, libraries: 2); wire.TabGate = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.TabGateIgnoresCancellation = true;
            wire.Described("late", libraryId: 1, description: "Stale", backdrop: true); wire.Described("current", libraryId: 2, description: "Current", backdrop: false);
            var (staleFrame, stale) = await OpenCollection(900, 800, new CollectionBrowsePage.NavArgs { CollectionId = "late", Title = "Late", IsUserCollection = false, LibraryId = 1 });
            try
            {
                await Until(() => wire.TabReads(1) == 1);
                navigation.Frame = staleFrame; staleFrame.Navigate(typeof(CollectionBrowsePage), new CollectionBrowsePage.NavArgs { CollectionId = "current", Title = "Current", IsUserCollection = false, LibraryId = 2 });
                var current = (CollectionBrowsePage)staleFrame.Content;
                await Until(() => wire.TabReads(2) == 1);
                wire.TabGate.TrySetResult(true);
                var currentDescription = (ExpandableDescription)current.FindName("CollectionDescription");
                await Until(() => currentDescription.Visibility == Visibility.Visible);
                await Task.Delay(400);
                Check(Descendants<TextBlock>(currentDescription).First().Text == "Current" && ((CollectionHeroBackdrop)current.FindName("HeroBackdrop")).Visibility == Visibility.Collapsed,
                    "A superseded collection's header repainted the current page.");
                Check(((ExpandableDescription)stale.FindName("CollectionDescription")).Visibility == Visibility.Collapsed && ((CollectionHeroBackdrop)stale.FindName("HeroBackdrop")).Visibility == Visibility.Collapsed,
                    "A header that arrived after its page was left repainted the departed page.");
            }
            finally { wire.TabGate?.TrySetResult(true); wire.TabGateIgnoresCancellation = false; parent.Children.Remove(staleFrame); }
            Log("PASS: owner Edit, read-only byline and Add to my Home, plain fallbacks and navigation ownership of the header.");
        }

        // ===== The hero's More actions menu =====
        internal async Task HeroMoreActionsAsync()
        {
            await OwnerMenuAndSyncAsync();
            await OwnerDeleteAsync();
            await ReadOnlyLibraryPagesAsync();
            await ActionOwnershipAsync();
            await CardMenuSharesTheActionsAsync();
            Log("PASS: hero More actions: owner Sync now / Delete… with confirmation, cancellation and failure, read-only library pages, and late navigation ownership.");
        }

        private static object SyncedList(bool shared = true, string type = "mdblist") => new { id = "own-1", creator_profile_id = "me", profile_id = "me", name = "Weekly list", collection_type = type, is_shared = shared, item_count = 6 };
        private static Button More(CollectionBrowsePage page) => (Button)page.FindName("CollectionMoreButton");
        private static bool Busy(CollectionBrowsePage page) => (bool)typeof(CollectionBrowsePage).GetField("_heroActionBusy", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        private static TextBlock Status(CollectionBrowsePage page) => (TextBlock)page.FindName("CollectionActionStatus");
        private static MenuFlyoutItem[] Entries(MenuFlyout menu) => menu.Items.OfType<MenuFlyoutItem>().ToArray();
        private static void Choose(MenuFlyoutItemBase item) => ((IInvokeProvider)new MenuFlyoutItemAutomationPeer((MenuFlyoutItem)item).GetPattern(PatternInterface.Invoke)).Invoke();
        private static bool MenuIsOpen(XamlRoot root) => VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.Child is MenuFlyoutPresenter || Descendants<MenuFlyoutPresenter>(popup.Child).Any());
        private static IEnumerable<ContentDialog> Dialogs(XamlRoot root) => VisualTreeHelper.GetOpenPopupsForXamlRoot(root).SelectMany(popup => popup.Child is ContentDialog own ? new[] { own } : Descendants<ContentDialog>(popup.Child));
        private static async Task CaptureMenu(CollectionBrowsePage page, string name)
        {
            var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(popup => popup.Child is MenuFlyoutPresenter own ? new[] { own } : Descendants<MenuFlyoutPresenter>(popup.Child)).First();
            await Capture(presenter, name);
        }
        private static string Describe(MenuFlyout menu) => string.Join(" | ", menu.Items.Select(item => item is MenuFlyoutSubItem sub ? sub.Text + "[" + string.Join(",", sub.Items.OfType<MenuFlyoutItem>().Select(entry => entry.Text)) + "]" : item is MenuFlyoutItem entry ? entry.Text : "-"));

        private async Task<MenuFlyout> OpenMore(CollectionBrowsePage page)
        {
            var more = More(page);
            // Choosing an entry through automation leaves the menu open (a click would close it): close it so the next open is a fresh one.
            if (more.Flyout is MenuFlyout previous && MenuIsOpen(page.XamlRoot)) { previous.Hide(); await Until(() => !MenuIsOpen(page.XamlRoot)); await Task.Delay(200); }
            for (var attempt = 0; attempt < 3 && !MenuIsOpen(page.XamlRoot); attempt++)
            {
                Invoke(more);
                for (var wait = 0; wait < 24 && !MenuIsOpen(page.XamlRoot); wait++) await Task.Delay(25);
            }
            await Until(() => MenuIsOpen(page.XamlRoot), () => "open popups: " + VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).Count);
            await Task.Delay(150);   // the menu's Opened handler refreshes its entries once the popup is up
            return (MenuFlyout)more.Flyout;
        }

        /// <summary>Opens the owner's personal collection "own-1" through the real page and waits for its header.</summary>
        private async Task<(Frame Frame, CollectionBrowsePage Page)> OpenOwned(object snapshot, double width = 1280, bool imports = true)
        {
            wire.Reset(own: 1, shared: 0, libraries: 1); wire.PersonalSnapshot = snapshot; wire.Imports = imports;
            var opened = await OpenCollection(width, 900, new CollectionBrowsePage.NavArgs { CollectionId = "own-1", Title = "Weekly list", IsUserCollection = true });
            await Until(() => ((Button)opened.Page.FindName("CollectionEditButton")).Visibility == Visibility.Visible && More(opened.Page).Visibility == Visibility.Visible);
            return opened;
        }

        /// <summary>The Collections card menu, which now shares the hero's policy, failure text and delete confirmation, still behaves as before.</summary>
        private async Task CardMenuSharesTheActionsAsync()
        {
            wire.Reset(own: 2, shared: 0, libraries: 0); wire.FirstOwnType = "mdblist";
            var (frame, page) = await OpenWall(900, 800);
            try
            {
                Border Card() => Descendants<Border>(Board(page)).First(border => border.Tag is SiloPlayer.Core.Models.Collections.Collection { Name: "Weekend picks 1" });
                await Until(() => Names(page).Contains("Weekend picks 1") && Card().ContextFlyout is MenuFlyout menu && Entries(menu).Any(item => item.Text == "Sync now"), () => string.Join(",", Names(page)));
                wire.SyncStatus = "failed"; wire.SyncMessage = "No key";
                Choose(Entries((MenuFlyout)Card().ContextFlyout).Single(item => item.Text == "Sync now"));
                await Until(() => wire.SyncRequests.Count == 1 && page.ViewModel.ErrorMessage == "Collection sync failed: No key", () => "error=" + page.ViewModel.ErrorMessage);

                Choose(Entries((MenuFlyout)Card().ContextFlyout).Single(item => item.Text == "Delete…"));
                await Until(() => Dialogs(page.XamlRoot).Any());
                var dialog = Dialogs(page.XamlRoot).Single();
                Check(Equals(dialog.Title, "Delete \"Weekend picks 1\"?") && Descendants<TextBlock>(dialog).Any(block => block.Text == "It's removed for you and every profile you share it with. This can't be undone."), "The card menu uses the same confirmation text.");
                Invoke(Descendants<Button>(dialog).Single(button => button.Name == "CloseButton"));
                await Until(() => !Dialogs(page.XamlRoot).Any());
                Check(wire.DeleteRequests.Count == 0 && Names(page).Contains("Weekend picks 1"), "Cancelling the card confirmation deletes nothing.");

                Choose(Entries((MenuFlyout)Card().ContextFlyout).Single(item => item.Text == "Delete…"));
                await Until(() => Dialogs(page.XamlRoot).Any());
                dialog = Dialogs(page.XamlRoot).Single();
                wire.FailDelete = true;
                var primary = Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton");
                Invoke(primary);
                await Until(() => Descendants<TextBlock>(dialog).Any(block => block.Visibility == Visibility.Visible && block.Text.Contains("delete refused")), () => string.Join(" / ", Descendants<TextBlock>(dialog).Select(block => block.Text)));
                Check(Dialogs(page.XamlRoot).Any() && Names(page).Contains("Weekend picks 1") && primary.IsEnabled, "A refused delete keeps the card confirmation open with its error.");
                wire.FailDelete = false;
                Invoke(primary);
                await Until(() => !Dialogs(page.XamlRoot).Any() && !Names(page).Contains("Weekend picks 1"), () => string.Join(",", Names(page)));
                Check(wire.DeleteRequests.Count == 2 && wire.DeleteRequests[1] == "/api/v2/collections/own-1 if-match=\"rev-1\"", "The card delete carried the revision: " + string.Join(",", wire.DeleteRequests));
            }
            finally { foreach (var open in Dialogs(page.XamlRoot).ToList()) open.Hide(); parent.Children.Remove(frame); }
            Log("PASS: Collections card menu Sync now failure text and Delete… confirmation (text, cancel, refusal, confirm) share the hero's policy.");
        }

        private async Task OwnerMenuAndSyncAsync()
        {
            foreach (var width in new[] { 1280, 460 })
            {
                var (frame, page) = await OpenOwned(SyncedList(), width);
                try
                {
                    var more = More(page);
                    Check(Auto.GetName(more) == "More actions", "More actions has its accessible name.");
                    var bounds = more.TransformToVisual(page).TransformBounds(new(0, 0, more.ActualWidth, more.ActualHeight));
                    Check(bounds.Left >= 0 && bounds.Right <= width + .5 && more.ActualWidth >= 40, $"More actions is clipped at {width}px (right edge {bounds.Right}).");
                    var menu = await OpenMore(page);
                    Check(Entries(menu).Select(item => item.Text).SequenceEqual(["Sync now", "Delete…"]), "The owner of a synced list sees Sync now and Delete…: " + Describe(menu));
                    Check(menu.Items.Count == 3 && menu.Items[1] is MenuFlyoutSeparator, "Sync now and Delete… are separated.");
                    Check(Entries(menu).All(item => item.Icon != null), "Every entry carries its icon.");
                    await Task.Delay(200); await Capture(page, $"collections-hero-more-owner-{width}.png"); await CaptureMenu(page, $"collections-hero-more-owner-menu-{width}.png");
                    menu.Hide(); await Task.Delay(120);
                }
                finally { parent.Children.Remove(frame); }
            }

            // Sync now from the real menu: in flight, then the titles and header are read again.
            var (ownerFrame, owner) = await OpenOwned(SyncedList());
            try
            {
                await Until(() => wire.CatalogRequests.Count >= 1);
                wire.SyncGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Choose(Entries(await OpenMore(owner))[0]);
                await Until(() => wire.SyncRequests.Count == 1);
                Check(Status(owner).Visibility == Visibility.Visible && Status(owner).Text == "Syncing…", "A sync in flight says so.");
                var whileSyncing = await OpenMore(owner);
                Check(!Entries(whileSyncing)[0].IsEnabled && Entries(whileSyncing)[0].Text == "Syncing…" && !Entries(whileSyncing)[1].IsEnabled, "Sync now and Delete… wait while a sync runs: " + string.Join(" | ", Entries(whileSyncing).Select(item => item.Text + "=" + item.IsEnabled)));
                whileSyncing.Hide(); await Task.Delay(100);
                var catalogBefore = wire.CatalogRequests.Count; var snapshotBefore = wire.SnapshotReads;
                wire.SyncGate.SetResult(true);
                await Until(() => Status(owner).Text == "Synced 9 titles");
                await Until(() => wire.CatalogRequests.Count > catalogBefore && wire.SnapshotReads > snapshotBefore);
                Check(wire.SyncRequests.Count == 1 && wire.SyncRequests[0] == "/api/v2/collections/own-1/sync", "Exactly one sync request, for this collection.");
                await Until(() => More(owner).Visibility == Visibility.Visible && More(owner).Flyout is MenuFlyout);
                var reopened = await OpenMore(owner);
                Check(Entries(reopened)[0].Text == "Sync now" && Entries(reopened)[0].IsEnabled && Entries(reopened)[1].IsEnabled, "The menu is usable again once the sync finished.");
                reopened.Hide(); await Task.Delay(100);

                // A reported failure: shown as an error, the titles are not read again.
                wire.SyncStatus = "failed"; wire.SyncMessage = "The list is private";
                var catalogAfter = wire.CatalogRequests.Count;
                Choose(Entries(await OpenMore(owner))[0]);
                await Until(() => Status(owner).Text == "Collection sync failed: The list is private");
                await Task.Delay(300);
                Check(ReferenceEquals(Status(owner).Foreground, Application.Current.Resources["ErrorBrush"]) && wire.CatalogRequests.Count == catalogAfter, "A failed sync shows an error and leaves the titles alone.");
                // A transport failure.
                wire.FailSync = true;
                Choose(Entries(await OpenMore(owner))[0]);
                await Until(() => Status(owner).Text.StartsWith("Failed to sync collection:", StringComparison.Ordinal));
                // A sync with warnings.
                wire.FailSync = false; wire.SyncStatus = "warning"; wire.SyncMessage = "";
                Choose(Entries(await OpenMore(owner))[0]);
                await Until(() => Status(owner).Text == "Synced 9 titles with warnings");
                Check(wire.SyncRequests.Count == 4, $"Four syncs were requested (was {wire.SyncRequests.Count}).");
            }
            finally { wire.SyncGate?.TrySetResult(true); parent.Children.Remove(ownerFrame); }

            // Sync only for a synced list on a server that can import; Delete… is always there for the owner.
            foreach (var (label, snapshot, imports) in new (string, object, bool)[] { ("server cannot import", SyncedList(), false), ("manual collection", SyncedList(type: "manual"), true), ("smart collection", SyncedList(type: "smart"), true) })
            {
                var (frame, page) = await OpenOwned(snapshot, 900, imports);
                try
                {
                    var menu = await OpenMore(page);
                    Check(Entries(menu).Select(item => item.Text).SequenceEqual(["Delete…"]), $"{label}: only Delete… ({Describe(menu)}).");
                    menu.Hide(); await Task.Delay(100);
                }
                finally { parent.Children.Remove(frame); }
            }
            Log("PASS: owner More actions: Sync now (in flight, disabled while running, success, reported failure, transport failure, warnings, reload of titles/header) and the capability/type rules.");
        }

        private async Task OwnerDeleteAsync()
        {
            foreach (var shared in new[] { true, false })
            {
                var (frame, page) = await OpenOwned(SyncedList(shared, shared ? "mdblist" : "manual"));
                try
                {
                    Type? destination = null; navigation.Frame = new Frame(); navigation.NavigationRequestHandler = (type, _) => { destination = type; return true; };
                    var menu = await OpenMore(page);
                    Choose(Entries(menu).Single(item => item.Text == "Delete…"));
                    await Until(() => Dialogs(page.XamlRoot).Any());
                    var dialog = Dialogs(page.XamlRoot).Single();
                    Check(Equals(dialog.Title, "Delete \"Weekly list\"?"), $"The confirmation names the collection: '{dialog.Title}'.");
                    var text = Descendants<TextBlock>(dialog).Select(block => block.Text).ToList();
                    Check(text.Contains(shared ? "It's removed for you and every profile you share it with. This can't be undone." : "This can't be undone."), "The confirmation says who loses the collection: " + string.Join(" / ", text));
                    // Cancel: nothing is deleted and the menu is usable again.
                    Invoke(Descendants<Button>(dialog).Single(button => button.Name == "CloseButton"));
                    await Until(() => !Dialogs(page.XamlRoot).Any());
                    Check(wire.DeleteRequests.Count == 0 && destination == null, "Cancelling the confirmation deletes nothing and stays on the page.");
                    await Until(() => !Busy(page));
                    var again = await OpenMore(page);
                    Check(Entries(again).Single(item => item.Text == "Delete…").IsEnabled, "Delete… is usable after a cancelled confirmation: " + Describe(again) + " busy=" + Busy(page) + string.Join(",", Entries(again).Select(item => item.IsEnabled)));

                    // A refused delete keeps the dialog open with the error and leaves the page.
                    wire.FailDelete = true;
                    Choose(Entries(again).Single(item => item.Text == "Delete…"));
                    await Until(() => Dialogs(page.XamlRoot).Any());
                    dialog = Dialogs(page.XamlRoot).Single();
                    var primary = Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton");
                    Invoke(primary);
                    await Until(() => Descendants<TextBlock>(dialog).Any(block => block.Visibility == Visibility.Visible && block.Text.Contains("delete refused")), () => string.Join(" / ", Descendants<TextBlock>(dialog).Select(block => block.Text)));
                    Check(wire.DeleteRequests.Count == 1 && wire.DeleteRequests[0] == "/api/v2/collections/own-1 if-match=\"rev-1\"", "The delete carried the collection's revision: " + string.Join(",", wire.DeleteRequests));
                    Check(Dialogs(page.XamlRoot).Any() && primary.IsEnabled && Equals(dialog.PrimaryButtonText, "Delete") && destination == null, "A refused delete keeps the dialog and the page.");

                    // Confirm for real: gone, back to the collections list.
                    wire.FailDelete = false;
                    Invoke(primary);
                    await Until(() => !Dialogs(page.XamlRoot).Any() && destination != null);
                    Check(wire.DeleteRequests.Count == 2 && destination == typeof(CollectionsPage), "A confirmed delete returns to Collections.");
                }
                finally { navigation.NavigationRequestHandler = null; foreach (var open in Dialogs(page.XamlRoot).ToList()) open.Hide(); parent.Children.Remove(frame); }
            }
            Log("PASS: owner Delete… confirmation text, cancellation, refused delete keeps the dialog, confirmed delete carries the revision and returns to Collections.");
        }

        private async Task ReadOnlyLibraryPagesAsync()
        {
            // Someone else's shared manual collection: every library page, grouped; never Sync or Delete.
            wire.Reset(own: 0, shared: 1, libraries: 1);
            wire.PersonalSnapshot = new { id = "sh-1", creator_profile_id = "other", name = "Theirs", is_shared = true, collection_type = "manual" };
            var (frame, page) = await OpenCollection(900, 800, new CollectionBrowsePage.NavArgs { CollectionId = "sh-1", Title = "Theirs", IsUserCollection = true });
            try
            {
                await Until(() => More(page).Visibility == Visibility.Visible);
                Check(((Button)page.FindName("CollectionAddToHomeButton")).Visibility == Visibility.Visible && ((Button)page.FindName("CollectionEditButton")).Visibility == Visibility.Collapsed, "A read-only collection keeps Add to my Home beside More actions.");
                var menu = await OpenMore(page);
                var group = menu.Items.OfType<MenuFlyoutSubItem>().Single();
                Check(menu.Items.Count == 1 && group.Text == "Add to my library page" && group.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).SequenceEqual(["Movies page", "Shows page", "Kids page"]),
                    "A read-only collection groups the library pages: " + Describe(menu));
                await Task.Delay(200); await Capture(page, "collections-hero-more-readonly-900.png"); await CaptureMenu(page, "collections-hero-more-readonly-menu-900.png");
                Type? destination = null; object? arguments = null; navigation.Frame = new Frame(); navigation.NavigationRequestHandler = (type, args) => { destination = type; arguments = args; return true; };
                Choose(group.Items.OfType<MenuFlyoutItem>().Single(item => item.Text == "Shows page"));
                Check(destination == typeof(CustomizeHomePage) && arguments is CustomizeHomeNavigationArgs { CollectionId: "sh-1", LibraryId: 2, ServerCollection: false }, "Add to my Shows page opens that library page for the shared collection.");
            }
            finally { navigation.NavigationRequestHandler = null; parent.Children.Remove(frame); }

            // A synced list restricted to one library: that page directly, no submenu.
            wire.Reset(own: 0, shared: 1, libraries: 1);
            wire.PersonalSnapshot = new { id = "sh-2", creator_profile_id = "other", name = "Kids picks", collection_type = "mdblist", source_config = new { library_ids = new[] { 3 } } };
            var (oneFrame, one) = await OpenCollection(900, 800, new CollectionBrowsePage.NavArgs { CollectionId = "sh-2", Title = "Kids picks", IsUserCollection = true });
            try
            {
                await Until(() => More(one).Visibility == Visibility.Visible);
                var menu = await OpenMore(one);
                Check(Entries(menu).Select(item => item.Text).SequenceEqual(["Add to my Kids page"]) && !menu.Items.OfType<MenuFlyoutSubItem>().Any(), "One matched library is a direct entry: " + Describe(menu));
                Type? destination = null; object? arguments = null; navigation.Frame = new Frame(); navigation.NavigationRequestHandler = (type, args) => { destination = type; arguments = args; return true; };
                Choose(Entries(menu)[0]);
                Check(destination == typeof(CustomizeHomePage) && arguments is CustomizeHomeNavigationArgs { CollectionId: "sh-2", LibraryId: 3 }, "The direct entry names the matched library.");
            }
            finally { navigation.NavigationRequestHandler = null; parent.Children.Remove(oneFrame); }

            // A server collection opened in a library: that library first, then the others, naming a library collection row.
            wire.Reset(own: 0, shared: 0, libraries: 2); wire.Described("srv-more", libraryId: 2, description: "Server set", backdrop: false);
            var (serverFrame, server) = await OpenCollection(900, 800, new CollectionBrowsePage.NavArgs { CollectionId = "srv-more", Title = "Server set", IsUserCollection = false, LibraryId = 2 });
            try
            {
                await Until(() => More(server).Visibility == Visibility.Visible);
                var menu = await OpenMore(server);
                var group = menu.Items.OfType<MenuFlyoutSubItem>().Single();
                Check(group.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).SequenceEqual(["Shows page", "Movies page", "Kids page"]), "The opened-in library comes first: " + Describe(menu));
                Check(((Button)server.FindName("CollectionEditButton")).Visibility == Visibility.Collapsed && menu.Items.Count == 1, "A server collection is read-only on Desktop: no Edit, Sync or Delete.");
                Type? destination = null; object? arguments = null; navigation.Frame = new Frame(); navigation.NavigationRequestHandler = (type, args) => { destination = type; arguments = args; return true; };
                Choose(group.Items.OfType<MenuFlyoutItem>().Single(item => item.Text == "Movies page"));
                Check(destination == typeof(CustomizeHomePage) && arguments is CustomizeHomeNavigationArgs { CollectionId: "srv-more", LibraryId: 1, ServerCollection: true }, "A server collection row is added on that library's page.");
            }
            finally { navigation.NavigationRequestHandler = null; parent.Children.Remove(serverFrame); }

            // Unreadable libraries: no menu at all, Add to my Home still works.
            wire.Reset(own: 0, shared: 0, libraries: 1); wire.FailLibraries = true; catalog.InvalidateLibraryCache();   // the library list is cached for minutes
            wire.Described("srv-none", libraryId: 1, description: "x", backdrop: false);
            var (noneFrame, none) = await OpenCollection(900, 800, new CollectionBrowsePage.NavArgs { CollectionId = "srv-none", Title = "None", IsUserCollection = false, LibraryId = 1 });
            try
            {
                await Until(() => ((Button)none.FindName("CollectionAddToHomeButton")).Visibility == Visibility.Visible);
                await Task.Delay(250);
                Check(More(none).Visibility == Visibility.Collapsed && More(none).Flyout == null, "An empty menu has no button.");
            }
            finally { parent.Children.Remove(noneFrame); }
            Log("PASS: read-only More actions: grouped / direct / server-ordered library pages route to the right library page, no Sync or Delete, no empty menu.");
        }

        private async Task ActionOwnershipAsync()
        {
            // A sync that finishes after the viewer left the page repaints nothing and reads nothing.
            var (frame, page) = await OpenOwned(SyncedList());
            try
            {
                await Until(() => wire.CatalogRequests.Count >= 1);
                wire.SyncGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Choose(Entries(await OpenMore(page))[0]);
                await Until(() => wire.SyncRequests.Count == 1);
                var ownBefore = wire.CatalogRequests.Count(query => query.Contains("own-1")); var snapshotsBefore = wire.SnapshotReads;
                navigation.Frame = frame; frame.Navigate(typeof(CollectionBrowsePage), new CollectionBrowsePage.NavArgs { CollectionId = "current", Title = "Current", IsUserCollection = false, LibraryId = 1 });
                var current = (CollectionBrowsePage)frame.Content;
                await Until(() => More(page).Visibility == Visibility.Collapsed && Status(page).Visibility == Visibility.Collapsed);
                var currentSnapshots = wire.SnapshotReads;
                wire.SyncGate.SetResult(true);
                await Task.Delay(500);
                Check(Status(page).Visibility == Visibility.Collapsed && Status(current).Visibility == Visibility.Collapsed, "A sync result arrived after its page was left and repainted a page.");
                Check(wire.CatalogRequests.Count(query => query.Contains("own-1")) == ownBefore, "A sync result arrived after its page was left and reloaded its titles.");
                Check(wire.SnapshotReads == currentSnapshots && snapshotsBefore <= currentSnapshots, "A sync result arrived after its page was left and re-read its header.");
            }
            finally { wire.SyncGate?.TrySetResult(true); parent.Children.Remove(frame); }

            // A confirmation left open over a page the viewer has since left cannot delete the collection.
            var (ownerFrame, owner) = await OpenOwned(SyncedList());
            try
            {
                Type? destination = null; navigation.Frame = ownerFrame; navigation.NavigationRequestHandler = null;
                Choose(Entries(await OpenMore(owner)).Single(item => item.Text == "Delete…"));
                await Until(() => Dialogs(owner.XamlRoot).Any());
                var dialog = Dialogs(owner.XamlRoot).Single();
                ownerFrame.Navigate(typeof(CollectionBrowsePage), new CollectionBrowsePage.NavArgs { CollectionId = "current", Title = "Current", IsUserCollection = false, LibraryId = 1 });
                await Task.Delay(150);
                navigation.NavigationRequestHandler = (type, _) => { destination = type; return true; };
                Invoke(Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton"));
                await Until(() => Descendants<TextBlock>(dialog).Any(block => block.Visibility == Visibility.Visible && block.Text.Contains("no longer open")));
                Check(wire.DeleteRequests.Count == 0 && destination == null, "A confirmation for a page the viewer left deleted the collection or navigated.");
                Invoke(Descendants<Button>(dialog).Single(button => button.Name == "CloseButton"));
                await Until(() => !Dialogs(owner.XamlRoot).Any());
            }
            finally { navigation.NavigationRequestHandler = null; foreach (var open in Dialogs(owner.XamlRoot).ToList()) open.Hide(); parent.Children.Remove(ownerFrame); }
            Log("PASS: a late sync result or a delete confirmed after the page was left changed nothing.");
        }

        internal async Task CollectionPickerPartialFailuresAsync()
        {
            foreach (var failure in new[] { "tab", "personal", "libraries", "both" })
            {
                wire.Reset(own: 1, shared: 0, libraries: 2);
                wire.FailedLibraryId = failure == "tab" ? 1 : null;
                wire.FailPersonal = failure is "personal" or "both";
                wire.FailLibraries = failure is "libraries" or "both";
                if (failure == "both") wire.PersonalGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                catalog.InvalidateLibraryCache();
                var host = new StackPanel();
                var config = new Dictionary<string, object>();
                var load = (Task)typeof(SiloPlayer.Views.Dialogs.RecipeGalleryDialog)
                    .GetMethod("AddCollectionFieldAsync", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [config, host])!;
                try
                {
                    if (failure == "both")
                    {
                        await Until(() => wire.PersonalReads == 1);
                        await Task.Delay(100);
                        Check(!load.IsCompleted, "A failed library discovery abandoned the pending personal read.");
                        wire.PersonalGate!.SetResult(true);
                    }
                    await load.WaitAsync(TimeSpan.FromSeconds(10));
                    var combo = host.Children.OfType<ComboBox>().Single();
                    var options = combo.Items.Cast<object>().ToArray();
                    if (!wire.FailPersonal)
                    {
                        combo.SelectedItem = options.Single(option => option.ToString() == "Weekend picks 1 — My Collections");
                        Check(config.GetValueOrDefault("user_collection_id") as string == "own-1" && !config.ContainsKey("library_collection_id"),
                            "A library failure prevented selecting a personal collection.");
                    }
                    if (!wire.FailLibraries)
                    {
                        combo.SelectedItem = options.Single(option => option.ToString() == "Anime — Shows");
                        Check(config.GetValueOrDefault("library_collection_id") as string == "srv-3" && !config.ContainsKey("user_collection_id"),
                            "Another source's failure prevented selecting a healthy library collection.");
                    }
                    if (failure == "tab")
                    {
                        Check(options.All(option => option.ToString() != "Criterion — Movies"), "The failed library returned picker options.");
                        Check(host.Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual([
                            "Movies collections could not be loaded. Close this panel and try again."]),
                            "A failed library must be named without claiming that every collection failed.");
                    }
                }
                finally { wire.PersonalGate?.TrySetResult(true); await load; }
            }
            wire.Reset(own: 0, shared: 0, libraries: 0);
            catalog.InvalidateLibraryCache();
            Log("PASS: collection picker retains selectable healthy sources after personal, library-tab or library-discovery failures and awaits every personal read.");
        }

        // ===== Helpers =====
        private async Task<(Frame Frame, CollectionsPage Page)> OpenWall(double width, double height, CollectionsNavigationArgs? args = null)
        {
            var frame = new Frame { Width = width, Height = height }; parent.Children.Add(frame); navigation.Frame = frame; navigation.NavigationRequestHandler = null;
            frame.Navigate(typeof(CollectionsPage), args);
            var page = (CollectionsPage)frame.Content;
            await Layout(frame, width, height);
            return (frame, page);
        }

        private async Task<(Frame Frame, CollectionBrowsePage Page)> OpenCollection(double width, double height, CollectionBrowsePage.NavArgs args)
        {
            var frame = new Frame { Width = width, Height = height }; parent.Children.Add(frame); navigation.Frame = frame; navigation.NavigationRequestHandler = null;
            frame.Navigate(typeof(CollectionBrowsePage), args);
            var page = (CollectionBrowsePage)frame.Content;
            await Layout(frame, width, height);
            return (frame, page);
        }

        private static Grid Board(CollectionsPage page) => (Grid)((StackPanel)page.FindName("CollectionsGrid")).Children.OfType<Grid>().Single();
        private static IEnumerable<Button> OpenButtons(DependencyObject root) => Descendants<Button>(root).Where(button => Auto.GetName(button).StartsWith("Open ", StringComparison.Ordinal));
        private static List<string> Names(CollectionsPage page) => OpenButtons(Board(page)).Select(button => Auto.GetName(button)["Open ".Length..]).ToList();
        private static Button[] Pills(CollectionsPage page) => Descendants<Button>((StackPanel)page.FindName("CollectionsFilterPills")).ToArray();
        private static Button Pill(CollectionsPage page, string name) => Pills(page).Single(button => Auto.GetName(button) == name);
        private static string? PillCount(CollectionsPage page, string name)
        {
            var texts = Descendants<TextBlock>(Pill(page, name)).ToArray();
            return texts.Length > 1 ? texts[1].Text : null;
        }
        private static IEnumerable<Button> ServerRows(CollectionsPage page) => Descendants<Button>((StackPanel)page.FindName("ServerCollectionsRows"));
        private static bool IsInside(DependencyObject node, FrameworkElement ancestor)
        {
            for (DependencyObject? current = node; current != null; current = VisualTreeHelper.GetParent(current)) if (ReferenceEquals(current, ancestor)) return true;
            return false;
        }
        private async Task Press(Button button) { Invoke(button); await Task.Delay(120); }
        private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
    }

    private static class Auto
    {
        internal static string GetName(DependencyObject element) => AutomationProperties.GetName(element) ?? "";
        internal static string GetItemStatus(DependencyObject element) => AutomationProperties.GetItemStatus(element) ?? "";
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Log(string message) => Program.Log(message);
    private static async Task Until(Func<bool> ready, Func<string>? state = null) { for (var count = 0; count < 240 && !ready(); count++) await Task.Delay(25); Check(ready(), "Collections presentation operation did not settle." + (state == null ? "" : " State: " + state())); }
    private static async Task Layout(FrameworkElement element, double width, double height) { element.Measure(new(width, height)); element.Arrange(new(0, 0, width, height)); element.UpdateLayout(); await Task.Delay(220); element.UpdateLayout(); }

    private static async Task Capture(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0, "Collections presentation control was not rendered.");
        var pixels = await bitmap.GetPixelsAsync(); var folder = await StorageFolder.GetFolderFromPathAsync(Program.ResultDirectory);
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
    }

    private static async Task<byte[]> PngAsync(int width, int height, byte red, byte green, byte blue)
    {
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4; var shade = 0.55 + 0.45 * x / width;
                pixels[offset] = (byte)(blue * shade); pixels[offset + 1] = (byte)(green * shade); pixels[offset + 2] = (byte)(red * shade); pixels[offset + 3] = 255;
            }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 96, 96, pixels); await encoder.FlushAsync();
        stream.Seek(0); using var copy = new MemoryStream(); await stream.AsStreamForRead().CopyToAsync(copy); return copy.ToArray();
    }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, Func<object>> factories) : IServiceProvider
    { public object? GetService(Type type) => factories.TryGetValue(type, out var factory) ? factory() : fallback.GetService(type); }

    private sealed class Wire(byte[] backdropPng, byte[] posterPng) : HttpMessageHandler
    {
        private const string Origin = "https://collections-presentation.invalid";
        private readonly Dictionary<int, int> _tabReads = [];
        private readonly Dictionary<string, (int LibraryId, string Description, bool Backdrop)> _described = [];
        internal bool FailPersonal, FailServer, FailLibraryTab, SingleProfile, TabGateIgnoresCancellation;
        internal bool Imports = true, FailLibraries, FailSync, FailDelete;
        internal string? FirstOwnType;
        internal string SyncStatus = "success", SyncMessage = "";
        internal TaskCompletionSource<bool>? PersonalGate, ServerGate, TabGate, BackdropGate, SyncGate, DeleteGate;
        internal readonly List<string> SyncRequests = [], DeleteRequests = [], CatalogRequests = [];
        internal int SnapshotReads;
        internal int? FailedLibraryId;
        internal object[] UserLibraries = [new { id = 1, name = "Movies", type = "movie", enabled = true }, new { id = 2, name = "Shows", type = "show", enabled = true }, new { id = 3, name = "Kids", type = "movie", enabled = true }];
        internal int PersonalReads, ServerReads;
        internal readonly List<string> BackdropRequests = [];
        internal readonly List<string> Paths = [];
        internal object? PersonalSnapshot;
        private readonly List<object> _groups = [];
        private int _own, _shared, _libraries;

        internal int TabReads(int libraryId) { lock (_tabReads) return _tabReads.GetValueOrDefault(libraryId); }
        internal void Reset(int own, int shared, int libraries, bool singleProfile = false)
        {
            _own = own; _shared = shared; _libraries = libraries; SingleProfile = singleProfile; FailedLibraryId = null;
            FailPersonal = FailServer = FailLibraryTab = false; PersonalGate = ServerGate = TabGate = BackdropGate = null; PersonalReads = ServerReads = 0;
            lock (_tabReads) _tabReads.Clear();
            BackdropRequests.Clear(); _described.Clear(); _groups.Clear(); PersonalSnapshot = null; lock (Paths) Paths.Clear();
            Imports = true; FailLibraries = FailSync = FailDelete = false; FirstOwnType = null; SyncStatus = "success"; SyncMessage = ""; SyncGate = DeleteGate = null; SnapshotReads = 0;
            lock (SyncRequests) { SyncRequests.Clear(); DeleteRequests.Clear(); CatalogRequests.Clear(); }
        }
        internal void Described(string id, int libraryId, string description, bool backdrop) => _described[id] = (libraryId, description, backdrop);

        private static object Collection(string id, string name, string creator, string type = "manual", bool shared = false, int items = 5)
            => new { id, creator_profile_id = creator, profile_id = creator, name, collection_type = type, is_shared = shared, item_count = items, poster_url = Origin + "/poster/" + id + ".png" };
        private static object Card(string id, string title, int items = 8) => new { id, title, item_count = items, poster_url = Origin + "/poster/" + id + ".png" };

        private object[] Personal()
        {
            var own = Enumerable.Range(1, _own).Select(n => Collection("own-" + n, "Weekend picks " + n, "me", n == 1 && FirstOwnType != null ? FirstOwnType : n % 2 == 0 ? "smart" : "manual", n == 1)).ToList();
            var list = own.Cast<object>().ToList();
            if (_shared > 0) list.Add(Collection("sh-1", "Family movie night", "other", shared: true));
            return list.ToArray();
        }

        private object[] ServerLibraries() => _libraries switch
        {
            0 => [],
            1 => [new { library_id = 1, library_name = "Movies", total_count = 3, collections = new[] { Card("srv-1", "Criterion"), Card("srv-2", "A24") } }],
            _ => [new { library_id = 1, library_name = "Movies", total_count = 3, collections = new[] { Card("srv-1", "Criterion"), Card("srv-2", "A24") } },
                  new { library_id = 2, library_name = "Shows", total_count = 1, collections = new[] { Card("srv-3", "Anime") } }],
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "collections-presentation.invalid") throw new InvalidOperationException("Collections presentation attempted external network.");
            var path = request.RequestUri.AbsolutePath; var status = HttpStatusCode.OK; object body = new { items = Array.Empty<object>() };
            lock (Paths) Paths.Add(request.Method + " " + path);
            if (path.StartsWith("/poster/")) return Image(posterPng);
            if (path.StartsWith("/backdrop/"))
            {
                BackdropRequests.Add(request.RequestUri.PathAndQuery);
                if (BackdropGate != null) await BackdropGate.Task.WaitAsync(ct);
                return Image(backdropPng);
            }
            if (path == "/api/v2/collections" && request.Method == HttpMethod.Get)
            {
                PersonalReads++;
                if (PersonalGate != null) await PersonalGate.Task.WaitAsync(ct);
                if (FailPersonal) return Reply(new { message = "personal unavailable" }, HttpStatusCode.ServiceUnavailable);
                body = new { items = Personal(), groups = _groups.ToArray() };
            }
            else if (path == "/api/v2/collections/groups" && request.Method == HttpMethod.Post)
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                body = new { id = "group-" + (_groups.Count + 1), name = payload.RootElement.GetProperty("name").GetString(),
                    slug = payload.RootElement.GetProperty("slug").GetString(), sort_order = _groups.Count };
                _groups.Add(body);
            }
            else if (path == "/api/v2/collections/server")
            {
                ServerReads++;
                if (ServerGate != null) await ServerGate.Task.WaitAsync(ct);
                if (FailServer) return Reply(new { message = "server unavailable" }, HttpStatusCode.ServiceUnavailable);
                body = new { libraries = ServerLibraries() };
            }
            else if (path == "/api/v2/collections/capabilities") body = new { item_reorder = true, imports = Imports };
            else if (path == "/api/v2/user/libraries")
            {
                if (FailLibraries) return Reply(new { message = "libraries unavailable" }, HttpStatusCode.ServiceUnavailable);
                body = new { items = UserLibraries };
            }
            else if (request.Method == HttpMethod.Post && path.StartsWith("/api/v2/collections/") && path.EndsWith("/sync"))
            {
                lock (SyncRequests) SyncRequests.Add(path);
                if (SyncGate != null) await SyncGate.Task;
                if (FailSync) return Reply(new { message = "sync unavailable" }, HttpStatusCode.ServiceUnavailable);
                body = new { status = SyncStatus, message = SyncMessage, items_matched = 9, items_unmatched = 0 };
            }
            else if (request.Method == HttpMethod.Delete && path.StartsWith("/api/v2/collections/"))
            {
                lock (SyncRequests) DeleteRequests.Add(path + " if-match=" + string.Join(",", request.Headers.IfMatch.Select(tag => tag.Tag)));
                if (DeleteGate != null) await DeleteGate.Task;
                if (FailDelete) return Reply(new { message = "delete refused" }, HttpStatusCode.ServiceUnavailable);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            else if (path == "/api/v2/profiles") body = new { items = SingleProfile ? new[] { new { id = "me", name = "Fixture" } } : new[] { new { id = "me", name = "Fixture" }, new { id = "other", name = "Other profile" } } };
            else if (path.StartsWith("/api/v2/library/") && path.EndsWith("/collections"))
            {
                var libraryId = int.Parse(path["/api/v2/library/".Length..^"/collections".Length]);
                lock (_tabReads) _tabReads[libraryId] = _tabReads.GetValueOrDefault(libraryId) + 1;
                if (TabGate != null) { if (TabGateIgnoresCancellation) await TabGate.Task; else await TabGate.Task.WaitAsync(ct); }
                if (FailLibraryTab || FailedLibraryId == libraryId) return Reply(new { message = "tab unavailable" }, HttpStatusCode.ServiceUnavailable);
                var cards = new List<object>();
                if (libraryId == 1) cards.AddRange([TabCard("srv-1", "Criterion"), TabCard("srv-2", "A24"), TabCard("srv-4", "Studio Ghibli")]);
                if (libraryId == 2) cards.Add(TabCard("srv-3", "Anime"));
                foreach (var (id, described) in _described.Where(entry => entry.Value.LibraryId == libraryId))
                    cards.Add(new
                    {
                        id, title = "Described " + id, item_count = 6, description = described.Description,
                        backdrop_url = described.Backdrop ? Origin + "/backdrop/" + id + ".png?sig=wide" : "", backdrop_thumbhash = described.Backdrop ? SampleThumbhash : "",
                        poster_url = Origin + "/poster/" + id + ".png",
                    });
                body = new { library_id = libraryId, collections = cards, groups = Array.Empty<object>() };
            }
            else if (path.StartsWith("/api/v2/collections/") && request.Method == HttpMethod.Get && !path.Contains("/preferences"))
            {
                Interlocked.Increment(ref SnapshotReads);
                var snapshot = Reply(PersonalSnapshot ?? new { id = path.Split('/').Last(), creator_profile_id = "me", name = "Snapshot" });
                snapshot.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"rev-1\"");
                return snapshot;
            }
            else if (path == "/api/v2/catalog")
            {
                lock (SyncRequests) CatalogRequests.Add(request.RequestUri.Query);
                body = new { items = Enumerable.Range(0, 6).Select(n => new { content_id = "item-" + n, title = "Collection title " + n, type = "movie", year = 2020 + n }).ToArray(), total = 6, total_exact = true, page = new { has_more = false } };
            }
            else if (path == "/api/v2/catalog/filters") body = new { genres = Array.Empty<string>() };
            else if (request.Method == HttpMethod.Put && path.StartsWith("/api/v2/settings/values/"))
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                body = new { key = path.Split('/').Last(), value = payload.RootElement.GetProperty("value").Clone(), source = "profile_client" };
            }
            return Reply(body, status);
        }

        private static object TabCard(string id, string title) => new { id, title, item_count = 8, poster_url = Origin + "/poster/" + id + ".png" };
        private static HttpResponseMessage Image(byte[] bytes) { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; response.Content.Headers.ContentType = new("image/png"); return response; }
        private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
    }
}
