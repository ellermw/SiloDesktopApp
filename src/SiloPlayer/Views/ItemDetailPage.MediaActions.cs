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
    private TitleAllowanceSession? _titleAllowanceOwner;
    private string? _titleAllowanceItem;
    private List<SiloPlayer.Core.Models.Auth.TitleAllowanceProfile> _titleAllowanceProfiles = [];
    private bool _titleAllowanceReadFailed;

    private async Task LoadTitleAllowanceActionsAsync(MediaItemDetail item, CancellationToken ct)
    {
        if (item.Type is not ("movie" or "series")) return;
        var navigation = _navigationCts; var library = ViewModel.LibraryId;
        var owner = AllowanceDialogOwner.Capture(() => ReferenceEquals(_navigationCts, navigation)
            && IsCurrentDetail(item.ContentId) && ViewModel.LibraryId == library, ct);
        _titleAllowanceOwner = owner; _titleAllowanceItem = item.ContentId;
        _titleAllowanceProfiles = []; _titleAllowanceReadFailed = false;
        // Retire an already-built action immediately, including failed/unsupported successor probes.
        BuildMoreFlyout();
        try
        {
            if (!await owner.LoadAsync()) return;
            var result = await owner.RunAsync((api, context, token) => api.GetTitleProfilesAsync(context, item.ContentId, token));
            if (!owner.IsCurrent || !ReferenceEquals(owner, _titleAllowanceOwner)) return;
            _titleAllowanceProfiles = result.Items;
        }
        catch (OperationCanceledException) { return; }
        catch { if (owner.IsCurrent && ReferenceEquals(owner, _titleAllowanceOwner)) _titleAllowanceReadFailed = true; }
        if (owner.IsCurrent && ReferenceEquals(owner, _titleAllowanceOwner)) BuildMoreFlyout();
    }

    private async Task LoadMediaActionCapabilitiesAsync(MediaItemDetail item, CancellationToken ct)
    {
        _ = LoadTitleAllowanceActionsAsync(item, ct);
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
        actions.RemoveAll(entry => entry is MenuFlyoutItem action && SiloPlayer.Services.Localized.GetText(action) == "web.detail.mediaInfo.title" && !curate);
        void Add(Func<string> label, string identity, string icon, Func<Task> execute, Func<AuthService, bool> authorized)
        {
            var action = MediaItemCardActions.BindText(new MenuFlyoutItem { Icon = WebUiIcon.Navigation(icon, 16) }, "Text", element => element.Text = label());
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(action, identity);
            action.Click += async (_, _) =>
            {
                if (!IsCurrentDetail(item.ContentId) || !authorized(auth)) return;
                action.IsEnabled = false;
                try { await execute(); }
                catch (OperationCanceledException) { }
                catch (Exception error) { App.Services.GetRequiredService<ToastService>().Error(error); }
                finally { action.IsEnabled = true; }
            };
            actions.Add(action);
        }
        if (_titleAllowanceItem == item.ContentId && _titleAllowanceOwner is { IsCurrent: true } allowanceOwner
            && item.Type is "movie" or "series")
        {
            if (_titleAllowanceReadFailed)
                Add(() => "Retry title allowances", "allowance", "refresh-cw", () => LoadTitleAllowanceActionsAsync(item, _navigationCts?.Token ?? default), _ => allowanceOwner.IsCurrent && ReferenceEquals(_titleAllowanceOwner, allowanceOwner));
            else if (allowanceOwner.Available && TitleAllowanceSession.HasAllowableProfile(_titleAllowanceProfiles))
            {
                var names = _titleAllowanceProfiles.Where(profile => profile.State is "allowed" or "allowed_by_collection").Select(profile => profile.Name).ToArray();
                Add(() => names.Length == 0 ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.allowFor") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.allowedFor", new Dictionary<string, object?> { ["profiles"] = string.Join(", ", names) }), "allowance", "users", async () =>
                {
                    await new AllowTitleDialog(allowanceOwner, item.ContentId, item.Title) { XamlRoot = XamlRoot }.ShowAsync();
                    if (allowanceOwner.IsCurrent) await LoadTitleAllowanceActionsAsync(item, _navigationCts?.Token ?? default);
                }, _ => allowanceOwner.IsCurrent && ReferenceEquals(_titleAllowanceOwner, allowanceOwner));
            }
        }
        if (admin) Add(() => SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.viewPlayHistory"), "history", "history", async () => await new PlayHistoryDialog(item.ContentId) { XamlRoot = XamlRoot }.ShowAsync(), AuthorizationPolicy.IsActingAdmin);
        if (item.Type is "movie" or "episode" && AuthorizationPolicy.CanEditMarkers(auth))
            Add(() => SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.editMarkers"), "edit-markers", "tags", () => ShowItemActionAsync(item, new MarkerEditorDialog(item.ContentId)), AuthorizationPolicy.CanEditMarkers);
        if (admin && (item.Type == "series" || item.Type == "movie" && item.Versions.Count > 1))
            Add(() => SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.splitVersions"), "split", "scissors", () => ShowItemActionAsync(item, new SplitVersionsDialog(item, ViewModel.LibraryId)), AuthorizationPolicy.IsActingAdmin);
        if (admin && currentCapabilities && _offerSeekPreviews)
            Add(() => SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.seekPreviews"), "previews", "gallery-horizontal", () => ShowItemActionAsync(item, new SeekPreviewsDialog(item.ContentId, item.Versions.ToDictionary(version => version.FileId.ToString(), version => string.IsNullOrWhiteSpace(version.FileName) ? "File " + version.FileId : version.FileName))), AuthorizationPolicy.IsActingAdmin);
        if (admin && (item.Type == "episode" || item.Type == "movie" && currentCapabilities && _offerMarkerDetection))
            Add(() => item.Type == "movie" ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.redetectCredits") : currentCapabilities && _offerMarkerDetection ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.redetectMarkers") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.redetectIntro"), "redetect", "refresh-cw", async () =>
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
        // Keep the existing action instances and handlers, including capability-hidden
        // actions that can become visible after the menu has been built.
        static int Rank(MenuFlyoutItemBase entry)
        {
            var identity = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(entry);
            if (identity.Length > 0) return identity switch
            { "watchlist" => 20, "not-interested" => 24, "allowance" => 25, "edit-markers" => 110,
                "redetect" => 111, "previews" => 112, "split" => 113, "history" => 121, _ => 0 };
            return SiloPlayer.Services.Localized.GetText(entry) switch
            {
                "web.detail.actions.shuffle" => 10, "web.detail.actions.playFromBeginning" => 11,
                "web.detail.actions.addToCollection" => 21, "web.detail.actions.download" => 22,
                "web.detail.actions.requestSeasons" => 23, "web.detail.actions.startWatchParty" => 30,
                "web.detail.actions.editMetadata" => 100, "web.detail.actions.matchItem" => 101,
                "web.detail.actions.refreshMetadata" => 102, "web.detail.mediaInfo.title" => 120,
                _ => (entry as MenuFlyoutItem)?.Text switch
                { "Reset Progress" => 12, "Stage this in live room" or "Suggest this to live room" => 31,
                    "Go to live room" => 32, _ => 0 }
            };
        }
        var ordered = actions.OrderBy(Rank).ToList();
        var management = ordered.Where(entry => Rank(entry) >= 100).ToList();
        var main = ordered.Where(entry => Rank(entry) < 100).ToList();
        MoreFlyout.Items.Clear();
        if (management.Count(entry => entry.Visibility == Visibility.Visible) > 1)
        {
            var manage = SiloPlayer.Controls.MediaItemCardActions.Localize(new MenuFlyoutSubItem { Text = "Manage", Icon = new FontIcon { Glyph = "\uE90F" } }, text: "web.detail.actions.manage");
            AddGroups(manage.Items, management.GroupBy(entry => Rank(entry) / 10));
            AddGroups(MoreFlyout.Items, main.GroupBy(entry => Rank(entry) / 10).Select(group => group.AsEnumerable()).Append(new[] { manage }));
        }
        else
            AddGroups(MoreFlyout.Items, main.GroupBy(entry => Rank(entry) / 10).Select(group => group.AsEnumerable()).Append(management));

        static void AddGroups(IList<MenuFlyoutItemBase> target, IEnumerable<IEnumerable<MenuFlyoutItemBase>> source)
        {
            var groups = source.Select(group => group.ToList()).Where(group => group.Count > 0).ToList();
            var separators = new List<MenuFlyoutSeparator>();
            for (var i = 0; i < groups.Count; ++i)
            {
                if (i > 0) { var separator = new MenuFlyoutSeparator(); separators.Add(separator); target.Add(separator); }
                foreach (var entry in groups[i]) target.Add(entry);
            }
            void UpdateSeparators()
            {
                var precedingVisible = false;
                for (var i = 0; i < groups.Count; ++i)
                {
                    var visible = groups[i].Any(entry => entry.Visibility == Visibility.Visible);
                    if (i > 0) separators[i - 1].Visibility = precedingVisible && visible ? Visibility.Visible : Visibility.Collapsed;
                    precedingVisible |= visible;
                }
            }
            foreach (var entry in groups.SelectMany(group => group))
                entry.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => UpdateSeparators());
            UpdateSeparators();
        }
    }

    private async Task ShowItemActionAsync(MediaItemDetail item, ItemScopedDialog dialog)
    {
        var client = App.Services.GetRequiredService<SiloApiClient>(); var context = client.CaptureContext();
        var auth = App.Services.GetRequiredService<AuthService>(); var session = auth.SessionGeneration; var userId = auth.CurrentUser?.Id;
        var libraryId = ViewModel.LibraryId; var token = _navigationCts?.Token ?? default;
        bool Current() => !token.IsCancellationRequested && IsCurrentDetail(item.ContentId) && ViewModel.LibraryId == libraryId
            && client.IsCurrentContext(context) && auth.SessionGeneration == session && auth.CurrentUser?.Id == userId
            && (dialog is not SplitVersionsDialog || AuthorizationPolicy.IsActingAdmin(auth));
        if (!Current()) return;
        dialog.BindNavigation(token); dialog.XamlRoot = XamlRoot; await dialog.ShowAsync();
        if (!dialog.HasSaved || !Current()) return;
        await ViewModel.ReloadAsync(item.ContentId);
        if (Current()) UpdateUI();
    }
}
