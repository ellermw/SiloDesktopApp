using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static class ConnectionQualityOct24NativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://connection-native-fixture.invalid"); client.SetProfile("fixture");
        using var auth = new AuthService(client, new AuthApi(client));
        var settings = new SettingsApi(client); var api = new PlaybackApi(client); var catalog = new CatalogApi(client);
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "connection-settings"));
        var theme = new ThemeService(local, settings);
        var vm = new SettingsViewModel(settings, catalog, new AuthApi(client), new HistoryImportApi(client), new WatchProvidersApi(client),
            auth, theme, local, new AccessibilityService(local, theme));
        using var player = new PlayerService(api, catalog, auth, client, local, settings);
        var observer=PlaybackConnectionService.For(api);
        observer.SetMediaActive(true); player.SetState(PlayerState.Idle);
        try { await observer.TestAsync("fixture-idle-check",false,"ethernet"); throw new InvalidOperationException("Fixture probe unexpectedly measured bytes."); }
        catch(NotSupportedException) { /* Reached capability validation: failed initial playback released its activity hold. */ }
        var remoteClient=new SiloApiClient(http);remoteClient.SetBaseUrl("https://connection-native-fixture.invalid");remoteClient.SetProfile("receiver");
        using var remoteAuth=new AuthService(remoteClient,new AuthApi(remoteClient));var remoteScope=new PlaybackAuthorityScope(remoteClient,remoteAuth);
        var authorityField=typeof(PlayerService).GetField("_activeAuthority",BindingFlags.Instance|BindingFlags.NonPublic)!;var localAuthority=authorityField.GetValue(player);
        authorityField.SetValue(player,remoteScope);
        typeof(PlayerService).GetMethod("SetConnectionMediaActive",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(player,[true]);
        foreach(var busyObserver in new[]{observer,PlaybackConnectionService.For(remoteScope.PlaybackApi)})
        {
            try{await busyObserver.TestAsync("fixture-busy",false,"ethernet");throw new InvalidOperationException("Receiver playback did not block a physical connection probe.");}
            catch(InvalidOperationException ex)when(ex.Message.StartsWith("Stop playback",StringComparison.Ordinal)){}
        }
        typeof(PlayerService).GetMethod("EndBufferingQuality",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(player,null);
        authorityField.SetValue(player,localAuthority);
        var provider = new Services(original, api, vm, player); slot.SetValue(null, provider);
        var control = new PlaybackConnectionSettingsControl { Width = 800 };
        parent.Children.Add(control);
        try
        {
            await Task.Delay(200);
            Program.Log($"TRACE connection load: loaded={control.IsLoaded}, busy={vm.ConnectionSettingsBusy}, supported={vm.BufferingQualitySupported}, message={vm.ConnectionSettingsMessage}, routes={string.Join(",",wire.Paths)}");
            await WaitAsync(() => vm.BufferingQualitySupported && !vm.ConnectionSettingsBusy);
            Check(vm.BufferingQualityMode == "always" && vm.BufferingDeviceOverride, "Resolved profile-device buffering preference missing.");
            Check(vm.ConnectionReading.Contains("18") && vm.LearnedConnectionLimit.Contains("5.6"), "Actual connection and learned-limit labels missing.");
            var combo = All<ComboBox>(control).Single();
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "ask"));
            vm.StatusMessage = "Unrelated settings update";
            Check(Equals(((ComboBoxItem)combo.SelectedItem).Tag,"ask"),"Unrelated settings updates discarded the buffering choice.");
            await WaitAsync(() => wire.Writes == 1 && !vm.ConnectionSettingsBusy && vm.BufferingQualityMode == "ask");
            Check(wire.LastScope == "profile" && wire.DeviceCleared, "Profile default did not clear the shadowing native device scope.");
            Invoke(All<Button>(control).Single(b => Equals(b.Content, "Reset")));
            await WaitAsync(() => wire.Resets == 1 && !vm.ConnectionSettingsBusy && !vm.CanResetLearnedLimit);
            foreach (var width in new[] { 800d, 360d })
            {
                control.Width = width; control.UpdateLayout(); await Task.Delay(80);
                foreach(var child in All<Button>(control).Where(b=>b.ActualWidth>0 && Visible(b)))
                { var bounds=child.TransformToVisual(control).TransformBounds(new Windows.Foundation.Rect(0,0,child.ActualWidth,child.ActualHeight)); Check(bounds.X>=-1&&bounds.Right<=control.ActualWidth+1,"A connection action overflows the native settings width."); }
                await MediaParityNativeFixture.CaptureAsync(control, $"oct24-connection-settings-{width:0}.png");
                Check(All<TextBlock>(control).Any(t => t.Text == "Connection"), "Connection heading missing after responsive layout.");
            }
            var controller = new BufferingQualityController("always");
            controller.Adopt(new("p", "a", "1080p", "1080p", 8000, [new() { Label = "720p", BitrateKbps = 4000 }]), 0);
            controller.Observe(new(0,false,false,false,false,false,0,null));
            controller.Observe(new(11000,true,false,false,false,false,0,2000));
            controller.Observe(new(18000,true,false,false,false,false,0,2000));
            typeof(PlayerService).GetField("_bufferingController", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, controller);
            typeof(PlayerService).GetField("<State>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, PlayerState.Minimized);
            var pill = new BufferingQualityPillControl { Width = 360 }; parent.Children.Add(pill);
            await Task.Delay(80); pill.UpdateLayout();
            Check(pill.Visibility == Visibility.Visible, "Explicit quality consent pill missing in minimized playback.");
            await MediaParityNativeFixture.CaptureAsync(pill, "oct24-buffering-prompt-minimized.png");
            Invoke(All<Button>(pill).Single(b => Equals(b.Content, "Keep quality")));
            Check(controller.Pill == null && controller.Summary.BufferingAction == "kept", "Keep quality failed to suppress lowering.");
            parent.Children.Remove(pill);
            Program.Log("PASS: native connection settings resolve device scope, immediately save profile default and clear device override, show measured reading/learned limit, reset via current API, render at360/800, and fixed-quality minimized consent Keep action works. No physical network benchmark or historic buffering repair claimed.");
        }
        finally { parent.Children.Remove(control); slot.SetValue(null, original); }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task WaitAsync(Func<bool> condition) { for (var i=0;i<100 && !condition();i++) await Task.Delay(20); Check(condition(), "Timed out waiting for native connection state."); }
    private static void Check(bool value,string detail) { if (!value) throw new InvalidOperationException(detail); }
    private static bool Visible(DependencyObject node)
    { for (DependencyObject? current=node;current!=null;current=VisualTreeHelper.GetParent(current)) if(current is UIElement { Visibility: Visibility.Collapsed }) return false; return true; }
    private static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject
    { if(root is T t) yield return t; for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i))) yield return child; }
    private sealed class Services(IServiceProvider original, PlaybackApi api, SettingsViewModel vm, PlayerService player) : IServiceProvider
    { public object? GetService(Type t) => t == typeof(PlaybackApi) ? api : t == typeof(SettingsViewModel) ? vm : t == typeof(PlayerService) ? player : original.GetService(t); }
    private sealed class Wire:HttpMessageHandler
    {
        internal int Writes,Resets; internal string Mode="always",LastScope="";
        internal bool DeviceCleared;
        internal List<string> Paths = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "connection-native-fixture.invalid", "Unexpected network origin.");
            var path=request.RequestUri.AbsolutePath;
            Paths.Add(path);
            if(request.Method == HttpMethod.Delete)
            { if(path.Contains("/settings/values/playback.buffering_quality_mode")) { DeviceCleared = true; return new(HttpStatusCode.NoContent); } Check(path=="/api/v2/playback/connection/learned-limit","Unexpected fixture delete."); Resets++; return new(HttpStatusCode.NoContent); }
            if(request.Method == HttpMethod.Put)
            { Check(path.Contains("/settings/values/playback.buffering_quality_mode"),"Unexpected fixture write."); using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Mode=body.RootElement.GetProperty("value").GetString()!; LastScope=request.RequestUri.Query.Contains("scope=profile_device")?"profile_device":"profile"; Writes++; }
            var json=path switch
            {
                "/api/v2/playback/capabilities" => """{"features":["buffering_quality_v1","connection_speed_v1"],"bandwidth_probe_automatic":false}""",
                "/api/v2/settings/values/effective" => "{\"items\":[{\"key\":\"playback.buffering_quality_mode\",\"value\":\""+Mode+"\",\"source\":\"profile_device\"}]}",
                "/api/v2/playback/connection" => JsonSerializer.Serialize(new {connection="local",probe=new { kbps=18000,lower_bound=false,measured_at=DateTimeOffset.UtcNow,transport="ethernet" },playback_sample=new{kbps=40000,measured_at=DateTimeOffset.UtcNow.AddHours(-1)},learned_limit=Resets>0?null:(object)new {kbps=5600,observed_at=DateTimeOffset.UtcNow,expires_at=DateTimeOffset.UtcNow.AddHours(24)}}),
                _ when request.Method==HttpMethod.Put => "{}",
                _ => throw new InvalidOperationException("Unexpected native fixture route: "+path),
            };
            return new(HttpStatusCode.OK) { Content=new StringContent(json,Encoding.UTF8,"application/json") };
        }
    }
}
