using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private AuthService _webhookAuth = null!;
    private SiloApiClient _webhookClient = null!;
    private int _webhookGeneration;
    private int _webhookDetailGeneration;
    private bool _webhookActive;
    private ApiIdentityContext _webhookIdentity;
    private long _webhookAccessGeneration;
    private CancellationTokenSource _webhookLifetime = new();
    private ContentDialog? _webhookEventDialog;
    private sealed class WebhookOwner(ApiRequestContext context, ApiIdentityContext identity, int generation, CancellationToken token)
    {
        public ApiRequestContext Context { get; set; } = context;
        public ApiIdentityContext Identity { get; } = identity;
        public int Generation { get; } = generation;
        public CancellationToken Token { get; } = token;
    }
    private bool WebhookAvailable => _webhookAuth != null && AuthorizationPolicy.CanManageHousehold(_webhookAuth);

    private void InitializeWebhookOwnership()
    {
        _webhookAuth = App.Services.GetRequiredService<AuthService>();
        _webhookClient = App.Services.GetRequiredService<SiloApiClient>();
        Loaded += (_, _) =>
        {
            _webhookActive = true;
            _webhookIdentity = _webhookClient.CaptureIdentityContext();
            _webhookAccessGeneration = _webhookClient.CaptureContext().AccessGeneration;
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
        var identity = _webhookClient.CaptureIdentityContext();
        var current = _webhookClient.CaptureContext();
        var sameIdentity = identity == _webhookIdentity;
        // Transport catch-up invalidates reads only. Keep an unchanged
        // manager's draft and Plex setup instead of ejecting them.
        if (_webhookActive && sameIdentity && WebhookAvailable && current.AccessGeneration == _webhookAccessGeneration) return;
        var reloadSection = sameIdentity && WebhookAvailable && ActiveSettingsSection == "WebhookSync";
        RetireWebhookOwnership();
        _webhookIdentity = identity;
        _webhookAccessGeneration = current.AccessGeneration;
        BuildSettingsOverview();
        if (reloadSection)
        {
            WebhookSyncPanel.Visibility = Visibility.Visible;
            _ = LoadWebhookConnectionsAsync();
        }
        else if (ActiveSettingsSection == "WebhookSync") ShowSettingsOverview();
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
        _webhookCreating = _webhookDeleting = false;
        WebhookPlexSignInButton.Content = L("web.settings.webhooks.add.signInToPlex", "Sign in to Plex"); WebhookPlexSignInButton.IsEnabled = true;
        WebhookConnectionDetail.Visibility = WebhookPlexServerRow.Visibility = Visibility.Collapsed;
        WebhookCreateConnectionButton.Content = L("web.settings.webhooks.add.create", "Create connection");
        WebhookDeleteConnectionButton.Content = L("web.settings.webhooks.detail.delete", "Delete connection");
        WebhookDeleteConnectionButton.IsEnabled = true;
        WebhookSyncPanel.Visibility = Visibility.Collapsed;
        NoConnectionsText.Text = "Switch to the primary profile and verify its PIN if requested, then reopen Webhook Sync.";
        NoConnectionsText.Visibility = Visibility.Visible;
        UpdateWebhookCreateButtonState();
    }

    /// <summary>Webhook action labels that code rewrites while work is pending, in the current language.</summary>
    private bool _webhookCreating, _webhookDeleting;
    private string? _webhookPlexHint;

    private void RefreshWebhookActionLabels()
    {
        if (!_webhookCreating) WebhookCreateConnectionButton.Content = L("web.settings.webhooks.add.create", "Create connection");
        if (!_webhookDeleting) WebhookDeleteConnectionButton.Content = L("web.settings.webhooks.detail.delete", "Delete connection");
        if (WebhookPlexSignInButton.IsEnabled)
            WebhookPlexSignInButton.Content = _webhookPlexServers.Count > 0
                ? L("web.settings.webhooks.add.reauthenticate", "Re-authenticate")
                : L("web.settings.webhooks.add.signInToPlex", "Sign in to Plex");
        // Only the initial hint follows the language; a sign-in status stays as reported.
        if (_webhookPlexHint == null || WebhookPlexAuthStatus.Text == _webhookPlexHint)
            WebhookPlexAuthStatus.Text = _webhookPlexHint = L("web.settings.webhooks.add.plexAuthorizeHint", "Authorize via plex.tv to discover your servers");
    }

    private WebhookOwner? CaptureWebhookOwner()
    {
        if (!_webhookActive || !WebhookAvailable) return null;
        var identity = _webhookIdentity;
        var context = _webhookClient.CaptureContext();
        return _webhookClient.IsCurrentIdentityContext(identity) && context.AccessGeneration == _webhookAccessGeneration
            ? new(context, identity, _webhookGeneration, _webhookLifetime.Token) : null;
    }

    private bool CurrentWebhook(WebhookOwner owner)
    {
        if (!_webhookActive || !WebhookAvailable || owner.Generation != _webhookGeneration || owner.Token.IsCancellationRequested) return false;
        var current = _webhookClient.CaptureContext();
        if (!_webhookClient.IsCurrentIdentityContext(owner.Identity) || current.AccessGeneration != owner.Context.AccessGeneration) return false;
        // Future operations in a same-identity setup use fresh read authority;
        // an already dispatched API request still checks its original context.
        owner.Context = current;
        return true;
    }

    private void WebhookFailure(WebhookOwner owner, Exception error, bool silentTransient = false)
    {
        if (!CurrentWebhook(owner) || error is OperationCanceledException) return;
        var guidance = AuthorizationPolicy.HouseholdManagementGuidance(error);
        if (error is ApiException { StatusCode: 403 })
        {
            RetireWebhookOwnership();
            WebhookSyncPanel.Visibility = Visibility.Visible;
            NoConnectionsText.Text = guidance; NoConnectionsText.Visibility = Visibility.Visible;
        }
        else if (silentTransient) return;
        ViewModel.ErrorMessage = guidance;
    }
}
