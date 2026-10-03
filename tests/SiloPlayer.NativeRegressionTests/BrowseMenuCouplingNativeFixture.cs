using System.Net;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using SiloPlayer.Views.Dialogs;

internal static class BrowseMenuCouplingNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = servicesField.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://browse-menu-coupling.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user", Permissions = [AuthorizationPolicy.MetadataCuration] });
        auth.SelectProfile("coupling", profile: new() { Id = "coupling", Name = "Fixture", IsPrimary = true });
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth)
            .AddSingleton(new CatalogApi(client)).AddSingleton(new MediaMaintenanceApi(client)).AddSingleton(new ToastService()).BuildServiceProvider();
        var signals = new Signals();
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(signals, static (receiver, message) =>
        {
            if (message.ContentId == "menu-one" && message.Kind == MediaSurfaceChangeKind.ItemMetadataRefreshed) ((Signals)receiver).Count++;
        });
        var anchor = new Button { Content = "Owned menu", Width = 180 };
        ContentDialog? active = null; MenuFlyout? menu = null; var callbacks = 0;
        servicesField.SetValue(null, services); parent.Children.Add(anchor);
        try
        {
            await Task.Delay(100);
            async Task<T> Open<T>(string caption) where T : ContentDialog
            {
                var item = new MediaItem { ContentId = "menu-one", Type = "movie", Title = "Menu movie", UserState = new() };
                var build = typeof(MediaItemMenu).GetMethod(nameof(MediaItemMenu.Build))!;
                Action changed = () => callbacks++;
                menu = build.GetParameters().Length == 5
                    ? (MenuFlyout)build.Invoke(null, [item, MediaItemMenu.Surface.Default, true, changed, anchor])!
                    : MediaItemMenu.Build(item, stateChanged: changed);
                menu.ShowAt(anchor); await Task.Delay(140);
                var action = menu.Items.OfType<MenuFlyoutItem>().Single(row => row.Text == caption);
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(action).GetPattern(PatternInterface.Invoke)).Invoke();
                menu.Hide();
                for (var attempt = 0; attempt < 80; attempt++)
                {
                    active = Dialogs(parent.XamlRoot).OfType<T>().FirstOrDefault();
                    if (active != null) { await Task.Delay(120); return (T)active; }
                    await Task.Delay(25);
                }
                throw new InvalidOperationException($"Actual {caption} menu invocation did not open its dialog on the mounted owner root; detail reads={wire.DetailReads}, history reads={wire.HistoryReads}.");
            }
            var canceled = await Open<EditMetadataDialog>("Edit Metadata");
            if (canceled.XamlRoot != anchor.XamlRoot || wire.DetailReads != 1) throw new InvalidOperationException("Metadata menu lost mounted ownership or detail fetch.");
            Invoke(canceled, "CloseButton"); await Until(() => !Dialogs(parent.XamlRoot).Contains(canceled)); active = null;
            if (callbacks != 0 || signals.Count != 0 || wire.Writes != 0) throw new InvalidOperationException("Canceled metadata menu refreshed or mutated media.");
            var editor = await Open<EditMetadataDialog>("Edit Metadata");
            var fields = (Dictionary<string, TextBox>)typeof(EditMetadataDialog).GetField("_textInputs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
            fields["title"].Text = "Coupled saved title";
            wire.FailSave = true; Invoke(editor, "PrimaryButton"); await Until(() => wire.Writes == 1 && editor.IsPrimaryButtonEnabled);
            if (editor.HasSaved || callbacks != 0 || signals.Count != 0 || fields["title"].Text != "Coupled saved title") throw new InvalidOperationException("Failed metadata save lost its draft or published a refresh.");
            wire.FailSave = false; Invoke(editor, "PrimaryButton"); await Until(() => callbacks == 1); active = null;
            if (!editor.HasSaved || wire.Writes != 2 || wire.LastTitle != "Coupled saved title" || signals.Count != 1) throw new InvalidOperationException($"Metadata menu Save must publish once and invoke its saved callback once; callbacks={callbacks}, broadcasts={signals.Count}, writes={wire.Writes}.");
            auth.SetCurrentUser(new() { Id = "fixture", Role = "admin" });
            var history = await Open<PlayHistoryDialog>("Play History"); await Until(() => wire.HistoryReads == 1);
            if (history.XamlRoot != anchor.XamlRoot || !wire.HistoryQuery.Contains("media_item_id=menu-one")) throw new InvalidOperationException("Admin menu history lost its owner root or server item filter.");
            Invoke(history, "CloseButton"); await Until(() => !Dialogs(parent.XamlRoot).Contains(history)); active = null;
            Program.Log("PASS: mounted menu→metadata Cancel/failure/retry/HasSaved/single-broadcast and acting-admin item-filtered history dialog.");
        }
        finally { active?.Hide(); menu?.Hide(); WeakReferenceMessenger.Default.UnregisterAll(signals); parent.Children.Remove(anchor); servicesField.SetValue(null, previous); }
    }
    private sealed class Signals { internal int Count; }
    private static void Invoke(ContentDialog dialog, string name) => ((IInvokeProvider)new ButtonAutomationPeer(Descendants<Button>(dialog).Single(button => button.Name == name)).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var attempt = 0; attempt < 100 && !ready(); attempt++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Menu coupling did not settle."); }
    private static IEnumerable<ContentDialog> Dialogs(XamlRoot root)
    {
        var found = new HashSet<ContentDialog>();
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            foreach (var dialog in new[] { popup.Child }.Concat(Descendants<DependencyObject>(popup.Child)).OfType<ContentDialog>()) found.Add(dialog);
            for (DependencyObject? ancestor = popup.Child; ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor)) if (ancestor is ContentDialog dialog) found.Add(dialog);
        }
        return found;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T typed) yield return typed; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private sealed class Wire : HttpMessageHandler
    {
        internal int DetailReads, Writes, HistoryReads; internal bool FailSave; internal string LastTitle = "Menu movie", HistoryQuery = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "browse-menu-coupling.invalid") throw new InvalidOperationException("Menu coupling attempted external network.");
            var path = request.RequestUri.AbsolutePath; var status = HttpStatusCode.OK;
            object body = new { items = Array.Empty<object>(), page = new { has_more = false } };
            if (path == "/api/v2/catalog/items/menu-one") { DetailReads++; body = Detail(); }
            if (path == "/api/v2/capabilities/metadata-ai") body = new { enabled = false };
            if (path == "/api/v2/admin/items/menu-one/metadata")
            {
                Writes++; using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); LastTitle = json.RootElement.GetProperty("title").GetString()!;
                if (FailSave) { status = HttpStatusCode.ServiceUnavailable; body = new { message = "fixture save rejected" }; } else body = Detail();
            }
            if (path == "/api/v2/admin/playback-history") { HistoryReads++; HistoryQuery = request.RequestUri.Query; }
            return new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        }
        private object Detail() => new { content_id = "menu-one", type = "movie", title = LastTitle, genres = Array.Empty<string>(), studios = Array.Empty<string>(), countries = Array.Empty<string>() };
    }
}
