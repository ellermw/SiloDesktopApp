using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class PartyPasteNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var property = typeof(WatchTogetherJoinPage).GetProperty("ClipboardTextReader", BindingFlags.NonPublic | BindingFlags.Instance);
        if (property == null) throw new InvalidOperationException("Paste cannot safely exercise clipboard read rejection: no injectable text-reader seam. The fixture must never access the OS clipboard.");
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)servicesField.GetValue(null)!;
        using var http = new HttpClient(new RejectNetwork()); var client = new SiloApiClient(http); client.SetBaseUrl("https://party-paste.invalid");
        var vm = new WatchTogetherJoinViewModel(new PlaybackApi(client)); servicesField.SetValue(null, new Services(original, vm));
        var page = new WatchTogetherJoinPage { Width = 500, Height = 720 }; parent.Children.Add(page); page.UpdateLayout(); await Task.Delay(100);
        try
        {
            var button = Descendants(page).OfType<Button>().Single(candidate => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(candidate) == "Paste room code");
            property.SetValue(page, (Func<Task<string?>>)(() => Task.FromException<string?>(new InvalidOperationException("Fixture clipboard unavailable"))));
            vm.RoomCode = "KEEP42"; Click(button); await UntilAsync(() => vm.ErrorMessage == "Could not paste room code.");
            if (vm.RoomCode != "KEEP42") throw new InvalidOperationException("Failed actual Paste discards the entered code.");
            property.SetValue(page, (Func<Task<string?>>)(() => Task.FromResult<string?>(null)));
            Click(button); await UntilAsync(() => vm.ErrorMessage == "Clipboard contains no room code.");
            property.SetValue(page, (Func<Task<string?>>)(() => Task.FromResult<string?>(" ab-c 123 ")));
            Click(button); await UntilAsync(() => vm.RoomCode == "ABC123");
            if (!vm.CanJoin || vm.ErrorMessage != null) throw new InvalidOperationException("Actual Paste recovery does not restore a joinable normalized code and clear stale failure feedback.");
            await MediaParityNativeFixture.CaptureAsync(page, "media-party-paste-recovered.png");
            Program.Log("PASS: PARTY_PASTE_COMPLETED actual Paste read rejection/no-text feedback/entered-code preservation/recovery, entirely injected without OS clipboard access.");
        }
        finally { parent.Children.Remove(page); await Task.Delay(100); servicesField.SetValue(null, original); }
    }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private static async Task UntilAsync(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Paste did not settle."); }
    private sealed class Services(IServiceProvider original, WatchTogetherJoinViewModel vm) : IServiceProvider { public object? GetService(Type type) => type == typeof(WatchTogetherJoinViewModel) ? vm : original.GetService(type); }
    private sealed class RejectNetwork : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("Paste code fixture must not send a network request."); }
}
