using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.MediaMaintenance;
using SiloPlayer.Services;

internal static class ConditionalDialogsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var wire = new PendingWire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://conditional-dialog-fixture.invalid");
        var outer = SiloPlayer.App.Services;
        using var services = new ServiceCollection().AddSingleton(new MediaMaintenanceApi(client)).AddSingleton(new ToastService()).BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        field.SetValue(null, services);
        try
        {
            var dialog = new MatchItemDialog("movie:1", "Fixture", 2020, "movie", null, null, null) { XamlRoot = parent.XamlRoot };
            var candidate = new MatchCandidate { Title = "First", ProviderIds = new() { ["tmdb"] = "1" } };
            typeof(MatchItemDialog).GetField("_selectedCandidate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, candidate);
            var apply = (Button)dialog.FindName("ApplyMatchButton");
            typeof(MatchItemDialog).GetMethod("ApplyMatch_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [apply, new RoutedEventArgs()]);
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var row = (Button)typeof(MatchItemDialog).GetMethod("BuildCandidateRow", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(dialog, [new MatchCandidate { Title = "Second", ProviderIds = new() { ["tmdb"] = "2" } }])!;
            var panel = (StackPanel)dialog.FindName("ResultsPanel"); panel.Children.Add(row);
            if (panel.Parent is ScrollViewer resultScroller) resultScroller.Content = null;
            else if (panel.Parent is Panel resultParent) resultParent.Children.Remove(panel);
            ((StackPanel)dialog.FindName("ResultsSection")).Children.Remove(panel);
            parent.Children.Add(panel);
            await Task.Delay(40);
            ((IInvokeProvider)new ButtonAutomationPeer(row).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(40); var enabledWhilePending = apply.IsEnabled;
            // A second activation must be refused independently of the visible button state.
            typeof(MatchItemDialog).GetMethod("ApplyMatch_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [apply, new RoutedEventArgs()]);
            await Task.Delay(40); var writes = wire.Writes;
            wire.Release.TrySetResult(); await Task.Delay(100); parent.Children.Remove(panel);
            if (enabledWhilePending || writes != 1) throw new InvalidOperationException($"Match apply pending gate failed: enabled={enabledWhilePending}; writes={writes}, expected disabled/1.");
            Program.Log("PASS: real MatchItemDialog keeps Apply disabled after changing selection and refuses a second pending write.");
        }
        finally { wire.Release.TrySetResult(); field.SetValue(null, outer); }
    }
    private sealed class PendingWire : HttpMessageHandler
    {
        public int Writes;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Writes++; Started.TrySetResult(); await Release.Task.WaitAsync(ct); return new(HttpStatusCode.NoContent); }
    }
}
