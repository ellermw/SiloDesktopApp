using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class MediaPartyPresentationNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)serviceField.GetValue(null)!;
        using var handler = new EmptyHandler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://party-presentation-fixture.invalid"); client.SetProfile("fixture-host");
        serviceField.SetValue(null, new LocalServices(original, client));
        var differences = new List<string>();
        foreach (var (key, expected) in new[] { ("SurfaceBrush", "#151E2B"), ("SurfaceRaisedBrush", "#223245"), ("BorderBrush", "#28384D") })
        {
            var brush = (SolidColorBrush)Application.Current.Resources[key]; var color = brush.Color;
            var actual = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            Program.Log($"TRACE: actual Party runtime {key}={actual}; pinned Cobalt={expected}.");
            if (actual != expected) differences.Add($"Runtime {key} must match the actual pinned Cobalt reference before RGB parity can be claimed.");
        }
        try
        {
            foreach (var width in new[] { 1280d, 600d, 500d })
            {
                var owner = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"], RequestedTheme = ElementTheme.Dark };
                var window = new Window { Content = owner };
                try
                {
                    window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)width, 720));
                    window.AppWindow.Show(false); await Task.Delay(100);
                    var scale = owner.XamlRoot.RasterizationScale;
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(720 * scale)));
                    var hub = new WatchTogetherJoinPage { Width = width, Height = 720 };
                    owner.Children.Add(hub); await Task.Delay(100); hub.UpdateLayout();
                    var content = (StackPanel)hub.FindName("ContentStack");
                    var gutter = width < 640 ? 24 : 32;
                    var expectedWidth = Math.Min(896 - gutter * 2, width - gutter * 2);
                    Program.Log($"TRACE: hub content {width}: actual={content.ActualWidth}, expected border-box interior={expectedWidth}.");
                    if (Math.Abs(content.ActualWidth - expectedWidth) > 2)
                        differences.Add($"{width}: hub content must fill the pinned896px border-box interior instead of shrinking to its children's natural width.");
                    AssertLine(hub, "WATCH PARTY", 11, 16.5, width, differences);
                    var headline = (TextBlock)hub.FindName("HeadlineText");
                    if (headline.Text.Replace("\n", " ") != "Watch together, wherever everyone is.")
                        differences.Add($"{width}: actual hub title must retain the original copy.");
                    AssertLine(headline, width < 640 ? 30 : 36, width < 640 ? 36 : 40, width, differences);
                    foreach (var label in new[] { "Start a party", "Join a party" }) AssertLine(hub, label, 18, 28, width, differences);
                    foreach (var label in new[] { "Open a room", "Share the code", "Watch in sync" }) AssertLine(hub, label, 14, 20, width, differences);
                    var create = hub.FindName("CreatePartyPanel") as Border;
                    var join = hub.FindName("JoinPartyPanel") as Border;
                    if (create == null || join == null || create.CornerRadius.TopLeft != 20 || join.CornerRadius.TopLeft != 20
                        || Math.Abs(create.Background.Opacity - .90) > .01 || Math.Abs(join.Background.Opacity - .72) > .01)
                        differences.Add($"{width}: hub actual card corners and theme-derived panel/subtle opacity must match measured original20/.90/.72.");
                    var glow = hub.FindName("CreatePartyGlow") as Image;
                    Program.Log($"TRACE: glow before explicit render {width}: element={glow?.ActualWidth}x{glow?.ActualHeight}; panel={create?.ActualWidth}x{create?.ActualHeight}; source={glow?.Source?.GetType().Name ?? "null"}.");
                    try { await ((Task)Call(hub, "RenderCreatePartyGlowAsync")!).WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (Exception error) { differences.Add($"{width}: actual glow render failed: {error.GetBaseException().GetType().Name}: {error.GetBaseException().Message}"); }
                    Program.Log($"TRACE: glow after awaited render {width}: element={glow?.ActualWidth}x{glow?.ActualHeight}; source={glow?.Source?.GetType().Name ?? "null"}; bitmap={(glow?.Source as Microsoft.UI.Xaml.Media.Imaging.BitmapImage)?.PixelWidth}x{(glow?.Source as Microsoft.UI.Xaml.Media.Imaging.BitmapImage)?.PixelHeight}.");
                    if (glow?.Source is not Microsoft.UI.Xaml.Media.Imaging.BitmapImage)
                        differences.Add($"{width}: original primary/20 blur64 decoration must be painted by a nonempty theme-derived raster.");
                    if (glow?.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage)
                    {
                        hub.UpdateLayout();
                        var raster = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                        await raster.RenderAsync(glow);
                        // The first offscreen window can finish layout before the
                        // compositor exposes a surface. Wait only for that surface;
                        // a sized but transparent render remains an actual failure.
                        for (var attempt = 0; attempt < 10 && (raster.PixelWidth == 0 || raster.PixelHeight == 0); attempt++)
                        {
                            await Task.Delay(100);
                            await raster.RenderAsync(glow);
                        }
                        var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await raster.GetPixelsAsync());
                        var painted = 0; for (var index = 3; index < pixels.Length; index += 4) if (pixels[index] > 0) painted++;
                        Program.Log($"TRACE: actual glow painted alpha pixels {width}: {raster.PixelWidth}x{raster.PixelHeight}; nonzero={painted}.");
                        if (painted == 0) differences.Add($"{width}: decoded decoration must paint actual theme-derived nonempty pixels.");
                    }
                    foreach (var name in new[] { "HostPickButton", "VoteButton" })
                    {
                        var button = (Button)hub.FindName(name);
                        var caption = Descendants(button).OfType<TextBlock>().Single(text => text.Text is "You choose, everyone watches." or "Anyone suggests, the room votes.");
                        AssertLine(hub, name == "HostPickButton" ? "Host picks" : "Everyone votes", 14, 20, width, differences);
                        AssertLine(hub, caption.Text, 12, 16, width, differences);
                        if (button.CornerRadius.TopLeft != 16) differences.Add($"{width}/{name}: actual mode corner must match original16px.");
                        var origin = caption.TransformToVisual(button).TransformPoint(new Windows.Foundation.Point());
                        Program.Log($"TRACE: hub {width}/{name} button={button.ActualWidth} caption={caption.ActualWidth} at={origin.X} height={caption.ActualHeight}.");
                        if (origin.X + caption.ActualWidth > button.ActualWidth - button.Padding.Right + 2)
                            differences.Add($"{width}/{name}: actual mode caption overflows its button instead of wrapping inside the icon/copy columns.");
                    }
                    await MediaParityNativeFixture.CaptureAsync(owner, $"media-party-presentation-hub-{width}.png");
                    owner.Children.Remove(hub); await Task.Delay(100);
                    var room = new WatchTogetherRoomPage { Width = width, Height = 720 };
                    try
                    {
                        room.ViewModel.RoomId = "fixture-room"; room.ViewModel.RoomToken = "fixture-proof";
                        room.ViewModel.GetType().GetProperty("Capabilities")!.SetValue(room.ViewModel, new WatchTogetherCapabilities
                        { State = "available", Allowed = true, Picker = true, MemberState = true, StagedSelection = true, LobbyReady = true, SelectionModeSwitch = true });
                        room.ViewModel.Room = new WatchTogetherRoomSnapshot
                        { RoomId = "fixture-room", Code = "ABC123", Phase = "lobby", SelectionMode = "host_pick", SelectedContentId = "fixture-staged", SelfCanManageRoom = true, MemberCount = 2,
                          Members = [new() { UserId = 1, ProfileId = "host", DisplayName = "Alex Host", IsHost = true, IsSelf = true, Connected = true },
                                     new() { UserId = 2, ProfileId = "guest", DisplayName = "Riley Guest", Connected = true, LobbyReady = true }] };
                        owner.Children.Add(room); Call(room, "UpdateRoomUi"); await Task.Delay(100);
                        await (Task)Call(room, "RunHostSearchAsync")!; room.UpdateLayout(); await Task.Delay(100);
                        if (((TextBlock)room.FindName("SearchEmptyText")).Text != "Nothing to browse yet. Search for something above.")
                            differences.Add($"{width}: actual completed empty All-shelf requests must show the original browse empty state.");
                        Call(room, "UpdateRoomUi");
                        // UpdateMembers replaces the actual tab/member subtree. Assert the
                        // mounted replacement after layout, rather than its old visual tree.
                        room.UpdateLayout(); await Task.Delay(100);
                        if (((TextBlock)room.FindName("SearchEmptyText")).Text != "Nothing to browse yet. Search for something above.")
                            differences.Add($"{width}: a subsequent real room repaint must retain the completed browse result.");
                        foreach (var (name, font, line) in new[] { ("StagedEyebrow", 11d, 16.5), ("StagedTitle", width < 640 ? 20d : 24d, width < 640 ? 28d : 32d), ("ReadySummary", 14d, 20d), ("ReadyCheckSummary", 12d, 16d), ("SearchEmptyText", 14d, 20d) })
                        {
                            var text = (TextBlock)room.FindName(name);
                            AssertLine(text, font, line, width, differences);
                        }
                        AssertLine(room, "Guest ready check", 14, 20, width, differences);
                        AssertLine(room, "Change", 14, 20, width, differences);
                        if (Math.Abs(((Button)room.FindName("ChangeStagedButton")).ActualHeight - 36) > 1)
                            differences.Add($"{width}: actual staged Change button must match original36px.");
                        AssertLine((DependencyObject)room.FindName("ReadyCheckMembers"), "Riley Guest", 14, 20, width, differences);
                        AssertLine(room, "Guests can pause", 14, 20, width, differences);
                        var inviteGrid = (Grid)room.FindName("StageInviteGrid");
                        var policyRow = (FrameworkElement)room.FindName("GuestPausePanel");
                        var nativeToggle = (ToggleSwitch)room.FindName("GuestPauseToggle");
                        var input = new Microsoft.UI.Xaml.Automation.Peers.ToggleSwitchAutomationPeer(nativeToggle);
                        if (Math.Abs(policyRow.ActualHeight - 20) > 1 || inviteGrid.RowSpacing != (width < 600 ? 12 : 0) || input.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle) == null)
                            differences.Add($"{width}: source20px policy layout row/12px row gap must retain real native toggle input and UIA.");
                        var invitePanel = room.FindName("StageInvitePanel") as Border;
                        if (width >= 600 && (invitePanel == null || Math.Abs(invitePanel.ActualHeight - 58) > 2))
                            differences.Add($"{width}: actual wide invite panel must be source58px without an unused second-row12px gap.");
                        if (((Button)room.FindName("StageCodeButton")).Content is not StackPanel copyRow || copyRow.Children.FirstOrDefault() is TextBlock)
                            differences.Add($"{width}: actual copy glyph must precede Code as the source control does.");
                        var people = (Button)room.FindName("PeopleTabButton");
                        var countBadge = Descendants(people).OfType<Border>().FirstOrDefault(border => border.Name == "PeopleCountBadge");
                        if (width >= 768 && (countBadge?.Child is not TextBlock count || count.Text != "2" || count.FontSize != 10))
                            differences.Add($"{width}: actual People tab must render the real member count in its small capsule.");
                        var memberRail = (StackPanel)room.FindName("MembersWrapPanel");
                        var readyRow = memberRail.Children.OfType<Grid>().Single(row => Descendants(row).OfType<TextBlock>().Any(text => text.Text == "Riley Guest"));
                        if (!Descendants(readyRow).OfType<Microsoft.UI.Xaml.Shapes.Path>().Any(path => path.Stroke != null))
                            differences.Add($"{width}: the actual lobby-ready People row must retain the source check glyph.");
                        foreach (var part in Descendants(nativeToggle).OfType<FrameworkElement>().Where(element => element.Name.Contains("Switch", StringComparison.Ordinal) || element.Name.Contains("Outer", StringComparison.Ordinal)))
                            Program.Log($"TRACE: actual policy input {width}: {part.Name}={part.ActualWidth}x{part.ActualHeight} margin={part.Margin}.");
                        var empty = (Grid)room.FindName("SearchEmptyState");
                        if (Math.Abs(empty.ActualHeight - 84) > 2) differences.Add($"{width}: actual completed empty browse paragraph must occupy source84px line20+verticalpadding64.");
                        if (((SiloPlayer.Controls.WrapPanel)room.FindName("ShelfChipsPanel")).Children.OfType<Button>().Any(button => Math.Abs(button.ActualHeight - 24) > 1))
                            differences.Add($"{width}: rendered filter chips must match source24px.");
                        foreach (var name in new[] { "ReadyCheckPanel" })
                        {
                            var panel = (Border)room.FindName(name);
                            if (panel.CornerRadius.TopLeft != 16 || Math.Abs(panel.Background.Opacity - .72) > .01 || Math.Abs(panel.BorderBrush.Opacity - .62) > .01)
                                differences.Add($"{width}: actual subtle stage panel must match source16px/.72/.62 roles.");
                        }
                        if (width < 768)
                        {
                            var trigger = (Button)room.FindName("RailSheetButton");
                            var text = Descendants(trigger).OfType<TextBlock>().Select(text => text.Text).ToArray();
                            var latest = ((StackPanel)room.FindName("ActivityList")).Children.OfType<TextBlock>().FirstOrDefault()?.Text;
                            var summary = latest == null ? "Activity" : latest[(latest.IndexOf(" · ", StringComparison.Ordinal) + 3)..];
                            var origin = trigger.TransformToVisual(room).TransformPoint(new Windows.Foundation.Point());
                            if (trigger.ActualWidth < width - 34 || Math.Abs(origin.X - 16) > 2 || !text.Contains("People · 2") || !text.Contains(summary))
                                differences.Add($"{width}: compact People/activity trigger must be the full-width bottom strip with actual member count and activity summary.");
                            var code = room.FindName("StageCodeButton") as Button ?? Descendants(room).OfType<Button>().Single(button => button.Content as string == "Code");
                            var invite = Descendants(room).OfType<TextBlock>().Single(text => text.Text == "INVITE");
                            var codeAt = code.TransformToVisual(room).TransformPoint(new Windows.Foundation.Point());
                            var inviteAt = invite.TransformToVisual(room).TransformPoint(new Windows.Foundation.Point());
                            var inviteInfo = (FrameworkElement)room.FindName("StageInviteInfo");
                            var policy = (FrameworkElement)room.FindName("GuestPausePanel");
                            var policyAt = policy.TransformToVisual(room).TransformPoint(new Windows.Foundation.Point());
                            Program.Log($"TRACE: compact invite {width}: code={code.ActualWidth}x{code.ActualHeight}@{codeAt.X},{codeAt.Y}; invite={invite.ActualWidth}x{invite.ActualHeight}@{inviteAt.X},{inviteAt.Y}; wrap={inviteInfo.ActualWidth}x{inviteInfo.ActualHeight}; policy={policy.ActualWidth}x{policy.ActualHeight}@{policyAt.X},{policyAt.Y}.");
                            var preferred = ((SiloPlayer.Controls.WrapPanel)room.FindName("StageInviteInfo")).Children.OfType<FrameworkElement>().Sum(child => child.DesiredSize.Width) + 24;
                            var inner = invitePanel!.ActualWidth - invitePanel.Padding.Left - invitePanel.Padding.Right - invitePanel.BorderThickness.Left - invitePanel.BorderThickness.Right;
                            Program.Log($"TRACE: actual invite fit {width}: preferredInfo={preferred}; policy={policy.DesiredSize.Width}; interior={inner}; policyRow={Grid.GetRow(policy)}.");
                            var fits = preferred + policy.DesiredSize.Width + inviteGrid.ColumnSpacing <= inner + .5;
                            if (fits ? Grid.GetRow(policy) != 0 || Math.Abs(policyAt.Y + policy.ActualHeight / 2 - codeAt.Y - code.ActualHeight / 2) > 2
                                : Grid.GetRow(policy) != 1 || policyAt.Y < codeAt.Y + code.ActualHeight + 10)
                                differences.Add($"{width}: actual Invite policy must stay on the Code row when preferred content fits and wrap only when it overflows.");
                            if (Math.Abs(codeAt.Y + code.ActualHeight / 2 - inviteAt.Y - invite.ActualHeight / 2) > 2)
                                differences.Add($"{width}: Code must stay beside invite text while only guest policy wraps to the next row.");
                        }
                        foreach (var state in new[] { "connected", "connecting", "disconnected" })
                        {
                            room.ViewModel.GetType().GetProperty("ConnectionState")!.SetValue(room.ViewModel, state);
                            Call(room, "UpdateConnectionUi");
                            var dot = (Microsoft.UI.Xaml.Shapes.Ellipse)room.FindName("StatusDot"); var color = ((SolidColorBrush)dot.Fill).Color;
                            var label = ((TextBlock)room.FindName("StatusText")).Text;
                            if (state == "connecting" && label != "Connecting…" || state == "disconnected" && color.R <= color.G + 50)
                                differences.Add($"{width}/{state}: actual connection strip must use the shared original label and disconnected red state.");
                        }
                        room.ViewModel.GetType().GetProperty("ConnectionState")!.SetValue(room.ViewModel, "disconnected"); Call(room, "UpdateConnectionUi");
                        await MediaParityNativeFixture.CaptureAsync(owner, $"media-party-presentation-room-{width}.png");
                        room.ViewModel.RoomId = "fixture-next-room"; Call(room, "UpdateRoomUi");
                        if (((TextBlock)room.FindName("SearchEmptyText")).Text != "Find something for everyone to watch.")
                            differences.Add($"{width}: a different room must initialize its own browse prompt.");
                        room.ViewModel.RoomId = "fixture-room";
                        client.SetProfile("fixture-next-profile"); Call(room, "UpdateRoomUi");
                        if (((TextBlock)room.FindName("SearchEmptyText")).Text != "Find something for everyone to watch.")
                            differences.Add($"{width}: changed API authority must not retain the old browse result.");
                        client.SetProfile("fixture-host");
                    }
                    finally { room.ViewModel.Dispose(); owner.Children.Remove(room); await Task.Delay(100); }
                }
                finally { window.Close(); }
            }
            if (handler.CatalogRequests < 4 || handler.HomeRequests < 2 || handler.PickerRequests < 2)
                throw new InvalidOperationException("Party presentation did not exercise actual successful empty catalog/home/picker requests.");
            if (differences.Count > 0) throw new InvalidOperationException("Party presentation paired differences:\n" + string.Join("\n", differences));
            Program.Log("PASS: MEDIA_PARTY_PRESENTATION_COMPLETED actual mode bounds, loaded empty shelves, responsive invite and footer, connection states.");
        }
        finally { serviceField.SetValue(null, original); }
    }
    private static void AssertLine(DependencyObject root, string label, double font, double line, double width, List<string> differences)
    {
        var text = Descendants(root).OfType<TextBlock>().FirstOrDefault(value => value.Text == label && value.ActualWidth > 0 && value.ActualHeight > 0);
        if (text == null) { differences.Add($"{width}: missing actual text {label}."); return; }
        AssertLine(text, font, line, width, differences);
    }
    private static void AssertLine(TextBlock text, double font, double line, double width, List<string> differences)
    {
        var label = string.IsNullOrEmpty(text.Name) ? text.Text : text.Name;
        var rows = text.ActualHeight / line;
        Program.Log($"TRACE: actual Party line {width}/{label}: font={text.FontSize}, line={text.LineHeight}, box={text.ActualWidth}x{text.ActualHeight}, rows={rows}.");
        if (Math.Abs(text.FontSize - font) > .1 || Math.Abs(text.LineHeight - line) > .1 || text.ActualHeight < line - 1 || Math.Abs(rows - Math.Round(rows)) > .04)
            differences.Add($"{width}/{label}: actual rendered line geometry must use source{font}px/{line}px.");
    }
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        { var child = VisualTreeHelper.GetChild(root, index); yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
    private sealed class LocalServices(IServiceProvider original, SiloApiClient client) : IServiceProvider
    {
        private readonly PlaybackApi _api = new(client);
        public object? GetService(Type type) => type == typeof(PlaybackApi) ? _api : type == typeof(CatalogApi) ? new CatalogApi(client)
            : type == typeof(HomeApi) ? new HomeApi(client) : type == typeof(SiloApiClient) ? client
            : type == typeof(WatchTogetherRoomViewModel) ? new WatchTogetherRoomViewModel(_api, client) : original.GetService(type);
    }
    private sealed class EmptyHandler : HttpMessageHandler
    {
        internal int CatalogRequests, HomeRequests, PickerRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; object body;
            if (path.EndsWith("/catalog")) { CatalogRequests++; body = new { items = Array.Empty<object>() }; }
            else if (path.EndsWith("/home/layout")) { HomeRequests++; body = new { sections = Array.Empty<object>() }; }
            else if (path.EndsWith("/picker")) { PickerRequests++; body = new { members = Array.Empty<object>(), continue_together = Array.Empty<object>(), watchlist_union = Array.Empty<object>() }; }
            else if (path.EndsWith("/member-state")) body = new { members = Array.Empty<object>(), items = Array.Empty<object>() };
            else if (path.EndsWith("/catalog/items/fixture-staged")) body = new { content_id = "fixture-staged", type = "movie", title = "The Fixture Movie" };
            else throw new InvalidOperationException("Unexpected isolated party presentation path: " + path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });
        }
    }
}
