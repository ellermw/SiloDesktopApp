using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using Windows.ApplicationModel.DataTransfer;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminNotificationSettingsPage
{
    private readonly StackPanel _channelList = new() { Spacing = 12 };
    private readonly List<Button> _channelButtons = [];
    private readonly List<ToggleSwitch> _channelToggles = [];
    private readonly Dictionary<ToggleSwitch, bool> _channelToggleValues = new();
    private bool _syncChannels, _secretShown;
    private AdminNotificationSettingsState? _secretTried;
    private readonly Dictionary<string, TextBlock> _channelResults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProgressRing> _channelBusy = new(StringComparer.Ordinal);
    private IReadOnlyList<AdminNotificationChannel>? _renderedChannels;
    private readonly TextBlock _channelError = U.Text("", 12), _channelEmpty = U.Text(N("serverChannels.empty"), 13, true);
    private Button _channelRetry = null!, _channelMore = null!, _channelAdd = null!;
    private static readonly string[] EventLabels = ["newMovies", "newEpisodes", "newAudiobooks", "newEbooks", "requestSubmitted", "requestApproved", "requestDeclined", "requestFulfilled"];
    private void BuildServerChannels(StackPanel parent)
    {
        parent.Children.Add(_channelError); parent.Children.Add(_channelList); _channelEmpty.Visibility = Visibility.Collapsed; parent.Children.Add(_channelEmpty);
        var actions = Actions(parent);
        _channelRetry = U.Action(N("reload"), "NotificationsChannelRetry"); actions.Children.Add(_channelRetry);
        _channelRetry.Click += async (_, _) => { if (_controller != null) await _controller.ReloadAsync(true); };
        _channelMore = U.Action(T("web.common.ui.loadMore.button"), "NotificationsChannelMore"); actions.Children.Add(_channelMore);
        _channelMore.Click += async (_, _) => { if (_controller != null) await _controller.LoadMoreChannelsAsync(); };
        _channelAdd = Action(actions, N("serverChannels.add"), "NotificationsChannelAdd", () => EditChannelAsync(null), s => s.ChannelsLoaded && !s.ChannelsLoading);
    }
    private void RenderChannels(AdminNotificationSettingsState state)
    {
        var enabled = state.Owner?.IsCurrent == true && state.Loaded && !state.Loading && !state.Saving && !state.OperationPending && !state.OperationUnknown && !state.Conflict && !state.OutcomeUnknown && !state.AccessBlocked && state.ChannelsLoaded && !state.ChannelsLoading;
        _channelError.Text = state.ChannelsLoading ? "Loading server channels…" : state.ChannelsError ?? (state.NextCursor != null ? state.Channels.Count >= 1000
            ? "Showing the first 1,000 destinations. Refine destinations on the server before loading more."
            : $"Showing {state.Channels.Count} destinations. Load more to see additional destinations." : "");
        _channelError.Visibility = _channelError.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _channelRetry.Visibility = state.ChannelsError != null ? Visibility.Visible : Visibility.Collapsed;
        _channelRetry.IsEnabled = state.Owner?.IsCurrent == true && !state.Loading && !state.Saving && !state.OperationPending && !state.ChannelsLoading;
        _channelMore.Visibility = state.NextCursor != null && state.Channels.Count < 1000 ? Visibility.Visible : Visibility.Collapsed; _channelMore.IsEnabled = enabled;
        var failingCount = state.Channels.Count(c => c.Failing);
        SetChip("server", state.ChannelsLoaded ? N("serverChannelsCard.destinations", ("count", state.Channels.Count), ("count_text", state.Channels.Count)) : "", ChipTone.Neutral);
        SetChip("server.failing", state.ChannelsLoaded && failingCount > 0 ? N("serverChannelsCard.failing", ("count", failingCount), ("count_text", failingCount)) : "", ChipTone.Warning);
        if (!ReferenceEquals(_renderedChannels, state.Channels))
        {
            _renderedChannels = state.Channels; _channelList.Children.Clear(); _channelButtons.Clear(); _channelToggles.Clear(); _channelToggleValues.Clear(); _channelResults.Clear(); _channelBusy.Clear();
            foreach (var channel in state.Channels) _channelList.Children.Add(ChannelCard(channel));
        }
        // Only a successful read that found nothing says so; a failed read keeps what it had and says it failed.
        _channelEmpty.Visibility = state.ChannelsLoaded && state.Channels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in _channelButtons) button.IsEnabled = enabled;
        _syncChannels = true;
        try
        {
            foreach (var toggle in _channelToggles)
            {
                toggle.IsEnabled = enabled;
                if (!state.OperationPending && !state.OperationUnknown) toggle.IsOn = _channelToggleValues[toggle];
            }
        }
        finally { _syncChannels = false; }
        foreach (var (id, result) in _channelResults)
        {
            U.UpdateLiveText(result, state.ChannelTestResults.TryGetValue(id, out var test) ? TestText(test, "serverChannels") : "");
            // Official: emerald when the test post succeeded, amber when it failed.
            result.Foreground = AdminMonitorUi.Solid(state.ChannelTestResults.TryGetValue(id, out var shown) && shown.Ok ? Windows.UI.Color.FromArgb(255, 0x10, 0xB9, 0x81) : AdminMonitorUi.Amber500);
        }
        foreach (var (id, ring) in _channelBusy) { ring.IsActive = state.OperationPending && state.ActiveTarget == id; ring.Visibility = ring.IsActive ? Visibility.Visible : Visibility.Collapsed; }
    }
    private Border ChannelCard(AdminNotificationChannel channel)
    {
        var stack = new StackPanel { Spacing = 8 };
        var heading = new Grid { ColumnSpacing = 8 };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 4 }; title.Children.Add(U.Text(AdminNotificationText.Plain(channel.Name))); title.Children.Add(U.Text(AdminNotificationText.Plain(channel.Type, 32) + " · " + AdminNotificationText.Plain(channel.UrlHost, 253), 12, true)); heading.Children.Add(title);
        var toggle = new ToggleSwitch { IsOn = channel.Enabled, OffContent = N("serverChannels.disabled"), OnContent = N("serverChannels.enabled"), MinWidth = 0 };
        AutomationProperties.SetName(toggle, AdminNotificationText.Plain(channel.Name) + " · " + N("serverChannels.enabled")); AutomationProperties.SetAutomationId(toggle, "NotificationsChannelEnabled_" + channel.Id);
        toggle.IsEnabled = false; Grid.SetColumn(toggle, 1); heading.Children.Add(toggle); stack.Children.Add(heading);
        _channelToggles.Add(toggle);
        _channelToggleValues[toggle] = channel.Enabled;
        toggle.Toggled += async (_, _) => { if (!_syncChannels && _controller != null && toggle.IsOn != channel.Enabled) await _controller.SaveChannelAsync(channel.Id, new() { ["enabled"] = toggle.IsOn }); };
        // Toggles participate in the same operation gate as the card commands.
        var events = AdminNotificationSettingsApi.EventKeys.Select((key, index) => (key, index)).Where(x => channel.Events.GetValueOrDefault(x.key)).Select(x => N("serverChannels.events." + EventLabels[x.index])).ToArray();
        stack.Children.Add(U.Text(events.Length == 0 ? N("serverChannels.noEvents") : string.Join(" · ", events), 12, true));
        if (channel.Failing)
        {
            // The server's failure text and disabled reason are third-party text: neutralized and bounded like a name.
            var serverText = AdminNotificationText.Plain(channel.LastFailureMessage, 200);
            var detail = serverText.Length > 0 ? serverText : channel.LastFailureStatus is { } status ? N("serverChannels.httpStatus", ("status", status)) : N("serverChannels.httpError");
            var disabled = AdminNotificationText.Plain(channel.DisabledReason, 200);
            var warning = new Grid { ColumnSpacing = 6 }; warning.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); warning.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var icon = AdminMonitorUi.Icon("alert-triangle", 14, AdminMonitorUi.Amber500); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new(0, 1, 0, 0); warning.Children.Add(icon);
            var failure = AmberText(disabled.Length > 0 ? N("serverChannels.disabledReason", ("reason", disabled)) : N("serverChannels.lastFailureWhen", ("when", Relative(channel.LastFailureAt!.Value)), ("detail", detail)));
            Grid.SetColumn(failure, 1); warning.Children.Add(failure); stack.Children.Add(warning);
        }
        else if (channel.LastSuccessAt is { } last) stack.Children.Add(U.Text(N("serverChannels.lastPost", ("when", Relative(last))), 12, true));
        var result = U.Text("", 12); _channelResults[channel.Id] = result; AutomationProperties.SetLiveSetting(result, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite); stack.Children.Add(result);
        var actions = Actions(stack);
        var busy = new ProgressRing { Width = 16, Height = 16, IsActive = false, Visibility = Visibility.Collapsed }; _channelBusy[channel.Id] = busy;
        void Command(string label, string id, Func<Task> action)
        {
            var button = U.Action(label, id + "_" + channel.Id); actions.Children.Add(button); _channelButtons.Add(button);
            // The visible label stays "Test", "Delete"; the accessible name says which channel (the official buttons have no per-channel name).
            AutomationProperties.SetName(button, label + " " + AdminNotificationText.Plain(channel.Name));
            button.Click += async (_, _) => await action();
        }
        Command(N("serverChannels.test"), "NotificationsChannelTest", async () => { if (_controller != null && !await _controller.TestChannelAsync(channel)) await RefusedAsync(channel); });
        Command(N("serverChannels.edit"), "NotificationsChannelEdit", () => EditChannelAsync(channel));
        if (channel.Type == "generic") Command(N("serverChannels.rotateSecret"), "NotificationsChannelRotate", async () => { if (_controller != null && !await _controller.RotateChannelAsync(channel)) await RefusedAsync(channel); });
        Command(N("serverChannels.delete"), "NotificationsChannelDelete", () => ConfirmAsync(N("serverChannels.deleteTitle", ("name", AdminNotificationText.Plain(channel.Name))), N("serverChannels.deleteDescription"), N("serverChannels.deleteConfirm"),
            () => _controller?.DeleteChannelAsync(channel) ?? Task.FromResult(false), itemChanged: () => _controller?.IsShown(channel) == false));
        actions.Children.Add(busy);
        // Official ChannelCard: rounded-xl (16 px) border at 60% of the border colour, p-4.
        return new Border { Child = stack, Padding = new(16), CornerRadius = new(16), BorderThickness = new(1), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.ColorOf("BorderBrush", Windows.UI.Color.FromArgb(255, 63, 63, 70)), .6) };
    }
    /// <summary>A Test or Rotate that was refused sent nothing. Say so, and when the listed channel is no longer the one that was shown
    /// reload the channels so the card is current. (A refused command with no owner left needs no message: the page is gone.)</summary>
    private async Task RefusedAsync(AdminNotificationChannel channel)
    {
        var controller = _controller; if (controller?.State is not { Owner.IsCurrent: true } state) return;
        var changed = !controller.IsShown(channel);
        ToastWarning(NotSentMessage(state, changed, changed));
        if (changed) await controller.ReloadAsync(true);
    }
    /// <summary>What a command that sent nothing should tell the admin, by what is actually the case.</summary>
    internal static string NotSentMessage(AdminNotificationSettingsState state, bool channelChanged, bool reloaded) =>
        state.AccessBlocked ? "Nothing was sent. This account can no longer change these settings. Reload the page."
        : state.OperationUnknown || state.OutcomeUnknown || state.Conflict ? "Nothing was sent. An earlier change may or may not have reached the server. Reload the page and review it before another change."
        : state.Loading || state.Saving || state.OperationPending || state.ChannelsLoading || !state.Loaded ? "Nothing was sent. The page is busy. Wait for it to finish, then try again."
        : channelChanged ? "Nothing was sent. This channel changed on the server after it was shown (its name, type or host)." + (reloaded ? " The list was reloaded; review it, then try again." : " Cancel, reload and review it, then try again.")
        : "Nothing was sent. The item changed on the server or the page is busy. Cancel, reload and review it, then try again.";
    private static string Relative(DateTimeOffset date) => AdminSubtitlesText.Relative(date, DateTimeOffset.UtcNow, instant => DateTimeDisplay.FormatDateTime(instant));
    private async Task EditChannelAsync(AdminNotificationChannel? channel)
    {
        var owner = _controller?.State.Owner;
        if (_dialog != null || owner?.IsCurrent != true) return;
        var content = new StackPanel { Spacing = 12, MaxWidth = 480 };
        content.Children.Add(U.Text(N("serverChannels.dialogDescription"), 13, true));
        content.Children.Add(U.Text(N("serverChannels.name")));
        var name = U.Input(N("serverChannels.name"), "NotificationsChannelName", N("serverChannels.namePlaceholder"));
        name.Width = double.NaN; name.HorizontalAlignment = HorizontalAlignment.Stretch; name.MaxLength = 64; name.Text = channel?.Name ?? ""; content.Children.Add(name);
        var urlLabel = N(channel == null ? "serverChannels.url" : "serverChannels.replaceUrl"); content.Children.Add(U.Text(urlLabel));
        var url = U.Input(urlLabel, "NotificationsChannelUrl", channel == null ? "https://discord.com/api/webhooks/…" : N("serverChannels.currentHost", ("host", AdminNotificationText.Plain(channel.UrlHost, 253))));
        url.Width = double.NaN; url.HorizontalAlignment = HorizontalAlignment.Stretch; url.MaxLength = 4096; url.IsSpellCheckEnabled = false; url.IsTextPredictionEnabled = false; content.Children.Add(url);
        // Official (5537df46a): the URL reads left to right once typed and for a new channel; the translated "currently pointing at" hint keeps the page direction.
        void UrlDirection() { if (channel == null || url.Text.Length > 0) url.FlowDirection = FlowDirection.LeftToRight; else url.ClearValue(FrameworkElement.FlowDirectionProperty); }
        url.TextChanged += (_, _) => UrlDirection(); UrlDirection();
        var toggles = new Dictionary<string, ToggleSwitch>();
        for (var i = 0; i < AdminNotificationSettingsApi.EventKeys.Count; i++)
        {
            if (i is 0 or 4) content.Children.Add(U.Text(N("serverChannels.events." + (i == 0 ? "newContent" : "mediaRequests"))));
            var key = AdminNotificationSettingsApi.EventKeys[i]; var label = N("serverChannels.events." + EventLabels[i]);
            var toggle = new ToggleSwitch { IsOn = channel?.Events.GetValueOrDefault(key) ?? i < 4, OnContent = "", OffContent = "", MinWidth = 0 };
            // Official: the label and its switch share one row at every width (flex items-center justify-between gap-3).
            AutomationProperties.SetName(toggle, label); toggles[key] = toggle;
            var eventRow = new Grid { ColumnSpacing = 12 }; eventRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); eventRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var eventLabel = U.Text(label); eventLabel.VerticalAlignment = VerticalAlignment.Center; eventRow.Children.Add(eventLabel);
            Grid.SetColumn(toggle, 1); toggle.VerticalAlignment = VerticalAlignment.Center; eventRow.Children.Add(toggle); content.Children.Add(eventRow);
        }
        var error = U.Text("", 12); content.Children.Add(error);
        var editor = new ContentControl { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var dialog = NewDialog(channel == null ? N("serverChannels.addTitle") : N("serverChannels.editTitle", ("name", AdminNotificationText.Plain(channel.Name))), editor,
            channel == null ? N("serverChannels.create") : T("common.save"), wrapTitle: true);
        dialog.IsPrimaryButtonEnabled = false;
        void Validate() => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text) && (channel != null || !string.IsNullOrWhiteSpace(url.Text)) && _controller?.State.OperationUnknown == false;
        name.TextChanged += (_, _) => Validate(); url.TextChanged += (_, _) => Validate(); Validate();
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral();
            try
            {
                if (!owner.IsCurrent || _controller?.State.Owner != owner) { dialog.Hide(); return; }
                var input = toggles.ToDictionary(p => p.Key, p => (object)p.Value.IsOn); input["name"] = name.Text.Trim();
                if (!string.IsNullOrWhiteSpace(url.Text)) input["url"] = url.Text.Trim();
                editor.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
                var sent = await _controller.SaveChannelAsync(channel?.Id, input);
                if (!owner.IsCurrent) { dialog.Hide(); return; }
                // A refused command sent nothing; the editor stays open with its values.
                if (!sent) { error.Text = "Nothing was sent. Wait for the page to finish or reload it, then try again."; editor.IsEnabled = true; Validate(); }
                else if (_controller.State.Error == null)
                {
                    args.Cancel = false;
                    if (channel == null) Toast(N("serverChannels.created", ("name", AdminNotificationText.Plain(input["name"] as string))));
                }
                else { error.Text = _controller.State.Error; editor.IsEnabled = true; Validate(); }
            }
            finally { deferral.Complete(); }
        };
        System.Action clear = () => { url.Text = ""; name.Text = ""; };
        _clearDialogPrivate = clear;
        try { await ShowDialogAsync(dialog, owner); }
        finally { clear(); if (ReferenceEquals(_clearDialogPrivate, clear)) _clearDialogPrivate = null; }
    }
    // A channel name in a title may be long; those dialogs wrap it (official dialog titles wrap anywhere). Other titles stay plain strings.
    private ContentDialog NewDialog(string title, UIElement content, string? primary = null, bool wrapTitle = false) => new()
    {
        Title = wrapTitle ? new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false } : title, Content = new ScrollViewer { Content = content, MaxHeight = 520, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        XamlRoot = XamlRoot, PrimaryButtonText = primary ?? "", CloseButtonText = T("common.cancel"), DefaultButton = ContentDialogButton.Close,
    };
    private async Task ShowDialogAsync(ContentDialog dialog, MonitorAuthorityLease owner, System.Action? opened = null)
    {
        if (_dialog != null || !owner.IsCurrent) return;
        var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaded = false;
        void OnLoaded(object sender, RoutedEventArgs args) { if (loaded) return; loaded = true; opened?.Invoke(); }
        void OnUnloaded(object sender, RoutedEventArgs args) => unloaded.TrySetResult();
        dialog.Loaded += OnLoaded; dialog.Unloaded += OnUnloaded;
        _dialog = dialog;
        dialog.Closing += (_, args) => { if (owner.IsCurrent && _controller?.State is { OperationPending: true } or { Saving: true }) args.Cancel = true; };
        using var retired = owner.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try { if (owner.IsCurrent) await dialog.ShowAsync(); }
        catch (Exception) { if (owner.IsCurrent) ReportLocalError("Could not open the notification dialog. Close other dialogs and try again."); }
        finally
        {
            if (loaded || dialog.IsLoaded) await unloaded.Task;
            dialog.Loaded -= OnLoaded; dialog.Unloaded -= OnUnloaded;
            if (ReferenceEquals(_dialog, dialog)) _dialog = null;
            QueueRender();
        }
    }
    /// <summary>Whether a Windows contrast theme is on. A seam for the native check, which cannot turn a contrast theme on.</summary>
    internal static Func<bool> HighContrastProbe { get; set; } = () => { try { return new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast; } catch { return false; } };
    /// <summary>Official --destructive-fill #dc2626 under white text; hover is the official <c>hover:bg-destructive-fill/90</c> (the fill at 90%
    /// opacity). The official button has no separate pressed state: the darker pressed fill is a native-only affordance. Page-local so the
    /// shared style and theme stay untouched. In a Windows contrast theme there is no fixed red: null leaves the dialog's default button,
    /// which uses the system colors. A style that is not based on the default Button style loses its keyboard focus rectangle unless it
    /// asks for the system one, so this one does (with the default button's focus margin).</summary>
    internal static Style? DestructiveConfirmStyle()
    {
        if (HighContrastProbe()) return null;
        var template = (ControlTemplate)XamlReader.Load("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Border x:Name="DestructiveRoot" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                CornerRadius="{TemplateBinding CornerRadius}" Padding="{TemplateBinding Padding}">
                <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CommonStates">
                  <VisualState x:Name="Normal"/>
                  <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="DestructiveRoot.Background" Value="#E6DC2626"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="DestructiveRoot.Background" Value="#991B1B"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="DestructiveRoot.Opacity" Value="0.5"/></VisualState.Setters></VisualState>
                </VisualStateGroup></VisualStateManager.VisualStateGroups>
                <ContentPresenter Content="{TemplateBinding Content}" Foreground="{TemplateBinding Foreground}" FontSize="{TemplateBinding FontSize}" FontWeight="{TemplateBinding FontWeight}"
                  HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
            </ControlTemplate>
            """);
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xDC, 0x26, 0x26))));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Microsoft.UI.Colors.White)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(10)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 8, 16, 8)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, Microsoft.UI.Text.FontWeights.Medium));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36d));
        style.Setters.Add(new Setter(Control.UseSystemFocusVisualsProperty, true));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualMarginProperty, new Thickness(-3)));
        return style;
    }
    /// <summary>A confirmation in the destructive style. The action returns false when nothing was sent (the page is busy, or the
    /// confirmed channel is no longer the one listed); the dialog then stays open with that said and no way to repeat it. A write the
    /// server accepted whose result could not be read back closes the dialog (the command is not repeatable and was not refused) and
    /// leaves the page in its failed-read state with Reload. After a refusal or a failure the button comes back only when a repeat can
    /// be sent: not while an outcome is unknown, a conflict is open or access is blocked.</summary>
    private async Task ConfirmAsync(string title, string description, string label, Func<Task<bool>> action, string? done = null, Func<bool>? itemChanged = null)
    {
        var owner = _controller?.State.Owner; if (_dialog != null || owner?.IsCurrent != true) return;
        var content = new StackPanel { Spacing = 12 }; content.Children.Add(U.Text(description)); var error = U.Text("", 12); content.Children.Add(error);
        var dialog = NewDialog(title, content, label, wrapTitle: true); if (DestructiveConfirmStyle() is { } destructive) dialog.PrimaryButtonStyle = destructive;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral();
            try
            {
                if (!owner.IsCurrent || _controller?.State.Owner != owner) { dialog.Hide(); return; }
                dialog.IsPrimaryButtonEnabled = false; var sent = await action();
                var state = _controller?.State;
                if (!owner.IsCurrent || state == null) args.Cancel = false;
                else if (!sent) error.Text = NotSentMessage(state, itemChanged?.Invoke() == true, false);
                else if (!state.Loaded && state.Error != null)
                {
                    // The server took the command; reading the result failed. The page shows its own load failure with Reload.
                    args.Cancel = false; ToastWarning("The change was accepted, but the settings could not be read again. Reload to see the current state.");
                }
                else if (state.Error == null) { args.Cancel = false; if (done != null) Toast(done); }
                else { error.Text = state.Error; dialog.IsPrimaryButtonEnabled = !(state.OperationUnknown || state.OutcomeUnknown || state.Conflict || state.AccessBlocked); }
            }
            finally { deferral.Complete(); }
        };
        await ShowDialogAsync(dialog, owner);
    }
    // Only one ContentDialog can be open on a root. Behind another one, such as the shell's restart confirmation,
    // the secret waits with the controller and is offered again once that dialog has unloaded.
    private bool SecretWaitsBehindDialog()
    {
        if (XamlRoot is not { } root) return false;
        foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is not ContentDialog other) continue;
            RoutedEventHandler? unloaded = null;
            unloaded = (_, _) => { other.Unloaded -= unloaded; _secretTried = null; QueueRender(); };
            other.Unloaded += unloaded;
            return true;
        }
        return false;
    }
    private async Task ShowSecretAsync()
    {
        var owner = _controller?.State.Owner; if (_dialog != null || owner?.IsCurrent != true || SecretWaitsBehindDialog()) return;
        var content = new StackPanel { Spacing = 12 }; content.Children.Add(U.Text(T("web.common.signingSecret.description"), 13, true));
        var text = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
        AutomationProperties.SetName(text, T("web.common.signingSecret.title")); content.Children.Add(text);
        var copy = U.Action(T("web.common.signingSecret.copy"), "NotificationsCopySigningSecret"); content.Children.Add(copy);
        copy.Click += (_, _) =>
        {
            if (!owner.IsCurrent || text.Text.Length == 0) return;
            try { var package = new DataPackage(); package.SetText(text.Text); Clipboard.SetContent(package); Clipboard.Flush(); copy.Content = T("web.common.signingSecret.copied"); }
            catch { copy.Content = T("web.common.signingSecret.copyFailed"); }
        };
        var dialog = NewDialog(T("web.common.signingSecret.title"), content); dialog.CloseButtonText = T("web.common.signingSecret.saved");
        System.Action clear = () => text.Text = ""; _clearDialogPrivate = clear;
        using var retired = owner.Token.Register(() => DispatcherQueue.TryEnqueue(() => text.Text = ""));
        _secretShown = true;
        // The one-time value is taken only by a dialog that has actually opened; one that never loads leaves it with the controller.
        try { await ShowDialogAsync(dialog, owner, () => { text.Text = _controller?.TakeSigningSecret() ?? ""; if (text.Text.Length == 0) DispatcherQueue.TryEnqueue(() => dialog.Hide()); }); }
        finally { _secretShown = false; clear(); if (ReferenceEquals(_clearDialogPrivate, clear)) _clearDialogPrivate = null; }
    }
}
