using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Views;

internal static partial class SettingsLocalizationNativeFixture
{
    private static async Task LanguageInitialLoadRecovery(StackPanel parent)
    {
        foreach (var failure in new[] { "capabilities", "profile" })
        {
            var wire = new LanguageWire { Stored = "ja", PickerFailure = failure };
            using var http = new HttpClient(wire);
            var client = new SiloApiClient(http); client.SetBaseUrl("https://" + LanguageWire.Host); client.SetProfile("profile-a");
            var preference = new AppLanguagePreference(new SettingsApi(client));
            var coordinator = new AppLocaleCoordinator(preference, LocaleService.Shared,
                new SettingsService(Path.Combine(Program.ResultDirectory, "language-initial-recovery")),
                () => ["en-US"], "en-US", AppLocalization.PostTo(parent.DispatcherQueue));
            var group = new AppLanguageSettingsGroup(preference, coordinator); parent.Children.Add(group);
            try
            {
                await group.EnsureLoadedAsync(); await Settle(group);
                var retry = ((StackPanel)group.Group.Child).Children.OfType<Button>().Single();
                Check(group.Visibility == Visibility.Visible && group.StatusText.Visibility == Visibility.Visible
                    && !group.Picker.IsEnabled && retry.Visibility == Visibility.Visible, "Initial " + failure + " failure has no visible recovery.");
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); await Settle(group);
                    Check(Equals(retry.Content, AppStrings.Get("common.tryAgain")), "Initial-load retry did not follow locale.");
                    Check(group.StatusText.Text == AppStrings.Get("web.common.ui.carousel.loadFailed", new Dictionary<string, object?> { ["title_lower"] = AppStrings.Get("settings.control.appLanguage") }), "Load error did not follow locale.");
                }
                wire.PickerFailure = null; Click(retry);
                await Until(() => group.Picker.IsEnabled && group.Picker.SelectedItem is ComboBoxItem);
                Check(Equals(((ComboBoxItem)group.Picker.SelectedItem).Tag, "ja") && group.StatusText.Visibility == Visibility.Collapsed,
                    "Initial-load retry did not restore the stored picker value.");
                Check(wire.Writes.Count == 0, "Initial-load retry wrote a setting.");
            }
            finally { parent.Children.Remove(group); Apply("en"); await Task.Delay(80); }
        }
        Program.Log("PASS initial language capabilities/profile failures stay visible, translate and retry without writes.");
    }

    private static async Task ReviewEighthDialogs(StackPanel parent, Wire wire, SiloApiClient client)
    {
        var notifications = new NotificationSettingsControl(); parent.Children.Add(notifications);
        try
        {
            await Settle(notifications);
            await InspectOpenedDialog(parent, () => Invoke(notifications, "UnlinkDiscord_Click", new Button(), new RoutedEventArgs()), async dialog =>
            {
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); await Settle(dialog);
                    Check(Equals(dialog.Title, AppStrings.Get("web.settings.watchProviders.disconnectConfirm", new Dictionary<string, object?> { ["provider"] = "Discord" })), "Unlink heading did not refresh.");
                    Check(Equals(dialog.Content, AppStrings.Get("web.settings.notifications.discord.description")), "Discord explanation did not refresh.");
                    Check(dialog.PrimaryButtonText == AppStrings.Get("web.settings.notifications.discord.unlink") && dialog.CloseButtonText == AppStrings.Get("common.cancel"), "Unlink actions did not refresh.");
                }
            });
        }
        finally { parent.Children.Remove(notifications); }
        var settings = new SettingsPage(); parent.Children.Add(settings);
        try
        {
            await Settle(settings);
            settings.ViewModel.CanEditHomeSections = true;
            settings.ViewModel.SelectedScope = "home";
            await InspectOpenedDialog(parent, () => Invoke(settings, "HomeSectionsReset_Click", new Button(), new RoutedEventArgs()), async dialog =>
            {
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); await Settle(dialog);
                    Check(Equals(dialog.Title, AppStrings.Get("web.home.rows.profilePage.resetTitle", new Dictionary<string, object?> { ["page_kind"] = "home", ["page"] = AppStrings.Get("web.home.rows.pages.home") })), "Rows reset heading did not refresh.");
                    Check(dialog.Content is string body && body.Contains(AppStrings.Get("web.home.rows.reset.otherPages")), "Reset explanation did not refresh.");
                    Check(dialog.PrimaryButtonText == AppStrings.Get("web.home.rows.reset.confirm") && dialog.CloseButtonText == AppStrings.Get("common.cancel"), "Reset actions did not refresh.");
                }
            });
            wire.RequirePin = true;
            await InspectOpenedDialog(parent, () => _ = (Task)Invoke(settings, "TryVerifyPinForYourDataAsync", new ProfileTransferApi(client), client.CaptureContext(), Field<int>(settings, "_yourDataGeneration"), "PIN required")!, async dialog =>
            {
                var pin = Descendants(dialog).OfType<PasswordBox>().Single(); pin.Password = "1234";
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); await Settle(dialog);
                    RequireText(dialog, "web.settings.yourData.pinDescription", "Enter this profile's PIN to export or import its data.");
                    Check(pin.Password == "1234" && pin.PlaceholderText == AppStrings.Get("web.profiles.pin.placeholder"), "PIN refresh changed the draft or kept the placeholder stale.");
                    Check(dialog.PrimaryButtonText == AppStrings.Get("web.profiles.pin.confirm") && dialog.CloseButtonText == AppStrings.Get("common.cancel"), "PIN actions did not refresh.");
                    Check(Equals(dialog.Title, AppStrings.Get("web.profiles.pin.title", new Dictionary<string, object?> { ["name"] = "Primary" })), "PIN heading lost its raw profile name.");
                }
            });
        }
        finally { wire.RequirePin = false; parent.Children.Remove(settings); }
        var requestPage = new RequestDetailPage(); parent.Children.Add(requestPage);
        try
        {
            var requested = new RequestMediaDetail { Title = "Raw request 東京", Request = new RequestState { RequestId = "fixture-cancel", RequestedByViewer = true } };
            typeof(RequestDetailPage).GetField("_item", Private)!.SetValue(requestPage, requested);
            await (Task)Invoke(requestPage, "AddCancelActionAsync", requested)!;
            var cancel = ((Panel)requestPage.FindName("ActionsPanel")).Children.OfType<Button>().Single();
            await InspectOpenedDialog(parent, () => Click(cancel), async dialog =>
            {
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); await Settle(dialog);
                    Check(Equals(dialog.Content, AppStrings.Get("web.requests.cancel.description", new Dictionary<string, object?> { ["title"] = requested.Title })), "Request cancellation body did not refresh with its raw title.");
                }
            });
        }
        finally { parent.Children.Remove(requestPage); }
        var detail = new ItemDetailPage(); parent.Children.Add(detail);
        try
        {
            await Settle(detail);
            const string raw = "Raw title 東京";
            await InspectOpenedDialog(parent, () => _ = (Task)Invoke(detail, "ShowDownloadDialogAsync", raw, Array.Empty<FileVersion>())!, async dialog =>
            {
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); await Settle(dialog);
                    Check(Equals(dialog.Title, AppStrings.Get("web.downloads.versionPicker.titleWithName", new Dictionary<string, object?> { ["title"] = raw })), "Download heading did not refresh with its raw title.");
                }
            });
            detail.ViewModel.Item = new MediaItemDetail { ContentId = "party-fixture", Title = raw, Type = "movie" };
            typeof(ItemDetailPage).GetField("_currentContentId", Private)!.SetValue(detail, "party-fixture");
            typeof(ItemDetailPage).GetField("_navigationCts", Private)!.SetValue(detail, new CancellationTokenSource());
            await InspectOpenedDialog(parent, () => _ = (Task)Invoke(detail, "ShowStartPartyAsync")!, async dialog =>
            {
                var mode = Descendants(dialog).OfType<ComboBox>().Single();
                var pause = Descendants(dialog).OfType<ToggleSwitch>().Single();
                mode.SelectedIndex = 1; pause.IsOn = !pause.IsOn;
                var chosenPause = pause.IsOn;
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); await Settle(dialog);
                    Check(Equals(mode.Header, AppStrings.Get("web.watchTogether.startSheet.howToPick")) && mode.SelectedIndex == 1 && pause.IsOn == chosenPause, "Party refresh changed draft selections or kept its header stale.");
                    Check(Equals(((ComboBoxItem)mode.Items[0]).Content, AppStrings.Get("web.watchTogether.common.hostPicks")) && Equals(((ComboBoxItem)mode.Items[1]).Content, AppStrings.Get("web.watchTogether.activity.modeVote")), "Unopened party choices did not refresh.");
                    Check(Equals(pause.Header, AppStrings.Get("web.watchTogether.startSheet.allowPause")), "Party pause label did not refresh.");
                    Check(dialog.PrimaryButtonText == AppStrings.Get("web.watchTogether.hub.create"), "Party create action did not refresh.");
                }
            });
        }
        finally { parent.Children.Remove(detail); Apply("en"); }
        Program.Log("PASS retained Discord, Home reset, data PIN, request cancellation, download and party dialogs translate through de/ar/ja and preserve drafts/raw titles.");
    }

    private static async Task InspectOpenedDialog(StackPanel parent, Action launch, Func<ContentDialog, Task> verify)
    {
        ContentDialog? dialog = null;
        launch();
        await Until(() => (dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot)
            .SelectMany(popup => popup.Child is ContentDialog own ? new[] { own } : Descendants(popup.Child).OfType<ContentDialog>()).FirstOrDefault()) != null);
        try { await Settle(dialog!); await verify(dialog!); }
        finally { dialog!.Hide(); await Task.Delay(160); }
    }
}
