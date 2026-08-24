using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace SiloPlayer.Controls;

public sealed partial class NotificationSettingsControl : UserControl
{
    private bool _isReady;
    private bool _emailEditorOpen;

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
        _isReady = false;
        await ViewModel.LoadAsync();
        SyncModeCombos();
        _isReady = true;
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

    private async void EmailModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || EmailModeCombo.SelectedItem is not ComboBoxItem { Tag: string mode } || mode == ViewModel.EmailMode) return;
        ViewModel.EmailMode = mode;
        await ViewModel.SaveEmailModeAsync();
    }

    private async void DiscordModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || DiscordModeCombo.SelectedItem is not ComboBoxItem { Tag: string mode } || mode == ViewModel.DiscordMode) return;
        ViewModel.DiscordMode = mode;
        await ViewModel.SaveDiscordModeAsync();
    }

    private async void NotificationPreference_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isReady) await ViewModel.SavePreferencesAsync();
    }

    private async void EmailEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isReady || sender is not ToggleSwitch toggle || toggle.IsOn == ViewModel.IsEmailEnabled) return;
        await ViewModel.SaveEmailEnabledAsync(toggle.IsOn);
        SyncModeCombos();
    }

    private async void DiscordEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isReady || sender is not ToggleSwitch toggle || toggle.IsOn == ViewModel.IsDiscordEnabled) return;
        await ViewModel.SaveDiscordEnabledAsync(toggle.IsOn);
        SyncModeCombos();
    }

    private void ToggleEmailEditor_Click(object sender, RoutedEventArgs e)
    {
        SetEmailEditorOpen(!_emailEditorOpen);
    }

    private void SetEmailEditorOpen(bool open)
    {
        _emailEditorOpen = open;
        EmailAddressEditor.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        EmailAddressActionButton.Content = open ? "Cancel" : ViewModel.EmailAddressActionText;
        if (open)
        {
            EmailAddressInput.Focus(FocusState.Programmatic);
            EmailAddressInput.SelectAll();
        }
    }

    private async void EmailAddressInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await SubmitEmailAddressAsync();
    }

    private async void SetEmail_Click(object sender, RoutedEventArgs e) => await SubmitEmailAddressAsync();

    private async Task SubmitEmailAddressAsync()
    {
        if (string.IsNullOrWhiteSpace(ViewModel.EmailAddressInput)) return;
        await ViewModel.RequestEmailAddressAsync();
        if (string.IsNullOrWhiteSpace(ViewModel.ErrorMessage)) SetEmailEditorOpen(false);
    }

    private async void ClearEmail_Click(object sender, RoutedEventArgs e) => await ViewModel.ClearEmailAddressAsync();

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
            Text = "Discord webhook URLs render as native embeds. Any other HTTPS endpoint receives signed JSON.",
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
            Title = webhook is null ? "Add webhook" : $"Edit \"{webhook.Name}\"",
            Content = content,
            PrimaryButtonText = webhook is null ? "Create" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text),
        };
        name.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (webhook is not null || !string.IsNullOrWhiteSpace(url.Text)) return;
            args.Cancel = true;
            ViewModel.ErrorMessage = "A webhook URL is required";
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

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
            Title = $"Delete \"{webhook.Name}\"?",
            Content = "Notifications will stop posting to this destination. This cannot be undone.",
            PrimaryButtonText = "Delete",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteWebhookAsync(webhook.Id);
    }

    private async Task ShowSecretAsync(string secret)
    {
        var copyButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE8C8", FontSize = 14 },
            Padding = new Thickness(7),
        };
        ToolTipService.SetToolTip(copyButton, "Copy signing secret");
        copyButton.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(secret);
            Clipboard.SetContent(package);
            copyButton.Content = new FontIcon { Glyph = "\uE73E", FontSize = 14 };
            ToolTipService.SetToolTip(copyButton, "Copied");
        };

        var secretRow = new Grid();
        secretRow.ColumnDefinitions.Add(new ColumnDefinition());
        secretRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        secretRow.Children.Add(new TextBlock
        {
            Text = secret,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(copyButton, 1);
        secretRow.Children.Add(copyButton);

        var content = new StackPanel { Spacing = 14, Width = 460 };
        content.Children.Add(new TextBlock
        {
            Text = "Silo signs every delivery with this secret so your receiver can verify it. It is shown only once — store it on the receiving service now. You can rotate it later if it is lost.",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = secretRow,
        });

        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Save your signing secret",
            Content = content,
            PrimaryButtonText = "I've saved it",
            DefaultButton = ContentDialogButton.Primary,
        }.ShowAsync();
    }

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
