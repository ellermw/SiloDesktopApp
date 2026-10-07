using System.Reflection;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Services;
using SiloPlayer.Views;
using SiloPlayer.ViewModels;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class MediaParityNativeFixture
{
    internal static IServiceCollection AddMediaParityFixture(this IServiceCollection services)
        => services; // Compatibility for the shared runner; dependencies are scoped below.
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)field.GetValue(null)!;
        using var media = new MediaServices(original);
        field.SetValue(null, media);
        try { await RunCoreAsync(parent); }
        finally { field.SetValue(null, original); }
    }
    private static async Task RunCoreAsync(StackPanel parent)
    {
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "first-navigation" or "series-loading") { await DetailFirstNavigationNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "latest-media" or "ratings-layout") { await MediaLatestNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "hub-visual") { await HubCurrentCopyAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "reader-real" or "reader-panel" or "reader-toc" or "reader-controls") { await ReaderInteropFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "recent-unknown") { await MediaInteractionsNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "manga")
        {
            await MangaStickyNativeFixture.RunAsync(parent);
            return;
        }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "actions")
        {
            await WatchPartyActionsNativeFixture.RunAsync(parent);
            await AudiobookSettingsActionsNativeFixture.RunAsync(parent);
            await RequestDetailActionsNativeFixture.RunAsync(parent);
            await PartyPasteNativeFixture.RunAsync(parent);
            return;
        }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "party-actions") { await WatchPartyActionsNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "start-pending") { await WatchPartyActionsNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "room-sheet") { await WatchPartyActionsNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "guest-mode") { await WatchPartyActionsNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "volume-paint") { await MediaVolumePaintNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "volume-visible") { await MediaVolumePaintNativeFixture.RunVisibleAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "party-presentation") { await MediaPartyPresentationNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "tv-populated" or "tv-visual" or "tv-landscape" or "tv-copy") { await MediaTvPopulatedNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "settings-actions") { await AudiobookSettingsActionsNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "request-actions") { await RequestDetailActionsNativeFixture.RunAsync(parent); return; }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") is "paste") { await PartyPasteNativeFixture.RunAsync(parent); return; }
        foreach (var (width, height) in new[] { (1440d, 900d), (1024d, 600d), (900d, 700d), (600d, 450d), (460d, 720d) })
        {
            var page = new ItemDetailPage { Width = width, Height = height };
            parent.Children.Add(page);
            await LayoutAsync(page, width, height);
            page.ViewModel.Item = new MediaItemDetail { Type = "series", ContentId = "fixture-series", Title = "The Fixture Series" };
            ((TextBlock)page.FindName("TitleText")).Text = "The Fixture Series";
            Invoke(page, "UpdateBackdropHeight");
            Invoke(page, "UpdateResponsiveLayout", width);
            // Actual detail repaints (including external-series promotion) must
            // retain the viewport-owned navigation without double-parenting it.
            Invoke(page, "ArrangeCurrentWebUiContentOrder", "series");
            Invoke(page, "ArrangeCurrentWebUiContentOrder", "series");
            await LayoutAsync(page, width, height);
            var viewport = Field<Grid>(page, "_tvViewport");
            var navigation = Field<ScrollViewer>(page, "_tvNavigation");
            var narrow = width < 1024;
            var shortLandscape = !narrow && width > height && height <= 650;
            if (narrow ? !double.IsNaN(viewport.Height) || Grid.GetColumn(navigation) != 0 || Grid.GetRow(navigation) != 1
                : Math.Abs(viewport.ActualHeight - height) > 2 || Grid.GetColumn(navigation) != (shortLandscape ? 1 : 0))
                throw new InvalidOperationException("TV detail does not use natural mobile flow or the bounded desktop navigation layout.");
            var seasonWidth = (double)Invoke(page, "SeasonWidth", width)!;
            if (seasonWidth != (width < 1024 ? 112 : shortLandscape ? 80 : 140))
                throw new InvalidOperationException("TV season cards do not follow the responsive sizes.");
            var extras = new Grid();
            for (var index = 0; index < 3; index++) extras.Children.Add(new Border());
            Invoke(page, "ReflowExtras", extras, width);
            if (extras.ColumnDefinitions.Count != (width < 640 ? 1 : width < 1024 ? 2 : 3)
                || Grid.GetRow((FrameworkElement)extras.Children[2]) != (width < 640 ? 2 : width < 1024 ? 1 : 0))
                throw new InvalidOperationException("Native extras don't reflow into one/two/three columns.");
            await CaptureAsync(page, $"media-tv-{width:0}x{height:0}.png");
            parent.Children.Remove(page);
        }

        foreach (var width in new[] { 1280d, 900d, 768d, 640d, 500d, 460d })
        {
            var artwork = new Uri(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "audiobook-cover.png"));
            var expanded = new AudiobookNowListening { Width = width, Height = 720 };
            parent.Children.Add(expanded);
            Invoke(expanded, "UpdatePlayback");
            Invoke(expanded, "UpdateSkipButtonLabels", new SiloPlayer.Core.Models.AppSettings());
            ((FontIcon)expanded.FindName("PlayPauseIcon")).Glyph = "\uE769"; // same playing state as the official fixture
            ((TextBlock)expanded.FindName("TitleText")).Text = "A Long Audiobook Title to Exercise Responsive Controls";
            ((TextBlock)expanded.FindName("AuthorText")).Text = "Fixture Author";
            ((TextBlock)expanded.FindName("NarratorText")).Text = "Narrated by Fixture Narrator";
            ((Image)expanded.FindName("CoverImage")).Source = new BitmapImage(artwork);
            ((FrameworkElement)expanded.FindName("CoverPlaceholder")).Visibility = Visibility.Collapsed;
            await LayoutAsync(expanded, width, 720);
            Invoke(expanded, "UpdateResponsiveLayout", width);
            var coverImage = (Image)expanded.FindName("CoverImage");
            var bitmap = (BitmapImage)coverImage.Source;
            Program.Log($"TRACE: audiobook cover width={width}, Image={coverImage.ActualWidth}x{coverImage.ActualHeight}, declared={coverImage.Width}x{coverImage.Height}, source={bitmap.PixelWidth}x{bitmap.PixelHeight}, Stretch={coverImage.Stretch}, alignment={coverImage.HorizontalAlignment}/{coverImage.VerticalAlignment}.");
            var artworkLayer = expanded.FindName("CoverArtwork") as Border;
            if (artworkLayer?.Background is not Microsoft.UI.Xaml.Media.ImageBrush { Stretch: Microsoft.UI.Xaml.Media.Stretch.UniformToFill } brush || !ReferenceEquals(brush.ImageSource, coverImage.Source) || Math.Abs(artworkLayer.ActualWidth - artworkLayer.ActualHeight) > 1)
                throw new InvalidOperationException("Actual audiobook artwork doesn't paint its source into a bounded square cover brush.");
            Program.Log($"TRACE: audiobook artwork layer={artworkLayer.ActualWidth}x{artworkLayer.ActualHeight}, source={bitmap.PixelWidth}x{bitmap.PixelHeight}, Stretch={brush.Stretch}.");
            var info = (FrameworkElement)expanded.FindName("ListeningInfo");
            var cover = (FrameworkElement)expanded.FindName("ListeningCover");
            if (Grid.GetRow(info) != (width < 768 ? 1 : 0) || cover.Width != (width < 768 ? Math.Min(width * .7, 320) : 360))
                throw new InvalidOperationException("Expanded audiobook cover and metadata do not reflow at the narrow breakpoint.");
            if (width >= 768) AssertVolumePaint((Slider)expanded.FindName("VolumeSlider"));
            await CaptureAsync(expanded, $"media-audiobook-expanded-{width:0}.png");
            parent.Children.Remove(expanded);

            var mini = new MiniPlayerBar { Width = width };
            parent.Children.Add(mini);
            ((FrameworkElement)mini.FindName("VideoBar")).Visibility = Visibility.Collapsed;
            ((FrameworkElement)mini.FindName("AudiobookBar")).Visibility = Visibility.Visible;
            ((TextBlock)mini.FindName("AudiobookTitleText")).Text = "A Long Audiobook Title to Exercise Responsive Controls";
            ((TextBlock)mini.FindName("AudiobookChapterText")).Text = "Chapter twelve: A Long Chapter Name";
            ((TextBlock)mini.FindName("AudiobookTimeText")).Text = "1:25:30 / 12:08:41";
            Set(mini, "_suppressSeek", true);
            var miniSeek = (Slider)mini.FindName("AudiobookSeekSlider");
            miniSeek.Maximum = 43721; miniSeek.Value = 5130;
            Set(mini, "_suppressSeek", false);
            Invoke(mini, "UpdateAudiobookSkipButtonLabels", new SiloPlayer.Core.Models.AppSettings());
            ((TextBlock)mini.FindName("SkipBackText")).Text = SiloPlayer.App.Services.GetRequiredService<PlayerService>().SeekIntervals.AudiobookBack.ToString();
            ((TextBlock)mini.FindName("SkipForwardText")).Text = SiloPlayer.App.Services.GetRequiredService<PlayerService>().SeekIntervals.AudiobookForward.ToString();
            ((Image)mini.FindName("AudiobookCoverImage")).Source = new BitmapImage(artwork);
            ((FrameworkElement)mini.FindName("AudiobookCoverPlaceholder")).Visibility = Visibility.Collapsed;
            await LayoutAsync(mini, width, 108);
            Invoke(mini, "UpdateResponsiveAudio");
            Invoke(mini, "UpdateAudiobookChapterButtons", new object?[] { null });
            if (mini.Height != (width < 640 ? 119 : 121))
                throw new InvalidOperationException("Mini audiobook height clips its 44px seek target and54/56px controls row.");
            if (((FrameworkElement)mini.FindName("AudiobookVolumeSlider")).Visibility != (width < 768 ? Visibility.Collapsed : Visibility.Visible)
                || width < 640 && ((FrameworkElement)mini.FindName("AudiobookPreviousChapterButton")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Audiobook mini bar timer restores hidden narrow utilities.");
            if (width >= 768) AssertVolumePaint((Slider)mini.FindName("AudiobookVolumeSlider"));
            await CaptureAsync(mini, $"media-audiobook-mini-{width:0}.png");
            parent.Children.Remove(mini);

            var reader = new EbookReaderPage { Width = width, Height = 720 };
            parent.Children.Add(reader);
            await LayoutAsync(reader, width, 720);
            Invoke(reader, "UpdateReaderResponsiveLayout");
            var panel = (FrameworkElement)reader.FindName("SidePanel");
            if (Grid.GetRow(panel) != (width < 1024 ? 1 : 0)
                || ((FrameworkElement)reader.FindName("HeaderProgressText")).Visibility != (width < 640 ? Visibility.Collapsed : Visibility.Visible))
                throw new InvalidOperationException("Reader panel and header don't follow narrow reflow.");
            await CaptureAsync(reader, $"media-reader-{width:0}.png");
            parent.Children.Remove(reader);

            var room = new WatchTogetherRoomPage { Width = width, Height = 720 };
            parent.Children.Add(room);
            room.ViewModel.GetType().GetProperty("Capabilities")!.SetValue(room.ViewModel, new WatchTogetherCapabilities { State = "available", Allowed = true, StagedSelection = true, LobbyReady = true, SelectionModeSwitch = true });
            room.ViewModel.Room = new WatchTogetherRoomSnapshot { Code = "ABC123", Phase = "lobby", SelectedContentId = "fixture-staged", SelfCanManageRoom = true, MemberCount = 2, Members = [new() { UserId = 1, ProfileId = "host", DisplayName = "Alex Host", IsHost = true, IsSelf = true, Connected = true }, new() { UserId = 2, ProfileId = "guest", DisplayName = "Riley Guest", Connected = true, LobbyReady = true }] };
            Invoke(room, "UpdateRoomUi");
            ((TextBlock)room.FindName("StagedTitle")).Text = "The Fixture Movie";
            await LayoutAsync(room, width, 720);
            // SizeChanged is the actual public layout trigger.
            if (((FrameworkElement)room.FindName("RoomRail")).Visibility != (width < 768 ? Visibility.Collapsed : Visibility.Visible)
                || ((FrameworkElement)room.FindName("LeaveButton")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("Party room rail doesn't become a narrow sheet, or the host has no explicit Leave action.");
            if (((TextBlock)room.FindName("ReadySummary")).Text != "1 of 1 ready" || ((FrameworkElement)room.FindName("StartStagedButton")).Visibility != Visibility.Visible
                || ((WrapPanel)room.FindName("ShelfChipsPanel")).Children.Count != 6 || ((Grid)room.FindName("RoomWorkspace")).ActualHeight > 722)
                throw new InvalidOperationException("Host staged lobby doesn't expose readiness and explicit Start.");
            await CaptureAsync(room, $"media-party-room-{width:0}.png");
            room.ViewModel.Room.SelfCanManageRoom = false;
            room.ViewModel.Room.Members[0].IsSelf = false; room.ViewModel.Room.Members[1].IsSelf = true;
            Invoke(room, "UpdateRoomUi");
            room.UpdateLayout();
            if (width >= 400 && (((FrameworkElement)room.FindName("ModeButton")).Visibility != Visibility.Collapsed
                || !VisualDescendants((DependencyObject)room.FindName("RoomHeaderSummary")).OfType<TextBlock>().Any(text => text.Text == "Host picks" && text.ActualWidth > 0
                    && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(text) is FrameworkElement chip && chip.Visibility == Visibility.Visible)))
                throw new InvalidOperationException("Guest's mounted room strip omits its read-only current selection mode.");
            if (((FrameworkElement)room.FindName("LobbyReadyButton")).Visibility != Visibility.Visible || ((FrameworkElement)room.FindName("HostPickSection")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("Guest staged lobby doesn't expose readiness and host-pick suggestions.");
            await CaptureAsync(room, $"media-party-room-guest-{width:0}.png");
            parent.Children.Remove(room);

            var hub = new WatchTogetherJoinPage { Width = width, Height = 720 };
            parent.Children.Add(hub);
            await LayoutAsync(hub, width, 720);
            AssertHubCurrentCopy(hub);
            await CaptureAsync(hub, $"media-party-hub-{width:0}.png");
            parent.Children.Remove(hub);
        }
        Program.Log("PASS: actual media controls use natural mobile and bounded desktop TV layouts, responsive audiobook and reader panels, and party rail/sheet with host Leave; fixture captures saved.");
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "layout-only")
        {
            Program.Log("PASS: MEDIA_RESPONSIVE_LAYOUT_COMPLETED actual loaded responsive controls and captures.");
            return;
        }
        var client = new SiloApiClient(new HttpClient(new NoNetworkHandler()));
        var auth = new SiloPlayer.Core.Services.AuthService(client, new AuthApi(client));
        using var themes = new ThemeMusicService(client, new SettingsApi(client), auth, SiloPlayer.App.Services.GetRequiredService<PlayerService>());
        var audio = new Windows.Media.Playback.MediaPlayer { Volume = 0 };
        typeof(ThemeMusicService).GetField("_audio", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(themes, audio);
        var fadeIn = (Task)Invoke(themes, "FadeAsync", audio, .35d, null)!;
        await Task.Delay(100);
        if (audio.Volume <= 0 || audio.Volume >= .35) throw new InvalidOperationException("Theme audio doesn't ramp from silence.");
        await fadeIn;
        if (Math.Abs(audio.Volume - .35) > .001) throw new InvalidOperationException("Theme fade doesn't reach reference volume.");
        var staleCompletion = false;
        var fadeOut = (Task)Invoke(themes, "FadeAsync", audio, 0d, (Action)(() => staleCompletion = true))!;
        await Task.Delay(50);
        await (Task)Invoke(themes, "FadeAsync", audio, .35d, null)!;
        await fadeOut;
        if (staleCompletion) throw new InvalidOperationException("An interrupted theme fade applies its stale pause/stop.");
        themes.Stop();
        if (Field<object?>(themes, "_audio") != null) throw new InvalidOperationException("Authority stop retains fading theme audio.");
        Program.Log("PASS: actual native theme MediaPlayer ramps volume, cancels superseded fades and stops immediately on authority revocation.");
        await ReaderSettingsAsync(parent);
        await MediaInteractionsNativeFixture.RunAsync(parent);
        await AudiobookDetailInteractionsNativeFixture.RunAsync(parent);
        await WatchPartyShelvesNativeFixture.RunAsync(parent);
        await RequestDetailPollingNativeFixture.RunAsync(parent);
        await MangaStickyNativeFixture.RunAsync(parent);
        await WatchPartyActionsNativeFixture.RunAsync(parent);
        await AudiobookSettingsActionsNativeFixture.RunAsync(parent);
        await RequestDetailActionsNativeFixture.RunAsync(parent);
        await PartyPasteNativeFixture.RunAsync(parent);
        Program.Log("PASS: MEDIA_PARITY_ALL_ACCEPTANCE_COMPLETED responsive controls, theme fades, reader persistence, party interactions, audiobook transport/narrations and shared shelves.");
    }

    private static async Task HubCurrentCopyAsync(StackPanel parent)
    {
        foreach (var width in new[] { 1280d, 500d })
        {
            var hub = new WatchTogetherJoinPage { Width = width, Height = 720 };
            parent.Children.Add(hub);
            try { await LayoutAsync(hub, width, 720); AssertHubCurrentCopy(hub); await CaptureAsync(hub, $"media-party-hub-current-{width}.png"); }
            finally { parent.Children.Remove(hub); await Task.Delay(100); }
        }
        Program.Log("PASS: PARTY_HUB_CURRENT_COPY_COMPLETED current Start/Join, mode captions, inline Paste and numbered instructions.");
    }
    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in VisualDescendants(child)) yield return next; }
    }
    private static void AssertHubCurrentCopy(WatchTogetherJoinPage hub)
    {
        var texts = VisualDescendants(hub).OfType<TextBlock>().Select(t => t.Text).ToArray();
        foreach (var required in new[] { "Start a party", "Join a party", "Host picks", "Everyone votes", "You choose, everyone watches.", "Anyone suggests, the room votes.", "Open a room", "Share the code", "Watch in sync", "Play, pause, and seek together. The room ends two minutes after the host drops off." })
            if (!texts.Contains(required)) throw new InvalidOperationException($"Actual party hub is missing current official copy: {required}");
        var paste = VisualDescendants(hub).OfType<Button>().Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Paste room code");
        var box = (TextBox)hub.FindName("RoomCodeBox");
        if (!ReferenceEquals(paste.Parent, box.Parent) || paste.TransformToVisual(box).TransformPoint(new Windows.Foundation.Point()).X < box.ActualWidth - 1)
            throw new InvalidOperationException("Actual party hub Paste doesn't share the code-input row.");
    }

    private static async Task ReaderSettingsAsync(StackPanel parent)
    {
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = serviceField.GetValue(null);
        var handler = new ReaderSettingsHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://reader-settings-fixture.invalid"); client.SetProfile("fixture-profile");
        var api = new EbooksApi(client);
        using var services = new ServiceCollection().AddSingleton(api).AddSingleton(new CatalogApi(client)).AddSingleton(new ToastService()).BuildServiceProvider();
        serviceField.SetValue(null, services);
        var pages = new List<EbookReaderPage>();
        try
        {
            EbookReaderPage Create()
            {
                var page = new EbookReaderPage { Width = 460, Height = 720 };
                Set(page, "_readerSettingsDirectory", Path.Combine(Program.ResultDirectory, "reader-settings-local"));
                Set(page, "_readerContext", api.CaptureContext()); Set(page, "_contentId", "fixture-book"); Set(page, "_initialized", true);
                pages.Add(page); parent.Children.Add(page); return page;
            }
            Task Save(EbookReaderPage page, double font)
            {
                Set(page, "_suppressControls", true); ((Slider)page.FindName("FontSizeSlider")).Value = font; Set(page, "_suppressControls", false);
                return (Task)Invoke(page, "SavePreferencesAsync")!;
            }
            var page = Create();
            await api.GetReaderConfigAsync("fixture-book");
            await Task.WhenAll(Save(page, 113), Save(page, 124), Save(page, 150));
            Program.Log("Reader coalescing fixture writes=" + string.Join(",", handler.Writes) + "; feedback=" + ((TextBlock)page.FindName("ReaderSyncStatus")).Text);
            if (!handler.Writes.SequenceEqual(new[] { 150d })) throw new InvalidOperationException("Native reader doesn't coalesce rapid changes to the latest configuration.");
            handler.Fail = true; await Save(page, 161);
            var feedback = (TextBlock)page.FindName("ReaderSyncStatus");
            if (feedback.Visibility != Visibility.Visible || !feedback.Text.Contains("Could not sync")) throw new InvalidOperationException("Native reader doesn't expose save failure recovery feedback.");
            await LayoutAsync(page, 460, 720); await CaptureAsync(page, "media-reader-save-failure.png");
            var restored = Create();
            Set(restored, "_ebooksApi", new EbooksApi(client)); // Reopening offline starts without an ETag, just like a new reader.
            await (Task)Invoke(restored, "LoadPreferencesAsync")!;
            if (((Slider)restored.FindName("FontSizeSlider")).Value != 161) throw new InvalidOperationException("Native reader loses the pending local settings when the server is unavailable.");
            handler.Fail = false; handler.Conflict = true; await Save(page, 162);
            if (handler.Writes.Last() != 162 || feedback.Visibility != Visibility.Collapsed || handler.Conflict)
                throw new InvalidOperationException("Native reader doesn't refresh the conflicting revision, retry latest settings and clear sync failure.");
            await Save(restored, 163);
            if (handler.Writes.Last() != 163 || ((TextBlock)restored.FindName("ReaderSyncStatus")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("An offline-opened reader cannot acquire a fresh revision and recover sync.");
            Program.Log("PASS: native reader latest-only settings writes, save-failure feedback, offline local recovery and guarded conflict retry.");
        }
        finally { foreach (var page in pages) parent.Children.Remove(page); serviceField.SetValue(null, original); }
    }
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!.Invoke(target, args);
    private static async Task LayoutAsync(FrameworkElement element, double width, double height)
    {
        element.Measure(new Windows.Foundation.Size(width, height));
        element.Arrange(new Windows.Foundation.Rect(0, 0, width, height));
        element.UpdateLayout();
        await Task.Delay(100);
    }
    private static void AssertVolumePaint(Slider slider)
    {
        var host = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(slider) as Grid;
        var rail = host?.Children.OfType<ProgressBar>().SingleOrDefault(bar => (string?)bar.Tag == "native-volume-paint");
        if (rail == null || rail.ActualWidth != 96 || rail.Minimum != slider.Minimum || rail.Maximum != slider.Maximum)
            throw new InvalidOperationException("Audiobook volume paint does not span the actual96px native range.");
        var provider = (Microsoft.UI.Xaml.Automation.Provider.IRangeValueProvider)new Microsoft.UI.Xaml.Automation.Peers.SliderAutomationPeer(slider).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.RangeValue);
        var value = slider.Value; provider.SetValue(25);
        if (slider.Value != 25 || rail.Value != 25) throw new InvalidOperationException("Actual native volume range invocation is not reflected by its full-width paint.");
        provider.SetValue(value);
    }
    internal static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(80);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidOperationException("Media fixture wasn't laid out for capture.");
        var pixels = await bitmap.GetPixelsAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Media fixture attempted a network request: " + request.RequestUri?.AbsolutePath);
    }
    private sealed class MediaServices : IServiceProvider, IDisposable
    {
        private readonly IServiceProvider _original;
        private readonly HttpClient _http = new(new NoNetworkHandler());
        private readonly SiloApiClient _api;
        private readonly PlaybackApi _playback;
        private readonly EbooksApi _ebooks;
        private readonly SettingsApi _settings;
        private readonly List<WatchTogetherRoomViewModel> _rooms = [];
        private WatchTogetherCoordinator? _coordinator;
        internal MediaServices(IServiceProvider original)
        {
            _original = original; _api = new(_http); _api.SetBaseUrl("https://media-fixture.invalid");
            _playback = new(_api); _ebooks = new(_api); _settings = new(_api);
        }
        public object? GetService(Type type)
        {
            if (type == typeof(EbooksApi)) return _ebooks;
            if (type == typeof(SettingsApi)) return _settings;
            if (type == typeof(PlaybackApi)) return _playback;
            if (type == typeof(WatchTogetherJoinViewModel)) return new WatchTogetherJoinViewModel(_playback);
            if (type == typeof(WatchTogetherRoomViewModel)) { var room = new WatchTogetherRoomViewModel(_playback, _api); _rooms.Add(room); return room; }
            if (type == typeof(WatchTogetherCoordinator)) return _coordinator ??= new WatchTogetherCoordinator(_original.GetRequiredService<PlayerService>());
            return _original.GetService(type);
        }
        public void Dispose() { _coordinator?.ClearActiveRoom(); foreach (var room in _rooms) room.Dispose(); _http.Dispose(); }
    }
    private sealed class ReaderSettingsHandler : HttpMessageHandler
    {
        internal bool Fail, Conflict;
        internal List<double> Writes { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/reader-config")) throw new InvalidOperationException("Unexpected reader fixture route.");
            if (Fail) return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"title\":\"Reader sync unavailable\"}", Encoding.UTF8, "application/problem+json") };
            if (request.Method == HttpMethod.Put)
            {
                if (Conflict) { Conflict = false; return new(HttpStatusCode.PreconditionFailed) { Content = new StringContent("{\"title\":\"Revision changed\"}", Encoding.UTF8, "application/problem+json") }; }
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Writes.Add(body.RootElement.GetProperty("config").GetProperty("settings").GetProperty("fontSize").GetDouble());
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"config\":{\"settings\":{\"fontSize\":112}}}", Encoding.UTF8, "application/json") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"fixture-reader-revision\"");
            return response;
        }
    }
}
