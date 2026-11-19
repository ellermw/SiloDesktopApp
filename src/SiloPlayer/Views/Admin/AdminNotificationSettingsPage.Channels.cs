using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
    private readonly Dictionary<string, TextBlock> _channelResults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProgressRing> _channelBusy = new(StringComparer.Ordinal);
    private IReadOnlyList<AdminNotificationChannel>? _renderedChannels;
    private readonly TextBlock _channelError = U.Text("", 12);
    private Button _channelRetry = null!, _channelMore = null!, _channelAdd = null!;
    private static readonly string[] EventLabels = ["newMovies", "newEpisodes", "newAudiobooks", "newEbooks", "requestSubmitted", "requestApproved", "requestDeclined", "requestFulfilled"];
    private void BuildServerChannels(StackPanel parent)
    {
        parent.Children.Add(_channelError); parent.Children.Add(_channelList);
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
        _chips["server"].Text = state.ChannelsLoaded ? N("serverChannelsCard.destinations", ("count", state.Channels.Count), ("count_text", state.Channels.Count)) +
            (failingCount > 0 ? " · " + N("serverChannelsCard.failing", ("count", failingCount), ("count_text", failingCount)) : "") : "";
        if (!ReferenceEquals(_renderedChannels, state.Channels))
        {
            _renderedChannels = state.Channels; _channelList.Children.Clear(); _channelButtons.Clear(); _channelToggles.Clear(); _channelToggleValues.Clear(); _channelResults.Clear(); _channelBusy.Clear();
            foreach (var channel in state.Channels) _channelList.Children.Add(ChannelCard(channel));
            if (state.ChannelsLoaded && state.Channels.Count == 0) _channelList.Children.Add(U.Text(N("serverChannels.empty"), 13, true));
        }
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
        foreach (var (id, result) in _channelResults) U.UpdateLiveText(result, state.ChannelTestResults.TryGetValue(id, out var test) ? TestText(test, "serverChannels") : "");
        foreach (var (id, ring) in _channelBusy) { ring.IsActive = state.OperationPending && state.ActiveTarget == id; ring.Visibility = ring.IsActive ? Visibility.Visible : Visibility.Collapsed; }
    }
    private Border ChannelCard(AdminNotificationChannel channel)
    {
        var stack = new StackPanel { Spacing = 8 };
        var heading = new Grid { ColumnSpacing = 8 };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 4 }; title.Children.Add(U.Text(channel.Name)); title.Children.Add(U.Text(channel.Type + " · " + channel.UrlHost, 12, true)); heading.Children.Add(title);
        var toggle = new ToggleSwitch { IsOn = channel.Enabled, OffContent = N("serverChannels.disabled"), OnContent = N("serverChannels.enabled"), MinWidth = 0 };
        AutomationProperties.SetName(toggle, channel.Name + " · " + N("serverChannels.enabled")); AutomationProperties.SetAutomationId(toggle, "NotificationsChannelEnabled_" + channel.Id);
        toggle.IsEnabled = false; Grid.SetColumn(toggle, 1); heading.Children.Add(toggle); stack.Children.Add(heading);
        _channelToggles.Add(toggle);
        _channelToggleValues[toggle] = channel.Enabled;
        toggle.Toggled += async (_, _) => { if (!_syncChannels && _controller != null && toggle.IsOn != channel.Enabled) await _controller.SaveChannelAsync(channel.Id, new() { ["enabled"] = toggle.IsOn }); };
        // Toggles participate in the same operation gate as the card commands.
        var events = AdminNotificationSettingsApi.EventKeys.Select((key, index) => (key, index)).Where(x => channel.Events.GetValueOrDefault(x.key)).Select(x => N("serverChannels.events." + EventLabels[x.index])).ToArray();
        stack.Children.Add(U.Text(events.Length == 0 ? N("serverChannels.noEvents") : string.Join(" · ", events), 12, true));
        if (channel.Failing)
        {
            var detail = channel.LastFailureMessage ?? (channel.LastFailureStatus is { } status ? N("serverChannels.httpStatus", ("status", status)) : N("serverChannels.httpError"));
            stack.Children.Add(U.Text(channel.DisabledReason is { } reason ? N("serverChannels.disabledReason", ("reason", reason)) : N("serverChannels.lastFailureWhen", ("when", Relative(channel.LastFailureAt!.Value)), ("detail", detail)), 12));
        }
        else if (channel.LastSuccessAt is { } last) stack.Children.Add(U.Text(N("serverChannels.lastPost", ("when", Relative(last))), 12, true));
        var result = U.Text("", 12); _channelResults[channel.Id] = result; AutomationProperties.SetLiveSetting(result, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite); stack.Children.Add(result);
        var actions = Actions(stack);
        var busy = new ProgressRing { Width = 16, Height = 16, IsActive = false, Visibility = Visibility.Collapsed }; _channelBusy[channel.Id] = busy;
        void Command(string label, string id, Func<Task> action)
        {
            var button = U.Action(label, id + "_" + channel.Id); actions.Children.Add(button); _channelButtons.Add(button);
            button.Click += async (_, _) => await action();
        }
        Command(N("serverChannels.test"), "NotificationsChannelTest", () => _controller?.TestChannelAsync(channel.Id) ?? Task.CompletedTask);
        Command(N("serverChannels.edit"), "NotificationsChannelEdit", () => EditChannelAsync(channel));
        if (channel.Type == "generic") Command(N("serverChannels.rotateSecret"), "NotificationsChannelRotate", () => _controller?.RotateChannelAsync(channel.Id) ?? Task.CompletedTask);
        Command(N("serverChannels.delete"), "NotificationsChannelDelete", () => ConfirmAsync(N("serverChannels.deleteTitle", ("name", channel.Name)), N("serverChannels.deleteDescription"), N("serverChannels.deleteConfirm"), () => _controller?.DeleteChannelAsync(channel.Id) ?? Task.CompletedTask));
        actions.Children.Add(busy);
        return new Border { Child = stack, Padding = new(16), CornerRadius = new(12), BorderThickness = new(1), BorderBrush = U.Brush("BorderBrush") };
    }
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
        var url = U.Input(urlLabel, "NotificationsChannelUrl", channel == null ? "https://discord.com/api/webhooks/…" : N("serverChannels.currentHost", ("host", channel.UrlHost)));
        url.Width = double.NaN; url.HorizontalAlignment = HorizontalAlignment.Stretch; url.MaxLength = 4096; url.IsSpellCheckEnabled = false; url.IsTextPredictionEnabled = false; content.Children.Add(url);
        var toggles = new Dictionary<string, ToggleSwitch>();
        for (var i = 0; i < AdminNotificationSettingsApi.EventKeys.Count; i++)
        {
            if (i is 0 or 4) content.Children.Add(U.Text(N("serverChannels.events." + (i == 0 ? "newContent" : "mediaRequests"))));
            var key = AdminNotificationSettingsApi.EventKeys[i]; var label = N("serverChannels.events." + EventLabels[i]);
            var toggle = new ToggleSwitch { IsOn = channel?.Events.GetValueOrDefault(key) ?? i < 4, OnContent = "", OffContent = "", MinWidth = 0 };
            AutomationProperties.SetName(toggle, label); toggles[key] = toggle; content.Children.Add(new AdminSettingsRow(label, "", toggle));
        }
        var error = U.Text("", 12); content.Children.Add(error);
        var editor = new ContentControl { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var dialog = NewDialog(channel == null ? N("serverChannels.addTitle") : N("serverChannels.editTitle", ("name", channel.Name)), editor,
            channel == null ? N("serverChannels.create") : T("common.save"));
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
                else if (_controller.State.Error == null) { args.Cancel = false; }
                else { error.Text = _controller.State.Error; editor.IsEnabled = true; Validate(); }
            }
            finally { deferral.Complete(); }
        };
        System.Action clear = () => { url.Text = ""; name.Text = ""; };
        _clearDialogPrivate = clear;
        try { await ShowDialogAsync(dialog, owner); }
        finally { clear(); if (ReferenceEquals(_clearDialogPrivate, clear)) _clearDialogPrivate = null; }
    }
    private ContentDialog NewDialog(string title, UIElement content, string? primary = null) => new()
    {
        Title = title, Content = new ScrollViewer { Content = content, MaxHeight = 520, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        XamlRoot = XamlRoot, PrimaryButtonText = primary ?? "", CloseButtonText = T("common.cancel"), DefaultButton = ContentDialogButton.Close,
    };
    private async Task ShowDialogAsync(ContentDialog dialog, MonitorAuthorityLease owner)
    {
        if (_dialog != null || !owner.IsCurrent) return;
        var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaded = false;
        void OnLoaded(object sender, RoutedEventArgs args) => loaded = true;
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
    private async Task ConfirmAsync(string title, string description, string label, Func<Task> action)
    {
        var owner = _controller?.State.Owner; if (_dialog != null || owner?.IsCurrent != true) return;
        var content = new StackPanel { Spacing = 12 }; content.Children.Add(U.Text(description)); var error = U.Text("", 12); content.Children.Add(error);
        var dialog = NewDialog(title, content, label);
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral();
            try
            {
                if (!owner.IsCurrent || _controller?.State.Owner != owner) { dialog.Hide(); return; }
                dialog.IsPrimaryButtonEnabled = false; await action();
                if (!owner.IsCurrent || _controller.State.Error == null) args.Cancel = false;
                else { error.Text = _controller.State.Error; dialog.IsPrimaryButtonEnabled = !_controller.State.OperationUnknown; }
            }
            finally { deferral.Complete(); }
        };
        await ShowDialogAsync(dialog, owner);
    }
    private async Task ShowSecretAsync(string secret)
    {
        var owner = _controller?.State.Owner; if (_dialog != null || owner?.IsCurrent != true) return;
        var content = new StackPanel { Spacing = 12 }; content.Children.Add(U.Text(T("web.common.signingSecret.description"), 13, true));
        var text = new TextBox { Text = secret, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
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
        try { await ShowDialogAsync(dialog, owner); }
        finally { _secretShown = false; clear(); secret = ""; if (ReferenceEquals(_clearDialogPrivate, clear)) _clearDialogPrivate = null; }
    }
}
