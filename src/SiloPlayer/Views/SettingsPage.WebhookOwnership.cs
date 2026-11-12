using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private AuthService _webhookAuth = null!;
    private SiloApiClient _webhookClient = null!;
    private int _webhookGeneration;
    private int _webhookDetailGeneration;
    private bool _webhookActive;
    private CancellationTokenSource _webhookLifetime = new();
    private ContentDialog? _webhookEventDialog;
    private sealed record WebhookOwner(ApiRequestContext Context, int Generation, CancellationToken Token);
    private bool WebhookAvailable => _webhookAuth != null && AuthorizationPolicy.CanManageHousehold(_webhookAuth);

    private void InitializeWebhookOwnership()
    {
        _webhookAuth = App.Services.GetRequiredService<AuthService>();
        _webhookClient = App.Services.GetRequiredService<SiloApiClient>();
        Loaded += (_, _) =>
        {
            _webhookActive = true;
            _webhookAuth.ProfileChanged += WebhookAuthorityChanged;
            _webhookAuth.UserChanged += WebhookAuthorityChanged;
            _webhookAuth.LoggedOut += WebhookAuthorityChanged;
            _webhookClient.ContextInvalidated += WebhookAuthorityChanged;
            BuildSettingsOverview();
            if (ActiveSettingsSection == "WebhookSync") _ = LoadWebhookConnectionsAsync();
        };
        Unloaded += (_, _) =>
        {
            _webhookActive = false;
            _webhookAuth.ProfileChanged -= WebhookAuthorityChanged;
            _webhookAuth.UserChanged -= WebhookAuthorityChanged;
            _webhookAuth.LoggedOut -= WebhookAuthorityChanged;
            _webhookClient.ContextInvalidated -= WebhookAuthorityChanged;
            RetireWebhookOwnership();
        };
    }

    private void WebhookAuthorityChanged()
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(WebhookAuthorityChanged); return; }
        RetireWebhookOwnership();
        BuildSettingsOverview();
        if (ActiveSettingsSection == "WebhookSync") ShowSettingsOverview();
    }

    private void RetireWebhookOwnership()
    {
        ++_webhookGeneration;
        ++_webhookDetailGeneration;
        _webhookLifetime.Cancel(); _webhookLifetime.Dispose(); _webhookLifetime = new();
        _webhookPlexAuthCts?.Cancel();
        _webhookRefreshTimer?.Stop();
        _webhookRefreshBusy = false;
        _webhookEventDialog?.Hide(); _webhookEventDialog = null;
        _selectedWebhookConnection = null;
        _webhookProfiles.Clear(); _webhookEvents.Clear(); _webhookPlexServers.Clear();
        _webhookActorSelectors.Clear(); _webhookConnectionCards.Clear(); _webhookEventsPage = 0;
        WebhookConnectionsPanel.Children.Clear(); WebhookActorsHost.Children.Clear(); WebhookEventsHost.Children.Clear();
        WebhookSetupInstructionsHost.Children.Clear();
        WebhookDefaultProfileCombo.Items.Clear(); WebhookConnectionProfileCombo.Items.Clear(); WebhookPlexServerCombo.Items.Clear();
        WebhookUrlBox.Text = WebhookConnectionNameBox.Text = WebhookManualServerName.Text = "";
        WebhookDetailName.Text = WebhookDetailHealth.Text = WebhookDetailStatus.Text = WebhookPlexAuthStatus.Text = "";
        WebhookPlexSignInButton.Content = "Sign in to Plex"; WebhookPlexSignInButton.IsEnabled = true;
        WebhookConnectionDetail.Visibility = WebhookPlexServerRow.Visibility = Visibility.Collapsed;
        WebhookCreateConnectionButton.Content = "Create connection";
        WebhookDeleteConnectionButton.Content = "Delete connection";
        WebhookDeleteConnectionButton.IsEnabled = true;
        WebhookSyncPanel.Visibility = Visibility.Collapsed;
        NoConnectionsText.Text = "Switch to the primary profile and verify its PIN if requested, then reopen Webhook Sync.";
        NoConnectionsText.Visibility = Visibility.Visible;
        UpdateWebhookCreateButtonState();
    }

    private WebhookOwner? CaptureWebhookOwner()
        => _webhookActive && WebhookAvailable ? new(_webhookClient.CaptureContext(), _webhookGeneration, _webhookLifetime.Token) : null;

    private bool CurrentWebhook(WebhookOwner owner)
        => _webhookActive && WebhookAvailable && owner.Generation == _webhookGeneration &&
           !owner.Token.IsCancellationRequested && _webhookClient.IsCurrentContext(owner.Context);

    private void WebhookFailure(WebhookOwner owner, Exception error)
    {
        if (!CurrentWebhook(owner) || error is OperationCanceledException) return;
        var guidance = AuthorizationPolicy.HouseholdManagementGuidance(error);
        if (error is ApiException { StatusCode: 403 }) RetireWebhookOwnership();
        ViewModel.ErrorMessage = guidance;
    }
}
