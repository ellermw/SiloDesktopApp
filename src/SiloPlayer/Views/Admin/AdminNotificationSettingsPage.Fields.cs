using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using Windows.ApplicationModel.DataTransfer;
using U = SiloPlayer.Controls.AdminSettingsUi;
using C = SiloPlayer.Core.Services.Admin.AdminNotificationSettingsController;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminNotificationSettingsPage
{
    private readonly TextBox _relayUrl = U.Input(N("relay.url"), "NotificationsRelayUrl", "https://push.siloserver.org");
    private readonly TextBox _deployment = U.Input(N("relay.deploymentId"), "NotificationsRelayDeployment");
    private readonly TextBlock _relayStatus = U.Text("", 12, true), _emailStatus = U.Text("", 12, true), _discordStatus = U.Text("", 12, true), _discordTestResult = U.Text("", 12);
    private Button _relayRegister = null!, _relayClear = null!, _discordClear = null!, _discordSave = null!, _discordTest = null!;
    private readonly ProgressRing _relayBusy = new() { Width = 16, Height = 16, IsActive = false, Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, DashboardWrapPanel> _titleFlow = new(StringComparer.Ordinal);
    private enum ChipTone { Neutral, Positive, Warning }
    /// <summary>One status pill beside a channel title (official Chip: rounded-full, border, px-2 py-0.5, text-xs font-medium).</summary>
    private void AddChip(string channel, string key)
    {
        var text = U.Text("", 12); text.FontWeight = Microsoft.UI.Text.FontWeights.Medium; text.TextWrapping = TextWrapping.NoWrap; _chips[key] = text;
        var pill = new Border { Child = text, Padding = new(8, 2, 8, 2), CornerRadius = new(12), BorderThickness = new(1), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
        _titleFlow[channel].Children.Add(pill); SetChipTone(key, ChipTone.Neutral);
    }
    private void SetChip(string key, string text, ChipTone tone) { _chips[key].Text = text; SetChipTone(key, tone); }
    private void SetChipTone(string key, ChipTone tone)
    {
        var text = _chips[key]; if (text.Parent is not Border pill) return;
        var emerald = Windows.UI.Color.FromArgb(255, 0x10, 0xB9, 0x81); var amber = AdminMonitorUi.Amber500;
        switch (tone)
        {
            case ChipTone.Positive: pill.BorderBrush = AdminMonitorUi.Solid(emerald, .3); pill.Background = AdminMonitorUi.Solid(emerald, .1); text.Foreground = AdminMonitorUi.Solid(emerald); break;
            case ChipTone.Warning: pill.BorderBrush = AdminMonitorUi.Solid(amber, .3); pill.Background = AdminMonitorUi.Solid(amber, .1); text.Foreground = AdminMonitorUi.Solid(amber); break;
            default: pill.BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.ColorOf("BorderBrush", Windows.UI.Color.FromArgb(255, 63, 63, 70)), .7); pill.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); text.Foreground = U.Brush("SecondaryTextBrush"); break;
        }
    }
    private static Expander Disclosure(string label, string id, StackPanel content)
    {
        var expander = new Expander { Header = label, Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(0), Margin = new(0, 4, 0, 4) };
        AutomationProperties.SetAutomationId(expander, id); AutomationProperties.SetName(expander, label); return expander;
    }
    private StackPanel Advanced(StackPanel parent, string id, string automationId)
    {
        var stack = new StackPanel { Spacing = 4 }; var expander = Disclosure(T("web.settings.advancedSection.title"), automationId, stack);
        var count = AdvancedKeys(id).Count + (id == "relay" ? 2 : 0);
        AutomationProperties.SetName(expander, T("web.settings.advancedSection.accessibleLabel", ("title", T("web.settings.advancedSection.title")), ("count", count), ("count_text", count)));
        _advanced[id] = expander; parent.Children.Add(expander); return stack;
    }
    private static IReadOnlyList<string> AdvancedKeys(string id) => id switch
    {
        "relay" => ["notifications.apple_push_delivery_enabled", "notifications.android_push_delivery_enabled"],
        "email" => ["notifications.email.digest_hour"],
        "discord" => ["notifications.discord.poster_mode", "notifications.discord.digest_hour"],
        "webhooks" => ["notifications.webhooks.max_per_profile", "notifications.webhooks.deliveries_per_minute_per_profile", "notifications.webhooks.allow_private_destinations"],
        "server" => ["notifications.server_channels.batch_seconds", "notifications.server_channels.mention_requesters"],
        _ => [],
    };
    private StackPanel Channel(string id, string title, string description, string icon, params string[] keys)
    {
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, N("enableChannel", ("channel", title))); AutomationProperties.SetAutomationId(toggle, "NotificationsChannelToggle_" + id);
        var header = new Grid { ColumnSpacing = 12, Padding = new(0, 12, 0, 12), HorizontalAlignment = HorizontalAlignment.Stretch };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(AdminMonitorUi.Icon(icon, 20, U.Brush("SecondaryTextBrush")));
        var labels = new StackPanel { Spacing = 4 };
        // Official: title and status pills share one wrapping row (flex-wrap gap-x-2 gap-y-1).
        var titleLine = new DashboardWrapPanel { Spacing = 8, RowSpacing = 4 }; _titleFlow[id] = titleLine;
        var name = U.Text(title); name.VerticalAlignment = VerticalAlignment.Center; titleLine.Children.Add(name);
        AddChip(id, id);
        labels.Children.Add(titleLine); labels.Children.Add(U.Text(description, 12, true));
        Grid.SetColumn(labels, 1); header.Children.Add(labels); Grid.SetColumn(toggle, 2); header.Children.Add(toggle);
        var body = new StackPanel { Spacing = 4, Margin = new(20, 0, 8, 8) };
        Expander? expander = null;
        if (id is "inApp" or "webPush")
        {
            AutomationProperties.SetAutomationId(header, "NotificationChannel_" + id); _delivery.Add(header);
        }
        else
        {
            expander = Disclosure(title, "NotificationChannel_" + id, body); expander.Header = header;
            _delivery.Add(expander);
        }
        _channels[id] = (toggle, keys, expander);
        toggle.Toggled += (_, _) =>
        {
            if (_sync) return;
            foreach (var key in keys) Set(key, toggle.IsOn ? "true" : "false");
            if (toggle.IsOn && expander != null) expander.IsExpanded = true;
        };
        return body;
    }
    private Button Action(StackPanel parent, string label, string id, Func<System.Threading.Tasks.Task> action, Func<AdminNotificationSettingsState, bool>? enabled = null)
    {
        var button = U.Action(label, id); parent.Children.Add(button); button.Click += async (_, _) => await action();
        _actions.Add((button, enabled ?? (_ => true))); return button;
    }
    private static StackPanel Actions(StackPanel parent)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(0, 8, 0, 8) }; parent.Children.Add(panel); return panel;
    }
    private void AddRow(StackPanel parent, string key, string label, string description = "", string? unit = null, string kind = "number")
    {
        Control control;
        if (kind == "toggle")
        {
            var toggle = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 };
            toggle.Toggled += (_, _) => Set(key, toggle.IsOn ? "true" : "false"); control = toggle;
        }
        else if (kind == "secret")
        {
            var password = U.Secret(label, "Notifications_" + key); _secretSynced[key] = "";
            password.PasswordChanged += (_, _) =>
            {
                if (_sync || _controller?.State.Loaded != true) return;
                _secretSynced[key] = password.Password;
                if (password.Password != _controller.State.Values.GetValueOrDefault(key, "")) Set(key, password.Password);
            };
            control = password;
        }
        else if (kind == "select")
        {
            var combo = U.Select(label, "Notifications_" + key, [("provider", N("discord.artworkProvider")), ("server", N("discord.artworkServer")), ("off", N("discord.artworkOff"))]);
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string value }) Set(key, value); }; control = combo;
        }
        else
        {
            // An emptied box shows the default that an empty value means.
            var input = U.Input(label, "Notifications_" + key, C.Display(key, ""), kind == "number"); input.IsSpellCheckEnabled = false; input.IsTextPredictionEnabled = false;
            input.TextChanging += (_, _) => Set(key, input.Text); control = input;
        }
        AutomationProperties.SetAutomationId(control, "Notifications_" + key);
        var row = new AdminSettingsRow(label, description, control, unit); _controls[key] = control; _rows[key] = row; _descriptions[key] = description; parent.Children.Add(row);
    }
    private void BuildDelivery()
    {
        Channel("inApp", N("inApp.title"), N("inApp.description"), "inbox", "notifications.ui_enabled");
        Channel("webPush", N("webPush.title"), N("webPush.description"), "monitor-smartphone", "notifications.web_push_enabled");
        var relay = Channel("relay", "Silo Push Relay", N("relay.description"), "radio-tower", "notifications.apple_push_delivery_enabled", "notifications.android_push_delivery_enabled");
        relay.Children.Add(U.Text(T("web.notifications.pushPrivacy.title")));
        relay.Children.Add(U.Text(T("web.notifications.pushPrivacy.relay"), 12, true));
        Action(relay, "github.com/Silo-Server/silo-push-relay", "NotificationsRelaySource", () => Launch("https://github.com/Silo-Server/silo-push-relay"));
        relay.Children.Add(U.Text(T("web.notifications.pushPrivacy.metadata"), 12, true)); relay.Children.Add(U.Text(T("web.notifications.pushPrivacy.generic"), 12, true));
        var relayAdvanced = Advanced(relay, "relay", "NotificationsRelayAdvanced");
        AddRow(relayAdvanced, "notifications.apple_push_delivery_enabled", "Apple Push (APNs)", kind: "toggle");
        AddRow(relayAdvanced, "notifications.android_push_delivery_enabled", "Android Push (FCM)", kind: "toggle");
        relayAdvanced.Children.Add(new AdminSettingsRow(N("relay.url"), N("relay.urlDescription"), _relayUrl));
        _relayUrl.IsSpellCheckEnabled = false; _relayUrl.IsTextPredictionEnabled = false; _relayUrl.MaxLength = 2048;
        // Official: the relay URL reads left to right in a right-to-left interface (5537df46a).
        _relayUrl.FlowDirection = FlowDirection.LeftToRight;
        _relayUrl.TextChanging += (_, _) => { if (!_sync) _controller?.SetRelayUrl(_relayUrl.Text); };
        _deployment.IsReadOnly = true; _deployment.IsEnabled = false;
        relayAdvanced.Children.Add(new AdminSettingsRow(N("relay.deploymentId"), N("relay.deploymentIdDescription"), _deployment));
        relayAdvanced.Children.Add(_relayStatus);
        var relayActions = Actions(relayAdvanced);
        _relayRegister = Action(relayActions, N("relay.register"), "NotificationsRelayRegister", async () =>
        {
            var sent = await (_controller?.RegisterRelayAsync() ?? Task.FromResult(false));
            if (sent && _controller?.State is { Error: null, OperationUnknown: false, RelayRegistration: not null }) Toast(N("relay.registered"));
        }, RelayEditable);
        _relayClear = Action(relayActions, N("relay.clearCredential"), "NotificationsRelayClear", () => ConfirmAsync(N("relay.clearTitle"), N("relay.clearDescription"), N("relay.clearConfirm"),
            () => _controller?.ClearRelayAsync() ?? Task.FromResult(false), N("relay.cleared")), RelayEditable);
        relayActions.Children.Add(_relayBusy);

        var email = Channel("email", N("email.title"), N("email.description"), "mail", "notifications.email_enabled"); AddChip("email", "email.summary");
        email.Children.Add(_emailStatus);
        Action(email, T("web.admin.settings.email.title"), "NotificationsEmailSettings", () => { AdminShellPage.TryOpenHref("/admin/settings/email"); return Task.CompletedTask; });
        AddRow(email, "notifications.email.allow_per_episode", N("email.perEpisode"), N("perEpisodeOffDescription"), kind: "toggle");
        AddRow(Advanced(email, "email", "NotificationsEmailAdvanced"), "notifications.email.digest_hour", N("digestHour"), N("digestHourDescription"), T("web.admin.common.units.hourRange"));

        var discord = Channel("discord", "Discord", N("discord.description"), "bot", "notifications.discord_enabled");
        discord.Children.Add(U.Text(N("discord.application")));
        var guide = new StackPanel { Spacing = 8 };
        guide.Children.Add(U.Text(N("discord.guideIntro", ("site", "discord.com/developers/applications")), 12, true));
        Action(guide, "Discord Developer Portal", "NotificationsDiscordPortal", () => Launch("https://discord.com/developers/applications"));
        foreach (var item in new[] { "guideCreate", "guideOAuth", "guideBot", "guideGrant", "guideInvite" })
            guide.Children.Add(U.Text(N("discord." + item, ("callback", "<" + N("discord.guidePublicUrl") + ">/api/v2/notifications/discord/link/callback")), 12, true));
        discord.Children.Add(Disclosure(N("discord.setupGuide"), "NotificationsDiscordGuide", guide));
        AddRow(discord, "discord.client_id", N("discord.clientId"), N("discord.fromOAuth2Page"), kind: "text");
        var invite = Actions(discord);
        Action(invite, N("discord.invite"), "NotificationsDiscordInvite", () => Launch(InviteUrl()), s => !string.IsNullOrWhiteSpace(s.Values.GetValueOrDefault("discord.client_id")));
        Action(invite, N("discord.copyLink"), "NotificationsDiscordCopyInvite", CopyInvite, s => !string.IsNullOrWhiteSpace(s.Values.GetValueOrDefault("discord.client_id")));
        discord.Children.Add(_discordStatus);
        AddRow(discord, "discord.client_secret", N("discord.clientSecret"), N("discord.fromOAuth2Page"), kind: "secret");
        AddRow(discord, "discord.bot_token", N("discord.botToken"), N("discord.fromBotPage"), kind: "secret");
        var credentials = Actions(discord);
        _discordSave = Action(credentials, T("common.save"), "NotificationsDiscordSave", async () =>
        {
            await (_controller?.SaveDiscordAsync() ?? Task.CompletedTask);
            if (_controller?.State is { Error: null, Saving: false } done && !done.DirtyKeys.Any(C.DiscordKeys.Contains)) Toast(N("discord.saved"));
        }, s => !s.SensitiveStatusError && s.DirtyKeys.Any(C.DiscordKeys.Contains));
        _discordTest = Action(credentials, N("discord.testToken"), "NotificationsDiscordTest", () => _controller?.TestDiscordAsync() ?? Task.CompletedTask, s => s.DiscordReady && !s.DirtyKeys.Any(C.DiscordKeys.Contains));
        _discordClear = Action(credentials, N("discord.clearCredentials"), "NotificationsDiscordClear", () => ConfirmAsync(N("discord.clearTitle"), N("discord.clearDescription"), N("discord.clearConfirm"), () => _controller?.ClearDiscordAsync() ?? Task.FromResult(false), N("discord.cleared")),
            s => !s.SensitiveStatusError && !C.DiscordKeys.Any(s.ManagedByEnv.Contains));
        AutomationProperties.SetLiveSetting(_discordTestResult, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite); discord.Children.Add(_discordTestResult);
        discord.Children.Add(U.Text(N("discord.delivery")));
        AddRow(discord, "notifications.discord.allow_per_episode", N("discord.perEpisode"), N("perEpisodeOffDescription"), kind: "toggle");
        var discordAdvanced = Advanced(discord, "discord", "NotificationsDiscordAdvanced");
        AddRow(discordAdvanced, "notifications.discord.poster_mode", N("discord.artwork"), N("discord.artworkDescription"), kind: "select");
        AddRow(discordAdvanced, "notifications.discord.digest_hour", N("digestHour"), N("digestHourDescription"), T("web.admin.common.units.hourRange"));

        var webhooks = Channel("webhooks", N("webhooks.title"), N("webhooks.description"), "webhook", "notifications.webhooks_enabled");
        var hookAdvanced = Advanced(webhooks, "webhooks", "NotificationsWebhooksAdvanced");
        AddRow(hookAdvanced, "notifications.webhooks.max_per_profile", N("webhooks.perPerson"));
        AddRow(hookAdvanced, "notifications.webhooks.deliveries_per_minute_per_profile", N("webhooks.deliveries"), N("webhooks.deliveriesDescription"), N("webhooks.perMinute"));
        AddRow(hookAdvanced, "notifications.webhooks.allow_private_destinations", N("webhooks.allowPrivate"), N("webhooks.allowPrivateDescription"), kind: "toggle");
        hookAdvanced.Children.Add(_privateWarning);

        var server = Channel("server", N("serverChannelsCard.title"), N("serverChannelsCard.description"), "megaphone", "notifications.server_channels_enabled"); AddChip("server", "server.failing");
        BuildServerChannels(server);
        var serverAdvanced = Advanced(server, "server", "NotificationsServerAdvanced");
        AddRow(serverAdvanced, "notifications.server_channels.batch_seconds", N("serverChannelsCard.batchWindow"), N("serverChannelsCard.batchWindowDescription"), N("unit.seconds"));
        AddRow(serverAdvanced, "notifications.server_channels.mention_requesters", N("serverChannelsCard.mentionRequester"), N("serverChannelsCard.mentionRequesterDescription"), kind: "toggle");
    }
    private readonly TextBlock _privateWarning = AmberText(N("webhooks.allowPrivateWarning"));
    private static TextBlock AmberText(string text) { var block = U.Text(text, 12); block.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Amber500); return block; }
    private static void Toast(string message) => App.Services.GetService<SiloPlayer.Services.ToastService>()?.Success(message);
    private static bool RelayEditable(AdminNotificationSettingsState s) => !s.SensitiveStatusError && !C.ReadOnlyKeys.Where(k => k.StartsWith("notifications.push_relay_", StringComparison.Ordinal)).Any(s.ManagedByEnv.Contains);
    private void RenderSetup(AdminNotificationSettingsState s)
    {
        string Value(string k) => s.Values.GetValueOrDefault(k, "");
        var registered = Value("notifications.push_relay_deployment_id").Length > 0;
        var reregister = Value("notifications.push_relay_reregistration_required") == "true";
        _relayRegister.Content = N(reregister ? "relay.reregister" : registered ? "relay.rotate" : "relay.register");
        AutomationProperties.SetName(_relayRegister, _relayRegister.Content.ToString());
        _relayBusy.IsActive = s.OperationPending && s.ActiveOperation is "relay-register" or "relay-clear";
        _relayBusy.Visibility = _relayBusy.IsActive ? Visibility.Visible : Visibility.Collapsed;
        _relayClear.Content = N(s.ActiveOperation == "relay-clear" ? "relay.clearing" : "relay.clearCredential");
        _relayClear.Visibility = registered ? Visibility.Visible : Visibility.Collapsed;
        SetChip("relay", N(reregister ? "relay.chipReregister" : s.Configured.Contains("notifications.push_relay_api_key") ? "relay.chipConfigured" : "relay.chipFirstSend"),
            reregister ? ChipTone.Warning : s.Configured.Contains("notifications.push_relay_api_key") ? ChipTone.Positive : ChipTone.Neutral);
        _deployment.Text = Value("notifications.push_relay_deployment_id");
        var lines = new List<string>();
        if (Value("notifications.push_relay_key_prefix").Length > 0) lines.Add(N("relay.credential", ("prefix", Value("notifications.push_relay_key_prefix"))));
        if (DateTimeOffset.TryParse(Value("notifications.push_relay_expires_at"), out var expiration)) lines.Add(expiration <= DateTimeOffset.UtcNow ? N("relay.expired") : N("relay.expires", ("date", DateTimeDisplay.FormatDateTime(expiration))));
        else lines.Add(N(registered ? "relay.expirationUnknown" : reregister ? "relay.noCredential" : "relay.notRegistered"));
        if (reregister) lines.Add(N("relay.reregistrationNote")); if (s.RelayUrlDirty) lines.Add(N("relay.applyUrl"));
        if (s.RelayRegistration is { } result) lines.Add(N(result.KeyPrefix != null && result.RelayRequestId != null ? "relay.readyKeyRelay" : result.KeyPrefix != null ? "relay.readyKey" : result.RelayRequestId != null ? "relay.readyRelay" : "relay.ready", ("deployment", result.DeploymentId), ("key", result.KeyPrefix), ("request", result.RelayRequestId)));
        U.UpdateLiveText(_relayStatus, string.Join("\n", lines));
        var hour = int.TryParse(Value("notifications.email.digest_hour"), out var h) && h is >= 0 and <= 23 ? h : 8;
        SetChip("email", N(s.MailReady ? "email.mailReady" : "email.mailMissing"), s.MailReady ? ChipTone.Positive : ChipTone.Neutral);
        SetChip("email.summary", N("email.summaryAt", ("time", $"{hour:00}:00")), ChipTone.Neutral);
        _emailStatus.Text = N(s.MailReady ? "email.mailReadyStatus" : "email.mailMissingStatus");
        SetChip("discord", N(s.DiscordReady ? "discord.appConnected" : "discord.appNotConnected"), s.DiscordReady ? ChipTone.Positive : On(s, "notifications.discord_enabled") ? ChipTone.Warning : ChipTone.Neutral);
        _discordStatus.Text = N(string.IsNullOrWhiteSpace(Value("discord.client_id")) ? "discord.enterClientId" : "discord.mustBeInServer") +
            (s.DirtyKeys.Any(C.DiscordKeys.Contains) ? "\n" + N("discord.saveFirst") : "");
        _discordClear.Visibility = s.Configured.Any(C.DiscordKeys.Contains) || !string.IsNullOrWhiteSpace(s.PersistedValues.GetValueOrDefault("discord.client_id")) ? Visibility.Visible : Visibility.Collapsed;
        _discordSave.Content = s.SavingDiscord ? N("discord.saving") : T("common.save");
        _discordTest.Content = N(s.ActiveOperation == "discord-test" ? "discord.testing" : "discord.testToken");
        AutomationProperties.SetName(_discordSave, _discordSave.Content.ToString()); AutomationProperties.SetName(_discordTest, _discordTest.Content.ToString());
        U.UpdateLiveText(_discordTestResult, s.DiscordTestResult is { } test ? TestText(test, "discord") : "");
        // Official: green for a successful test, red for a failed one.
        _discordTestResult.Foreground = AdminMonitorUi.Solid(s.DiscordTestResult?.Ok == true ? Windows.UI.Color.FromArgb(255, 0x4A, 0xDE, 0x80) : AdminMonitorUi.LiveText);
        _privateWarning.Visibility = On(s, "notifications.webhooks.allow_private_destinations") ? Visibility.Visible : Visibility.Collapsed;
        SetChip("webhooks", _privateWarning.Visibility == Visibility.Visible ? N("webhooks.privateAllowed") : "", ChipTone.Warning);
    }
    private static string TestText(AdminNotificationTestResult result, string section) => N(section + (string.IsNullOrEmpty(result.Message) ? ".testResult" : ".testResultWithMessage"),
        ("outcome", result.Ok ? "ok" : "failed"), ("status", result.HttpStatus?.ToString() ?? "none"), ("duration_ms", result.DurationMs), ("message", result.Message));
    private string InviteUrl() => "https://discord.com/oauth2/authorize?client_id=" + Uri.EscapeDataString(_controller?.State.Values.GetValueOrDefault("discord.client_id", "").Trim() ?? "") + "&scope=bot&permissions=0";
    private async Task CopyInvite()
    {
        try { var data = new DataPackage(); data.SetText(InviteUrl()); Clipboard.SetContent(data); Clipboard.Flush(); _discordStatus.Text = N("discord.inviteCopied"); }
        catch { ReportLocalError(N("discord.inviteCopyFailed")); }
        await Task.CompletedTask;
    }
    private async Task Launch(string url)
    {
        try { if (_controller?.State.Owner?.IsCurrent == true && ExternalBrowserUrlPolicy.TryGetSafeUri(url, out var safeUri) && !await Windows.System.Launcher.LaunchUriAsync(safeUri)) ReportLocalError("Could not open the setup link."); }
        catch { ReportLocalError("Could not open the setup link."); }
    }
    private void ReportLocalError(string message) { _error.Text = message; _errorAlert.Visibility = Visibility.Visible; }
}
