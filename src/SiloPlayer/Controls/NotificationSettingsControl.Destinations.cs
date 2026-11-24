using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

/// <summary>
/// Settings &gt; Notifications &gt; Other destinations (official silo-server 3b2b32168, #3550). The state, rules and
/// requests are <see cref="NotificationPluginDestinationsState"/> and <see cref="NotificationDestinationForm"/>;
/// this draws the list and the dialogs and shows the outcomes.
/// </summary>
public sealed partial class NotificationSettingsControl
{
    private bool _destinationsReady;

    private NotificationPluginDestinationsState Destinations => ViewModel.PluginDestinations;
    private ToastService Toasts => App.Services.GetRequiredService<ToastService>();

    private void InitializeDestinations()
    {
        Destinations.PropertyChanged += (_, _) => RenderDestinations();
        Unloaded += (_, _) => Destinations.Close();
        AppLocalization.Register(this, RenderDestinations);
        _destinationsReady = true;
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>Redraws the section from the state: hidden unless an enabled plugin offers a kind or the profile still has destinations.</summary>
    internal void RenderDestinations()
    {
        if (!_destinationsReady) return;
        var state = Destinations;
        PluginDestinationsGroup.Visibility = state.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        var loading = state.IsLoading && state.Destinations.Count == 0;
        DestinationsSkeleton.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        DestinationsError.Text = state.LoadError ?? "";
        DestinationsError.Visibility = state.LoadError != null ? Visibility.Visible : Visibility.Collapsed;
        DestinationsHost.Children.Clear();
        if (!loading && !state.LoadFailed)
            foreach (var destination in state.Destinations) DestinationsHost.Children.Add(DestinationCard(destination));
        DestinationsEmpty.Visibility = !loading && !state.LoadFailed && state.Destinations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DestinationsAddRow.Visibility = !loading && !state.LoadFailed && state.Channels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AddDestinationButton.Content = "＋  " + AppStrings.Get("web.settings.notifications.destinations.add", fallback: "Add destination");
        AddDestinationButton.IsEnabled = !state.AtLimit && !state.IsWriting;
        DestinationsLimitText.Text = state.AtLimit ? NotificationDestinationText.LimitText(state.MaxPerProfile) : "";
        DestinationsLimitText.Visibility = state.AtLimit ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TextBlock Line(string text, Brush? foreground = null, double size = 12) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = foreground ?? Res("SecondaryTextBrush"),
    };

    private FrameworkElement DestinationCard(NotificationPluginDestination destination)
    {
        var state = Destinations;
        var busy = state.IsWriting;
        var stack = new StackPanel { Spacing = 8 };
        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = destination.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        title.Children.Add(new Border
        {
            Background = Res("SurfaceBrush"), CornerRadius = new(6), Padding = new(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Text = destination.Available ? destination.ChannelName : AppStrings.Get("web.settings.notifications.destinations.unavailable", fallback: "Unavailable"),
            },
        });
        head.Children.Add(title);
        var enabled = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        enabled.Children.Add(new TextBlock
        {
            Text = destination.Enabled ? AppStrings.Get("web.settings.value.enabled", fallback: "Enabled") : AppStrings.Get("web.settings.value.disabled", fallback: "Disabled"),
            FontSize = 12, Foreground = Res("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center,
        });
        var toggle = new ToggleSwitch { IsOn = destination.Enabled, OnContent = "", OffContent = "", MinWidth = 0, IsEnabled = !busy };
        toggle.Toggled += async (_, _) => { if (toggle.IsOn != destination.Enabled) await SetDestinationEnabledAsync(destination, toggle.IsOn); };
        enabled.Children.Add(toggle); Grid.SetColumn(enabled, 1); head.Children.Add(enabled);
        stack.Children.Add(head);

        if (!destination.Available)
            stack.Children.Add(Line(AppStrings.Get("web.settings.notifications.destinations.unavailableNote",
                fallback: "The plugin that provides this destination is turned off or removed, so nothing is sent.")));
        stack.Children.Add(Line(NotificationDestinationText.ReasonsText(destination.NotifyNextUp, destination.NotifyRequests)));
        if (NotificationDestinationText.LastSuccessText(destination) is { } success) stack.Children.Add(Line(success));
        if (NotificationDestinationText.WarningText(destination) is { } warning)
        {
            var amber = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF5, 0x9E, 0x0B));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new FontIcon { Glyph = "", FontSize = 13, Foreground = amber, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 2, 0, 0) });
            row.Children.Add(new TextBlock { Text = warning, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = amber, MaxWidth = 560 });
            stack.Children.Add(row);
        }
        if (state.TestResults.TryGetValue(destination.Id, out var test))
            stack.Children.Add(Line(NotificationDestinationText.TestText(test),
                test.Ok ? Res("SuccessBrush") : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF5, 0x9E, 0x0B))));

        var testing = busy && state.WritingId == destination.Id;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        var testButton = new Button
        {
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"], IsEnabled = !busy && destination.Available,
            Content = testing
                ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new ProgressRing { Width = 14, Height = 14, IsActive = true },
                    new TextBlock { Text = AppStrings.Get("web.settings.notifications.webhooks.test", fallback: "Test") } } }
                : AppStrings.Get("web.settings.notifications.webhooks.test", fallback: "Test"),
        };
        testButton.Click += async (_, _) => await TestDestinationAsync(destination);
        var editButton = new Button { Content = AppStrings.Get("web.settings.notifications.webhooks.edit", fallback: "Edit"), Style = (Style)Application.Current.Resources["SecondaryButtonStyle"], IsEnabled = !busy && destination.Available };
        editButton.Click += async (_, _) => await OpenDestinationDialogAsync(destination);
        var deleteButton = new Button { Content = AppStrings.Get("web.settings.notifications.webhooks.delete", fallback: "Delete"), Style = (Style)Application.Current.Resources["DestructiveButtonStyle"], IsEnabled = !busy };
        // The validator the person saw when they chose to delete; a reload while the dialog is open must not swap in a newer one.
        deleteButton.Click += async (_, _) => await ConfirmDeleteDestinationAsync(destination, destination.ETag);
        buttons.Children.Add(testButton); buttons.Children.Add(editButton); buttons.Children.Add(deleteButton);
        stack.Children.Add(buttons);
        return new Border { BorderBrush = Res("BorderBrush"), BorderThickness = new(1), CornerRadius = new(10), Padding = new(14), Child = stack };
    }

    // --- actions -------------------------------------------------------------------------------

    private async void AddDestination_Click(object sender, RoutedEventArgs e) => await OpenDestinationDialogAsync(null);

    // Only a reply dropped by an account or profile switch (or a closed page) is silent; the state says so, judged against the
    // identity the write was sent under. A timeout under the same identity is a failure the person sees: the state has re-read
    // the list and nothing is sent again.
    private async Task SetDestinationEnabledAsync(NotificationPluginDestination destination, bool enabled)
    {
        try { await Destinations.SetEnabledAsync(destination, enabled); }
        catch (NotificationDestinationWriteDroppedException) { }
        catch (Exception ex) { Toasts.Error(NotificationDestinationText.ErrorMessage(ex, AppStrings.Get("web.settings.notifications.destinations.updateFailed", fallback: "Failed to update"))); }
        finally { RenderDestinations(); }
    }

    private async Task TestDestinationAsync(NotificationPluginDestination destination)
    {
        try { await Destinations.TestAsync(destination.Id); }
        catch (NotificationDestinationWriteDroppedException) { }
        catch (Exception ex) { Toasts.Error(NotificationDestinationText.ErrorMessage(ex, AppStrings.Get("web.settings.notifications.webhooks.testFailed", fallback: "Test request failed"))); }
        finally { RenderDestinations(); }
    }

    private async Task ConfirmDeleteDestinationAsync(NotificationPluginDestination destination, string etag)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Content = AppStrings.Get("web.settings.notifications.destinations.deleteDescription", fallback: "Notifications will stop going to this destination. This cannot be undone."),
            PrimaryButtonText = AppStrings.Get("web.settings.notifications.webhooks.delete", fallback: "Delete"),
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = AppStrings.Get("common.cancel", fallback: "Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        MediaItemCardActions.BindText(dialog, "Title", element => element.Title = AppStrings.Get(
            "web.settings.notifications.webhooks.deleteTitle", new Dictionary<string, object?> { ["name"] = destination.Name }));
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            // The request goes out once; the dialog closes whatever the outcome, as the WebUI's does.
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                await Destinations.DeleteAsync(destination.Id, etag);
                Toasts.Success(AppStrings.Get("web.settings.notifications.destinations.deleted", fallback: "Destination deleted"));
            }
            catch (NotificationDestinationWriteDroppedException) { }
            catch (Exception ex) { Toasts.Error(NotificationDestinationText.ErrorMessage(ex, AppStrings.Get("web.settings.notifications.destinations.deleteFailed", fallback: "Failed to delete destination"))); }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
        RenderDestinations();
    }

    /// <summary>The add (null) or edit dialog: choose a kind, fill the plugin's form, set what to send for.</summary>
    internal async Task OpenDestinationDialogAsync(NotificationPluginDestination? destination)
    {
        var state = Destinations;
        var channels = state.Channels;
        var editing = destination != null;
        var channel = editing ? channels.FirstOrDefault(item => item.Key == destination!.ChannelKey)
            : channels.FirstOrDefault();
        var form = new NotificationDestinationForm(channel, destination);
        var pending = false;

        var name = new TextBox { Header = AppStrings.Get("web.settings.notifications.webhooks.name", fallback: "Name"), MaxLength = 64,
            Text = destination?.Name ?? channel?.DisplayName ?? "" };
        var nameError = Line("", Res("ErrorBrush"));
        nameError.Visibility = Visibility.Collapsed;
        var description = Line("");
        var formHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var rejection = Line("", Res("ErrorBrush"), 14); rejection.Visibility = Visibility.Collapsed;
        var kept = Line(AppStrings.Get("web.settings.notifications.destinations.secretsKept", fallback: "Secret fields you leave blank keep their saved values."));
        var clearRows = new StackPanel { Spacing = 4 };
        NotificationDestinationFormView? formView = null;
        ContentDialog? dialog = null;

        void Describe() => description.Text = !string.IsNullOrEmpty(form.Channel?.Description) ? form.Channel!.Description
            : AppStrings.Get("web.settings.notifications.destinations.dialogDescription", fallback: "Choose where this profile's notifications go.");
        void Refresh()
        {
            if (dialog == null) return;
            dialog.IsPrimaryButtonEnabled = !pending && !string.IsNullOrWhiteSpace(name.Text) && form.Channel != null && (formView?.IsValid ?? true);
        }
        void DrawClearRows()
        {
            clearRows.Children.Clear();
            foreach (var view in form.Clearable)
            {
                var clearing = form.Clearing.Contains(view.Key);
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var label = Line(view.Label, clearing ? Res("ErrorBrush") : null);
                if (clearing) label.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                var toggle = new Button
                {
                    Style = (Style)Application.Current.Resources["GhostButtonStyle"], Padding = new(8, 2, 8, 2), MinHeight = 0, FontSize = 12,
                    Content = clearing ? AppStrings.Get("web.settings.notifications.destinations.keepSecret", fallback: "Keep saved secret")
                        : AppStrings.Get("web.settings.notifications.destinations.clearSecret", fallback: "Clear saved secret"),
                };
                AutomationPropertiesPressed(toggle, clearing);
                toggle.Click += (_, _) => { form.ToggleClearing(view.Key); DrawClearRows(); };
                row.Children.Add(label); Grid.SetColumn(toggle, 1); row.Children.Add(toggle); clearRows.Children.Add(row);
            }
        }
        void DrawForm()
        {
            Describe();
            if (form.Declared.Count == 0) { formView = null; formHost.Content = null; clearRows.Children.Clear(); kept.Visibility = Visibility.Collapsed; rejection.Visibility = Visibility.Collapsed; Refresh(); return; }
            formView = new NotificationDestinationFormView(form);
            formView.Changed += () => { rejection.Visibility = Visibility.Collapsed; DrawClearRows(); Refresh(); };
            var group = new StackPanel { Spacing = 8 };
            group.Children.Add(formView); group.Children.Add(clearRows); group.Children.Add(rejection);
            group.Children.Add(kept);
            kept.Visibility = form.SecretsSet.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            formHost.Content = group; DrawClearRows(); Refresh();
        }

        var content = new StackPanel { Spacing = 14, Width = 470 };
        content.Children.Add(description);
        if (!editing && channels.Count > 1)
        {
            var kind = new ComboBox { Header = AppStrings.Get("web.settings.notifications.destinations.kind", fallback: "Kind"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var item in channels) kind.Items.Add(new ComboBoxItem { Content = item.DisplayName, Tag = item });
            kind.SelectedIndex = Math.Max(0, channels.ToList().IndexOf(channel!));
            kind.SelectionChanged += (_, _) =>
            {
                if (kind.SelectedItem is not ComboBoxItem { Tag: NotificationPluginChannel picked }) return;
                form.ChooseChannel(picked, name.Text, out var next);
                rejection.Visibility = Visibility.Collapsed;
                if (name.Text != next) name.Text = next;
                DrawForm();
            };
            content.Children.Add(kind);
        }
        var nameBlock = new StackPanel { Spacing = 4 };
        nameBlock.Children.Add(name); nameBlock.Children.Add(nameError);
        name.TextChanged += (_, _) => { nameError.Visibility = Visibility.Collapsed; Refresh(); };
        content.Children.Add(nameBlock);
        content.Children.Add(formHost);
        content.Children.Add(ReasonToggles(form));
        DrawForm();

        dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Content = new ScrollViewer { Content = content, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(0, 0, 14, 0) },
            PrimaryButtonText = editing ? AppStrings.Get("common.save", fallback: "Save") : AppStrings.Get("web.settings.notifications.destinations.addSubmit", fallback: "Add"),
            CloseButtonText = AppStrings.Get("common.cancel", fallback: "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        MediaItemCardActions.BindText(dialog, "Title", element => element.Title = editing
            ? AppStrings.Get("web.settings.notifications.webhooks.editTitle", new Dictionary<string, object?> { ["name"] = destination!.Name })
            : AppStrings.Get("web.settings.notifications.destinations.addTitle", fallback: "Add a notification destination"));
        Refresh();

        NotificationPluginDestination? created = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            // Stay open until the server accepts; the request goes out once per press and the button is off meanwhile.
            args.Cancel = true;
            var deferral = args.GetDeferral();
            pending = true; Refresh();
            nameError.Visibility = Visibility.Collapsed; rejection.Visibility = Visibility.Collapsed; formView?.SetServerErrors(null);
            try
            {
                if (editing) await state.UpdateAsync(destination!, form, name.Text);
                else created = await state.CreateAsync(form, name.Text);
                args.Cancel = false;
            }
            catch (NotificationDestinationWriteDroppedException) { args.Cancel = false; }
            catch (Exception ex)
            {
                // A timeout under the same identity lands here too: the dialog stays open and says the outcome is unknown.
                var failure = NotificationDestinationText.Classify(ex, form.Declared.Count > 0);
                if (failure.NameError != null) { nameError.Text = failure.NameError; nameError.Visibility = Visibility.Visible; }
                if (failure.FieldErrors != null) formView?.SetServerErrors(failure.FieldErrors);
                if (failure.SettingsRejection != null) { rejection.Text = failure.SettingsRejection; rejection.Visibility = Visibility.Visible; }
                if (failure.ToastMessage != null) Toasts.Error(failure.ToastMessage);
            }
            finally { pending = false; Refresh(); deferral.Complete(); }
        };
        await dialog.ShowAsync();
        if (created != null)
            Toasts.Success(AppStrings.Get("web.settings.notifications.destinations.added", new Dictionary<string, object?> { ["name"] = created.Name }));
        RenderDestinations();
    }

    /// <summary>"Send notifications for": Next Up and requests. A switch the profile's own preferences already silence is off and locked.</summary>
    private FrameworkElement ReasonToggles(NotificationDestinationForm form)
    {
        var box = new StackPanel { Spacing = 8 };
        box.Children.Add(new TextBlock { Text = AppStrings.Get("web.settings.notifications.webhooks.sendFor", fallback: "Send notifications for"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        void Add(string label, bool on, bool disabledInProfile, Action<bool> set)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = label });
            if (disabledInProfile)
                text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = "  " + AppStrings.Get("web.settings.notifications.webhooks.disabledInProfile", fallback: "disabled in profile preferences"),
                    FontSize = 12, Foreground = Res("SecondaryTextBrush"),
                });
            var toggle = new ToggleSwitch { IsOn = on, IsEnabled = !disabledInProfile, OnContent = "", OffContent = "", MinWidth = 0 };
            toggle.Toggled += (_, _) => set(toggle.IsOn);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, label);
            row.Children.Add(text); Grid.SetColumn(toggle, 1); row.Children.Add(toggle); box.Children.Add(row);
        }
        // Requests have no per-profile reason toggle; only the master switch suppresses them.
        Add(NotificationDestinationText.NextUpLabel(), form.NotifyNextUp, !ViewModel.NotificationsEnabled || !ViewModel.NotifyNextUp, value => form.NotifyNextUp = value);
        Add(NotificationDestinationText.RequestsLabel(), form.NotifyRequests, !ViewModel.NotificationsEnabled, value => form.NotifyRequests = value);
        return box;
    }

    private static void AutomationPropertiesPressed(Button button, bool pressed)
        => Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(button, pressed ? AppStrings.Get("web.player.quality.selected", fallback: "Selected") : "");

}
