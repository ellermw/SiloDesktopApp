using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminIntegrationsPage
{
    private AdminChatGptLogin? _login;
    private ContentDialog? _loginDialog;
    private PasswordBox? _callbackBox;
    private Button? _completeLogin;
    private TextBlock? _callbackError;
    private bool _callbackSubmitted;
    private const string LoginReadFailed = "Could not check this sign-in with the server. Still retrying.";

    private void BuildChatGptAccounts()
    {
        if (_capabilities?.ChatGptSignIn != true)
        {
            if (Value("ai.auth_mode") == "chatgpt") _domainParts.Add(AdminSettingsUi.Alert("ChatGPT sign-in is not available on this server. Choose an API provider or retry capabilities."));
            return;
        }
        var panel = new StackPanel { Spacing = 10 };
        if (_accountModelsError != null) panel.Children.Add(AdminSettingsUi.Alert(_accountModelsError));
        var accounts = _connection?.Accounts ?? [];
        var select = AdminSettingsUi.Select("ChatGPT account", "AdminChatGptAccount", accounts.Select(account => (account.ClientId,
            (account.Email == "" ? "Saved account" : account.Email) + " · " + AccountState(account))));
        foreach (var item in select.Items.OfType<ComboBoxItem>()) item.IsEnabled = accounts.Any(account => account.ClientId == item.Tag?.ToString() && account.Selectable);
        select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == _connection?.ActiveClientId);
        select.IsEnabled = Ready;
        select.SelectionChanged += async (_, _) =>
        {
            if (select.SelectedItem is not ComboBoxItem item || item.Tag?.ToString() is not { } id || id == _connection?.ActiveClientId) return;
            await RunCommandAsync(async lease =>
            {
                var status = await _api.SelectChatGptAccountAsync(lease, id);
                if (!_active || !lease.IsCurrent) return;
                _connection = status; _accountModels = []; await _controller!.ReloadAsync(true);
            }, () => _connection?.Accounts.Any(account => account.ClientId == id && account.Selectable) == true);
        };
        panel.Children.Add(Field("Account", "Accounts remain registered after disconnect so they can be reconnected.", select));
        AddAction(panel, "Sign in with ChatGPT", "AdminChatGptConnect", Ready && _connection != null, () => BeginLoginAsync(null));
        foreach (var account in accounts)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(AdminSettingsUi.Text((account.Email == "" ? "Saved account" : account.Email) + " · " + AccountState(account), 13, true));
            if (!account.PlanIneligible && (!account.UsageLimited || account.NeedsLogin))
                AddAction(row, "Reconnect", "AdminChatGptReconnect-" + account.ClientId, Ready, () => BeginLoginAsync(account.ClientId));
            if (account.Connected || account.UsageLimited || account.NeedsLogin)
                AddAction(row, "Disconnect", "AdminChatGptDisconnect-" + account.ClientId, Ready, () => RunCommandAsync(async lease =>
                {
                    var revoked = await _api.DisconnectChatGptAccountAsync(lease, account.ClientId);
                    SetDomainStatus(lease, revoked ? "ChatGPT account disconnected." : "Disconnected locally. Remote revocation was not confirmed; review the account's connected apps.");
                    if (lease.IsCurrent && _active) { _accountModels = []; await _controller!.ReloadAsync(true); }
                }, () => _connection?.Accounts.Any(current => current.ClientId == account.ClientId && (current.Connected || current.UsageLimited || current.NeedsLogin)) == true,
                    "Disconnect ChatGPT account", "Revoke this account's connection to the server? Its registration is kept for reconnect."));
            panel.Children.Add(row);
        }
        if (_connection?.Active is { UsageLimited: true }) panel.Children.Add(AdminSettingsUi.Alert("ChatGPT usage is paused. Test the text model to recheck usage, or choose another provider."));
        if (_connection?.Active is { PlanIneligible: true }) panel.Children.Add(AdminSettingsUi.Alert("This account's plan is not eligible. Choose another connected account."));
        _domainParts.Add(Group("ChatGPT plan", "Sign-in runs in your own browser. Paste the final callback URL yourself; the player never reads browser cookies or account credentials.", panel));
    }
    private static string AccountState(AdminChatGptAccount account) => account.PlanIneligible ? "Plan ineligible" : account.NeedsLogin ? "Sign-in required" : account.UsageLimited ? "Usage paused" : account.Connected ? "Connected" : "Disconnected";

    private async Task BeginLoginAsync(string? accountId)
    {
        if (_dialog != null) return;
        await RunCommandAsync(async lease =>
        {
            var login = await _api.StartChatGptLoginAsync(lease, accountId);
            if (_active && lease.IsCurrent) { ClearLogin(); _login = login; _callbackSubmitted = false; }
        }, () => _capabilities?.ChatGptSignIn == true && (accountId == null || _connection?.Accounts.Any(account => account.ClientId == accountId && !account.PlanIneligible) == true));
        if (_login == null || !Ready || _controller == null) return;
        using var owner = _controller.CaptureAction();
        var panel = new StackPanel { Spacing = 12, MaxWidth = 560 };
        panel.Children.Add(AdminSettingsUi.Text("Open ChatGPT in your browser, choose the account to connect, then paste the final callback URL. Do not paste your password or API key.", 14, true));
        var open = AdminSettingsUi.Action("Open sign-in in my browser", "AdminChatGptOpenBrowser");
        open.Click += (_, _) => _browserTask = OpenBrowserAsync();
        async Task OpenBrowserAsync()
        {
            var login = _login; if (login == null || !owner.IsCurrent || !_active) return;
            try
            {
                var browser = App.Services.GetRequiredService<IAdminChatGptBrowser>();
                if (!await browser.OpenAsync(login.AuthorizationUrl, owner) && owner.IsCurrent && _callbackError != null) _callbackError.Text = "The browser could not be opened. Close this dialog and retry sign-in.";
            }
            catch (OperationCanceledException) { }
            catch { if (owner.IsCurrent && _callbackError != null) _callbackError.Text = "The browser could not be opened."; }
        }
        panel.Children.Add(open);
        panel.Children.Add(AdminSettingsUi.Text("Expected callback: " + _login.CallbackUri + " (query hidden)", 13, true));
        _callbackBox = AdminSettingsUi.Secret("Final callback URL", "AdminChatGptCallback"); _callbackBox.Width = 480;
        _callbackError = AdminSettingsUi.Text("", 13, true); panel.Children.Add(_callbackBox); panel.Children.Add(_callbackError);
        _completeLogin = AdminSettingsUi.Action("Complete sign-in", "AdminChatGptComplete", primary: true);
        _completeLogin.Click += (_, _) => _callbackTask = InvokeInteractionAsync(CompleteLoginAsync); panel.Children.Add(_completeLogin);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Sign in with ChatGPT", Content = panel, CloseButtonText = "Cancel" };
        _loginDialog = dialog;
        try { await ShowDialogAsync(dialog, owner); }
        finally { ClearLogin(); SchedulePoll(); }
    }
    private async Task CompleteLoginAsync()
    {
        var box = _callbackBox; var login = _login;
        if (box == null) return;
        var callback = box.Password.Trim(); box.Password = "";
        try
        {
            if (login == null || !Ready || _callbackSubmitted) return;
            if (!AdminIntegrationPolicy.IsCallback(callback, login.CallbackUri)) { if (_callbackError != null) _callbackError.Text = "Paste the final callback URL from this sign-in attempt, with its state and code or error."; return; }
            _callbackSubmitted = true;
            var completed = false;
            await RunCommandAsync(async lease =>
            {
                await _api.CompleteChatGptLoginAsync(lease, callback);
                completed = true;
                if (_active && lease.IsCurrent) await _controller!.ReloadAsync(true);
            }, () => ReferenceEquals(_login, login));
            if (!completed && !_commandUnknown && ReferenceEquals(_login, login))
            {
                _callbackSubmitted = false;
                if (_callbackError != null) _callbackError.Text = "The callback was not accepted. Paste the correct final URL, or cancel and start a new sign-in.";
            }
        }
        finally { box.Password = ""; callback = ""; if (_completeLogin != null) _completeLogin.IsEnabled = !_callbackSubmitted && Ready; }
    }
    /// <summary>Settles the pending sign-in against a status read; returns the failure to show, which the caller keeps past its own error reset.</summary>
    private string? ObserveLogin(AdminChatGptStatus status)
    {
        var login = _login; if (login == null) return null;
        if (status.AttemptId == login.AttemptId && status.LoginResult == "connected")
        {
            AdminSettingsUi.UpdateLiveText(_domainStatus, status.FirstConnection ? "ChatGPT connected. The server's initial text model settings have been enabled; reload and review them." : "ChatGPT account connected.");
            _loginDialog?.Hide(); ClearLogin();
        }
        else if ((status.AttemptId == login.AttemptId && status.LoginResult is "failed" or "expired") || DateTimeOffset.UtcNow >= login.ExpiresAt)
        {
            _loginDialog?.Hide(); ClearLogin();
            return "ChatGPT sign-in failed or expired. Start a new sign-in attempt.";
        }
        return null;
    }
    private void ClearLogin()
    {
        if (_callbackBox != null) _callbackBox.Password = "";
        if (_callbackError != null) _callbackError.Text = "";
        _login = null; _callbackSubmitted = false; _callbackBox = null; _callbackError = null; _completeLogin = null; _loginDialog = null;
    }
}
