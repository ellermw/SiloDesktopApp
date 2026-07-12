using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.ViewModels;
using Windows.System;

namespace SiloPlayer.Controls;

public sealed partial class NotificationSettingsControl : UserControl
{
    public NotificationSettingsViewModel ViewModel { get; } =
        App.Services.GetRequiredService<NotificationSettingsViewModel>();

    public NotificationSettingsControl()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += NotificationSettingsControl_Loaded;
    }

    private async void NotificationSettingsControl_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        SyncModeCombos();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        SyncModeCombos();
    }

    private void SyncModeCombos()
    {
        SelectMode(EmailModeCombo, ViewModel.EmailMode);
        SelectMode(DiscordModeCombo, ViewModel.DiscordMode);
        ApplyModeCapability(EmailModeCombo, ViewModel.Capability.Email.Modes);
        ApplyModeCapability(DiscordModeCombo, ViewModel.Capability.Discord.Modes);
    }

    private static void SelectMode(ComboBox combo, string mode)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is ComboBoxItem { Tag: string value } && value == mode)
            {
                combo.SelectedIndex = index;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static void ApplyModeCapability(ComboBox combo, IReadOnlyCollection<string> modes)
    {
        var allowPerEpisode = modes.Contains("per_episode");
        var selectedMode = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is "per_episode" or "per_episode_and_digest")
                item.IsEnabled = allowPerEpisode || string.Equals(item.Tag as string, selectedMode, StringComparison.Ordinal);
        }
    }

    private void EmailModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EmailModeCombo.SelectedItem is ComboBoxItem { Tag: string mode }) ViewModel.EmailMode = mode;
    }

    private void DiscordModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiscordModeCombo.SelectedItem is ComboBoxItem { Tag: string mode }) ViewModel.DiscordMode = mode;
    }

    private async void SavePreferences_Click(object sender, RoutedEventArgs e) => await ViewModel.SavePreferencesAsync();
    private async void SaveEmailMode_Click(object sender, RoutedEventArgs e) => await ViewModel.SaveEmailModeAsync();
    private async void SetEmail_Click(object sender, RoutedEventArgs e) => await ViewModel.RequestEmailAddressAsync();
    private async void ClearEmail_Click(object sender, RoutedEventArgs e) => await ViewModel.ClearEmailAddressAsync();
    private async void SaveDiscordMode_Click(object sender, RoutedEventArgs e) => await ViewModel.SaveDiscordModeAsync();

    private async void LinkDiscord_Click(object sender, RoutedEventArgs e)
    {
        var url = await ViewModel.StartDiscordLinkAsync();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Launcher.LaunchUriAsync(uri);
    }

    private async void UnlinkDiscord_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Unlink Discord?",
            Content = "Discord direct messages will stop for this account.",
            PrimaryButtonText = "Unlink",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.UnlinkDiscordAsync();
    }

    private async void RemovePush_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) await ViewModel.DeleteWebPushSubscriptionAsync(id);
    }

    private async void AddWebhook_Click(object sender, RoutedEventArgs e) => await OpenWebhookDialogAsync(null);

    private async void EditWebhook_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NotificationWebhook webhook }) await OpenWebhookDialogAsync(webhook);
    }

    private async Task OpenWebhookDialogAsync(NotificationWebhook? webhook)
    {
        var name = new TextBox { Header = "Name", Text = webhook?.Name ?? "", PlaceholderText = "Family Discord", MaxLength = 64 };
        var url = new TextBox
        {
            Header = webhook is null ? "URL" : "Replace URL (optional)",
            PlaceholderText = webhook is null ? "https://discord.com/api/webhooks/…" : $"Currently pointing at {webhook.UrlHost}",
        };
        var favorites = MakeReasonToggle("Favorites", webhook?.NotifyFavorites ?? true,
            !ViewModel.NotificationsEnabled || !ViewModel.NotifyFavorites);
        var watchlist = MakeReasonToggle("Watchlist", webhook?.NotifyWatchlist ?? true,
            !ViewModel.NotificationsEnabled || !ViewModel.NotifyWatchlist);
        var watching = MakeReasonToggle("Continue Watching", webhook?.NotifyContinueWatching ?? true,
            !ViewModel.NotificationsEnabled || !ViewModel.NotifyContinueWatching);
        var nextUp = MakeReasonToggle("Next Up", webhook?.NotifyNextUp ?? true,
            !ViewModel.NotificationsEnabled || !ViewModel.NotifyNextUp);
        var requests = MakeReasonToggle("Requests", webhook?.NotifyRequests ?? true, !ViewModel.NotificationsEnabled);
        var content = new StackPanel { Spacing = 12, Width = 470 };
        content.Children.Add(new TextBlock
        {
            Text = "Discord webhook URLs render as native embeds. Other HTTPS endpoints receive signed JSON.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        content.Children.Add(name);
        content.Children.Add(url);
        content.Children.Add(new TextBlock { Text = "Send notifications for", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(favorites);
        content.Children.Add(watchlist);
        content.Children.Add(watching);
        content.Children.Add(nextUp);
        content.Children.Add(requests);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = webhook is null ? "Add webhook" : $"Edit “{webhook.Name}”",
            Content = content,
            PrimaryButtonText = webhook is null ? "Create" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(name.Text) || (webhook is null && string.IsNullOrWhiteSpace(url.Text)))
        {
            ViewModel.ErrorMessage = "A name and webhook URL are required.";
            return;
        }

        var input = new NotificationWebhookInput
        {
            Name = name.Text.Trim(),
            Url = string.IsNullOrWhiteSpace(url.Text) ? null : url.Text.Trim(),
            NotifyFavorites = favorites.IsOn,
            NotifyWatchlist = watchlist.IsOn,
            NotifyContinueWatching = watching.IsOn,
            NotifyNextUp = nextUp.IsOn,
            NotifyRequests = requests.IsOn,
        };
        var saved = await ViewModel.SaveWebhookAsync(webhook?.Id, input);
        if (!string.IsNullOrWhiteSpace(saved?.SigningSecret))
            await ShowSecretAsync(saved.SigningSecret!);
    }

    private static ToggleSwitch MakeReasonToggle(string label, bool value, bool disabled) => new()
    {
        Header = disabled ? $"{label} — disabled in profile preferences" : label,
        IsOn = value,
        IsEnabled = !disabled,
    };

    private async void WebhookEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { Tag: NotificationWebhook webhook } toggle && toggle.IsOn != webhook.Enabled)
            await ViewModel.ToggleWebhookAsync(webhook, toggle.IsOn);
    }

    private async void TestWebhook_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var result = await ViewModel.TestWebhookAsync(id);
        if (result is null) return;
        await ShowMessageAsync(result.Ok ? "Webhook test succeeded" : "Webhook test failed",
            $"{(result.HttpStatus is int status ? $"HTTP {status}, " : "")}{result.DurationMs} ms{(string.IsNullOrWhiteSpace(result.Message) ? "" : $" — {result.Message}")}");
    }

    private async void RotateSecret_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NotificationWebhook { Type: "generic" } webhook }) return;
        var secret = await ViewModel.RotateWebhookSecretAsync(webhook.Id);
        if (!string.IsNullOrWhiteSpace(secret)) await ShowSecretAsync(secret!);
    }

    private async void DeleteWebhook_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NotificationWebhook webhook }) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete “{webhook.Name}”?",
            Content = "Notifications will stop posting to this destination. This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteWebhookAsync(webhook.Id);
    }

    private Task ShowSecretAsync(string secret) => ShowMessageAsync(
        "Save this signing secret now",
        $"The secret is shown only once. Store it securely.\n\n{secret}");

    private async Task ShowMessageAsync(string title, string message)
    {
        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Close",
        }.ShowAsync();
    }
}
