using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;

namespace SiloPlayer.Services.PlaybackMenus;

internal sealed record PlaybackAudioChoice(int SourceIndex, string Title, string Metadata,
    string Codec, string Channels, bool IsDefault, bool IsCurrent);

internal enum PlaybackMenuCloseReason { Selection, PointerSelection, Cancel, OutsidePointer, Deactivated, OwnerRetired }

/// <summary>A menu-sized owned surface. Playback and account authority stay with PlayerService.</summary>
internal sealed class PlaybackAudioMenu
{
    private readonly Window _window;
    private readonly ListView _list;
    private readonly Grid _root;
    private readonly MpvVideoWindow _owner;
    private readonly PlaybackMenuAnchor _anchor;
    private readonly Func<bool> _isCurrent;
    private readonly Action<int> _selected;
    private readonly Action<PlaybackMenuCloseReason> _closed;
    private readonly Action<DependencyObject>? _applyTypography;
    private readonly IReadOnlyList<PlaybackAudioChoice> _choices;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action _dismiss;
    private readonly DispatcherQueueTimer _ownerWatch;
    private bool _finished;
    private bool _nativeClosed;
    private bool _shown;
    private bool _focusOnLoad;
    private bool _pointerSelection;

    internal PlaybackAudioMenu(MpvVideoWindow owner, PlaybackMenuAnchor anchor,
        IReadOnlyList<PlaybackAudioChoice> choices, Func<bool> isCurrent,
        Action<int> selected, Action<PlaybackMenuCloseReason> closed, Action<DependencyObject>? applyTypography = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count < 2) throw new ArgumentException("Audio selection requires multiple tracks.", nameof(choices));
        _owner = owner; _anchor = anchor; _choices = choices;
        _isCurrent = isCurrent; _selected = selected; _closed = closed;
        _applyTypography = applyTypography;
        _dismiss = () => Close(PlaybackMenuCloseReason.OutsidePointer);
        _list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection(),
        };
        AutomationProperties.SetName(_list, "Audio tracks");
        foreach (var choice in choices)
        {
            var copy = new StackPanel { Spacing = 4, Margin = new Thickness(4, 8, 4, 8) };
            copy.Children.Add(new TextBlock
            {
                Text = choice.Title, TextWrapping = TextWrapping.Wrap,
                MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            if (choice.Metadata.Length > 0)
                copy.Children.Add(new TextBlock { Text = choice.Metadata, Opacity = .7, TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 });
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var text in new[] { choice.Codec, choice.Channels, choice.IsDefault ? "Default" : "" }.Where(s => s.Length > 0))
                badges.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 2, 5, 2),
                    Background = ThemeBrush("SurfaceRaisedBrush", 0x28, 0x28, 0x2E),
                    Child = new TextBlock { Text = text, FontSize = 11 },
                });
            copy.Children.Add(badges);
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(copy);
            var check = new TextBlock { Text = choice.IsCurrent ? "\u2713" : "", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(check, 1); row.Children.Add(check);
            var item = new ListViewItem { Content = row, Tag = choice.SourceIndex, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(item, string.Join(", ", new[] { choice.Title, choice.Metadata, choice.Codec, choice.Channels,
                choice.IsDefault ? "Default" : "", choice.IsCurrent ? "Current track" : "" }.Where(s => s.Length > 0)));
            ToolTipService.SetToolTip(item, AutomationProperties.GetName(item));
            item.Loaded += OnRowLoaded;
            _list.Items.Add(item);
        }
        var heading = new TextBlock { Text = "Audio", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(12, 10, 12, 8) };
        _root = new Grid
        {
            RequestedTheme = ElementTheme.Dark, Background = ThemeBrush("PopoverBrush", 0x18, 0x18, 0x1C),
            BorderBrush = ThemeBrush("BorderBrush", 0x28, 0x28, 0x2E),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
        };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(heading); Grid.SetRow(_list, 1); _root.Children.Add(_list);
        _window = new Window { Content = _root, Title = "Silo Desktop Player — Audio" };
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        SetWindowLongPtrW(Hwnd, -8, owner.Hwnd);
        if (_window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        }
        var styles = GetWindowLongPtrW(Hwnd, -20).ToInt64();
        SetWindowLongPtrW(Hwnd, -20, new IntPtr((styles | 0x80) & ~0x40000));
        try { _window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico")); } catch { /* Cosmetic only. */ }
        _root.Loaded += OnLoaded;
        _root.PreviewKeyDown += OnKeyDown;
        _root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        _root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        _root.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerReleased), true);
        _list.ItemClick += OnItemClick;
        _window.Activated += OnActivated;
        _window.Closed += OnClosed;
        _ownerWatch = _root.DispatcherQueue.CreateTimer();
        _ownerWatch.Interval = TimeSpan.FromMilliseconds(100);
        _ownerWatch.Tick += OnOwnerWatch;
    }

    internal IntPtr Hwnd { get; }

    internal async Task ShowAsync()
    {
        if (!_isCurrent() || _owner.Hwnd == IntPtr.Zero || !IsWindowVisible(_owner.Hwnd))
        {
            Close(PlaybackMenuCloseReason.OwnerRetired);
            return;
        }
        Position();
        _owner.SetMenuDismissal(_dismiss);
        _owner.SurfaceChanged += OnOwnerChanged;
        var foreground = GetForegroundWindow();
        _focusOnLoad = foreground == _owner.Hwnd || foreground == GetWindow(_owner.Hwnd, 4);
        _shown = true;
        _ownerWatch.Start();
        _window.AppWindow.Show(_focusOnLoad);
        try { await _loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) when (_finished) { return; }
        if (!_isCurrent()) Close(PlaybackMenuCloseReason.OwnerRetired);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_finished || !_isCurrent()) { Close(PlaybackMenuCloseReason.OwnerRetired); return; }
        _applyTypography?.Invoke(_root);
        var selected = Math.Max(0, _choices.ToList().FindIndex(c => c.IsCurrent));
        _list.SelectedIndex = selected;
        ScrollSelectionIntoView();
        if (_focusOnLoad && GetForegroundWindow() == Hwnd && _list.Items[selected] is ListViewItem item)
            item.Focus(FocusState.Programmatic);
        _loaded.TrySetResult();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_shown && args.WindowActivationState == WindowActivationState.Deactivated)
            Close(PlaybackMenuCloseReason.Deactivated);
    }

    private void OnRowLoaded(object sender, RoutedEventArgs args)
    {
        if (!_finished && sender is DependencyObject row) _applyTypography?.Invoke(row);
    }

    private void OnOwnerChanged()
    {
        if (_owner.Hwnd == IntPtr.Zero || !_isCurrent() || !IsWindowVisible(_owner.Hwnd)) Close(PlaybackMenuCloseReason.OwnerRetired);
        else Position();
    }

    private void OnOwnerWatch(DispatcherQueueTimer sender, object args)
    {
        // API/profile identity can retire without a native window message.
        // This check exists only while the menu owns its UI lease.
        if (_owner.Hwnd == IntPtr.Zero || !_isCurrent() || !IsWindowVisible(_owner.Hwnd))
            Close(PlaybackMenuCloseReason.OwnerRetired);
    }

    private void Position()
    {
        if (!GetWindowRect(_owner.Hwnd, out var video)) throw new InvalidOperationException("Playback surface unavailable.");
        var scale = Math.Max(1, GetDpiForWindow(_owner.Hwnd)) / 96d;
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_owner.Hwnd), Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        var bounds = video;
        var intersection = new Rect
        {
            Left = Math.Max(video.Left, area.X), Top = Math.Max(video.Top, area.Y),
            Right = Math.Min(video.Right, area.X + area.Width), Bottom = Math.Min(video.Bottom, area.Y + area.Height),
        };
        // A deliberately offscreen owner stays offscreen; never expose or
        // activate it merely because the nearest display has a work area.
        if (intersection.Right > intersection.Left && intersection.Bottom > intersection.Top) bounds = intersection;
        var width = Math.Min(bounds.Right - bounds.Left, (int)Math.Ceiling(350 * scale));
        var height = Math.Min(bounds.Bottom - bounds.Top, (int)Math.Ceiling(Math.Min(460d, 52d + _choices.Count * 102d) * scale));
        var right = video.Left + (int)(_anchor.Right * (video.Right - video.Left));
        var top = video.Top + (int)(_anchor.Top * (video.Bottom - video.Top)) - height - (int)(8 * scale);
        var left = Math.Clamp(right - width, bounds.Left, Math.Max(bounds.Left, bounds.Right - width));
        top = Math.Clamp(top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height));
        _window.AppWindow.MoveAndResize(new RectInt32(left, top, Math.Max(1, width), Math.Max(1, height)));
    }

    private void OnItemClick(object sender, ItemClickEventArgs args)
    {
        // WinUI reports Content for an explicit item container, for pointer
        // clicks as well as UIA Invoke. Resolve only our exact owned row.
        var item = _list.Items.OfType<ListViewItem>()
            .FirstOrDefault(row => ReferenceEquals(row, args.ClickedItem) || ReferenceEquals(row.Content, args.ClickedItem));
        if (item?.Tag is int index) Select(index, _pointerSelection);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args) => _pointerSelection = true;

    private void OnPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        // ItemClick runs in the release dispatch. Clear after that dispatch so
        // later keyboard/UIA activation cannot inherit an earlier pointer.
        _root.DispatcherQueue.TryEnqueue(() => _pointerSelection = false);
    }

    private void Select(int index, bool pointer = false)
    {
        if (_finished || !_isCurrent()) { Close(PlaybackMenuCloseReason.OwnerRetired); return; }
        Close(pointer ? PlaybackMenuCloseReason.PointerSelection : PlaybackMenuCloseReason.Selection);
        _selected(index);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var action = args.Key switch
        {
            VirtualKey.Escape or VirtualKey.GamepadB => "cancel",
            VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA => "activate",
            VirtualKey.Home => "home", VirtualKey.End => "end",
            VirtualKey.Up or VirtualKey.Left => "up",
            VirtualKey.Down or VirtualKey.Right or VirtualKey.Tab => "down",
            _ => (int)args.Key switch
            {
                0xCB or 0xCD or 0xD3 or 0xD6 => "up",
                0xCC or 0xCE or 0xD4 or 0xD5 => "down", _ => null,
            },
        };
        if (action != null) { args.Handled = true; Input(action); }
    }

    internal void Input(string action)
    {
        _pointerSelection = false;
        if (_finished || !_isCurrent()) { Close(PlaybackMenuCloseReason.OwnerRetired); return; }
        if (action == "cancel") { Close(PlaybackMenuCloseReason.Cancel); return; }
        if (action == "activate")
        {
            if (_list.SelectedItem is ListViewItem { Tag: int index }) Select(index);
            return;
        }
        var current = Math.Max(0, _list.SelectedIndex);
        _list.SelectedIndex = action switch
        {
            "home" => 0, "end" => _list.Items.Count - 1,
            "up" or "left" => Math.Max(0, current - 1),
            "down" or "right" => Math.Min(_list.Items.Count - 1, current + 1), _ => current,
        };
        ScrollSelectionIntoView();
        if (GetForegroundWindow() == Hwnd && _list.SelectedItem is ListViewItem item) item.Focus(FocusState.Keyboard);
    }

    internal void Close(PlaybackMenuCloseReason reason)
    {
        if (_finished) return;
        _finished = true;
        _ownerWatch.Stop();
        _ownerWatch.Tick -= OnOwnerWatch;
        var restore = reason is PlaybackMenuCloseReason.Selection or PlaybackMenuCloseReason.Cancel
            && _isCurrent() && GetForegroundWindow() == Hwnd;
        _owner.ClearMenuDismissal(_dismiss);
        _owner.SurfaceChanged -= OnOwnerChanged;
        _root.Loaded -= OnLoaded; _root.PreviewKeyDown -= OnKeyDown;
        _root.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed));
        _root.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased));
        _root.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerReleased));
        _list.ItemClick -= OnItemClick;
        foreach (ListViewItem row in _list.Items) row.Loaded -= OnRowLoaded;
        _window.Activated -= OnActivated; _window.Closed -= OnClosed;
        _loaded.TrySetCanceled();
        try { if (!_nativeClosed) _window.Close(); }
        finally
        {
            // Closing releases the foreground naturally. Restore only a
            // current explicit selection/cancel before its lease is retired;
            // deactivation and outside clicks never restore foreground.
            var foreground = GetForegroundWindow();
            if (restore && _isCurrent() &&
                (foreground == Hwnd || foreground == _owner.Hwnd || foreground == GetWindow(_owner.Hwnd, 4)))
                SetForegroundWindow(_owner.Hwnd);
            _closed(reason);
        }
    }

    private void ScrollSelectionIntoView()
    {
        _list.ScrollIntoView(_list.SelectedItem, ScrollIntoViewAlignment.Leading);
        // Realizing a variable-height row can change the virtualized extent.
        // Re-align after that row has its measured wrapped-text height.
        _list.UpdateLayout();
        _list.ScrollIntoView(_list.SelectedItem, ScrollIntoViewAlignment.Leading);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _nativeClosed = true;
        Close(PlaybackMenuCloseReason.OwnerRetired);
    }

    private static Brush ThemeBrush(string key, byte red, byte green, byte blue) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, red, green, blue));

    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
}
