using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Views.Dialogs;

internal static class MediaToolsDialogsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!; var original = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = new SiloApiClient(http); client.SetBaseUrl("https://media-tools-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetCurrentUser(new() { Id = "fixture", Role = "admin" });
        slot.SetValue(null, new Services(original, client, auth));
        var window = new Window { Content = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] } }; var owner = (Grid)window.Content;
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000)); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1100, 900)); window.AppWindow.Show(false); await Task.Delay(120);
            await MarkersAsync(owner, wire);
            await SeekAsync(owner, wire);
            await DetectAsync(owner, wire);
            await SplitAsync(owner, wire);
            await LargeSplitAsync(owner, wire);
            using (var navigation = new CancellationTokenSource())
            {
                var abandoned = new MarkerEditorDialog("fixture") { XamlRoot = owner.XamlRoot }; abandoned.BindNavigation(navigation.Token); var abandonedShowing = abandoned.ShowAsync(); await Task.Delay(100); var writesBefore = wire.Writes.Count;
                navigation.Cancel(); await Until(() => abandonedShowing.Status != Windows.Foundation.AsyncStatus.Started); await abandonedShowing; Check(wire.Writes.Count == writesBefore, "Navigation cancellation writes or retains the old marker editor");
            }
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(460, 740)); await Task.Delay(100);
            foreach (var dialog in new ItemScopedDialog[] { new MarkerEditorDialog("fixture"), new SeekPreviewsDialog("fixture", new Dictionary<string, string>()), new RedetectMarkersDialog("fixture", true), new SplitVersionsDialog(new MediaItemDetail { ContentId = "fixture", Title = "Fixture movie", Type = "movie" }, 1) })
            {
                dialog.XamlRoot = owner.XamlRoot; var showing = dialog.ShowAsync(); await Task.Delay(150); dialog.UpdateLayout();
                try
                {
                    var background = Descendants<Border>(dialog).Single(border => border.Name == "BackgroundElement");
                    Program.Log("TRACE " + dialog.GetType().Name + " narrow=" + background.ActualWidth + "x" + background.ActualHeight);
                    Check(background.ActualWidth <= 428.5 && background.ActualHeight <= 676.5, dialog.GetType().Name + " escapes the narrow viewport margin");
                    if (dialog is SplitVersionsDialog) Check(background.ActualHeight <= 629.5, "Split shell exceeds the current85vh bound");
                    var close = Descendants<Button>(dialog).Single(button => button.Name == "CloseButton"); var top = close.TransformToVisual(background).TransformPoint(new Windows.Foundation.Point());
                    Check(top.Y >= 0 && top.Y + close.ActualHeight <= background.ActualHeight + 1, "Narrow media tool footer is clipped");
                    Invoke(close); await showing;
                }
                finally { dialog.Hide(); await showing; }
            }
            var revoked = new MarkerEditorDialog("fixture") { XamlRoot = owner.XamlRoot }; var revokedShowing = revoked.ShowAsync(); await Task.Delay(120); var count = wire.Writes.Count; auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
            Invoke(Descendants<Button>(revoked).Single(button => button.Name == "PrimaryButton")); await Task.Delay(100); Check(wire.Writes.Count == count, "Revoked marker permission wrote remotely"); revoked.Hide(); await revokedShowing;
            Program.Log("PASS: actual media tools load current contracts, gate pending commands, preview before split, invalidate stale plans, and retain visible narrow footers without live API writes.");
        }
        finally { slot.SetValue(null, original); window.Close(); }
    }
    private static async Task MarkersAsync(FrameworkElement owner, Wire wire)
    {
        var unchanged = new MarkerEditorDialog("fixture") { XamlRoot = owner.XamlRoot }; var showing = unchanged.ShowAsync(); await Task.Delay(130);
        Invoke(Descendants<Button>(unchanged).Single(button => button.Name == "PrimaryButton")); await Until(() => showing.Status != Windows.Foundation.AsyncStatus.Started); await showing;
        Check(wire.Writes.Count == 0, "Untouched marker dialog resaves detected markers");
        var dialog = new MarkerEditorDialog("fixture") { XamlRoot = owner.XamlRoot }; showing = dialog.ShowAsync(); await Task.Delay(130);
        try
        {
            var status = (TextBlock)typeof(ItemScopedDialog).GetField("Status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            Check(status.Text.Length == 0 && status.Visibility == Visibility.Collapsed, "A ready marker dialog still reserves an empty status row");
            Check(Descendants<TextBlock>(dialog).Any(text => text.Text.Contains(", ") && text.Text.Contains("Oct") && !text.Text.Contains("2026")), "Marker history omits the current short-date/time presentation");
            var surface = Descendants<Border>(dialog).Single(border => border.Name == "BackgroundElement");
            var cancel = Descendants<Button>(dialog).Single(button => button.Name == "CloseButton");
            var save = Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton");
            Check(cancel.TransformToVisual(surface).TransformPoint(new Windows.Foundation.Point()).X < save.TransformToVisual(surface).TransformPoint(new Windows.Foundation.Point()).X, "Marker footer must place Cancel before Save");
            var start = Descendants<TextBox>(dialog).First(input => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(input) == "Intro start");
            Check(start.Text == "1:30", "Marker times are not initialized from start_seconds"); start.Text = "1:31";
            wire.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); Invoke(Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton")); await Until(() => wire.Writes.Count == 1);
            Check(!start.IsEnabled && !Descendants<Button>(dialog).Single(button => button.Name == "CloseButton").IsEnabled, "Pending marker mutation leaves dismiss/edit controls enabled");
            var body = wire.Writes.Last().Body; Check(body.GetProperty("intro").GetProperty("start_seconds").GetDouble() == 91, "Edited marker did not use current contract"); Check(!body.TryGetProperty("credits", out _), "Unchanged absent marker is overwritten");
            wire.Gate.SetResult(); await Until(() => showing.Status != Windows.Foundation.AsyncStatus.Started); await showing; Check(dialog.HasSaved, "Saved marker response is not recorded");
        }
        finally { wire.Gate?.TrySetResult(); wire.Gate = null; dialog.Hide(); await showing; }
    }
    private static async Task SeekAsync(FrameworkElement owner, Wire wire)
    {
        wire.SeekOff = true; var dialog = new SeekPreviewsDialog("fixture", new Dictionary<string, string> { ["1"] = "Fixture.mkv" }) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync(); await Task.Delay(130);
        try { Check(!dialog.IsPrimaryButtonEnabled, "Off-library seek previews can regenerate"); Check(Descendants<TextBlock>(dialog).Any(text => text.Text == "Off for this library"), "Off seek state is omitted"); }
        finally { dialog.Hide(); await showing; wire.SeekOff = false; }
        dialog = new SeekPreviewsDialog("fixture", new Dictionary<string, string> { ["1"] = "Fixture.mkv" }) { XamlRoot = owner.XamlRoot }; showing = dialog.ShowAsync(); await Task.Delay(130);
        try { Check(dialog.IsPrimaryButtonEnabled, "Ready seek previews cannot regenerate"); Invoke(Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton")); await Until(() => wire.Writes.Any(write => write.Path.EndsWith("/trickplay/regenerate"))); Check(showing.Status == Windows.Foundation.AsyncStatus.Started, "Seek regeneration unexpectedly closes dialog"); }
        finally { dialog.Hide(); await showing; }
    }
    private static async Task DetectAsync(FrameworkElement owner, Wire wire)
    {
        var dialog = new RedetectMarkersDialog("fixture", true) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync(); await Task.Delay(130);
        try
        {
            Button Choice(string name) => Descendants<Button>(dialog).Single(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == name);
            Check(!Choice("Intro").IsEnabled && Choice("Credits").IsEnabled && !Choice("Intro and credits").IsEnabled, "Explicit off detection setting is ignored");
            Invoke(Choice("Credits")); await Until(() => showing.Status != Windows.Foundation.AsyncStatus.Started); await showing;
            Check(wire.Writes.Last().Body.GetProperty("kind").GetString() == "credits", "Detection kind changes during submission");
        }
        finally { dialog.Hide(); await showing; }
    }
    private static async Task SplitAsync(FrameworkElement owner, Wire wire)
    {
        var dialog = new SplitVersionsDialog(new MediaItemDetail { ContentId = "fixture", Title = "Fixture movie", Type = "movie" }, 1) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync(); await Task.Delay(130);
        try
        {
            var first = Descendants<CheckBox>(dialog).First(check => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(check) == "Select /folder/A.mkv"); first.IsChecked = true;
            var unmatched = Descendants<CheckBox>(dialog).Single(check => check.Content as string == "Detach as unmatched (identify later)"); unmatched.IsChecked = true;
            Check(!dialog.IsPrimaryButtonEnabled, "Split can commit before dry-run preview"); await Until(() => dialog.IsPrimaryButtonEnabled);
            var preview = wire.Writes.Last(); Check(preview.Body.GetProperty("dry_run").GetBoolean() && preview.Body.GetProperty("file_ids").GetArrayLength() == 1, "Split preview uses an invalid file set");
            first.IsChecked = false; Check(!dialog.IsPrimaryButtonEnabled, "Changing the split plan retains a valid old preview"); first.IsChecked = true; await Until(() => dialog.IsPrimaryButtonEnabled);
            var second = Descendants<CheckBox>(dialog).First(check => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(check) == "Select /folder/B.mkv"); second.IsChecked = true;
            Check(!dialog.IsPrimaryButtonEnabled && Descendants<TextBlock>(dialog).Any(text => text.Text.StartsWith("All files are selected")), "Selecting every file still allows split"); second.IsChecked = false; await Until(() => dialog.IsPrimaryButtonEnabled);
            Invoke(Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton")); await Until(() => showing.Status != Windows.Foundation.AsyncStatus.Started); await showing;
            Check(dialog.HasSaved && !wire.Writes.Last().Body.GetProperty("dry_run").GetBoolean(), "Split confirmation never commits the previewed plan");
        }
        finally { dialog.Hide(); await showing; }
    }
    private static async Task LargeSplitAsync(FrameworkElement owner, Wire wire)
    {
        wire.FileCount = 2500;
        var dialog = new SplitVersionsDialog(new MediaItemDetail { ContentId = "fixture", Title = "Long series", Type = "series" }, 1) { XamlRoot = owner.XamlRoot };
        var showing = dialog.ShowAsync();
        try
        {
            await Until(() => Descendants<CheckBox>(dialog).Any(check => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(check) == "Select /folder/0.mkv"));
            dialog.UpdateLayout();
            Check(Descendants<CheckBox>(dialog).Count() < 100, "A long series eagerly realizes thousands of split-file controls");
            var first = Descendants<CheckBox>(dialog).Single(check => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(check) == "Select /folder/0.mkv"); first.IsChecked = true;
            var scroller = Descendants<ScrollViewer>(dialog).Single(view => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(view) == "Files to move");
            scroller.ChangeView(null, scroller.ScrollableHeight, null, true); await Task.Delay(150); dialog.UpdateLayout();
            Check(Descendants<CheckBox>(dialog).Count() < 100, "Scrolling the split list retains offscreen controls");
            Check(Descendants<CheckBox>(dialog).Any(check => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(check) == "Select /folder/2499.mkv"), "The virtual split list cannot reach the final file");
            scroller.ChangeView(null, 0, null, true); await Task.Delay(150); dialog.UpdateLayout();
            Check(Descendants<CheckBox>(dialog).Single(check => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(check) == "Select /folder/0.mkv").IsChecked == true, "Recycling a file row loses its selection");
        }
        finally { dialog.Hide(); await showing; wire.FileCount = 2; }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 160; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Media tools did not settle"); }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject { if (node is T item) yield return item; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i))) yield return child; }
    private sealed class Services(IServiceProvider original, SiloApiClient client, AuthService auth) : IServiceProvider { public object? GetService(Type type) => type == typeof(SiloApiClient) ? client : type == typeof(AuthService) ? auth : original.GetService(type); }
    private record Write(string Path, JsonElement Body);
    private sealed class Wire : HttpMessageHandler
    {
        public List<Write> Writes = []; public TaskCompletionSource? Gate; public bool SeekOff; public int FileCount = 2;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; var isWrite = request.Method != HttpMethod.Get; JsonElement body = default;
            if (path.EndsWith("/files") && FileCount > 2)
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items = Enumerable.Range(0, FileCount).Select(index => new { id = index.ToString(), observed_root_path = "/folder", file_path = $"/folder/{index}.mkv" }), page = new { has_more = false } })) };
            if (isWrite) { body = JsonSerializer.Deserialize<JsonElement>(request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(ct)); Writes.Add(new(path, body)); if (Gate != null) await Gate.Task.WaitAsync(ct); }
            var json = path.EndsWith("/history") ? "{\"history\":[{\"created_at\":\"2026-10-09T16:04:00Z\",\"username\":\"Fixture\",\"segment\":\"intro\",\"action\":\"set\",\"before\":null,\"after\":{\"start_seconds\":90,\"end_seconds\":110}}]}" : path.EndsWith("/effective") ? "{\"markers.detect_intros\":\"false\",\"markers.detect_credits\":\"true\"}" : path.EndsWith("/trickplay") ? "{\"files\":[{\"file_id\":\"1\",\"state\":\"" + (SeekOff ? "off" : "ready") + "\",\"thumbnail_count\":100,\"thumbnail_width\":320,\"interval_ms\":10000}]}" : path.EndsWith("/regenerate") ? "{\"queued\":1}" : path.EndsWith("/files") ? "{\"items\":[{\"id\":\"1\",\"observed_root_path\":\"/folder\",\"file_path\":\"/folder/A.mkv\"},{\"id\":\"2\",\"observed_root_path\":\"/folder\",\"file_path\":\"/folder/B.mkv\"}],\"page\":{\"has_more\":false}}" : path.EndsWith("/split") ? JsonSerializer.Serialize(new { dry_run = body.GetProperty("dry_run").GetBoolean(), files_moved = 1, target_content_id = "fixture-target", target_created = true, reattribution = new { progress_moved = 1, history_moved = 2, history_ambiguous = 0, downloads = 1 } }) : path.Contains("/markers/items/") ? "{\"intro\":{\"start_seconds\":90.4,\"end_seconds\":110.5}}" : "{}";
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
