using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.ViewModels;

public partial class NotificationSettingsViewModel(NotificationsApi notificationsApi) : ObservableObject
{
    public ObservableCollection<NotificationWebhook> Webhooks { get; } = [];
    public ObservableCollection<WebPushSubscriptionView> WebPushSubscriptions { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private NotificationPreferences _preferences = new();
    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private bool _notifyFavorites;
    [ObservableProperty] private bool _notifyWatchlist;
    [ObservableProperty] private bool _notifyContinueWatching;
    [ObservableProperty] private bool _notifyNextUp;
    [ObservableProperty] private NotificationCapability _capability = new();
    [ObservableProperty] private NotificationEmailPreferences _emailPreferences = new();
    [ObservableProperty] private NotificationDiscordPreferences _discordPreferences = new();
    [ObservableProperty] private string _emailMode = "off";
    [ObservableProperty] private string _discordMode = "off";
    [ObservableProperty] private string _emailAddressInput = "";
    [ObservableProperty] private bool _hasEmail;
    [ObservableProperty] private bool _hasDiscord;
    [ObservableProperty] private bool _hasWebPush;
    [ObservableProperty] private bool _hasWebhooks;
    [ObservableProperty] private bool _hasWebPushSubscriptions;
    [ObservableProperty] private bool _hasWebhookEntries;
    [ObservableProperty] private int _maxWebhooks = 10;

    public string EmailDigestDescription => $"Daily digest around {Capability.Email.DigestHour:00}:00 server time.";
    public string DiscordDigestDescription => $"Daily digest around {Capability.Discord.DigestHour:00}:00 server time.";
    public bool IsEmailEnabled => !string.Equals(EmailMode, "off", StringComparison.Ordinal);
    public bool IsDiscordEnabled => !string.Equals(DiscordMode, "off", StringComparison.Ordinal);
    public bool HasVerifiedEmail => !string.IsNullOrWhiteSpace(EmailPreferences.CustomEmail);
    public bool HasPendingEmail => !string.IsNullOrWhiteSpace(EmailPreferences.PendingEmail);
    public bool CanToggleEmail => IsEmailEnabled || HasVerifiedEmail;
    public string EmailAddressActionText => HasVerifiedEmail ? "Change" : "Add address";
    public string EmailDestinationText => HasVerifiedEmail
        ? EmailPreferences.CustomEmail
        : "No address set — verify one to receive emails";
    public string EmailPendingText => HasPendingEmail
        ? $"Verification email sent to {EmailPreferences.PendingEmail} — it becomes active once the link in it is opened."
        : "";
    public bool CanAddWebhook => HasWebhooks && Webhooks.Count < MaxWebhooks;
    public bool IsWebhookLimitReached => HasWebhooks && !CanAddWebhook;
    public string WebhookLimitText => $"Limit of {MaxWebhooks} webhooks reached";

    partial void OnEmailModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsEmailEnabled));
        OnPropertyChanged(nameof(CanToggleEmail));
    }
    partial void OnDiscordModeChanged(string value) => OnPropertyChanged(nameof(IsDiscordEnabled));
    partial void OnEmailPreferencesChanged(NotificationEmailPreferences value)
    {
        OnPropertyChanged(nameof(HasVerifiedEmail));
        OnPropertyChanged(nameof(HasPendingEmail));
        OnPropertyChanged(nameof(CanToggleEmail));
        OnPropertyChanged(nameof(EmailAddressActionText));
        OnPropertyChanged(nameof(EmailDestinationText));
        OnPropertyChanged(nameof(EmailPendingText));
    }

    partial void OnMaxWebhooksChanged(int value)
    {
        OnPropertyChanged(nameof(CanAddWebhook));
        OnPropertyChanged(nameof(IsWebhookLimitReached));
        OnPropertyChanged(nameof(WebhookLimitText));
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var prefsTask = notificationsApi.GetPreferencesAsync();
            var capabilityTask = notificationsApi.GetCapabilityAsync();
            await Task.WhenAll(prefsTask, capabilityTask);
            Preferences = prefsTask.Result;
            NotificationsEnabled = Preferences.Enabled;
            NotifyFavorites = Preferences.NotifyFavorites;
            NotifyWatchlist = Preferences.NotifyWatchlist;
            NotifyContinueWatching = Preferences.NotifyContinueWatching;
            NotifyNextUp = Preferences.NotifyNextUp;
            Capability = capabilityTask.Result;
            HasEmail = Capability.Email.Available;
            HasDiscord = Capability.Discord.Available;
            HasWebPush = Capability.WebPush.Available;
            HasWebhooks = Capability.Webhooks.Available;
            MaxWebhooks = Capability.Webhooks.MaxPerProfile > 0 ? Capability.Webhooks.MaxPerProfile : 10;

            var optional = new List<Task>();
            Task<NotificationEmailPreferences>? emailTask = null;
            Task<NotificationDiscordPreferences>? discordTask = null;
            Task<List<WebPushSubscriptionView>>? pushTask = null;
            Task<List<NotificationWebhook>>? webhooksTask = null;
            if (HasEmail) { emailTask = notificationsApi.GetEmailPreferencesAsync(); optional.Add(emailTask); }
            if (HasDiscord) { discordTask = notificationsApi.GetDiscordPreferencesAsync(); optional.Add(discordTask); }
            if (HasWebPush) { pushTask = notificationsApi.GetWebPushSubscriptionsAsync(); optional.Add(pushTask); }
            if (HasWebhooks) { webhooksTask = notificationsApi.GetWebhooksAsync(); optional.Add(webhooksTask); }
            await Task.WhenAll(optional);

            if (emailTask is not null)
            {
                EmailPreferences = emailTask.Result;
                EmailMode = EmailPreferences.Mode;
            }
            if (discordTask is not null)
            {
                DiscordPreferences = discordTask.Result;
                DiscordMode = DiscordPreferences.Mode;
            }
            WebPushSubscriptions.Clear();
            if (pushTask is not null)
                foreach (var subscription in pushTask.Result) WebPushSubscriptions.Add(subscription);
            Webhooks.Clear();
            if (webhooksTask is not null)
                foreach (var webhook in webhooksTask.Result) Webhooks.Add(webhook);
            RefreshCollectionState();
            OnPropertyChanged(nameof(EmailDigestDescription));
            OnPropertyChanged(nameof(DiscordDigestDescription));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SavePreferencesAsync()
        => await RunAsync(async () =>
        {
            Preferences = await notificationsApi.UpdatePreferencesAsync(new NotificationPreferences
            {
                ProfileId = Preferences.ProfileId,
                Enabled = NotificationsEnabled,
                NotifyFavorites = NotifyFavorites,
                NotifyWatchlist = NotifyWatchlist,
                NotifyContinueWatching = NotifyContinueWatching,
                NotifyNextUp = NotifyNextUp,
            });
            NotificationsEnabled = Preferences.Enabled;
            NotifyFavorites = Preferences.NotifyFavorites;
            NotifyWatchlist = Preferences.NotifyWatchlist;
            NotifyContinueWatching = Preferences.NotifyContinueWatching;
            NotifyNextUp = Preferences.NotifyNextUp;
            StatusMessage = "Notification preferences saved.";
        });

    public async Task SaveEmailEnabledAsync(bool enabled)
    {
        if (enabled && !HasVerifiedEmail) return;
        EmailMode = enabled ? "daily_digest" : "off";
        await SaveEmailModeAsync();
    }

    public async Task SaveDiscordEnabledAsync(bool enabled)
    {
        if (!DiscordPreferences.Linked) return;
        DiscordMode = enabled ? "daily_digest" : "off";
        await SaveDiscordModeAsync();
    }

    public async Task SaveEmailModeAsync()
        => await RunAsync(async () =>
        {
            EmailPreferences = await notificationsApi.UpdateEmailPreferencesAsync(
                new NotificationEmailPreferencesUpdate { Mode = EmailMode });
            EmailMode = EmailPreferences.Mode;
            StatusMessage = "Email notification frequency saved.";
        });

    public async Task RequestEmailAddressAsync()
    {
        var address = EmailAddressInput.Trim();
        if (address.Length == 0) return;
        await RunAsync(async () =>
        {
            EmailPreferences = await notificationsApi.RequestEmailAddressAsync(address);
            EmailAddressInput = "";
            StatusMessage = $"Verification email sent to {EmailPreferences.PendingEmail}.";
        });
    }

    public async Task ClearEmailAddressAsync()
        => await RunAsync(async () =>
        {
            EmailPreferences = await notificationsApi.ClearEmailAddressAsync();
            EmailMode = EmailPreferences.Mode;
            StatusMessage = "Email destination removed.";
        });

    public async Task<string?> StartDiscordLinkAsync()
    {
        string? url = null;
        await RunAsync(async () => url = (await notificationsApi.StartDiscordLinkAsync()).Url);
        return url;
    }

    public async Task SaveDiscordModeAsync()
        => await RunAsync(async () =>
        {
            DiscordPreferences = await notificationsApi.UpdateDiscordPreferencesAsync(DiscordMode);
            DiscordMode = DiscordPreferences.Mode;
            StatusMessage = "Discord notification frequency saved.";
        });

    public async Task UnlinkDiscordAsync()
        => await RunAsync(async () =>
        {
            await notificationsApi.UnlinkDiscordAsync();
            DiscordPreferences = new NotificationDiscordPreferences();
            DiscordMode = "off";
            StatusMessage = "Discord account unlinked.";
        });

    public async Task<NotificationWebhook?> SaveWebhookAsync(string? id, NotificationWebhookInput input)
    {
        NotificationWebhook? result = null;
        await RunAsync(async () =>
        {
            result = id is null
                ? await notificationsApi.CreateWebhookAsync(input)
                : await notificationsApi.UpdateWebhookAsync(id, input);
            await ReloadWebhooksAsync();
            StatusMessage = id is null ? "Webhook created." : "Webhook saved.";
        });
        return result;
    }

    public async Task ToggleWebhookAsync(NotificationWebhook webhook, bool enabled)
        => await RunAsync(async () =>
        {
            await notificationsApi.UpdateWebhookAsync(webhook.Id, new NotificationWebhookInput { Enabled = enabled });
            await ReloadWebhooksAsync();
        });

    public async Task<NotificationWebhookTestResult?> TestWebhookAsync(string id)
    {
        NotificationWebhookTestResult? result = null;
        await RunAsync(async () => result = await notificationsApi.TestWebhookAsync(id));
        return result;
    }

    public async Task<string?> RotateWebhookSecretAsync(string id)
    {
        string? secret = null;
        await RunAsync(async () => secret = (await notificationsApi.RotateWebhookSecretAsync(id)).SigningSecret);
        return secret;
    }

    public async Task DeleteWebhookAsync(string id)
        => await RunAsync(async () =>
        {
            await notificationsApi.DeleteWebhookAsync(id);
            await ReloadWebhooksAsync();
            StatusMessage = "Webhook deleted.";
        });

    public async Task DeleteWebPushSubscriptionAsync(string id)
        => await RunAsync(async () =>
        {
            await notificationsApi.DeleteWebPushSubscriptionAsync(id);
            var existing = WebPushSubscriptions.FirstOrDefault(item => item.Id == id);
            if (existing is not null) WebPushSubscriptions.Remove(existing);
            RefreshCollectionState();
            StatusMessage = "Browser subscription removed.";
        });

    private async Task ReloadWebhooksAsync()
    {
        var values = await notificationsApi.GetWebhooksAsync();
        Webhooks.Clear();
        foreach (var value in values) Webhooks.Add(value);
        RefreshCollectionState();
    }

    private void RefreshCollectionState()
    {
        HasWebPushSubscriptions = WebPushSubscriptions.Count > 0;
        HasWebhookEntries = Webhooks.Count > 0;
        OnPropertyChanged(nameof(CanAddWebhook));
        OnPropertyChanged(nameof(IsWebhookLimitReached));
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try { await action(); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
