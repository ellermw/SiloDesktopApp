using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminUserResourceUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Official source/token dialogs, user discovery, mapping and durable import runs at 217ff25.</summary>
public sealed partial class AdminHistoryImportPage : Page, IAdminPageSuspension
{
    private readonly AdminUserResourcesApi _api;
    private readonly AdminUserResourceHost _host;
    private AdminRouteRequest _route = new(AdminRoute.HistoryImport);
    private IReadOnlyList<JsonElement> _sources = [], _mappings = [], _accounts = [];
    private readonly List<JsonElement> _runs = [];
    private AdminResourceDocument? _source;
    private JsonElement _capabilities;
    private StackPanel _mappingPanel = Stack(12), _runPanel = Stack(8);
    private readonly HashSet<string> _runCursors = [];
    private string? _nextRun;
    private string _runFilter = "all";
    private string? _expanded;
    private int? _lookback;
    private JsonElement _bulk;
    private DispatcherTimer? _poll;
    private DateTimeOffset _nextPoll;
    private bool _polling, _loadingSourceData, _mappingsReady, _mappingsFailed, _runsFailed;
    private int _pollIndex;
    private readonly List<PasswordBox> _secrets = [];
    private readonly List<Button> _writes = [];
    private long _sourceDataVersion;
    private int _mappingVisible = 50;
    public AdminUserResourceController Session => _host.Session;
    public AdminHistoryImportPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled; Id(this, "AdminHistoryImportPage");
        _api = App.Services.GetRequiredService<AdminUserResourcesApi>();
        _host = new(this, new(App.Services.GetRequiredService<AdminMonitorAuthority>()), LoadAsync, Clear);
        _host.StateChanged += UpdateActions;
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); if (e.Parameter is AdminRouteRequest route) _route = route; }
    public void SuspendAdminPage() => _host.Suspend();
    private void Clear()
    {
        _sourceDataVersion++; _loadingSourceData = false; _expanded = null; _pollIndex = 0;
        _poll?.Stop(); if (_poll != null) _poll.Tick -= PollTick; _poll = null;
        foreach (var secret in _secrets) secret.Password = ""; _secrets.Clear();
        _sources = _mappings = _accounts = []; _runs.Clear(); _runCursors.Clear(); _source = null; _capabilities = _bulk = default; _nextRun = null;
        _mappingPanel.Children.Clear(); _runPanel.Children.Clear(); _writes.Clear(); _mappingsReady = _mappingsFailed = _runsFailed = false;
    }
    private static string H(string key, params (string Name, object? Value)[] args) => T("web.admin.historyImport." + key, args);
    private void SetContent(params UIElement[] elements) { _host.Content.Children.Clear(); foreach (var element in elements) _host.Content.Children.Add(element); }
    private void ShowFailure(string text) { SetContent(Text(text), Button(T("common.retry"), "ImportRetry", () => _ = _host.ReloadAsync())); _host.RaiseChanged(); }

    private async Task LoadAsync()
    {
        var pageOwner = Session.Owner;
        SetContent(Text(H("page.loading")));
        var capabilities = await Session.ReadAsync("capabilities", lease => _api.ImportCapabilitiesAsync(lease));
        if (!Session.IsCurrent(pageOwner)) return;
        if (capabilities.ValueKind != JsonValueKind.Object) { ShowFailure(H("page.sourcesFailed")); return; }
        _capabilities = capabilities;
        if (!_capabilities.Flag("available") || !_capabilities.Flag("guarded_configuration") || !_capabilities.Flag("durable_runs")) { SetContent(Text(H("page.unavailable"))); return; }
        var sources = await Session.ReadAsync("sources", owner => _api.ConfigurationAsync(owner, cursor => _api.ListImportSourcesAsync(owner, cursor)));
        if (!Session.IsCurrent(pageOwner)) return;
        if (sources == null) { ShowFailure(H("page.sourcesFailed")); return; }
        _sources = sources;
        var selected = _route.Get("source");
        if (!_sources.Any(x => x.Text("id") == selected)) selected = _sources.FirstOrDefault().Text("id");
        if (!string.IsNullOrEmpty(selected))
        {
            var source = await Session.ReadAsync("source", lease => _api.GetImportSourceAsync(lease, selected));
            if (!Session.IsCurrent(pageOwner)) return;
            if (source == null) { ShowFailure(H("page.sourcesFailed")); return; }
            _source = source; _route = _route.WithQuery("source", selected); AdminShellPage.TryReplace(this, _route);
        }
        RenderPage();
        if (_source != null) await LoadSourceDataAsync();
        if (Session.IsCurrent(pageOwner)) { _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; _poll.Tick += PollTick; _poll.Start(); }
    }

    private void RenderPage()
    {
        _writes.Clear(); var elements = new List<UIElement> { LogsNativeUi.Header(H("page.title"), H("page.subtitle")) };
        if (_sources.Count == 0)
        {
            var empty = Stack(10); empty.Children.Add(Text(H("sourceBar.emptyTitle"), 14)); empty.Children.Add(Text(H("sourceBar.emptyBody"), 12, true)); empty.Children.Add(WriteButton(H("sourceBar.addServer"), "ImportAddSource", () => _ = SourceDialogAsync(null), accent: true));
            elements.Add(Panel(empty)); SetContent([.. elements]); UpdateActions(); return;
        }
        var bar = Stack(10); var row = Inline(8);
        var select = Select(H("sourceBar.selectServer"), _sources.Select(x => (x.Text("id"), x.Text("name") + " · " + x.Text("source_type"))), _source?.Value.Text("id") ?? "", id =>
        { if (_source?.Value.Text("id") != id) AdminShellPage.Open(new AdminRouteRequest(AdminRoute.HistoryImport).WithQuery("source", id)); });
        select.MinWidth = 260; Id(select, "ImportSourceSelect"); row.Children.Add(select);
        if (_source != null)
        {
            var source = _source; var value = source.Value; var address = Text(SafeSourceAddress(value.Text("base_url")), 12, true); address.FlowDirection = FlowDirection.LeftToRight; row.Children.Add(address);
            var actions = Inline(4);
            actions.Children.Add(WriteButton(H("sourceBar.setApiKey"), "ImportToken", () => _ = TokenDialogAsync(source), enabled: () => source.CanWrite));
            actions.Children.Add(WriteButton(H("sourceBar.editServer"), "ImportEditSource", () => _ = SourceDialogAsync(source), enabled: () => source.CanWrite));
            actions.Children.Add(WriteButton(H("sourceBar.deleteServer"), "ImportDeleteSource", () => _ = DeleteSourceAsync(source), enabled: () => source.CanWrite));
            actions.Children.Add(WriteButton(H("sourceBar.add"), "ImportAddSource", () => _ = SourceDialogAsync(null)));
            bar.Children.Add(row); bar.Children.Add(actions);
            bar.Children.Add(value.Flag("has_admin_token") ? Chip(H("sourceBar.apiKeyConfigured"), "SuccessBrush") : Chip(H("sourceBar.noApiKey"), "WarningBrush"));
        }
        elements.Add(Panel(bar));
        if (_source != null)
        {
            if (_source.Value.Text("source_type") == "plex") elements.Add(PlexLimits());
            if (_source.Value.Flag("needs_reconfiguration")) { var warn = Text(H("page.needsReconfiguration")); warn.Foreground = Brush("WarningBrush"); elements.Add(warn); }
        }
        _mappingPanel = Stack(12); _runPanel = Stack(8);
        elements.Add(_mappingPanel); elements.Add(_runPanel); SetContent([.. elements]); RenderMappings(); RenderRuns(); UpdateActions();
    }
    private static void DetachFromParent(FrameworkElement element) { if (element.Parent is Panel panel) panel.Children.Remove(element); }
    /// <summary>What an admin-token Plex import cannot read: resume points, titles marked watched and watchlists need the user's own sign-in.</summary>
    private static FrameworkElement PlexLimits()
    {
        var note = Stack(4); note.Children.Add(Text(H("plexLimits.title"), 14)); note.Children.Add(Text(H("plexLimits.intro"), 12));
        foreach (var key in new[] { "resumePoints", "markedWatched", "watchlists" }) note.Children.Add(Text("• " + H("plexLimits." + key), 12));
        note.Children.Add(Text(H("plexLimits.outro"), 12));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(note, H("plexLimits.title"));
        return new Border { Child = note, Padding = new(16), CornerRadius = new(12), BorderThickness = new(1), BorderBrush = Brush("WarningBrush"), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
    }

    /// <summary>A request made while another load runs replaces it, never drops: the refresh a write asked for must show that write. The older load is retired by version and starts no further read.</summary>
    private async Task LoadSourceDataAsync()
    {
        if (_source == null) return;
        _loadingSourceData = true;
        var source = _source; var pageOwner = Session.Owner; var revision = ++_sourceDataVersion; var id = source.Value.Text("id");
        try
        {
            if (source.Value.Flag("has_admin_token") && !source.Value.Flag("needs_reconfiguration"))
            {
                _mappingsFailed = false;
                var mappings = await Session.ReadAsync("mappings", owner => _api.ConfigurationAsync(owner, cursor => _api.ListImportMappingsAsync(owner, id, cursor)));
                if (!Session.IsCurrent(pageOwner) || revision != _sourceDataVersion) return;
                _mappingsReady = mappings != null; _mappingsFailed = mappings == null; if (mappings != null) _mappings = mappings;
                if (_mappingsReady) await LoadAccountNamesAsync(pageOwner, revision);
                if (!Session.IsCurrent(pageOwner) || revision != _sourceDataVersion) return;
            }
            var page = await Session.ReadAsync("runs", owner => _api.ListImportRunsAsync(owner, id));
            if (!Session.IsCurrent(pageOwner) || revision != _sourceDataVersion) return;
            _runsFailed = page == null;
            if (page != null) { _runs.Clear(); _runs.AddRange(page.Items); _nextRun = page.NextCursor; _runCursors.Clear(); }
            if (_source != source || Session.Owner is not { IsCurrent: true }) return;
            RenderMappings(); RenderRuns();
        }
        finally { if (revision == _sourceDataVersion) _loadingSourceData = false; }
    }
    /// <summary>Account names for the mapping and run rows. They are display only: a failed read leaves "User #id" and raises no error.</summary>
    private async Task LoadAccountNamesAsync(MonitorAuthorityLease? pageOwner, long revision)
    {
        if (_accounts.Count > 0) return;
        var accounts = await Session.ReadAsync("account-names", async owner => { try { return await _api.ConfigurationAsync(owner, cursor => _api.ListAccountsAsync(owner, cursor)); } catch (Exception error) when (error is ApiException or InvalidDataException or AdminResourceTooManyException) { return null; } });
        if (!Session.IsCurrent(pageOwner) || revision != _sourceDataVersion) return;
        if (accounts != null) _accounts = accounts;
    }
    private string AccountName(string id) => _accounts.FirstOrDefault(x => x.Text("id") == id).Text("username") is { Length: > 0 } name ? name : H("userFallback", ("id", id));

    private PasswordBox Secret(string label)
    {
        var secret = new PasswordBox { Header = label, PasswordRevealMode = PasswordRevealMode.Peek, MaxLength = 65536 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(secret, label); _secrets.Add(secret); return secret;
    }

    // ---------- source dialogs ----------
    private async Task SourceDialogAsync(AdminResourceDocument? editor)
    {
        var existing = editor?.Value; var type = existing?.Text("source_type") ?? "jellyfin"; var enabled = existing?.Flag("enabled") ?? true; var login = type == "plex" && editor == null; var clear = false;
        var name = Input(H("sourceDialog.name"), "ImportSourceName", existing?.Text("name") ?? ""); var url = Input(H("sourceDialog.serverUrl"), "ImportSourceUrl", existing?.Text("base_url") ?? "");
        string Hint(string key) => type switch { "emby" => key == "name" ? H("sourceDialog.namePlaceholderEmby") : "https://emby.example.com", "plex" => key == "name" ? H("sourceDialog.namePlaceholderPlex") : "https://plex.example.com:32400", _ => key == "name" ? H("sourceDialog.namePlaceholderJellyfin") : "https://jellyfin.example.com" };
        name.PlaceholderText = Hint("name"); url.PlaceholderText = Hint("url"); url.FlowDirection = FlowDirection.LeftToRight;
        var fields = Stack(12); var credentials = Stack(10); var limits = Stack(0);
        var credential = Secret(existing == null ? H("sourceDialog.apiKeyLabel", ("muted", "")) : H("sourceDialog.replacementCredential"));
        var username = Input(H("sourceDialog.plexUser"), "ImportPlexUser", ""); username.PlaceholderText = "you@example.com"; var password = Secret(H("sourceDialog.plexPassword"));
        void RenderCredentials()
        {
            credentials.Children.Clear(); limits.Children.Clear();
            if (type == "plex") limits.Children.Add(PlexLimits());
            credential.Header = existing != null ? H("sourceDialog.replacementCredential") : H(type == "plex" ? "sourceDialog.plexTokenLabel" : "sourceDialog.apiKeyLabel", ("muted", ""));
            credential.PlaceholderText = H(type == "plex" ? "sourceDialog.plexTokenPlaceholder" : "sourceDialog.apiKeyPlaceholder");
            if (existing != null)
            {
                credentials.Children.Add(credential); credential.IsEnabled = !clear;
                credentials.Children.Add(Toggle(H("sourceDialog.clearCredential"), clear, value => { clear = value; credential.IsEnabled = !value; }));
                credentials.Children.Add(Text(H("sourceDialog.addressChangeHint"), 12, true));
                if (existing.Value.Flag("needs_reconfiguration")) { var warn = Text(H("sourceDialog.needsReconfiguration"), 12); warn.Foreground = Brush("WarningBrush"); credentials.Children.Add(warn); }
                return;
            }
            if (type == "plex")
            {
                credentials.Children.Add(Segmented(H("credential.signInWithPlex"), [("login", H("credential.signInWithPlex")), ("token", H("credential.pasteToken"))], login ? "login" : "token", value => { login = value == "login"; RenderCredentials(); }, "ImportCredentialMode"));
                if (login) { credentials.Children.Add(username); credentials.Children.Add(password); credentials.Children.Add(Text(H("credential.twoStepHint"), 12, true)); return; }
            }
            credentials.Children.Add(credential);
        }
        var kind = Select(H("sourceDialog.type"), [("jellyfin", "Jellyfin"), ("emby", "Emby"), ("plex", "Plex")], type, value => { type = value; login = type == "plex"; name.PlaceholderText = Hint("name"); url.PlaceholderText = Hint("url"); RenderCredentials(); });
        kind.IsEnabled = editor == null; Id(kind, "ImportSourceType");
        fields.Children.Add(Columns(name, kind)); fields.Children.Add(url); fields.Children.Add(limits);
        fields.Children.Add(credentials); fields.Children.Add(Toggle(H("sourceDialog.enabled"), enabled, value => enabled = value)); RenderCredentials();
        try
        {
            var saved = await _host.SaveDialogAsync(H(editor == null ? "sourceDialog.titleAdd" : "sourceDialog.titleEdit"), fields, editor == null ? H("sourceDialog.addServer") : T("common.save"),
                () => !string.IsNullOrWhiteSpace(name.Text) && !string.IsNullOrWhiteSpace(url.Text), async () =>
                {
                    var wrote = false;
                    await Session.WriteAsync(async owner =>
                    {
                        var token = credential.Password.Trim();
                        try
                        {
                            if (editor == null && login && type == "plex") { token = await _api.ImportPlexLoginAsync(owner, username.Text.Trim(), password.Password); password.Password = ""; }
                            var body = new JsonObject { ["name"] = name.Text.Trim(), ["base_url"] = url.Text.Trim(), ["enabled"] = enabled };
                            if (editor == null) { body["source_type"] = type; body["sort_order"] = 0; }
                            if (editor != null && clear) body["admin_token"] = ""; else if (token.Length > 0) body["admin_token"] = token;
                            if (editor == null) await _api.CreateImportSourceAsync(owner, JsonSerializer.SerializeToElement(body)); else await _api.SaveImportSourceAsync(owner, editor, JsonSerializer.SerializeToElement(body));
                            wrote = true;
                        }
                        // The typed key stays until the save is accepted or the dialog closes (217ff25 AdminHistoryImport.tsx handleSave):
                        // cleared on a refusal, the corrected retry would be sent without it.
                        finally { token = ""; password.Password = ""; if (wrote) credential.Password = ""; }
                    });
                    return wrote;
                });
            if (saved) await _host.ReloadAsync();
        }
        finally { credential.Password = password.Password = ""; _secrets.Remove(credential); _secrets.Remove(password); username.Text = ""; }
    }

    private async Task TokenDialogAsync(AdminResourceDocument source)
    {
        var isPlex = source.Value.Text("source_type") == "plex"; var login = isPlex; var remove = false;
        var token = Secret(H("tokenDialog.tokenLabel")); token.PlaceholderText = H("tokenDialog.tokenPlaceholder");
        var username = Input(H("tokenDialog.email"), "ImportTokenPlexUser", ""); username.PlaceholderText = "you@example.com"; var password = Secret(H("tokenDialog.password"));
        var fields = Stack(12); var body = Stack(10);
        void Render()
        {
            body.Children.Clear();
            if (isPlex) body.Children.Add(Segmented(H("credential.signInWithPlex"), [("login", H("credential.signInWithPlex")), ("token", H("credential.pasteToken"))], login ? "login" : "token", value => { login = value == "login"; Render(); }, "ImportTokenMode"));
            if (isPlex && login) { body.Children.Add(Text(H("tokenDialog.plexSignInIntro"), 12, true)); body.Children.Add(username); body.Children.Add(password); body.Children.Add(Text(H("credential.twoStepHint"), 12, true)); }
            else { body.Children.Add(Text(H(isPlex ? "tokenDialog.plexTokenIntro" : "tokenDialog.apiKeyIntro"), 12, true)); body.Children.Add(token); }
        }
        fields.Children.Add(body); Render();
        if (source.Value.Flag("has_admin_token"))
        {
            fields.Children.Add(Text(H("tokenDialog.replaceNotice"), 12, true));
            fields.Children.Add(Toggle(H("tokenDialog.remove"), false, value => remove = value));
        }
        try
        {
            var saved = await _host.SaveDialogAsync(H("tokenDialog.title", ("name", Safe(source.Value.Text("name")))), fields, T("common.save"),
                () => remove || (isPlex && login ? username.Text.Trim().Length > 0 && password.Password.Length > 0 : token.Password.Trim().Length > 0), async () =>
                {
                    var wrote = false;
                    await Session.WriteAsync(async owner =>
                    {
                        var credential = token.Password.Trim();
                        try
                        {
                            if (remove) await _api.ClearImportTokenAsync(owner, source);
                            else
                            {
                                if (isPlex && login) credential = await _api.ImportPlexLoginAsync(owner, username.Text.Trim(), password.Password);
                                await _api.SetImportTokenAsync(owner, source, credential);
                            }
                            wrote = true;
                        }
                        finally { credential = ""; token.Password = password.Password = ""; }
                    });
                    return wrote;
                });
            if (saved) await _host.ReloadAsync();
        }
        finally { token.Password = password.Password = ""; _secrets.Remove(token); _secrets.Remove(password); username.Text = ""; }
    }

    private async Task DeleteSourceAsync(AdminResourceDocument source)
    {
        var owner = Session.Owner;
        if (!await _host.ConfirmAsync(H("page.deleteDialog.title"), H("page.deleteDialog.description", ("name", Safe(source.Value.Text("name")))), H("page.deleteDialog.confirm"), destructive: true) || !Session.IsCurrent(owner)) return;
        if (await Session.WriteAsync(lease => _api.DeleteImportSourceAsync(lease, source)) && Session.IsCurrent(owner)) await _host.ReloadAsync();
    }

    private Button WriteButton(string label, string id, Action command, bool accent = false, Func<bool>? enabled = null)
    {
        var owner = Session.Owner;
        var button = Button(label, id, () => { if (Session.IsCurrent(owner) && !Session.Busy && !Session.NeedsReview) command(); }, accent);
        button.Tag = enabled; _writes.Add(button); return button;
    }
    private void UpdateActions()
    {
        var canWrite = Session.Owner is { IsCurrent: true } && !Session.Busy && !Session.NeedsReview;
        foreach (var button in _writes) button.IsEnabled = canWrite && (button.Tag is not Func<bool> extra || extra());
    }
    private void ForgetButtons(UIElement element)
    {
        if (element is Button button) _writes.Remove(button);
        if (element is Panel panel) foreach (var child in panel.Children) ForgetButtons(child);
        if (element is Border { Child: { } content }) ForgetButtons(content);
    }
    private static string SafeSourceAddress(string value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0 ? value : "Source address requires review";
    /// <summary>Run diagnostics from the server are shown when they read as plain text; anything that looks like an address or a secret is replaced.</summary>
    private static string SafeDiagnostic(string text) => text.Contains("://", StringComparison.Ordinal) || text.Contains("token", StringComparison.OrdinalIgnoreCase) || text.Contains("password", StringComparison.OrdinalIgnoreCase) || text.Contains("secret", StringComparison.OrdinalIgnoreCase)
        ? "The source reported a diagnostic. Review its configuration and credentials." : text.Length > 512 ? text[..512] + "…" : text;
}
