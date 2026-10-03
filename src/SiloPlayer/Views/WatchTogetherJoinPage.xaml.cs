using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.ViewModels;
using SiloPlayer.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace SiloPlayer.Views;

/// <summary>
/// Watch Party lobby — create a new room or join an existing one via code / invite token.
/// Mirrors the current public Silo WebUI at github.com/Silo-Server/silo-server,
/// main commit 8e2e840474a085c6df6571a5a2850f7eb996810c.
///
/// Navigation parameters:
///   null         → blank create-or-join form
///   string token → auto-join using the given invite token (from deep-link)
///
/// On successful create/join, navigates to <see cref="WatchTogetherRoomPage"/> passing a
/// <see cref="WatchTogetherRoomNavigationArgs"/> with (roomId, roomAccessToken).
/// </summary>
public sealed partial class WatchTogetherJoinPage : Page
{
    public WatchTogetherJoinViewModel ViewModel { get; }
    internal string? RecentPartyDirectory { get; set; }
    internal Func<Task<string?>> ClipboardTextReader { get; set; } = ReadClipboardTextAsync;
    private bool _subscribed;

    public WatchTogetherJoinPage()
    {
        ViewModel = App.Services.GetRequiredService<WatchTogetherJoinViewModel>();
        this.InitializeComponent();
        var accent = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        Border IconTile(string name) => new() { Width = 32, Height = 32, CornerRadius = new CornerRadius(12),
            Background = name == "sparkles" ? new SolidColorBrush(Windows.UI.Color.FromArgb(38, accent.Color.R, accent.Color.G, accent.Color.B)) : (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            Child = name == "sparkles" ? WebUiIcon.Create(name, 16, accent) : WebUiIcon.Navigation(name, 16, (Brush)Application.Current.Resources["SecondaryTextBrush"]) };
        StartHeading.Children.Insert(0, IconTile("sparkles")); JoinHeading.Children.Insert(0, IconTile("log-in"));
        foreach (var option in new[] { (HostPickButton, "crown"), (VoteButton, "vote") })
        {
            var copy = (StackPanel)option.Item1.Content;
            option.Item1.Content = null;
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = WebUiIcon.Navigation(option.Item2, 16); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 2, 0, 0);
            Grid.SetColumn(copy, 1);
            option.Item1.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            row.Children.Add(icon); row.Children.Add(copy); option.Item1.Content = row;
        }
        CreatePartyPanel.SizeChanged += (_, _) => _ = RenderCreatePartyGlowAsync();
        Loaded += (_, _) =>
        {
            _glowPrimary = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            if (_glowColorSubscription == 0)
                _glowColorSubscription = _glowPrimary.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, (_, _) => _ = RenderCreatePartyGlowAsync());
            _ = RenderCreatePartyGlowAsync();
        };
        Unloaded += (_, _) => DetachGlow();
        SubscribeToViewModel();
        Loaded += (_, _) => SubscribeToViewModel();
        Unloaded += (_, _) => UnsubscribeFromViewModel();
        UpdateSelectionButtons();
    }

    private void DetachGlow()
    {
        ++_glowRevision;
        if (_glowPrimary != null && _glowColorSubscription != 0)
            _glowPrimary.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, _glowColorSubscription);
        _glowColorSubscription = 0;
    }
    private long _glowRevision, _glowColorSubscription;
    private SolidColorBrush? _glowPrimary;
    private (int Width, int Height, Windows.UI.Color Color)? _glowLayout;
    private async Task RenderCreatePartyGlowAsync()
    {
        if (!IsLoaded) return;
        // An Image without a Source has zero natural size in the auto-sized
        // card. The content owns the card bounds; use its interior to break
        // that dependency, then give the decorative image those exact bounds.
        var width = (int)Math.Ceiling(CreatePartyPanel.ActualWidth - CreatePartyPanel.BorderThickness.Left - CreatePartyPanel.BorderThickness.Right);
        var height = (int)Math.Ceiling(CreatePartyPanel.ActualHeight - CreatePartyPanel.BorderThickness.Top - CreatePartyPanel.BorderThickness.Bottom);
        if (width <= 0 || height <= 0) return;
        CreatePartyGlow.Width = width; CreatePartyGlow.Height = height;
        var primary = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;
        if (_glowLayout is { } previous && previous.Width == width && previous.Height == height && previous.Color.Equals(primary)) return;
        var revision = ++_glowRevision;
        // Original globe: 256px circle, top/right -96px, primary/20 diagonal
        // gradient, followed by CSS blur64. Overscan keeps the blur unclipped.
        const int overscan = 192;
        var device = Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
        using var source = new Microsoft.Graphics.Canvas.CanvasRenderTarget(device, width + overscan * 2, height + overscan * 2, 96);
        using (var drawing = source.CreateDrawingSession())
        using (var gradient = new Microsoft.Graphics.Canvas.Brushes.CanvasLinearGradientBrush(device,
            Windows.UI.Color.FromArgb(51, primary.R, primary.G, primary.B), Windows.UI.Color.FromArgb(0, primary.R, primary.G, primary.B)))
        {
            drawing.Clear(Microsoft.UI.Colors.Transparent);
            gradient.StartPoint = new System.Numerics.Vector2(width - 159 + overscan, -97 + overscan);
            gradient.EndPoint = new System.Numerics.Vector2(width + 97 + overscan, 159 + overscan);
            drawing.FillEllipse(width - 31 + overscan, 31 + overscan, 128, 128, gradient);
        }
        using var blurred = new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect { Source = source, BlurAmount = 64, BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Soft };
        using var target = new Microsoft.Graphics.Canvas.CanvasRenderTarget(device, width, height, 96);
        using (var drawing = target.CreateDrawingSession())
        {
            drawing.Clear(Microsoft.UI.Colors.Transparent);
            drawing.DrawImage(blurred, -overscan, -overscan);
        }
        using var bytes = new MemoryStream();
        await target.SaveAsync(bytes.AsRandomAccessStream(), Microsoft.Graphics.Canvas.CanvasBitmapFileFormat.Png);
        if (revision != _glowRevision) return;
        bytes.Position = 0;
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        await bitmap.SetSourceAsync(bytes.AsRandomAccessStream());
        if (revision != _glowRevision) return;
        CreatePartyGlow.Source = bitmap;
        _glowLayout = (width, height, primary);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SubscribeToViewModel();
        _ = LoadRecentRoomsAsync();

        // Deep-link: invite token auto-joins
        if (e.Parameter is string token && !string.IsNullOrWhiteSpace(token))
        {
            HeadlineText.Text = "Joining Watch Party";
            AutoJoinPanel.Visibility = Visibility.Visible;
            ContentStack.Visibility = Visibility.Collapsed;
            await ViewModel.JoinByInviteTokenAsync(token.Trim());
            if (ViewModel.LastResponse == null)
            {
                AutoJoinPanel.Visibility = Visibility.Collapsed;
                ContentStack.Visibility = Visibility.Visible;
            }
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        DetachGlow();
        UnsubscribeFromViewModel();
    }

    private void SubscribeToViewModel()
    {
        if (_subscribed) return;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        _subscribed = true;
    }

    private void UnsubscribeFromViewModel()
    {
        if (!_subscribed) return;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _subscribed = false;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.LastResponse) && ViewModel.LastResponse != null)
        {
            var resp = ViewModel.LastResponse;
            ViewModel.LastResponse = null; // clear so back/forward doesn't retrigger
            if (string.IsNullOrEmpty(resp.RoomAccessToken)) return;
            // A completed request can outlive navigation, or this page can be
            // hosted without a Frame. Only an active navigation owner may
            // persist the room proof and move into the room.
            var frame = Frame;
            if (!_subscribed || frame == null) return;
            var client = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>();
            var authority = client.CaptureContext();
            _ = SiloPlayer.Services.RecentPartyStore.RememberAsync(client, App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>(), resp, RecentPartyDirectory);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_subscribed || !ReferenceEquals(Frame, frame) || !client.IsCurrentContext(authority)) return;
                frame.Navigate(typeof(WatchTogetherRoomPage), new WatchTogetherRoomNavigationArgs
                {
                    RoomId = resp.Room.RoomId,
                    RoomAccessToken = resp.RoomAccessToken,
                });
            });
        }
        else if (e.PropertyName == nameof(ViewModel.SelectionMode))
        {
            DispatcherQueue.TryEnqueue(UpdateSelectionButtons);
        }
    }

    private async void PasteRoomCode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = await ClipboardTextReader();
            if (string.IsNullOrWhiteSpace(text)) { ViewModel.ErrorMessage = "Clipboard contains no room code."; return; }
            ViewModel.ErrorMessage = null;
            await ApplyPastedTextAsync(text);
        }
        catch { ViewModel.ErrorMessage = "Could not paste room code."; }
    }
    private static async Task<string?> ReadClipboardTextAsync()
    {
        var clipboard = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        return clipboard.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)
            ? await clipboard.GetTextAsync() : null;
    }
    private async Task ApplyPastedTextAsync(string text)
    {
        text = text.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var link))
        {
            var query = new Windows.Foundation.WwwFormUrlDecoder(link.Query);
            var token = query.FirstOrDefault(p => p.Name == "token")?.Value;
            if (!string.IsNullOrWhiteSpace(token)) { await ViewModel.JoinByInviteTokenAsync(token); return; }
        }
        ViewModel.RoomCode = text.Replace(" ", "").Replace("-", "").ToUpperInvariant();
    }
    private async Task LoadRecentRoomsAsync(List<SiloPlayer.Services.RecentPartyStore.Entry>? suppliedEntries = null)
    {
        var client = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>();
        var auth = App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>();
        var authority = client.CaptureContext();
        var entries = suppliedEntries ?? await SiloPlayer.Services.RecentPartyStore.ReadAsync(client, auth, RecentPartyDirectory);
        if (!client.IsCurrentContext(authority) || !_subscribed) return;
        RecentRoomsList.Children.Clear();
        RecentRoomsPanel.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in entries)
        {
            var row = new Button { Content = $"{entry.Code} · Checking…", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, IsEnabled = false };
            row.Click += (_, _) =>
            {
                if (!_subscribed || !client.IsCurrentContext(authority)) return;
                Frame?.Navigate(typeof(WatchTogetherRoomPage), new WatchTogetherRoomNavigationArgs { RoomId = entry.RoomId, RoomAccessToken = entry.Token });
            };
            RecentRoomsList.Children.Add(row);
            try
            {
                var room = await App.Services.GetRequiredService<SiloPlayer.Core.Api.PlaybackApi>().GetWatchTogetherRoomAsync(entry.RoomId, entry.Token);
                if (!client.IsCurrentContext(authority) || !_subscribed) return;
                row.Content = $"{entry.Code} · {(room.Room.Phase == "ended" ? "Ended" : "Rejoin")}";
                row.IsEnabled = room.Room.Phase != "ended";
            }
            catch (SiloPlayer.Core.Api.ApiException ex) when (ex.StatusCode == 403) { row.Visibility = Visibility.Collapsed; }
            catch (SiloPlayer.Core.Api.ApiException ex) when (ex.StatusCode is 404 or 409 or 410) { row.Content = $"{entry.Code} · Ended"; }
            catch { row.Content = $"{entry.Code} · Status unavailable · Rejoin"; row.IsEnabled = true; }
        }
    }

    private void HostPickButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectionMode = "host_pick";
    }

    private void VoteButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectionMode = "vote";
    }

    private void SelectionButton_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Right or VirtualKey.Down or VirtualKey.End)
        {
            ViewModel.SelectionMode = "vote";
            VoteButton.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.Left or VirtualKey.Up or VirtualKey.Home)
        {
            ViewModel.SelectionMode = "host_pick";
            HostPickButton.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
    }

    private void UpdateSelectionButtons()
    {
        // Style swaps in WinUI 3 are expensive — the framework invalidates
        // the ControlTemplate and rebuilds the visual tree on every swap,
        // which freezes the UI thread for 1-3 seconds on complex content.
        // Flip the individual properties directly instead.
        bool hostPick = ViewModel.SelectionMode == "host_pick";
        ApplySelectionLook(HostPickButton, hostPick);
        ApplySelectionLook(VoteButton, !hostPick);
    }

    private static void ApplySelectionLook(Button btn, bool active)
    {
        if (active)
        {
            btn.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBackgroundBrush"];
            btn.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
            var foreground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)btn.Foreground).Color;
            btn.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(128, foreground.R, foreground.G, foreground.B));
            btn.BorderThickness = new Thickness(1);
        }
        else
        {
            btn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            btn.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
            var border = ((SolidColorBrush)Application.Current.Resources["BorderBrush"]).Color;
            btn.BorderBrush = new SolidColorBrush(border) { Opacity = .6 };
            btn.BorderThickness = new Thickness(1);
        }
    }

    private async void RoomCodeBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.CanJoin)
        {
            await ViewModel.JoinByCodeCommand.ExecuteAsync(null);
        }
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;
        var compact = width < 640;
        var narrow = width < 768;
        var gutter = compact ? 24 : 32;
        PageShell.Padding = new Thickness(gutter, 48, gutter, 48);
        ContentStack.MaxWidth = 896 - gutter * 2;
        ContentStack.Width = Math.Min(ContentStack.MaxWidth, Math.Max(0, width - gutter * 2));
        ActionGrid.ColumnDefinitions[0].Width = new GridLength(narrow ? 1 : 1.15, GridUnitType.Star);
        ActionGrid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ActionGrid.ColumnSpacing = narrow ? 0 : 16;
        InfoGrid.ColumnDefinitions[1].Width = InfoGrid.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        InfoGrid.ColumnSpacing = compact ? 0 : 24;
        HeadlineText.FontSize = width < 640 ? 30 : 36;
        HeadlineText.LineHeight = width < 640 ? 36 : 40;
        if (ViewModel.LastResponse == null && AutoJoinPanel.Visibility != Visibility.Visible)
            HeadlineText.Text = compact ? "Watch together,\nwherever everyone is." : "Watch together, wherever everyone is.";
        PasteLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ModeOptionsGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ModeOptionsGrid.ColumnSpacing = compact ? 0 : 8;
        Grid.SetRow(VoteButton, compact ? 1 : 0); Grid.SetColumn(VoteButton, compact ? 0 : 1);

        for (var index = 0; index < InfoGrid.Children.Count; index++)
        {
            if (InfoGrid.Children[index] is not FrameworkElement child) continue;
            Grid.SetColumn(child, compact ? 0 : index);
            Grid.SetRow(child, compact ? index : 0);
        }

        for (var index = 0; index < ActionGrid.Children.Count; index++)
        {
            if (ActionGrid.Children[index] is not FrameworkElement child) continue;
            Grid.SetColumn(child, narrow ? 0 : index);
            Grid.SetRow(child, narrow ? index : 0);
        }
    }
}

/// <summary>
/// Navigation parameter bundle for <see cref="WatchTogetherRoomPage"/>. Defined here
/// because this is the page that produces it.
/// </summary>
public sealed class WatchTogetherRoomNavigationArgs
{
    public string RoomId { get; set; } = "";
    public string RoomAccessToken { get; set; } = "";
}
