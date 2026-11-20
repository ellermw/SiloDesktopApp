using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.ApplicationModel.DataTransfer;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminApiKeysPage : Page, IAdminGutterScrollPage, IAdminPageSuspension
{
    public AdminApiKeysController Controller { get; }
    private readonly AdminAccessPageUi _ui;
    private readonly Button _create = U.Action(T("createKey"), "ApiKeysCreate", primary: true);
    public FrameworkElement ScrollContent => _ui.ScrollContent;
    public AdminApiKeysPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled;
        Controller = App.Services.GetRequiredService<AdminApiKeysController>();
        _ui = new(this, Controller, T("title"), T("subtitle"), Render) { Reopen = Controller.OpenAsync };
        _ui.Actions.Children.Add(_create); _create.Click += async (_, _) => await CreateAsync();
        Loaded += async (_, _) => await Controller.OpenAsync(); Unloaded += (_, _) => Close();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); base.OnNavigatedFrom(e); }
    public void SuspendAdminPage() => Close();
    private void Close() { _ui.Close(); Controller.Dispose(); }
    private void Render()
    {
        _create.IsEnabled = Controller.Current && !Controller.Busy && !Controller.WritesBlocked && Controller.Capabilities is { Available: true } && Controller.AccountsLoaded;
        _ui.Fields.Children.Clear(); if (!Controller.Current) return;
        if (Controller.Capabilities is { Available: false }) { _ui.Fields.Children.Add(U.Text(T("unavailable"))); return; }
        if (!Controller.Loaded) return;
        var table = new StackPanel { Spacing = 0, MinWidth = 1100 };
        table.Children.Add(Row(new[] { "label", "user", "prefix", "access", "rateLimit", "created", "lastUsed", "actions" }.Select(k => T("list.column." + k)).ToArray(), null));
        if (Controller.Rows.Count == 0) table.Children.Add(U.Text("No API keys yet."));
        foreach (var key in Controller.Rows)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (Controller.CanManage(key.UserId) && Controller.Capabilities?.GuardedConfiguration == true)
            {
                var edit = U.Action("Edit rate limit", "ApiKeyTier_" + key.Id); var revoke = U.Action("Revoke", "ApiKeyRevoke_" + key.Id);
                edit.IsEnabled = revoke.IsEnabled = !Controller.Busy && !Controller.ListReviewRequired; actions.Children.Add(edit); actions.Children.Add(revoke);
                edit.Click += async (_, _) => await EditAsync(key.Id, false); revoke.Click += async (_, _) => await EditAsync(key.Id, true);
            }
            table.Children.Add(Row([key.Label, key.Username, key.KeyPrefix.Length == 0 ? "Unavailable" : key.KeyPrefix + "…", key.Scopes.Count == 0 ? "Full access" : string.Join(", ", key.Scopes), key.RateTier == "elevated" ? "Elevated" : "Standard", Date(key.CreatedAt), key.LastUsedAt == null ? "Never" : Date(key.LastUsedAt)], actions));
        }
        _ui.Fields.Children.Add(new ScrollViewer { Content = table, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        if (Controller.NextCursor != null)
        {
            var more = U.Action(T("list.loadMore"), "ApiKeysMore"); more.IsEnabled = !Controller.Busy && Controller.Rows.Count < 1000;
            more.Click += async (_, _) => await Controller.MoreAsync(); _ui.Fields.Children.Add(more);
            if (Controller.Rows.Count >= 1000) _ui.Fields.Children.Add(U.Text("Showing 1,000 keys. Reload to start a fresh bounded list."));
        }
        if (Controller.AccountCursor != null)
        {
            var more = U.Action("Load more accounts", "ApiKeysMoreAccounts"); more.IsEnabled = !Controller.Busy && Controller.Accounts.Count < 1000;
            more.Click += async (_, _) => await Controller.MoreAccountsAsync(); _ui.Fields.Children.Add(more);
        }
    }
    private static string Date(string value) => DateTimeOffset.TryParse(value, out var date) ? SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(date) : value;
    private static Grid Row(string[] values, FrameworkElement? actions)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new(12), BorderThickness = new(0, 0, 0, 1), BorderBrush = U.Brush("BorderBrush") };
        for (var i = 0; i < 8; i++) row.ColumnDefinitions.Add(new() { Width = new(i == 3 ? 2 : i == 7 ? 2.5 : 1, GridUnitType.Star) });
        for (var i = 0; i < values.Length; i++) { var text = U.Text(values[i], 12); Grid.SetColumn(text, i); row.Children.Add(text); }
        if (actions != null) { Grid.SetColumn(actions, 7); row.Children.Add(actions); } return row;
    }
    private async Task CreateAsync()
    {
        var owner = Controller.Owner; if (owner is not { IsCurrent: true } || Controller.Busy || _ui.Dialog != null) return;
        var label = U.Input(T("create.label"), "ApiKeyLabel"); label.Width = double.NaN; label.HorizontalAlignment = HorizontalAlignment.Stretch; label.MaxLength = 4096;
        var users = U.Select(T("create.user"), "ApiKeyUser", Controller.Accounts.Where(a => Controller.CanManage(a.Id)).Select(a => (a.Id, a.Username))); AdminAccessPageUi.Select(users, owner.AccountId);
        var access = U.Select(T("create.access"), "ApiKeyAccess", [("full", T("access.full")), ("scoped", T("create.accessScoped"))]); access.SelectedIndex = 0;
        var choices = new StackPanel { Spacing = 8 }; var checks = new Dictionary<string, CheckBox>();
        foreach (var scope in Controller.Capabilities!.Scopes)
        {
            var box = new CheckBox { Content = scope.Name, Tag = scope.Name }; AutomationProperties.SetName(box, scope.Name);
            checks[scope.Name] = box; choices.Children.Add(AdminAccessPageUi.Stack(box, U.Text(scope.Description, 12, true)));
        }
        choices.Visibility = Visibility.Collapsed; access.SelectionChanged += (_, _) => choices.Visibility = AdminAccessPageUi.Selected(access) == "scoped" ? Visibility.Visible : Visibility.Collapsed;
        var error = U.Text("", 13); var submit = U.Action(T("create.submit"), "ApiKeyCreateSubmit", primary: true);
        // Like the official form, the access choice only exists when the server offers scopes; otherwise every key is full access.
        var content = AdminAccessPageUi.Stack(U.Text(T("create.description")), U.Text(T("create.label")), label, U.Text(T("create.user")), users);
        if (Controller.Capabilities!.Scopes.Count > 0)
        {
            content.Children.Add(U.Text(T("create.access"))); content.Children.Add(access); content.Children.Add(U.Text(T("create.accessFullHint"), 12, true));
            content.Children.Add(choices); content.Children.Add(U.Text(T("create.scopesHint"), 12, true));
        }
        content.Children.Add(error); content.Children.Add(submit);
        var dialog = new ContentDialog { Title = T("create.title"), Content = content, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        dialog.Closing += (_, args) => { if (Controller.Busy && owner.IsCurrent) args.Cancel = true; };
        var dialogOpen = true;
        string? secret = null; TextBlock? secretText = null;
        submit.Click += async (_, _) =>
        {
            if (Controller.Busy || Controller.WritesBlocked || secret != null) return;
            var scoped = AdminAccessPageUi.Selected(access) == "scoped";
            submit.IsEnabled = false; label.IsEnabled = users.IsEnabled = access.IsEnabled = false; foreach (var check in checks.Values) check.IsEnabled = false;
            try
            {
                secret = await Controller.CreateAsync(label.Text, AdminAccessPageUi.Selected(users), checks.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToArray(), scoped, confirmed: true);
                if (secret == null) { error.Text = Controller.Message ?? "Review the key label, owner and access."; return; }
                if (!owner.IsCurrent || !_ui.Active || !dialogOpen) { secret = null; return; }
                AdminAccessPageUi.ClearPrivateControls(content); content.Children.Clear();
                content.Children.Add(U.Text(T("create.secretNotice")));
                secretText = U.Text(secret, 13); secretText.IsTextSelectionEnabled = true; AutomationProperties.SetAutomationId(secretText, "ApiKeySecret"); content.Children.Add(secretText);
                var copy = U.Action(T("create.copyAndClose"), "ApiKeyCopy"); content.Children.Add(copy);
                copy.Click += (_, _) =>
                {
                    if (!owner.IsCurrent || secret == null) return;
                    try { var package = new DataPackage(); package.SetText(secret); Clipboard.SetContentWithOptions(package, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false }); dialog.Hide(); }
                    catch { error.Text = T("create.copyFailed"); if (!content.Children.Contains(error)) content.Children.Add(error); }
                };
                dialog.CloseButtonText = "Close";
            }
            finally
            {
                if (secret == null) { submit.IsEnabled = !Controller.WritesBlocked; label.IsEnabled = users.IsEnabled = access.IsEnabled = !Controller.WritesBlocked; foreach (var check in checks.Values) check.IsEnabled = !Controller.WritesBlocked; }
            }
        };
        try { await _ui.ShowAsync(dialog); }
        finally { dialogOpen = false; secret = null; if (secretText != null) secretText.Text = ""; label.Text = ""; if (Controller.Current && !Controller.WritesBlocked) await Controller.OpenAsync(); }
    }
    private async Task EditAsync(string id, bool revoke)
    {
        if (_ui.Dialog != null || Controller.Busy) return;
        await Controller.EditAsync(id); var reviewed = Controller.Editor; var owner = Controller.Owner;
        if (reviewed == null || reviewed.Value.Id != id || owner is not { IsCurrent: true }) return;
        var tier = U.Select(T("editor.newRateLimit"), "ApiKeyTier", Controller.Capabilities!.RateTiers.Where(t => t is "standard" or "elevated").Select(t => (t, T("rateLimit." + t) + (Controller.RateHints.TryGetValue(t, out var hint) ? " — " + hint : ""))));
        AdminAccessPageUi.Select(tier, reviewed.Value.RateTier);
        var description = U.Text(revoke ? T("editor.descriptionRevoke", new { label = reviewed.Value.Label }) : T("editor.descriptionRateLimit", new { label = reviewed.Value.Label, rate_limit = reviewed.Value.RateTier }));
        var content = AdminAccessPageUi.Stack(description);
        if (!revoke) content.Children.Add(tier);
        if (!revoke) content.Children.Add(U.Text(T("editor.rateLimitHelp"), 12, true));
        var error = U.Text("", 13); content.Children.Add(error);
        var reload = U.Action(T("editor.reload"), "ApiKeyReloadCurrent"); reload.Visibility = Visibility.Collapsed; content.Children.Add(reload);
        var dialog = new ContentDialog { Title = T(revoke ? "editor.titleRevoke" : "editor.titleRateLimit"), Content = content, PrimaryButtonText = T(revoke ? "editor.revoke" : "editor.save"), CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        dialog.Closing += (_, args) => { if (Controller.Busy && owner.IsCurrent) args.Cancel = true; };
        reload.Click += async (_, _) =>
        {
            await Controller.EditAsync(id);
            if (Controller.Editor is { } fresh && fresh.Value.Id == id && owner.IsCurrent && !Controller.ReviewRequired)
            { reviewed = fresh; error.Text = ""; reload.Visibility = Visibility.Collapsed; dialog.IsPrimaryButtonEnabled = true; description.Text = T(revoke ? "editor.descriptionRevoke" : "editor.descriptionRateLimit", new { label = fresh.Value.Label, rate_limit = fresh.Value.RateTier }); }
            else error.Text = Controller.Message ?? "Reload failed.";
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral(); dialog.IsPrimaryButtonEnabled = false; reload.IsEnabled = false;
            try
            {
                if (ReferenceEquals(owner, Controller.Owner) && owner.IsCurrent) await Controller.ChangeAsync(reviewed, revoke ? null : AdminAccessPageUi.Selected(tier), true);
                if (Controller.Editor == null && !Controller.ReviewRequired) { dialog.Hide(); return; }
                error.Text = Controller.Message ?? "Reload the current key before continuing."; reload.Visibility = Visibility.Visible;
            }
            finally { reload.IsEnabled = !Controller.Busy; deferral.Complete(); }
        };
        await _ui.ShowAsync(dialog);
        Controller.CloseEditor();
    }
    private static string T(string suffix, object? values = null) => AdminText.T("web.admin.apiKeys." + suffix, values);
}
