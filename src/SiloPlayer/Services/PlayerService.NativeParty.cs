using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Services.PlaybackMenus;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private event Action? NativePartyStateChanged;
    private ContentDialog? _nativePartyEndDialog;

    private PlaybackChoiceMenu? CreateNativePartyMenu(NativeMenuOwner owner, PlaybackMenuRequest request,
        Action<PlaybackMenuCloseReason> finish)
    {
        var coordinator = App.Services.GetService<WatchTogetherCoordinator>();
        var room = coordinator?.ActiveRoom;
        if (room?.Room == null) return null;
        var roomId = room.Room.RoomId;
        bool RoomCurrent() => owner.IsCurrent() && _playbackTerminalState == null && ReferenceEquals(coordinator!.ActiveRoom, room)
            && room.Room?.RoomId == roomId && room.ClosedReason == null;
        bool Current() => RoomCurrent() && !IsLoading && !_switchingContent && _nativeMenuGate.IsCurrent(request);
        if (!RoomCurrent()) return null;
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(12, 6, 12, 12), MaxHeight = 270 };
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var policy = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, MaxLines = 3 };
        var members = new StackPanel { Spacing = 8 };
        var memberScroll = new ScrollViewer { Content = members, MaxHeight = 160, IsTabStop = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(memberScroll, "Watch party viewers");
        footer.Children.Add(summary); footer.Children.Add(policy); footer.Children.Add(error); footer.Children.Add(memberScroll);
        XamlRoot? footerRoot = null;
        void ResizeFooter(XamlRoot root, XamlRootChangedEventArgs args)
            => memberScroll.MaxHeight = Math.Clamp(root.Size.Height - 450, 40, 160);
        void FooterLoaded(object sender, RoutedEventArgs args)
        {
            if (footer.XamlRoot is not { } root || ReferenceEquals(root, footerRoot)) return;
            if (footerRoot != null) footerRoot.Changed -= ResizeFooter;
            footerRoot = root; root.Changed += ResizeFooter;
            memberScroll.MaxHeight = Math.Clamp(root.Size.Height - 450, 40, 160);
        }
        footer.Loaded += FooterLoaded;
        PlaybackChoiceMenu? menu = null;
        IReadOnlyList<PlaybackMenuChoice> Choices()
        {
            var snapshot = room.Room!;
            var rows = new List<PlaybackMenuChoice>();
            var available = !room.IsBusy && room.ClosedReason == null;
            var reason = room.IsBusy ? "A room update is pending." : "";
            if (snapshot.SelfCanManageRoom)
            {
                rows.Add(new("invite", "Invite", "Copy invite link", [], IsEnabled: available, DisabledReason: reason, CloseOnInvoke: false));
                rows.Add(new("toggle-policy", snapshot.GuestControlPolicy == "guest_play_pause" ? "Host Only" : "Allow Pause",
                    "Guest permission covers pause and resume. Seeking stays with the host.", [], IsEnabled: available,
                    DisabledReason: reason, CloseOnInvoke: false, IsPending: room.PendingPlaybackPanelAction == "toggle-policy"));
                var canStop = available && snapshot.Phase == "playing" && room.ConnectionState == "connected";
                rows.Add(new("stop", "Stop", "Stop for everyone and return to the room with this item staged.", [], IsEnabled: canStop,
                    DisabledReason: reason.Length > 0 ? reason : snapshot.Phase != "playing" ? "Playback is already stopped." :
                        room.ConnectionState != "connected" ? "Reconnect to the room before stopping playback." : "",
                    CloseOnInvoke: false, IsPending: room.PendingPlaybackPanelAction == "stop"));
                rows.Add(new("end", "End watch party…", "End the room for everyone after confirmation.", [], IsEnabled: available,
                    DisabledReason: reason, CloseOnInvoke: false));
            }
            if (_offeredRoomQuality != null && _offeredFromQuality == ActiveQualityTier && IsWatchTogetherPlayback)
                rows.Add(new("lower-quality", "Lower quality", "Keep watching with a lower streaming quality.", [], IsEnabled: available,
                    DisabledReason: reason));
            if (rows.Count == 0)
                rows.Add(new("room-status", "Room status", "The host manages this watch party.", [], IsEnabled: false,
                    DisabledReason: "Only the host can invite, change policy, stop or end the room.", CloseOnInvoke: false));
            return rows;
        }
        void Refresh()
        {
            if (!Current()) { menu?.Close(PlaybackMenuCloseReason.OwnerRetired); return; }
            var snapshot = room.Room!;
            var connection = room.ConnectionState switch { "connected" => "Connected", "connecting" => "Connecting",
                "reconnecting" => "Reconnecting", _ => "Disconnected" };
            summary.Text = $"{snapshot.Code} · {snapshot.MemberCount} {(snapshot.MemberCount == 1 ? "viewer" : "viewers")} · {connection}";
            policy.Text = snapshot.SelfCanManageRoom
                ? snapshot.GuestControlPolicy == "guest_play_pause" ? "Guests can pause & resume" : "Only you control playback"
                : snapshot.SelfCanControlTransport ? "You can pause & resume" : "Host controls playback";
            error.Text = room.ErrorMessage ?? (room.IsBusy ? "Updating watch party…" : room.NoticeMessage ?? "");
            error.ClearValue(TextBlock.ForegroundProperty);
            if (room.ErrorMessage != null) error.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];
            error.Visibility = error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            var identities = snapshot.Members.Select(member => $"{member.UserId}:{member.ProfileId}").ToArray();
            var rebuild = members.Children.Count != identities.Length || members.Children.Cast<TextBlock>()
                .Select(text => (string)text.Tag).Where((id, index) => id != identities[index]).Any();
            var scrollOffset = memberScroll.VerticalOffset;
            if (rebuild) members.Children.Clear();
            for (var index = 0; index < snapshot.Members.Count; index++)
            {
                var member = snapshot.Members[index];
                var status = member.IsBuffering ? "Buffering" : member.IsSyncing ? "Syncing" : member.IsReady ? "Ready" :
                    member.Connected ? "Connected" : "Disconnected";
                var text = rebuild ? new TextBlock { Tag = identities[index], TextWrapping = TextWrapping.Wrap,
                    MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 } : (TextBlock)members.Children[index];
                text.Text = $"{member.DisplayLabel}{(member.IsHost ? " · Host" : "")} · {status}";
                AutomationProperties.SetName(text, text.Text); ToolTipService.SetToolTip(text, text.Text);
                if (rebuild) members.Children.Add(text);
            }
            if (rebuild) _nativeMenuDispatcher?.TryEnqueue(() => { if (Current()) memberScroll.ChangeView(null, scrollOffset, null, true); });
            memberScroll.Visibility = snapshot.Members.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            menu?.UpdateChoices(Choices());
        }
        void Changed(object? sender, PropertyChangedEventArgs args) => QueueRefresh();
        void QueueRefresh()
        {
            if (_nativeMenuDispatcher?.HasThreadAccess == true) Refresh();
            else _nativeMenuDispatcher?.TryEnqueue(Refresh);
        }
        void Action(string id)
        {
            // Close-on-invoke legitimately retires the presentation gate. The
            // captured playback and room still own dispatch after that close.
            if (!RoomCurrent() || IsLoading || _switchingContent || !Choices().Any(row => row.Id == id && row.IsEnabled)) return;
            if (id == "end")
            {
                owner.Mpv.SendScriptMessage("osc-native-dialog-opening", request.OwnerToken, request.Sequence.ToString(CultureInfo.InvariantCulture));
                var activate = PlaybackDialogHost.IsForegroundOwnedBy(owner.Video.Hwnd);
                menu?.Close(PlaybackMenuCloseReason.Selection);
                if (RoomCurrent()) _ = ShowNativePartyEndAsync(owner, request, coordinator!, room, RoomCurrent, activate);
                return;
            }
            _ = coordinator!.InvokePlaybackPanelActionAsync(room, id, RoomCurrent);
        }
        void Closed(PlaybackMenuCloseReason reason)
        {
            footer.Loaded -= FooterLoaded;
            if (footerRoot != null) footerRoot.Changed -= ResizeFooter;
            room.PropertyChanged -= Changed; NativePartyStateChanged -= QueueRefresh;
            finish(reason);
        }
        menu = new PlaybackChoiceMenu(owner.Video, request.Anchor, "Watch Party", Choices(), Current,
            Action, Closed, root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root), footer);
        room.PropertyChanged += Changed; NativePartyStateChanged += QueueRefresh; Refresh();
        return menu;
    }

    private async Task ShowNativePartyEndAsync(NativeMenuOwner owner, PlaybackMenuRequest request,
        WatchTogetherCoordinator coordinator, WatchTogetherRoomViewModel room, Func<bool> isCurrent, bool activate)
    {
        void CloseMarker(bool restore)
        {
            if (ReferenceEquals(owner.Mpv, _mpv)) owner.Mpv.SendScriptMessage("osc-native-dialog-closed", request.OwnerToken,
                request.Sequence.ToString(CultureInfo.InvariantCulture), restore ? "true" : "false");
        }
        if (!isCurrent() || room.Room?.SelfCanManageRoom != true || !await _playbackDialogGate.WaitAsync(0))
        { CloseMarker(false); return; }
        Microsoft.UI.Dispatching.DispatcherQueueTimer? watch = null;
        ContentDialog? dialog = null;
        try
        {
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed,
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"] };
            var content = new StackPanel { Spacing = 10, Children =
                { new TextBlock { Text = "End the watch party for everyone?", TextWrapping = TextWrapping.Wrap }, error } };
            dialog = new ContentDialog { Title = "End watch party?", Content = content, PrimaryButtonText = "End Party",
                CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"] };
            _nativePartyEndDialog = dialog;
            watch = _nativeMenuDispatcher!.CreateTimer(); watch.Interval = TimeSpan.FromMilliseconds(100);
            watch.Tick += (_, _) => { if (!isCurrent() || room.Room?.SelfCanManageRoom != true) dialog.Hide(); };
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                args.Cancel = true;
                var deferral = args.GetDeferral();
                try
                {
                    if (!isCurrent() || room.Room?.SelfCanManageRoom != true) { args.Cancel = false; return; }
                    dialog.IsPrimaryButtonEnabled = false; dialog.PrimaryButtonText = "Ending…"; error.Visibility = Visibility.Collapsed;
                    await coordinator.InvokePlaybackPanelActionAsync(room, "end", isCurrent);
                    if (!isCurrent() || room.ClosedReason == "host_left") args.Cancel = false;
                    else
                    {
                        error.Text = room.ErrorMessage ?? "The watch party could not be ended. Try again.";
                        error.Visibility = Visibility.Visible; dialog.PrimaryButtonText = "End Party"; dialog.IsPrimaryButtonEnabled = true;
                    }
                }
                finally { deferral.Complete(); }
            };
            if (!isCurrent()) return;
            watch.Start();
            await PlaybackDialogHost.ShowAsync(owner.Video.Hwnd, async (xamlRoot, hostHwnd) =>
            {
                if (!isCurrent()) return;
                dialog.XamlRoot = xamlRoot;
                App.Services.GetService<AccessibilityService>()?.ApplySaved(dialog);
                // ContentDialog focuses its default button while loading even
                // when its host used Show(false). Keep background controls
                // unfocusable only through that initial pass; Loaded restores
                // normal keyboard/controller/pointer behavior before interaction.
                void EnableAfterLoad(object sender, RoutedEventArgs args) => dialog.IsEnabled = true;
                var background = !PlaybackDialogHost.IsForegroundOwnedBy(hostHwnd);
                if (background) { dialog.IsEnabled = false; dialog.Loaded += EnableAfterLoad; }
                try { await dialog.ShowAsync(); }
                finally { dialog.Loaded -= EnableAfterLoad; }
            }, activate, isCurrent);
        }
        catch (Exception) { if (isCurrent()) ShowNotice("Watch Party", "The confirmation could not open. Try again.", "error"); }
        finally
        {
            watch?.Stop(); _playbackDialogGate.Release();
            if (ReferenceEquals(_nativePartyEndDialog, dialog)) _nativePartyEndDialog = null;
            CloseMarker(isCurrent());
            if (isCurrent()) PublishNativeMenuScopes();
        }
    }
}
