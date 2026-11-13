using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

/// <summary>
/// Task5 Reader/Watch Party language coverage on actual published controls:
/// Reader shell and its authored WebView renderer bridge, the Manga files
/// dialog, and the Watch Party join, room, picker and participant/playback
/// surfaces. Each area switches English → Japanese → Arabic live and checks
/// explicit catalog keys, raw data, direction and retained refresh. Failures
/// are collected per area so a red run shows every uncovered area.
/// </summary>
internal static class ReaderPartyLanguageNativeFixture
{
    private static readonly List<string> Failures = [];

    internal static async Task RunAsync(StackPanel parent)
    {
        var locales = LocaleService.Shared;
        var before = locales.Current;
        Failures.Clear();
        var host = new Grid { Width = 1280, Height = 760 };
        parent.Children.Add(host);
        using var shell = AppLocalization.AttachShell(host, host);
        try
        {
            await Area("reader-shell", () => ReaderShellAsync(host));
            await Area("reader-renderer", () => ReaderRendererAsync(host));
            await Area("manga-files", () => MangaFilesAsync(host));
            await Area("party-join", () => PartyJoinAsync(host));
            await Area("party-room", () => PartyRoomAsync(host));
        }
        finally
        {
            parent.Children.Remove(host);
            locales.Apply(locales.Create(before.Resolution));
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Reader/Watch Party language coverage failed: " + string.Join(" | ", Failures));
        Program.Log("PASS: READER_PARTY_LANGUAGE_COMPLETED reader shell/renderer bridge, manga files, Watch Party join/room/picker/participants follow ja/ar live with raw data and book direction preserved.");
    }

    private static async Task Area(string name, Func<Task> run)
    {
        Apply("en"); await Task.Delay(60);
        try { await run(); Program.Log("PASS: reader-party-language/" + name); }
        catch (Exception ex)
        {
            var message = name + ": " + (ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message);
            Failures.Add(message);
            Program.Log("FAIL: reader-party-language/" + message);
        }
        finally { Apply("en"); await Task.Delay(60); }
    }

    // ───────────────────────── Reader shell ─────────────────────────
    private static async Task ReaderShellAsync(Grid host)
    {
        using var handler = new Wire(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://reader-language.invalid"); client.SetProfile("reader-language");
        var api = new EbooksApi(client);
        using var services = new ServiceCollection().AddSingleton(api).AddSingleton(new CatalogApi(client)).AddSingleton(new ToastService()).BuildServiceProvider();
        using var swap = SwapServices(services);
        var page = new EbookReaderPage { Width = 1100, Height = 740 };
        host.Children.Add(page);
        try
        {
            await Settle(page);
            ((TextBlock)page.FindName("TitleText")).Text = "The Fixture Book";
            Set(page, "_contentId", "fixture-book");
            Set(page, "_readerContext", api.CaptureContext());
            await (Task)Call(page, "LoadAnnotationsAsync")!;
            Call(page, "OpenPanel", "annotations");
            Apply("ja"); await Settle(page);
            var contents = (Button)page.FindName("ContentsTab");
            Require(Translated("web.reader.tabs.contentsAria") is var tabName && AutomationProperties.GetName(contents) == tabName && Equals(ToolTipService.GetToolTip(contents), tabName),
                "Contents tab name/tooltip did not use web.reader.tabs.contentsAria: " + AutomationProperties.GetName(contents));
            Require(Texts(contents).Contains(Translated("web.reader.tabs.contents")), "Contents tab label not translated");
            Require(AutomationProperties.GetName((Button)page.FindName("PanelToggleButton")) == Translated("web.reader.toolbar.closePanel"), "panel toggle name not translated");
            Require(Equals(((CheckBox)page.FindName("WakeLockCheck")).Content, Translated("web.reader.settings.keepAwake")), "keep-awake checkbox not translated");
            Require(((ComboBox)page.FindName("ThemeCombo")).Items.OfType<ComboBoxItem>().Select(item => item.Content).Contains(Translated("web.reader.theme.sepia")), "theme choices not translated");
            Require(Texts((FrameworkElement)page.FindName("SettingsPanel")).Contains(Translated("web.reader.settings.typography")), "settings heading not translated");
            Require(((TextBlock)page.FindName("LoadingText")).Text == Translated("web.reader.loading"), "reader loading text not translated: " + ((TextBlock)page.FindName("LoadingText")).Text);
            Require(((TextBlock)page.FindName("ProgressText")).Text == 0d.ToString("P0", AppStrings.FormatCulture), "progress percentage not formatted for the display culture");
            var annotations = (StackPanel)page.FindName("AnnotationsList");
            page.UpdateLayout();
            Require(Texts(annotations).Contains(AppStrings.Get("web.reader.notes.kind", new Dictionary<string, object?> { ["kind"] = "highlight" })), "annotation kind not localized: " + string.Join(",", Texts(annotations)));
            Require(Texts(annotations).Contains("Raw highlighted passage") && ((TextBlock)page.FindName("TitleText")).Text == "The Fixture Book", "raw book/annotation text changed: " + string.Join(",", Texts(annotations)) + " title=" + ((TextBlock)page.FindName("TitleText")).Text);
            await MediaParityNativeFixture.CaptureAsync(page, "reader-party-language-reader-ja.png");

            Apply("ar"); await Settle(page);
            Require(page.FlowDirection == FlowDirection.RightToLeft, "reader chrome did not follow the Arabic shell direction");
            Require(((FrameworkElement)page.FindName("ReadingSurface")).FlowDirection == FlowDirection.LeftToRight, "Arabic interface mirrored the reading surface; book direction must come from the book");
            Require(((FontIcon)page.FindName("BackIcon")).MirroredWhenRightToLeft && !Descendants<FontIcon>((Button)page.FindName("NextButton")).Any(icon => icon.MirroredWhenRightToLeft),
                "Back navigation must mirror in Arabic while book-relative page chevrons stay unmirrored");
            Require(AutomationProperties.GetName(contents) == Translated("web.reader.tabs.contentsAria"), "retained contents tab did not refresh to Arabic");
            Require(Texts(annotations).Contains(AppStrings.Get("web.reader.notes.kind", new Dictionary<string, object?> { ["kind"] = "highlight" })), "retained annotation row did not refresh to Arabic");
            Require(Texts(annotations).Contains("Raw highlighted passage"), "Arabic switch changed raw annotation text");
            Call(page, "SetPanelOpen", false);
            Require(AutomationProperties.GetName((Button)page.FindName("PanelToggleButton")) == Translated("web.reader.toolbar.openPanel"), "dynamic panel toggle label not keyed");
            await MediaParityNativeFixture.CaptureAsync(page, "reader-party-language-reader-ar.png");
        }
        finally { host.Children.Remove(page); await Task.Delay(60); }
    }

    // ─────────────────────── Reader renderer bridge ───────────────────────
    private static async Task ReaderRendererAsync(Grid host)
    {
        var fixtures = Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")
            ?? throw new InvalidOperationException("SILO_NATIVE_TEST_READER_FIXTURES is not set.");
        using var handler = new Wire(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://reader-language.invalid"); client.SetProfile("reader-language");
        var api = new EbooksApi(client);
        using var services = new ServiceCollection().AddSingleton(api).AddSingleton(new CatalogApi(client)).AddSingleton(new ToastService()).BuildServiceProvider();
        using var swap = SwapServices(services);
        Apply("ar"); await Task.Delay(60);
        var page = new EbookReaderPage { Width = 1100, Height = 740 };
        host.Children.Add(page);
        try
        {
            page.Measure(new Windows.Foundation.Size(1100, 740)); page.Arrange(new Windows.Foundation.Rect(0, 0, 1100, 740));
            var web = (WebView2)page.FindName("ReaderWebView");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(Program.ResultDirectory, "reader-language-webview"), null).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            await web.EnsureCoreWebView2Async(environment).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            Call(page, "ConfigureReaderWebView");
            var fixtureError = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            web.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                using var message = JsonDocument.Parse(args.TryGetWebMessageAsString());
                if (message.RootElement.GetProperty("type").GetString() == "fixture-error")
                    fixtureError.TrySetResult(message.RootElement.GetProperty("message").GetString() ?? "");
            };
            var bytes = File.ReadAllBytes(Path.Combine(fixtures, "unequal.epub"));
            var directory = Path.Combine(Program.ResultDirectory, "reader-language-epub");
            var extractor = typeof(EbookReaderPage).Assembly.GetType("SiloPlayer.Services.EbookPackageExtractor")!;
            var book = extractor.GetMethod("Extract", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [bytes, directory, "epub"]);
            File.WriteAllBytes(Path.Combine(directory, "reader-source.epub"), bytes);
            Set(page, "_contentId", "fixture-book"); Set(page, "_fileId", 42);
            Set(page, "_readerContext", api.CaptureContext()); Set(page, "_initialized", true);
            Set(page, "_book", book); Set(page, "_openGeneration", 1);
            web.CoreWebView2.SetVirtualHostNameToFolderMapping(EbookReaderWebPolicy.ReaderHost, Path.Combine(Program.AppDirectory, "Assets", "Reader"), CoreWebView2HostResourceAccessKind.DenyCors);
            web.CoreWebView2.SetVirtualHostNameToFolderMapping(EbookReaderWebPolicy.BookHost, directory, CoreWebView2HostResourceAccessKind.Allow);
            web.Source = new Uri($"https://{EbookReaderWebPolicy.ReaderHost}/index.html?generation=1");
            await Until(() => (bool)Get(page, "_rendererReady")!, "actual renderer did not become ready");
            await Task.Delay(300);
            using (var state = JsonDocument.Parse(JsonSerializer.Deserialize<string>(await web.ExecuteScriptAsync(
                "JSON.stringify({lang:document.documentElement.lang,shell:getComputedStyle(document.documentElement).direction,book:(()=>{const c=document.querySelector('foliate-view')?.renderer?.getContents?.()?.[0];return c?getComputedStyle(c.doc.documentElement).direction:null})()})"))!))
            {
                var root = state.RootElement;
                Program.Log("TRACE reader renderer (ar): " + root.GetRawText());
                Require(root.GetProperty("lang").GetString() == "ar", "renderer shell did not receive the Arabic locale payload");
                Require(root.GetProperty("shell").GetString() == "ltr", "Arabic locale mirrored the renderer shell");
                Require(root.GetProperty("book").GetString() is null or "ltr", "Arabic locale changed the book's own direction");
            }
            var location = (string?)Get(page, "_sharedLocation"); var progress = (double)Get(page, "_sharedProgress")!;
            Apply("ja"); await Settle(page); await Task.Delay(300);
            var lang = JsonSerializer.Deserialize<string>(await web.ExecuteScriptAsync("document.documentElement.lang"));
            Require(lang == "ja", "renderer did not receive the live Japanese payload: " + lang);
            Require((string?)Get(page, "_sharedLocation") == location && Math.Abs((double)Get(page, "_sharedProgress")! - progress) < 1e-9, "language switch changed reading location/progress");
            await web.ExecuteScriptAsync("window.siloReader.open({url:'https://" + EbookReaderWebPolicy.BookHost + "/missing.epub',format:'epub'}).catch(e=>chrome.webview.postMessage(JSON.stringify({type:'fixture-error',message:e.message})))");
            var error = await fixtureError.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Require(error == Translated("web.reader.error.openFailed"), "authored renderer error did not use the keyed locale payload: " + error);
            Program.Log("TRACE reader renderer (ja) error: " + error);
        }
        finally { host.Children.Remove(page); ((WebView2)page.FindName("ReaderWebView")).Close(); await Task.Delay(60); }
    }

    // ─────────────────────────── Manga files ───────────────────────────
    private static async Task MangaFilesAsync(Grid host)
    {
        using var handler = new Wire(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://reader-language.invalid"); client.SetProfile("reader-language");
        using var services = new ServiceCollection().AddSingleton(new CatalogApi(client)).BuildServiceProvider();
        using var swap = SwapServices(services);
        Apply("ja"); await Task.Delay(60);
        var dialog = new MangaFilesDialog("fixture-series", "Raw Manga Title") { XamlRoot = host.XamlRoot };
        var showing = dialog.ShowAsync();
        try
        {
            await Until(() => !Field<ProgressRing>(dialog, "_progress").IsActive, "manga files did not load");
            var heading = Texts((FrameworkElement)dialog.Title).FirstOrDefault() ?? "";
            Require(heading == AppStrings.Get("web.item.mangaFiles.titleWithName", new Dictionary<string, object?> { ["title"] = "Raw Manga Title" }) && heading.Contains("Raw Manga Title")
                && heading != AppStrings.English("web.item.mangaFiles.titleWithName", new Dictionary<string, object?> { ["title"] = "Raw Manga Title" }), "manga heading not keyed: " + heading);
            var body = Field<StackPanel>(dialog, "_body");
            Require(Texts(body).Contains(Translated("web.common.format.manga.volume", new() { ["number"] = "13" })), "volume label not keyed: " + string.Join(",", Texts(body)));
            Require(Texts(body).Contains("raw-file-13.cbz"), "raw file name changed");
            Apply("ar"); await Settle(dialog);
            Require(dialog.FlowDirection == FlowDirection.RightToLeft, "open manga dialog did not follow Arabic direction");
            var summary = Translated("web.item.mangaFiles.summary", new() { ["count"] = 2, ["file_count"] = 2.ToString("N0", AppStrings.FormatCulture), ["size"] = (2560 / 1024d).ToString("0.0", AppStrings.FormatCulture) + " KB" });
            Require(Field<TextBlock>(dialog, "_description").Text == summary, "open dialog summary did not refresh to Arabic: " + Field<TextBlock>(dialog, "_description").Text + " expected " + summary);
            Require(Texts(Field<StackPanel>(dialog, "_body")).Contains(Translated("web.item.mangaFiles.chapterNumber", new() { ["number"] = 2.125.ToString(AppStrings.FormatCulture) })), "retained chapter label did not refresh to Arabic: " + string.Join(",", Texts(Field<StackPanel>(dialog, "_body"))));
            await MediaParityNativeFixture.CaptureAsync((FrameworkElement)dialog.Content, "reader-party-language-manga-ar.png");
        }
        finally { dialog.Hide(); await showing; }
    }

    // ─────────────────────────── Party join ───────────────────────────
    private static async Task PartyJoinAsync(Grid host)
    {
        using var http = new HttpClient(new RejectNetwork()); var client = new SiloApiClient(http); client.SetBaseUrl("https://party-language.invalid");
        var vm = new WatchTogetherJoinViewModel(new PlaybackApi(client));
        using var swap = SwapServices(new JoinServices(Original(), vm));
        var page = new WatchTogetherJoinPage { Width = 900, Height = 760 };
        host.Children.Add(page);
        try
        {
            await Settle(page);
            Apply("ja"); await Settle(page);
            var buttons = Descendants<Button>(page).ToList();
            Require(buttons.Any(button => Equals(button.Content, Translated("web.watchTogether.hub.create"))), "Create button not translated");
            Require(buttons.Any(button => Equals(button.Content, Translated("web.watchTogether.hub.join"))), "Join button not translated");
            var code = (TextBox)page.FindName("RoomCodeBox");
            Require(AutomationProperties.GetName(code) == Translated("web.watchTogether.hub.codeLabel"), "room code accessible name not translated");
            Require(((TextBlock)page.FindName("HeadlineText")).Text == Translated("web.watchTogether.hub.headline"), "headline not translated");
            Require(Texts(page).Contains(Translated("watchParty.page.start")) && Texts(page).Contains(Translated("web.watchTogether.hub.stepSyncTitle")), "join cards/steps not translated");
            vm.RoomCode = "ABCD1234";
            await vm.JoinByCodeCommand.ExecuteAsync(null);
            Require(vm.ErrorMessage == Translated("web.watchTogether.hub.joinFailed"), "join failure not keyed: " + vm.ErrorMessage);
            await MediaParityNativeFixture.CaptureAsync(page, "reader-party-language-join-ja.png");
            Apply("ar"); await Settle(page);
            Require(vm.ErrorMessage == Translated("web.watchTogether.hub.joinFailed"), "retained join failure did not refresh to Arabic: " + vm.ErrorMessage);
            Require(Descendants<Button>(page).Any(button => Equals(button.Content, Translated("web.watchTogether.hub.create"))), "retained Create button did not refresh to Arabic");
            Require(vm.RoomCode == "ABCD1234", "language switch changed the entered room code");
        }
        finally { host.Children.Remove(page); await Task.Delay(100); }
    }

    // ─────────────────────────── Party room ───────────────────────────
    private static async Task PartyRoomAsync(Grid host)
    {
        using var handler = new PartyWire(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://party-language.invalid"); client.SetProfile("fixture-host");
        using var swap = SwapServices(new RoomServices(Original(), client));
        WatchTogetherRoomPage? room = null;
        try
        {
            room = new WatchTogetherRoomPage { Width = 1280, Height = 760 };
            var vm = room.ViewModel;
            vm.RoomId = "fixture-room"; vm.RoomToken = "fixture-proof";
            vm.GetType().GetProperty("Capabilities")!.SetValue(vm, new WatchTogetherCapabilities { Allowed = true, State = "available", Picker = true, MemberState = true, LobbyReady = true, StagedSelection = true, SelectionModeSwitch = true });
            var members = new List<WatchTogetherRoomMember>
            {
                new() { UserId = 1, ProfileId = "fixture-host", DisplayName = "Ada Host", IsHost = true, IsSelf = true, Connected = true },
                new() { UserId = 2, ProfileId = "guest", DisplayName = "Zoë Guest", Connected = true, LobbyReady = true },
            };
            vm.Room = new WatchTogetherRoomSnapshot { RoomId = "fixture-room", Code = "RAWCODE1", Phase = "lobby", SelectionMode = "host_pick", SelfCanManageRoom = true, MemberCount = 2, SelectedContentId = "fixture-movie", Members = members, InvitePath = "/watch/invite" };
            vm.ConnectionState = "connected";
            Invoke(room, "UpdateRoomUi"); Invoke(room, "UpdateConnectionUi");
            host.Children.Add(room); await Settle(room);
            await (Task)Invoke(room, "RunHostSearchAsync")!;
            Apply("ja"); await Settle(room);
            var countArgs = new Dictionary<string, object?> { ["count"] = 2, ["count_text"] = 2.ToString("N0", AppStrings.FormatCulture) };
            Require(((TextBlock)room.FindName("PhaseText")).Text == Translated("web.watchTogether.strip.live", countArgs), "room strip not keyed: " + ((TextBlock)room.FindName("PhaseText")).Text);
            Require(((TextBlock)room.FindName("StatusText")).Text == Translated("web.watchTogether.connection.connected"), "connection status not translated");
            Require(((TextBlock)room.FindName("CodeText")).Text == "RAWCODE1", "room code changed");
            var rail = (FrameworkElement)room.FindName("MembersWrapPanel");
            Require(Texts(rail).Contains("Ada Host") && Texts(rail).Contains("Zoë Guest"), "raw member names changed");
            Require(Texts(rail).Contains(Translated("web.watchTogether.rail.you")) && Texts(rail).Contains(Translated("web.watchTogether.rail.stateReady")), "participant labels not translated: " + string.Join(",", Texts(rail)));
            Require(((TextBlock)room.FindName("ReadyCheckHeading")).Text == Translated("web.watchTogether.readyCheck.title"), "ready check heading not translated");
            Require(Descendants<Button>((FrameworkElement)room.FindName("ShelfChipsPanel")).Any(chip => Equals(chip.Content, Translated("web.watchTogether.browse.chipAll"))), "browse chips not translated");
            var shelves = ((StackPanel)room.FindName("BrowseShelfRows")).Children.OfType<StackPanel>().Select(shelf => ((TextBlock)shelf.Children[0]).Text).ToList();
            Require(shelves.Contains(Translated("web.watchTogether.browse.togetherTitle")) && shelves.Contains("Raw Discovery Row"), "picker shelf titles not keyed or raw row changed: " + string.Join(",", shelves));
            Invoke(room, "BuildPickerSeasons", new List<Season> { new() { SeasonNumber = 0 }, new() { SeasonNumber = 1, UserData = new SeasonUserData { WatchedCount = 0, UnplayedCount = 3 } } });
            var seasons = ((StackPanel)room.FindName("PickerSeasons")).Children.OfType<Button>().Select(AutomationProperties.GetName).ToList();
            Require(seasons.Contains(Translated("web.watchTogether.common.specials")) && seasons.Contains(Translated("web.watchTogether.common.season", new() { ["number"] = "1" })), "picker season labels not keyed: " + string.Join(",", seasons));
            var activity = Texts((FrameworkElement)room.FindName("ActivityList"));
            Require(activity.Any(text => text.Contains(AppStrings.Get("web.watchTogether.activity.joined", new Dictionary<string, object?> { ["who"] = "Zoë Guest" }))), "activity not keyed: " + string.Join(",", activity));
            await MediaParityNativeFixture.CaptureAsync(room, "reader-party-language-room-ja.png");

            Apply("ar"); await Settle(room);
            Require(((TextBlock)room.FindName("PhaseText")).Text == AppStrings.Get("web.watchTogether.strip.live", new Dictionary<string, object?> { ["count"] = 2, ["count_text"] = 2.ToString("N0", AppStrings.FormatCulture) }), "retained room strip did not refresh to Arabic");
            Require(((StackPanel)room.FindName("BrowseShelfRows")).Children.OfType<StackPanel>().Select(shelf => ((TextBlock)shelf.Children[0]).Text).Contains(Translated("web.watchTogether.browse.togetherTitle")), "retained shelf title did not refresh to Arabic: " + string.Join(",", ((StackPanel)room.FindName("BrowseShelfRows")).Children.OfType<StackPanel>().Select(shelf => ((TextBlock)shelf.Children[0]).Text)));
            Require(((StackPanel)room.FindName("PickerSeasons")).Children.OfType<Button>().Select(AutomationProperties.GetName).Contains(Translated("web.watchTogether.common.specials")), "retained picker seasons did not refresh to Arabic");
            Require(Texts((FrameworkElement)room.FindName("ActivityList")).Any(text => text.Contains(AppStrings.Get("web.watchTogether.activity.joined", new Dictionary<string, object?> { ["who"] = "Zoë Guest" }))), "retained activity did not refresh to Arabic");
            Require(room.FlowDirection == FlowDirection.RightToLeft, "room did not follow Arabic direction");

            // Mounted playback participant/state metadata.
            vm.Room = new WatchTogetherRoomSnapshot { RoomId = "fixture-room", Code = "RAWCODE1", Phase = "playing", PlaybackState = "playing", SelectionMode = "host_pick", SelfCanManageRoom = true, MemberCount = 2, SelectedContentId = "fixture-movie", Members = members };
            Invoke(room, "UpdateRoomUi"); await Settle(room);
            Require(Texts((FrameworkElement)room.FindName("MembersWrapPanel")).Contains(Translated("web.watchTogether.rail.stateWatching")), "playing participant state not translated");
            Require(Texts((FrameworkElement)room.FindName("NowPlayingPanel")).Contains(AppStrings.Uppercase(Translated("web.watchTogether.playing.eyebrow"))), "now playing eyebrow not translated");
            Require(((TextBlock)room.FindName("PhaseText")).Text == Translated("web.watchTogether.strip.playing", new() { ["count"] = 2, ["count_text"] = 2.ToString("N0", AppStrings.FormatCulture) }), "playing strip not keyed");
            await Until(() => ((TextBlock)room.FindName("NowPlayingTitle")).Text == "Raw Movie Title", "raw now-playing title missing");
            Apply("ja"); await Settle(room);
            Require(((TextBlock)room.FindName("NowPlayingTitle")).Text == "Raw Movie Title", "raw now-playing title changed on language switch");
            Require(((TextBlock)room.FindName("NowPlayingSubtitle")).Text == Translated("web.watchTogether.common.movie"), "now-playing type not keyed");
            await MediaParityNativeFixture.CaptureAsync(room, "reader-party-language-room-playing-ja.png");

            // Actual End dialog follows a live switch while open.
            vm.Room = new WatchTogetherRoomSnapshot { RoomId = "fixture-room", Code = "RAWCODE1", Phase = "lobby", SelectionMode = "host_pick", SelfCanManageRoom = true, MemberCount = 2, Members = members };
            Invoke(room, "UpdateRoomUi"); await Settle(room);
            Click((Button)room.FindName("EndButton"));
            ContentDialog? dialog = null;
            await Until(() => (dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(room.XamlRoot).Select(popup => popup.Child).OfType<ContentDialog>().FirstOrDefault()
                ?? VisualTreeHelper.GetOpenPopupsForXamlRoot(room.XamlRoot).SelectMany(popup => Descendants<ContentDialog>(popup.Child)).FirstOrDefault()) != null, "End dialog did not open");
            try
            {
                Require(Equals(dialog!.Title, Translated("web.watchTogether.endDialog.title")) && dialog.PrimaryButtonText == Translated("watchParty.end.confirm"), "End dialog not keyed");
                Apply("ar"); await Settle(room);
                Require(Equals(dialog.Title, Translated("web.watchTogether.endDialog.title")) && dialog.FlowDirection == FlowDirection.RightToLeft, "open End dialog did not refresh to Arabic");
            }
            finally { dialog?.Hide(); await Task.Delay(150); }
        }
        finally
        {
            if (room != null) { room.ViewModel.Dispose(); host.Children.Remove(room); await Task.Delay(150); }
        }
    }

    // ─────────────────────────── helpers ───────────────────────────
    private static string Translated(string key, Dictionary<string, object?>? args = null)
    {
        var value = AppStrings.Get(key, args);
        if (!AppStrings.Current.IsEnglish && value == AppStrings.English(key, args))
            throw new InvalidOperationException($"Catalog key {key} has no {AppStrings.Language} translation; choose a translated assertion.");
        return value;
    }
    private static void Apply(string language)
    {
        // Collect first: retained refresh must not depend on managed wrappers of
        // code-created WinUI elements surviving garbage collection.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        LocaleService.Shared.Apply(LocaleService.Shared.Create(AppLocaleResolution.Resolve(language, ["en-US"], "en-US")));
    }
    private static async Task Settle(FrameworkElement element) { await Task.Delay(120); element.UpdateLayout(); await Task.Delay(60); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready, string message) { for (var i = 0; i < 300 && !ready(); i++) await Task.Delay(50); if (!ready()) throw new TimeoutException(message); }
    private static List<string> Texts(FrameworkElement root) => Descendants<TextBlock>(root).Select(text => text.Text).ToList();
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T self) yield return self;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static T Field<T>(object target, string name) => (T)Get(target, name)!;
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static object? Invoke(object target, string name, params object?[] args) => Call(target, name, args);
    private static readonly FieldInfo ServicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static IServiceProvider Original() => (IServiceProvider)ServicesField.GetValue(null)!;
    private static IDisposable SwapServices(IServiceProvider services)
    {
        var original = ServicesField.GetValue(null);
        ServicesField.SetValue(null, services);
        return new Restore(() => ServicesField.SetValue(null, original));
    }
    private sealed class Restore(Action action) : IDisposable { public void Dispose() => action(); }
    private sealed class JoinServices(IServiceProvider original, WatchTogetherJoinViewModel vm) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(WatchTogetherJoinViewModel) ? vm : original.GetService(type); }
    private sealed class RoomServices(IServiceProvider original, SiloApiClient client) : IServiceProvider
    {
        private readonly PlaybackApi _api = new(client);
        public object? GetService(Type type) => type == typeof(PlaybackApi) ? _api : type == typeof(CatalogApi) ? new CatalogApi(client) : type == typeof(HomeApi) ? new HomeApi(client)
            : type == typeof(SiloApiClient) ? client : type == typeof(WatchTogetherRoomViewModel) ? new WatchTogetherRoomViewModel(_api, client) : original.GetService(type);
    }
    private sealed class RejectNetwork : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("Language fixture rejected network."); }
    private static HttpResponseMessage Reply(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json") };

    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/annotations")) return Task.FromResult(Reply(new { items = new[] { new { id = "a1", kind = "highlight", location = "chapter:0;fraction:0", selected_text = "Raw highlighted passage" } }, page = new { has_more = false } }));
            if (path.EndsWith("/manga-files")) return Task.FromResult(Reply(new
            {
                folder_paths = new[] { "D:/Raw/Manga" },
                items = new object[]
                {
                    new { content_id = "c13", title = "Raw volume title", volume = "v13", file_name = "raw-file-13.cbz", file_size = 1024 },
                    new { content_id = "c2", title = "Raw chapter title", chapter_index = 2.125, file_name = "raw-file-2.cbz", file_size = 1536 },
                }
            }));
            if (path.EndsWith("/progress")) return Task.FromResult(Reply(new { progress = new { file_id = "42", location = "", progress = 0 } }));
            if (path.EndsWith("/reader-config")) return Task.FromResult(Reply(new { config = new { } }));
            throw new InvalidOperationException("Unexpected reader language route " + path);
        }
    }

    private sealed class PartyWire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/picker")) return Task.FromResult(Reply(new WatchTogetherPickerResponse { ContinueTogether = [new() { Item = new MediaItem { ContentId = "together-series", Type = "series", Title = "Raw Together Series" } }] }));
            if (path.EndsWith("/member-state")) return Task.FromResult(Reply(new WatchTogetherMemberStateResponse()));
            if (path.EndsWith("/home/layout")) return Task.FromResult(Reply(new HomeLayoutResponse { Sections = [new HomeSection { Id = "discovery", SectionType = "popular", Title = "Raw Discovery Row" }] }));
            if (path.Contains("/home/sections/")) return Task.FromResult(Reply(new { title = "Raw Discovery Row", items = new[] { new MediaItem { ContentId = "discovery-movie", Type = "movie", Title = "Raw Discovery Movie" } } }));
            if (path.StartsWith("/api/v2/catalog/items/")) return Task.FromResult(Reply(new { content_id = "fixture-movie", type = "movie", title = "Raw Movie Title" }));
            if (path == "/api/v2/catalog") return Task.FromResult(Reply(new { items = Array.Empty<MediaItem>() }));
            throw new InvalidOperationException("Unexpected party language route " + path);
        }
    }
}
