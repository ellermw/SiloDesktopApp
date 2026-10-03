using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;

internal static class AudiobookDetailInteractionsNativeFixture
{
    private static nint _nativeMpv;
    internal static async Task RunAsync(StackPanel parent)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)servicesField.GetValue(null)!;
        using var handler = new RejectNetwork(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://audiobook-detail-fixture.invalid");
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "audiobook-settings"));
        using var auth = new AuthService(client, new AuthApi(client));
        using var player = new PlayerService(new PlaybackApi(client), new CatalogApi(client), auth, client, settings, new SettingsApi(client));
        object? navigationTarget = null; Type? navigationType = null;
        var navigation = new NavigationService { Frame = new Frame(), NavigationRequestHandler = (type, parameter) => { navigationType = type; navigationTarget = parameter; return true; } };
        servicesField.SetValue(null, new FixtureServices(original, player, navigation));
        ItemDetailPage? page = null; object? mpv = null;
        try
        {
            page = new ItemDetailPage { Width = 900, Height = 720 };
            Set(page, "_playerService", player);
            var item = new MediaItemDetail
            {
                ContentId = "fixture-audio", Type = "audiobook", Title = "The Fixture Audiobook",
                Versions = [new() { FileId = 42, Duration = 60 }],
                Audiobook = new AudiobookDetailExtension
                {
                    TotalDurationSeconds = 60,
                    Narrators = [new() { Name = "Current Narrator" }],
                    OtherNarrations = [new() { ContentId = "fixture-alternative", Title = "The Fixture Audiobook", Narrators = ["Alternate Narrator"], Year = 2024 }]
                }
            };
            page.ViewModel.Item = item;
            ((TextBlock)page.FindName("TitleText")).Text = item.Title;
            Invoke(page, "ConfigureBookDetail", item);
            parent.Children.Add(page); page.UpdateLayout(); await Task.Delay(100);
            var narrator = (HyperlinkButton)page.FindName("NarrationPickerButton");
            if (narrator.Visibility != Visibility.Visible || ((FrameworkElement)page.FindName("BookNarratorLine")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Alternative narrations do not replace the narrator line with its keyboard-accessible picker.");
            ((IInvokeProvider)new HyperlinkButtonAutomationPeer(narrator).GetPattern(PatternInterface.Invoke)).Invoke();
            MenuFlyoutItem? alternate = null; ToggleMenuFlyoutItem? current = null;
            await UntilAsync(() =>
            {
                var elements = VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).SelectMany(popup => Descendants(popup.Child));
                alternate = elements.OfType<MenuFlyoutItem>().FirstOrDefault(choice => choice.Text == "Alternate Narrator · 2024");
                current = elements.OfType<ToggleMenuFlyoutItem>().FirstOrDefault();
                return alternate != null && current != null;
            });
            if (!current!.IsChecked || current.IsEnabled) throw new InvalidOperationException("Narration picker does not identify its current narration.");
            await MediaParityNativeFixture.CaptureAsync(page, "media-audiobook-narration-picker.png");
            var menuRoot = VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).Single(popup => Descendants(popup.Child).Contains(alternate!)).Child;
            await MediaParityNativeFixture.CaptureAsync((FrameworkElement)menuRoot, "media-audiobook-narration-menu.png");
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(alternate!).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => Equals(navigationTarget, "fixture-alternative"));
            if (navigationType != typeof(ItemDetailPage)) throw new InvalidOperationException("Alternative narration does not navigate to catalog detail.");
            await UntilAsync(() => !VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).Any(popup => Descendants(popup.Child).Contains(alternate!)));

            var settingsFlyout = (Flyout)typeof(PlayerService).Assembly.GetType("SiloPlayer.Controls.AudiobookSettingsFlyout")!.GetMethod("Create")!.Invoke(null, [player, settings])!;
            settingsFlyout.ShowAt((FrameworkElement)page.FindName("PrimaryPlayButton"));
            var panel = (StackPanel)settingsFlyout.Content;
            await UntilAsync(() => panel.IsLoaded && panel.ActualWidth > 0 && panel.ActualHeight > 0);
            var intervalRows = panel.Children.OfType<StackPanel>().Where(row => row.Children.OfType<WrapPanel>().Any()).ToArray();
            if (intervalRows.Length != 2 || intervalRows.Any(row => !row.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Select(button => (int)button.Tag).SequenceEqual(SeekPreferences.Choices)))
                throw new InvalidOperationException("Audiobook settings do not show both exact contract interval chip rows.");
            var activeIntervals = intervalRows.Select(row => row.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Single(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(button) == "Selected")).ToArray();
            if ((int)activeIntervals[0].Tag != player.SeekIntervals.AudiobookBack || (int)activeIntervals[1].Tag != player.SeekIntervals.AudiobookForward)
                throw new InvalidOperationException("Audiobook interval chip rows do not identify their simultaneous selections.");
            var smart = panel.Children.OfType<Grid>().SelectMany(row => row.Children.OfType<ToggleSwitch>()).Single();
            ((IToggleProvider)new ToggleSwitchAutomationPeer(smart).GetPattern(PatternInterface.Toggle)).Toggle();
            if (settings.Load().AudiobookSmartRewind != smart.IsOn) throw new InvalidOperationException("Compact smart-rewind switch does not persist its local setting.");
            var settingsRoot = VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).Single(popup => Descendants(popup.Child).Contains(panel)).Child;
            var settingsPresenter = Descendants(settingsRoot).OfType<FlyoutPresenter>().Single();
            await UntilAsync(() => settingsPresenter.IsLoaded && settingsPresenter.ActualWidth > 0 && settingsPresenter.ActualHeight > 0);
            await Task.Delay(200); // let the native popup opening transition finish
            Program.Log($"TRACE: audiobook settings popup root={settingsRoot.GetType().Name}, presenter={settingsPresenter.ActualWidth}x{settingsPresenter.ActualHeight}, visible={settingsPresenter.Visibility}, opacity={settingsPresenter.Opacity}, content={panel.ActualWidth}x{panel.ActualHeight}, loaded={panel.IsLoaded}.");
            try { await MediaParityNativeFixture.CaptureAsync(panel, "media-audiobook-settings-chip-panel.png"); }
            catch (InvalidOperationException ex) when (ex.Message == "Media fixture wasn't laid out for capture.")
            {
                // This popup's loaded controls have already been exercised.
                // RTB may omit a native popup compositor surface; preserve its
                // measured bounds and capture its same content, never a copy.
                settingsFlyout.Hide();
                await UntilAsync(() => !VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).Any(popup => Descendants(popup.Child).Contains(panel)));
                settingsFlyout.Content = null;
                var captureHost = new Border { Width = 260, Padding = new Thickness(16, 14, 16, 14), CornerRadius = new CornerRadius(8), Background = settingsPresenter.Background, Child = panel };
                parent.Children.Add(captureHost);
                try
                {
                    captureHost.UpdateLayout(); await UntilAsync(() => panel.IsLoaded && captureHost.ActualWidth > 0 && captureHost.ActualHeight > 0);
                    await MediaParityNativeFixture.CaptureAsync(captureHost, "media-audiobook-settings-chip-panel.png");
                    Program.Log("TRACE: settings popup RTB omitted its surface; captured the same exercised controls temporarily mounted in fixture parent. Native popup bounds remain260x264.");
                }
                finally { parent.Children.Remove(captureHost); captureHost.Child = null; settingsFlyout.Content = panel; }
            }
            settingsFlyout.Hide();

            // Run the actual native transport against silent local PCM with null
            // audio output; never open a remote stream or the installed player.
            var wave = Path.Combine(Program.ResultDirectory, "audiobook-detail-silence.wav");
            WriteSilence(wave);
            if (_nativeMpv == 0)
            {
                _nativeMpv = NativeLibrary.Load(Path.Combine(Program.AppDirectory, "libmpv-2.dll"));
                NativeLibrary.SetDllImportResolver(typeof(PlayerService).Assembly,
                    (name, _, _) => name.Equals("libmpv-2.dll", StringComparison.OrdinalIgnoreCase) ? _nativeMpv : 0);
                // Keep this handle until process exit: cached P/Invoke pointers
                // remain valid for any subsequent native acceptance group.
            }
            // The desktop project compiles the real player source into its own
            // published assembly; it does not ship a separate Player assembly.
            mpv = Activator.CreateInstance(typeof(PlayerService).Assembly.GetType("SiloPlayer.Player.MpvPlayer", throwOnError: true)!)!;
            InvokePublic(mpv, "Initialize", 16, 16);
            InvokePublic(mpv, "SetProperty", "ao", "null");
            InvokePublic(mpv, "SetProperty", "video", "no");
            Set(player, "_mpv", mpv);
            Property(player, "IsAudiobook", true); Property(player, "ContentId", item.ContentId); Property(player, "State", PlayerState.Minimized);
            Property(player, "Duration", 60d);
            var activePage = page;
            Action<bool> pauseChanged = paused => { Property(player, "IsPaused", paused); Invoke(activePage, "OnDetailPauseChanged", paused); };
            Action<double> positionChanged = position => { Property(player, "Position", position); Invoke(activePage, "OnDetailPositionChanged", position); };
            mpv.GetType().GetEvent("PauseChanged")!.AddEventHandler(mpv, pauseChanged);
            mpv.GetType().GetEvent("PositionChanged")!.AddEventHandler(mpv, positionChanged);
            InvokePublic(mpv, "LoadFile", wave, null, 0d); InvokePublic(mpv, "Play");
            await UntilAsync(() => !(bool)mpv.GetType().GetProperty("IsPaused")!.GetValue(mpv)! && player.Position > 0);
            Invoke(page, "UpdateActiveAudiobook");
            if (((TextBlock)page.FindName("PlayButtonText")).Text != "Pause" || ((FrameworkElement)page.FindName("BookProgressSummaryText")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("Active audiobook detail does not show Pause and current progress.");
            var play = (Button)page.FindName("PrimaryPlayButton");
            if (play.Visibility != Visibility.Visible || ((FrameworkElement)page.FindName("SplitPlayButton")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("Active audiobook transport is not visible.");
            ((IInvokeProvider)new ButtonAutomationPeer(play).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => (bool)mpv.GetType().GetProperty("IsPaused")!.GetValue(mpv)! && ((TextBlock)page.FindName("PlayButtonText")).Text == "Resume");
            await MediaParityNativeFixture.CaptureAsync(page, "media-audiobook-active-paused.png");
            ((IInvokeProvider)new ButtonAutomationPeer(play).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => !(bool)mpv.GetType().GetProperty("IsPaused")!.GetValue(mpv)! && ((TextBlock)page.FindName("PlayButtonText")).Text == "Pause");
            if (handler.Calls != 0) throw new InvalidOperationException("Active audiobook detail starts a new remote playback request.");
            mpv.GetType().GetEvent("PauseChanged")!.RemoveEventHandler(mpv, pauseChanged);
            mpv.GetType().GetEvent("PositionChanged")!.RemoveEventHandler(mpv, positionChanged);
            Program.Log("PASS: AUDIOBOOK_DETAIL_INTERACTIONS_COMPLETED actual narration menu/current selection/navigation and native local mpv Pause/Resume without playback/start.");
        }
        finally
        {
            if (page != null)
            {
                if (typeof(ItemDetailPage).GetField("_rootElement", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page) is FrameworkElement root)
                    root.SizeChanged -= (SizeChangedEventHandler)typeof(ItemDetailPage).GetMethod("OnRootSizeChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.CreateDelegate(typeof(SizeChangedEventHandler), page);
                parent.Children.Remove(page);
            }
            Set(player, "_mpv", null); (mpv as IDisposable)?.Dispose();
            navigation.Frame = null; servicesField.SetValue(null, original);
        }
    }
    private static void WriteSilence(string path)
    {
        const int samples = 8000 * 60, bytes = samples * 2;
        using var stream = File.Create(path); using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Descendants(VisualTreeHelper.GetChild(root, i))) yield return item; }
    private static async Task UntilAsync(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Audiobook interaction fixture did not settle."); }
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Property(object target, string name, object? value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static object? InvokePublic(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance)!.Invoke(target, args);
    private sealed class FixtureServices(IServiceProvider original, PlayerService player, NavigationService navigation) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(PlayerService) ? player : type == typeof(NavigationService) ? navigation : original.GetService(type);
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; throw new InvalidOperationException("Audiobook fixture attempted a remote request."); }
    }
}
