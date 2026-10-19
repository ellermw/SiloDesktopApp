using System.Net;
using System.Reflection;
using System.Text.Json;
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
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Foundation;

internal static class UpstreamOct18UiNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://oct18-ui-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        var catalog = new CatalogApi(client);
        var login = new LoginViewModel(auth, new AuthApi(client), new SettingsApi(client), client)
        { ShowPasswordForm = true, RecoveryAvailable = true, ServerName = "Fixture Silo" };
        slot.SetValue(null, new Services(original, client, auth, catalog, login));
        try
        {
            var mode = Environment.GetEnvironmentVariable("SILO_OCT18_UI_CASE");
            if (mode is null or "menu") await MenuAsync(parent, auth, wire);
            if (mode is null or "subtitles") await SubtitlesAsync(parent, wire);
            if (mode is null or "login") await LoginAsync(parent, login);
        }
        finally { slot.SetValue(null, original); }
    }

    private static async Task MenuAsync(StackPanel parent, AuthService auth, Wire wire)
    {
        auth.SetCurrentUser(new UserInfo { Id = "fixture", Role = "admin", DownloadAllowed = true });
        var page = new ItemDetailPage { Width = 900, Height = 720 }; parent.Children.Add(page);
        using var lifetime = new CancellationTokenSource();
        try
        {
            var detail = new MediaItemDetail { ContentId = "fixture-movie", Title = "Fixture movie", Type = "movie",
                Versions = [new FileVersion { FileId = 1 }, new FileVersion { FileId = 2 }],
                UserData = new() { PositionSeconds = 120, Played = true }, UserState = new() };
            page.ViewModel.Item = detail; Set(page, "_navigationCts", lifetime); Set(page, "_currentContentId", detail.ContentId);
            Set(page, "_selectedVersion", detail.Versions[0]); Set(page, "_mediaActionCapabilitiesId", detail.ContentId);
            Set(page, "_offerSeekPreviews", true); Set(page, "_offerMarkerDetection", true);
            Call(page, "BuildMoreFlyout"); await Task.Delay(100);
            var menu = (MenuFlyout)page.FindName("MoreFlyout");
            var manage = menu.Items.OfType<MenuFlyoutSubItem>().SingleOrDefault(item => item.Text == "Manage");
            Check(manage is not null, "Multiple administrator actions must be nested under Manage.");
            Check(Visible(manage!.Items).SequenceEqual(new[] { "Edit Metadata", "Match Item", "Refresh Metadata", "|", "Edit Markers", "Re-detect Credits", "Seek Previews", "Split Versions", "|", "Media Info", "View Play History" }),
                "Manage must preserve upstream metadata/tool/inspection group order: " + string.Join(",", Visible(manage.Items)));
            Check(Visible(menu.Items).SequenceEqual(new[] { "Reset Progress", "|", "Add to Watchlist", "Add to Collection", "Download", "Not Interested", "|", "Start a Watch Party", "|", "Manage" }),
                "Overflow must separate Playback, Library, Party and Manage: " + string.Join(",", Visible(menu.Items)));
            Check(!AllActions(menu.Items).Any(item => item.Text.Contains("Subtitles")), "Subtitle search must leave More.");
            foreach (var width in new[] { 460d, 1280d })
            {
                page.Width = width; page.UpdateLayout();
                menu.ShowAt(page); await Task.Delay(80);
                var peer = FrameworkElementAutomationPeer.FromElement(manage) ?? FrameworkElementAutomationPeer.CreatePeerForElement(manage);
                Check(peer?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider, "Native Manage must expose keyboard-accessible submenu expansion.");
                ((IExpandCollapseProvider)peer!.GetPattern(PatternInterface.ExpandCollapse)!).Expand();
                await Task.Delay(80);
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot))
                    if (popup.Child is FrameworkElement visual && visual.ActualWidth > 0)
                        await MediaParityNativeFixture.CaptureAsync(visual, $"oct18-manage-{width}-{visual.GetType().Name}.png");
                menu.Hide(); await Task.Delay(40);
            }
            // A capability update can hide the only playback action after grouping.
            wire.SupportsReset = false; Call(page, "BuildMoreFlyout"); await Task.Delay(80);
            Check(Visible(menu.Items).First() == "Add to Watchlist", "An unavailable playback group must not leave a leading separator.");
            Check(AllActions(menu.Items).Single(item => item.Text == "Reset Progress").Visibility == Visibility.Collapsed,
                "Grouping must retain reset capability gating.");
            auth.SetCurrentUser(new UserInfo { Id = "fixture", Role = "user" }); Call(page, "BuildMoreFlyout"); await Task.Delay(80);
            Check(!menu.Items.OfType<MenuFlyoutSubItem>().Any(item => item.Text == "Manage") &&
                !AllActions(menu.Items).Any(item => item.Text is "Edit Metadata" or "Edit Markers" or "View Play History" or "Media Info"),
                "A regular viewer must see neither privileged management items nor an empty Manage submenu.");
            auth.SetCurrentUser(new UserInfo { Id = "fixture", Role = "user", Permissions = ["marker_edit"] });
            Call(page, "BuildMoreFlyout"); await Task.Delay(80);
            Check(!menu.Items.OfType<MenuFlyoutSubItem>().Any(item => item.Text == "Manage") && Visible(menu.Items).Last() == "Edit Markers",
                "Exactly one authorized management action must remain inline at the end.");
            Program.Log("PASS: October18 actual native More admin/viewer/single permission groups, capability-hidden reset, and opened Manage cascade at460/1280.");
        }
        finally { parent.Children.Remove(page); }
    }

    private static async Task SubtitlesAsync(StackPanel parent, Wire wire)
    {
        var page = new ItemDetailPage { Width = 900, Height = 720 }; parent.Children.Add(page);
        try
        {
            var version = new FileVersion { FileId = 17, SubtitleTracks = Enumerable.Range(0, 24).Select(i => new VersionSubtitleTrack { Index = i, Language = "eng", Codec = "srt", Title = "Fixture track " + i }).ToList() };
            page.ViewModel.Item = new MediaItemDetail { ContentId = "subtitle-fixture", Title = "Fixture subtitles", Type = "movie", Versions = [version] };
            Set(page, "_selectedVersion", version); Call(page, "BuildSubtitlesPopoverFlyout", version, true);
            var flyout = (Flyout)page.FindName("SubtitlesPopoverFlyout");
            var content = (FrameworkElement)flyout.Content;
            Program.Log($"TRACE Subs before open: root={content.GetType().Name}, buttons={Descendants<Button>(content).Count()}, labels={Descendants<TextBlock>(content).Count()}.");
            flyout.ShowAt(page); await Task.Delay(100); content.UpdateLayout();
            Program.Log($"TRACE Subs after open: root={content.GetType().Name}, buttons={Descendants<Button>(content).Count()}, labels={Descendants<TextBlock>(content).Count()}.");
            var search = Descendants<Button>(content).SingleOrDefault(button => Descendants<TextBlock>(button).Any(text => text.Text == "Search for Subtitles…"));
            Check(search is not null, "Subs must offer Search for Subtitles… below the subtitle inventory.");
            var scroll = Descendants<ScrollViewer>(content).Single(); scroll.ChangeView(null, scroll.ScrollableHeight, null, true); await Task.Delay(60);
            var searchBounds = Bounds(search!, content); var scrollBounds = Bounds(scroll, content);
            Check(searchBounds.Y >= scrollBounds.Bottom - 1 && searchBounds.Bottom <= content.ActualHeight + 1,
                "Subtitle search must stay below the bounded scroll inventory.");
            await MediaParityNativeFixture.CaptureAsync(content, "oct18-subtitles-footer.png");
            ((IInvokeProvider)new ButtonAutomationPeer(search!).GetPattern(PatternInterface.Invoke)!).Invoke();
            await Task.Delay(150);
            var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(popup => Descendants<SubtitleSearchDialog>(popup.Child)).SingleOrDefault();
            Check(dialog is not null && (int)typeof(SubtitleSearchDialog).GetField("_mediaFileId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)! == 17,
                "Subs search action must open the existing dialog for the selected media file.");
            dialog!.Hide(); await Task.Delay(60);
            Check(wire.Writes == 0, "Opening subtitle search must not mutate server state.");
            Call(page, "BuildSubtitlesPopoverFlyout", null, true);
            Check(((Button)page.FindName("SubtitlesPopoverButton")).Visibility == Visibility.Collapsed, "No selected version must hide the Subs entry point.");
            Program.Log("PASS: October18 native Subs sticky search footer opens the existing dialog for selected file17 without writes; absent version hides Subs.");
        }
        finally
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot))
                foreach (var dialog in Descendants<SubtitleSearchDialog>(popup.Child)) dialog.Hide();
            ((Flyout)page.FindName("SubtitlesPopoverFlyout")).Hide(); parent.Children.Remove(page);
        }
    }

    private static async Task LoginAsync(StackPanel parent, LoginViewModel vm)
    {
        var page = new LoginPage { Width = 900, Height = 740 }; parent.Children.Add(page);
        try
        {
            foreach (var width in new[] { 360d, 460d, 1280d, 3840d })
            {
                page.Width = width; page.Measure(new Size(width, 740)); page.Arrange(new Rect(0, 0, width, 740)); page.UpdateLayout(); await Task.Delay(60);
                var password = (PasswordBox)page.FindName("PasswordBox");
                var forgot = Descendants<HyperlinkButton>(page).Single(button => Equals(button.Content, "Forgot password?"));
                var passwordBounds = Bounds(password, page); var forgotBounds = Bounds(forgot, page);
                Check(forgot.Visibility == Visibility.Visible && forgotBounds.Y >= passwordBounds.Bottom - 1,
                    $"Forgot password must appear below the password input at width{width}.");
                Check(Math.Abs(forgotBounds.Right - passwordBounds.Right) <= 1 && forgotBounds.X >= passwordBounds.X - 1,
                    $"Forgot password must align to the input's right edge within the form at width{width}.");
                var reveal = Descendants<Button>((DependencyObject)password.Parent).Single(button => AutomationProperties.GetName(button) == "Show password");
                ((IInvokeProvider)new ButtonAutomationPeer(reveal).GetPattern(PatternInterface.Invoke)!).Invoke();
                Check(password.PasswordRevealMode == PasswordRevealMode.Visible, "Moving recovery must preserve wrapped password reveal.");
                ((IInvokeProvider)new ButtonAutomationPeer(reveal).GetPattern(PatternInterface.Invoke)!).Invoke();
                await MediaParityNativeFixture.CaptureAsync(page, $"oct18-login-{width}.png");
            }
            var recovery = Descendants<HyperlinkButton>(page).Single(button => Equals(button.Content, "Forgot password?"));
            vm.RecoveryAvailable = false; await Task.Delay(30); Check(recovery.Visibility == Visibility.Collapsed, "Unavailable recovery must stay hidden.");
            var directory = new AuthProvider { Id = "directory-fixture", DisplayName = "Fixture directory", Mode = "credentials" };
            var local = new AuthProvider { Id = "local", DisplayName = "Silo password", Mode = "credentials" };
            vm.CredentialProviders.Add(local); vm.CredentialProviders.Add(directory); vm.HasCredentialProviderPicker = true;
            vm.RecoveryAvailable = true; vm.SelectedCredentialProvider = directory; await Task.Delay(30);
            Check(ReferenceEquals(vm.SelectedCredentialProvider, directory), "Fixture directory must be an available selection in the real credentials picker.");
            Check(recovery.Visibility == Visibility.Collapsed, "Directory credentials must not offer local recovery.");
            vm.SelectedCredentialProvider = local; await Task.Delay(30);
            Check(recovery.Visibility == Visibility.Visible, "Available local recovery must return.");
            Program.Log("PASS: October18 native recovery below password/right aligned at360/460/1280/3840, reveal works and capability/provider gating remains.");
        }
        finally { parent.Children.Remove(page); }
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement relative) => element.TransformToVisual(relative).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    private static string[] Visible(IEnumerable<MenuFlyoutItemBase> items) => items.Where(item => item.Visibility == Visibility.Visible).Select(item => item is MenuFlyoutSeparator ? "|" : item is MenuFlyoutItem action ? action.Text : ((MenuFlyoutSubItem)item).Text).ToArray();
    private static IEnumerable<MenuFlyoutItem> AllActions(IEnumerable<MenuFlyoutItemBase> items) => items.OfType<MenuFlyoutItem>().Concat(items.OfType<MenuFlyoutSubItem>().SelectMany(item => AllActions(item.Items)));
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    { if (node is T own) yield return own; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); ++i) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i))) yield return child; }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Call(object target, string name, params object?[] values) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, values);
    private sealed class Services(IServiceProvider original, SiloApiClient client, AuthService auth, CatalogApi catalog, LoginViewModel login) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(SiloApiClient) ? client : type == typeof(AuthService) ? auth : type == typeof(CatalogApi) ? catalog :
            type == typeof(PlaybackApi) ? new PlaybackApi(client) : type == typeof(LoginViewModel) ? login :
            type == typeof(ItemDetailViewModel) ? new ItemDetailViewModel(catalog, new ItemDetailPrefetchCache(catalog, auth)) :
            type == typeof(UICustomizationService) ? new UICustomizationService(new SettingsApi(client)) : original.GetService(type);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool SupportsReset = true; internal int Writes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get) ++Writes;
            var path = request.RequestUri!.AbsolutePath;
            object body = path.Contains("progress/capabilities") ? new { clear_resume_position = SupportsReset } :
                path == "/api/v2/capabilities/not-interested" ? new { state = "available" } : new { items = Array.Empty<object>() };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
