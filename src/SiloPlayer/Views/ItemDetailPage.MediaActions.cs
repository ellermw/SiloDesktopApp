using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Views.Dialogs;
using System.Text.Json;

namespace SiloPlayer.Views;

public sealed partial class ItemDetailPage
{
    private string? _mediaActionCapabilitiesId;
    private bool _offerSeekPreviews, _offerMarkerDetection, _honorMarkerSettings;

    private async Task LoadMediaActionCapabilitiesAsync(MediaItemDetail item, CancellationToken ct)
    {
        var auth = App.Services.GetRequiredService<AuthService>();
        if (item.Type is not ("movie" or "series" or "episode") || !AuthorizationPolicy.IsActingAdmin(auth)) return;
        var client = App.Services.GetRequiredService<SiloApiClient>(); var context = client.CaptureContext();
        async Task<JsonElement> Read(string path)
        {
            try { return await client.SendRequestAsync<JsonElement>(context, HttpMethod.Get, path, null, ct); }
            catch (OperationCanceledException) { return default; }
            catch { return default; } // Optional advertised features fail closed.
        }
        var libraryTask = Read("/api/v2/libraries/capabilities");
        var markerTask = item.Type == "series" ? Task.FromResult(default(JsonElement)) : Read("/api/v2/admin/markers/capabilities");
        await Task.WhenAll(libraryTask, markerTask);
        if (ct.IsCancellationRequested || !IsCurrentDetail(item.ContentId) || !client.IsCurrentContext(context) || !AuthorizationPolicy.IsActingAdmin(auth)) return;
        static bool Enabled(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;
        var libraries = await libraryTask; var markers = await markerTask;
        _mediaActionCapabilitiesId = item.ContentId;
        _offerSeekPreviews = Enabled(libraries, "trickplay") && Enabled(libraries, "trickplay_supported");
        _offerMarkerDetection = Enabled(markers, "redetect_markers") && (item.Type == "episode" || Enabled(markers, "movie_credits"));
        _honorMarkerSettings = Enabled(markers, "detection_kind_settings");
        BuildMoreFlyout();
    }

    private void CompleteCurrentMediaMenu(MediaItemDetail item)
    {
        if (item.Type is not ("movie" or "series" or "season" or "episode")) return;
        var auth = App.Services.GetRequiredService<AuthService>(); var admin = AuthorizationPolicy.IsActingAdmin(auth);
        var curate = AuthorizationPolicy.CanCurateMetadata(auth);
        var currentCapabilities = _mediaActionCapabilitiesId == item.ContentId;
        var actions = MoreFlyout.Items.Where(entry => entry is not MenuFlyoutSeparator).ToList();
        actions.RemoveAll(entry => entry is MenuFlyoutItem action && action.Text == "Media Info" && !curate);
        foreach (var entry in actions.OfType<MenuFlyoutItem>())
        {
            if (entry.Text == "Search Subtitles") entry.Text = "Add Subtitles";
            if (entry.Text == "Request seasons") entry.Text = "Request Seasons";
        }
        void Add(string label, string icon, Func<Task> execute, Func<AuthService, bool> authorized)
        {
            var action = new MenuFlyoutItem { Text = label, Icon = WebUiIcon.Navigation(icon, 16) };
            action.Click += async (_, _) =>
            {
                if (!IsCurrentDetail(item.ContentId) || !authorized(auth)) return;
                action.IsEnabled = false;
                try { await execute(); }
                catch (OperationCanceledException) { }
                catch (Exception error) { App.Services.GetRequiredService<ToastService>().Error(error.Message); }
                finally { action.IsEnabled = true; }
            };
            actions.Add(action);
        }
        if (admin) Add("View Play History", "history", async () => await new PlayHistoryDialog(item.ContentId) { XamlRoot = XamlRoot }.ShowAsync(), AuthorizationPolicy.IsActingAdmin);
        if (item.Type is "movie" or "episode" && AuthorizationPolicy.CanEditMarkers(auth))
            Add("Edit Markers", "tags", () => ShowItemActionAsync(item, new MarkerEditorDialog(item.ContentId)), AuthorizationPolicy.CanEditMarkers);
        if (curate && (item.Type == "series" || item.Type == "movie" && item.Versions.Count > 1))
            Add("Split Versions", "scissors", () => ShowItemActionAsync(item, new SplitVersionsDialog(item, ViewModel.LibraryId)), AuthorizationPolicy.CanCurateMetadata);
        if (admin && currentCapabilities && _offerSeekPreviews)
            Add("Seek Previews", "gallery-horizontal", () => ShowItemActionAsync(item, new SeekPreviewsDialog(item.ContentId, item.Versions.ToDictionary(version => version.FileId.ToString(), version => string.IsNullOrWhiteSpace(version.FileName) ? "File " + version.FileId : version.FileName))), AuthorizationPolicy.IsActingAdmin);
        if (admin && (item.Type == "episode" || item.Type == "movie" && currentCapabilities && _offerMarkerDetection))
            Add(item.Type == "movie" ? "Re-detect Credits" : currentCapabilities && _offerMarkerDetection ? "Re-detect Markers" : "Re-detect Intro Markers", "refresh-cw", async () =>
            {
                if (item.Type == "episode" && currentCapabilities && _offerMarkerDetection)
                    await ShowItemActionAsync(item, new RedetectMarkersDialog(item.ContentId, _honorMarkerSettings));
                else
                {
                    var client = App.Services.GetRequiredService<SiloApiClient>(); var context = client.CaptureContext();
                    await new MediaMarkerApi(client).RedetectAsync(context, item.ContentId, item.Type, item.Type == "movie" ? "credits" : "intro", _navigationCts?.Token ?? default, legacyIntro: item.Type == "episode");
                    if (IsCurrentDetail(item.ContentId) && client.IsCurrentContext(context) && AuthorizationPolicy.IsActingAdmin(auth)) App.Services.GetRequiredService<ToastService>().Success("Marker detection queued.");
                }
            }, AuthorizationPolicy.IsActingAdmin);
        // ActionBar groups viewer operations, Watch Together and curation in this order.
        static int Rank(MenuFlyoutItemBase entry) => (entry as MenuFlyoutItem)?.Text switch
        {
            "Shuffle" => 10, "Add to favorites" or "Remove from favorites" => 0,
            "Play from Beginning" => 20, "Add to Watchlist" or "Remove from Watchlist" => 30,
            "Add to Collection" => 40, "Download" => 50, "Add Subtitles" => 60, "Request Seasons" => 70,
            "Start a party with this" => 80, "Stage this in live room" or "Suggest this to live room" => 81, "Go to live room" => 82,
            "Media Info" => 90, "View Play History" => 91, "Refresh Metadata" => 92,
            "Re-detect Markers" or "Re-detect Credits" or "Re-detect Intro Markers" => 93, "Seek Previews" => 94, "Edit Metadata" => 95, "Edit Markers" => 96,
            "Match Item" => 97, "Split Versions" => 98, _ => 5,
        };
        MoreFlyout.Items.Clear(); int lastGroup = 0;
        foreach (var entry in actions.OrderBy(Rank))
        {
            var rank = Rank(entry); var group = rank >= 90 ? 2 : rank >= 80 ? 1 : 0;
            if (group > lastGroup && MoreFlyout.Items.Count > 0) MoreFlyout.Items.Add(new MenuFlyoutSeparator());
            MoreFlyout.Items.Add(entry); lastGroup = group;
        }
    }

    private async Task ShowItemActionAsync(MediaItemDetail item, ItemScopedDialog dialog)
    {
        var client = App.Services.GetRequiredService<SiloApiClient>(); var context = client.CaptureContext();
        if (!IsCurrentDetail(item.ContentId)) return;
        dialog.BindNavigation(_navigationCts?.Token ?? default); dialog.XamlRoot = XamlRoot; await dialog.ShowAsync();
        if (!dialog.HasSaved || !IsCurrentDetail(item.ContentId) || !client.IsCurrentContext(context)) return;
        await ViewModel.LoadCommand.ExecuteAsync(item.ContentId);
        if (IsCurrentDetail(item.ContentId) && client.IsCurrentContext(context)) UpdateUI();
    }
}
