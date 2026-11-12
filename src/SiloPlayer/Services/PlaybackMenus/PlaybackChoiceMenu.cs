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

internal sealed record PlaybackMenuChoice(string Id, string Title, string Metadata,
    IReadOnlyList<string> Badges, bool IsCurrent = false, bool IsEnabled = true,
    string DisabledReason = "", bool CloseOnInvoke = true, bool IsPending = false);

internal enum PlaybackMenuCloseReason { Selection, PointerSelection, Cancel, OutsidePointer, Deactivated, OwnerRetired }

/// <summary>A menu-sized owned surface. Playback and account authority stay with PlayerService.</summary>
internal class PlaybackChoiceMenu
{
    private readonly Window _window;
    private readonly ListView _list;
    private readonly Grid _root;
    private readonly MpvVideoWindow _owner;
    private readonly PlaybackMenuAnchor _anchor;
    private readonly Func<bool> _isCurrent;
    private readonly Action<string> _selected;
    private readonly Action<PlaybackMenuCloseReason> _closed;
    private readonly Action<DependencyObject>? _applyTypography;
    private IReadOnlyList<PlaybackMenuChoice> _choices;
    private readonly FrameworkElement? _footer;
    private readonly Func<PlaybackMenuChoice, FrameworkElement?>? _leadingContent;
    private readonly bool _allowDisabledTraversal;
    private int _footerIndex = -1;
    private bool _keepSelectionVisible = true;
    private bool _layoutQueued;
    private bool _placementPending;
    private readonly PointerEventHandler _wheelHandler;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action _dismiss;
    private readonly DispatcherQueueTimer _ownerWatch;
    private bool _finished;
    private bool _nativeClosed;
    private bool _shown;
    private bool _focusOnLoad;
    private bool _pointerSelection;

    internal PlaybackChoiceMenu(MpvVideoWindow owner, PlaybackMenuAnchor anchor, string title,
        IReadOnlyList<PlaybackMenuChoice> choices, Func<bool> isCurrent,
        Action<string> selected, Action<PlaybackMenuCloseReason> closed, Action<DependencyObject>? applyTypography = null, FrameworkElement? footer = null,
        Func<PlaybackMenuChoice, FrameworkElement?>? leadingContent = null, bool allowDisabledTraversal = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count == 0) throw new ArgumentException("A menu requires a choice.", nameof(choices));
        _owner = owner; _anchor = anchor; _choices = choices;
        _isCurrent = isCurrent; _selected = selected; _closed = closed;
        _applyTypography = applyTypography; _footer = footer; _leadingContent = leadingContent;
        _allowDisabledTraversal = allowDisabledTraversal;
        _dismiss = () => Close(PlaybackMenuCloseReason.OutsidePointer);
        _list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection(),
        };
        if (_allowDisabledTraversal) _list.IsTabStop = true;
        AutomationProperties.SetName(_list, title + " choices");
        BuildChoices();
        var heading = new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(12, 10, 12, 8) };
        _root = new Grid
        {
            RequestedTheme = ElementTheme.Dark, Background = ThemeBrush("PopoverBrush", 0x18, 0x18, 0x1C),
            BorderBrush = ThemeBrush("BorderBrush", 0x28, 0x28, 0x2E),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
        };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(heading); Grid.SetRow(_list, 1); _root.Children.Add(_list);
        if (_footer != null)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_footer, 2); _root.Children.Add(_footer);
        }
        _window = new Window { Content = _root, Title = "Silo Desktop Player — " + title };
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        SetWindowLongPtrW(Hwnd, -8, owner.Hwnd);
        if (_window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        }
        var styles = GetWindowLongPtrW(Hwnd, -20).ToInt64();
        SetWindowLongPtrW(Hwnd, -20, new IntPtr((styles | 0x80) & ~0x40000));
        // The presenter can retain a classic frame even with its title bar and
        // border disabled. Let the XAML outline be the popup's only border.
        SetWindowLongPtrW(Hwnd, -16, new IntPtr(GetWindowLongPtrW(Hwnd, -16).ToInt64() & ~0x00C40000L));
        SetWindowLongPtrW(Hwnd, -20, new IntPtr(GetWindowLongPtrW(Hwnd, -20).ToInt64() & ~0x00020301L));
        SetWindowPos(Hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0237); // FRAMECHANGED, no move/resize/activation/z-order change.
        var noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE; XAML supplies the outline.
        DwmSetWindowAttribute(Hwnd, 34, ref noBorder, sizeof(int));
        var rounded = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(Hwnd, 33, ref rounded, sizeof(int));
        try { _window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico")); } catch { /* Cosmetic only. */ }
        _root.Loaded += OnLoaded;
        _root.PreviewKeyDown += OnKeyDown;
        _root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        _root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        _root.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerReleased), true);
        _list.ItemClick += OnItemClick;
        _list.SizeChanged += OnListSizeChanged;
        _wheelHandler = OnListPointerWheel;
        _list.AddHandler(UIElement.PointerWheelChangedEvent, _wheelHandler, true);
        if (_footer != null) _footer.SizeChanged += OnFooterSizeChanged;
        _window.Activated += OnActivated;
        _window.Closed += OnClosed;
        _ownerWatch = _root.DispatcherQueue.CreateTimer();
        _ownerWatch.Interval = TimeSpan.FromMilliseconds(100);
        _ownerWatch.Tick += OnOwnerWatch;
    }

    internal IntPtr Hwnd { get; }

    private void BuildChoices()
    {
        foreach (var choice in _choices) _list.Items.Add(BuildChoice(choice));
    }

    private static FrameworkElement BuildDefaultContent(PlaybackMenuChoice choice)
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
        if (!choice.IsEnabled && choice.DisabledReason.Length > 0)
            copy.Children.Add(new TextBlock { Text = choice.DisabledReason, FontSize = 12,
                TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var text in choice.Badges.Where(s => s.Length > 0))
            badges.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 2, 5, 2),
                Background = ThemeBrush("SurfaceRaisedBrush", 0x28, 0x28, 0x2E),
                Child = new TextBlock { Text = text, FontSize = 11 },
            });
        copy.Children.Add(badges);
        return copy;
    }

    private ListViewItem BuildChoice(PlaybackMenuChoice choice)
    {
            var copy = BuildDefaultContent(choice);
            var row = new Grid { ColumnSpacing = 8, Opacity = choice.IsEnabled ? 1 : .55 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (_leadingContent?.Invoke(choice) is { } leading)
            {
                var content = new Grid { ColumnSpacing = 8 };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.Children.Add(leading); Grid.SetColumn(copy, 1); content.Children.Add(copy); row.Children.Add(content);
            }
            else row.Children.Add(copy);
            var check = new TextBlock { Text = choice.IsPending ? "Pending" : choice.IsCurrent ? "✓" : "", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(check, 1); row.Children.Add(check);
            var item = new ListViewItem { Content = row, Tag = choice.Id, IsEnabled = choice.IsEnabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(item, string.Join(", ", new[] { choice.Title, choice.Metadata, string.Join(", ", choice.Badges),
                choice.IsCurrent ? "Current selection" : "", choice.IsPending ? "Pending" : "", choice.DisabledReason }.Where(s => s.Length > 0)));
            ToolTipService.SetToolTip(item, AutomationProperties.GetName(item));
            item.Loaded += OnRowLoaded;
            return item;
    }

    internal void UpdateChoices(IReadOnlyList<PlaybackMenuChoice> choices)
    {
        if (_finished || !_isCurrent() || choices.Count == 0) return;
        static bool Same(PlaybackMenuChoice a, PlaybackMenuChoice b) =>
            a.Id == b.Id && a.Title == b.Title && a.Metadata == b.Metadata && a.Badges.SequenceEqual(b.Badges) &&
            a.IsCurrent == b.IsCurrent && a.IsEnabled == b.IsEnabled && a.DisabledReason == b.DisabledReason &&
            a.CloseOnInvoke == b.CloseOnInvoke && a.IsPending == b.IsPending;
        if (_choices.Count == choices.Count && _choices.Zip(choices).All(pair => Same(pair.First, pair.Second))) return;
        var sameIds = _choices.Count == choices.Count && _choices.Zip(choices).All(pair => pair.First.Id == pair.Second.Id);
        // A live refresh is not navigation. Leave scrollbar/touch offsets alone,
        // including layout queued by updated row sizes; explicit navigation
        // resumes selection-following in Input.
        if (sameIds) _keepSelectionVisible = false;
        var previous = _choices; _choices = choices;
        void UpdateContainer(ListViewItem row, PlaybackMenuChoice choice)
        {
            var replacement = BuildChoice(choice);
            var content = replacement.Content; replacement.Content = null;
            replacement.Loaded -= OnRowLoaded;
            row.Content = content; row.IsEnabled = replacement.IsEnabled;
            AutomationProperties.SetName(row, AutomationProperties.GetName(replacement));
            ToolTipService.SetToolTip(row, AutomationProperties.GetName(replacement));
            if (_shown) _applyTypography?.Invoke(row);
        }
        if (sameIds)
        {
            // Preserve containers, focus and wheel position during status/stat updates.
            for (var i = 0; i < choices.Count; i++)
            {
                if (Same(previous[i], choices[i])) continue;
                UpdateContainer((ListViewItem)_list.Items[i], choices[i]);
            }
        }
        else
        {
            var selected = (_list.SelectedItem as ListViewItem)?.Tag as string;
            foreach (ListViewItem row in _list.Items) row.Loaded -= OnRowLoaded;
            _list.Items.Clear(); BuildChoices();
            _list.SelectedIndex = Math.Max(0, choices.ToList().FindIndex(choice => choice.Id == selected));
            if (_shown) ScrollSelectionIntoView();
        }
        if (_shown) QueueLayout(placement: true);
    }

    internal async Task ShowAsync()
    {
        if (!_isCurrent() || _owner.Hwnd == IntPtr.Zero || !IsWindowVisible(_owner.Hwnd))
        {
            Close(PlaybackMenuCloseReason.OwnerRetired);
            return;
        }
        _footer?.Measure(new Windows.Foundation.Size(326, 400));
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
        QueueLayout(placement: true);
        var selected = _choices.ToList().FindIndex(c => c.IsCurrent && (c.IsEnabled || _allowDisabledTraversal));
        if (selected < 0) selected = _choices.ToList().FindIndex(c => c.IsEnabled || _allowDisabledTraversal);
        selected = Math.Max(0, selected);
        _list.SelectedIndex = selected;
        ScrollSelectionIntoView();
        if (_focusOnLoad) FocusSelection(FocusState.Programmatic);
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
        else { Position(); QueueLayout(); }
    }

    private void OnListSizeChanged(object sender, SizeChangedEventArgs args) => QueueLayout(placement: true);
    private void OnFooterSizeChanged(object sender, SizeChangedEventArgs args) => QueueLayout(placement: true);
    private void OnListPointerWheel(object sender, PointerRoutedEventArgs args) => _keepSelectionVisible = false;

    private void QueueLayout(bool placement = false)
    {
        if (_finished || !_shown) return;
        _placementPending |= placement;
        if (_layoutQueued) return;
        _layoutQueued = true;
        if (!_root.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _layoutQueued = false;
            if (_finished || !_isCurrent()) return;
            // The owner can retire between queueing and this callback, before
            // the owner watch ticks. Position() requires a live surface.
            if (_owner.Hwnd == IntPtr.Zero || !IsWindowVisible(_owner.Hwnd)) { Close(PlaybackMenuCloseReason.OwnerRetired); return; }
            var reposition = _placementPending; _placementPending = false;
            if (reposition) Position();
            if (_keepSelectionVisible && _footerIndex < 0) ScrollSelectionIntoView();
        })) _layoutQueued = false;
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
        var width = Math.Max(1, Math.Min(bounds.Right - bounds.Left, (int)Math.Ceiling(350 * scale)));
        // Measure the real wrapped rows, typography and visible footer. A fixed
        // allowance per choice made short menus mostly empty and let a collapsed
        // error footer give Quality the taller Subtitle menu cap. Measure at the
        // tallest size the popup may take, never unbounded: an infinite viewport
        // on the live list can discard the scroll offset a refresh must keep.
        var cap = Math.Min(_footer?.Visibility == Visibility.Visible ? 620d : 460d, Math.Max(1, bounds.Bottom - bounds.Top) / scale);
        _root.Measure(new Windows.Foundation.Size(width / scale, cap));
        var height = Math.Max(1, Math.Min(bounds.Bottom - bounds.Top,
            (int)Math.Ceiling(Math.Min(cap, _root.DesiredSize.Height) * scale)));
        var right = video.Left + (int)(_anchor.Right * (video.Right - video.Left));
        var top = video.Top + (int)(_anchor.Top * (video.Bottom - video.Top)) - height - (int)(8 * scale);
        var left = Math.Clamp(right - width, bounds.Left, Math.Max(bounds.Left, bounds.Right - width));
        top = Math.Clamp(top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height));
        var position = _window.AppWindow.Position; var size = _window.AppWindow.Size;
        if (position.X != left || position.Y != top || size.Width != width || size.Height != height)
            _window.AppWindow.MoveAndResize(new RectInt32(left, top, width, height));
    }

    private void OnItemClick(object sender, ItemClickEventArgs args)
    {
        // WinUI reports Content for an explicit item container, for pointer
        // clicks as well as UIA Invoke. Resolve only our exact owned row.
        var item = _list.Items.OfType<ListViewItem>()
            .FirstOrDefault(row => ReferenceEquals(row, args.ClickedItem) || ReferenceEquals(row.Content, args.ClickedItem));
        if (item?.Tag is string id) Select(id, _pointerSelection);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args) => _pointerSelection = true;

    private void OnPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        // ItemClick runs in the release dispatch. Clear after that dispatch so
        // later keyboard/UIA activation cannot inherit an earlier pointer.
        _root.DispatcherQueue.TryEnqueue(() => _pointerSelection = false);
    }

    private void Select(string id, bool pointer = false)
    {
        if (_finished || !_isCurrent()) { Close(PlaybackMenuCloseReason.OwnerRetired); return; }
        var choice = _choices.FirstOrDefault(choice => choice.Id == id);
        if (choice == null || !choice.IsEnabled) return;
        _keepSelectionVisible = true;
        if (choice.CloseOnInvoke) Close(pointer ? PlaybackMenuCloseReason.PointerSelection : PlaybackMenuCloseReason.Selection);
        _selected(id);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is not (VirtualKey.Escape or VirtualKey.GamepadB) && IsInFooter(args.OriginalSource as DependencyObject)) return;
        if (HandleKeyboardInput(args.Key)) args.Handled = true;
    }

    private bool HandleKeyboardInput(VirtualKey key)
    {
        var shift = key == VirtualKey.Tab && (GetKeyState((int)VirtualKey.Shift) & 0x8000) != 0;
        var action = KeyboardAction(key, shift);
        if (action == null) return false;
        Input(action);
        return true;
    }

    private static string? KeyboardAction(VirtualKey key, bool shift) =>
        key switch
        {
            VirtualKey.Escape or VirtualKey.GamepadB => "cancel",
            VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA => "activate",
            VirtualKey.Home => "home", VirtualKey.End => "end",
            VirtualKey.Up or VirtualKey.Left => "up",
            VirtualKey.Down or VirtualKey.Right => "down",
            VirtualKey.Tab => shift ? "up" : "down",
            _ => (int)key switch
            {
                0xCB or 0xCD or 0xD3 or 0xD6 => "up",
                0xCC or 0xCE or 0xD4 or 0xD5 => "down", _ => null,
            },
        };

    internal void Input(string action)
    {
        _pointerSelection = false;
        if (_finished || !_isCurrent()) { Close(PlaybackMenuCloseReason.OwnerRetired); return; }
        if (action == "cancel") { Close(PlaybackMenuCloseReason.Cancel); return; }
        if (action is "home" or "end" or "up" or "left" or "down" or "right") _keepSelectionVisible = true;
        if (action == "activate")
        {
            if (_footerIndex >= 0)
            {
                var controls = FooterButtons();
                if (_footerIndex < controls.Count && controls[_footerIndex].Tag is Action callback) callback();
            }
            else if (_list.SelectedItem is ListViewItem { Tag: string id }) Select(id);
            return;
        }
        var current = Math.Max(0, _list.SelectedIndex);
        var buttons = FooterButtons();
        if (_footerIndex >= 0 && action is "up" or "left" or "down" or "right")
        {
            _footerIndex += action is "up" or "left" ? -1 : 1;
            _footerIndex = Math.Clamp(_footerIndex, -1, buttons.Count - 1);
            if (_footerIndex >= 0) { if (GetForegroundWindow() == Hwnd) buttons[_footerIndex].Focus(FocusState.Keyboard); return; }
        }
        else if (action is "down" or "right" && current == _list.Items.Count - 1 && buttons.Count > 0)
        {
            _footerIndex = 0; if (GetForegroundWindow() == Hwnd) buttons[0].Focus(FocusState.Keyboard); return;
        }
        _footerIndex = -1;
        var next = action switch
        {
            "home" => 0, "end" => _list.Items.Count - 1,
            "up" or "left" => Math.Max(0, current - 1),
            "down" or "right" => Math.Min(_list.Items.Count - 1, current + 1), _ => current,
        };
        var step = action is "end" or "up" or "left" ? -1 : 1;
        while (!_allowDisabledTraversal && next >= 0 && next < _choices.Count && !_choices[next].IsEnabled) next += step;
        if (next >= 0 && next < _choices.Count) _list.SelectedIndex = next;
        ScrollSelectionIntoView();
        FocusSelection(FocusState.Keyboard);
    }

    private void FocusSelection(FocusState state)
    {
        if (GetForegroundWindow() != Hwnd) return;
        if (_list.SelectedItem is ListViewItem { IsEnabled: true } item) item.Focus(state);
        // A read-only inventory still needs a keyboard receiver while its
        // selected row cannot receive focus or be activated.
        else if (_allowDisabledTraversal) _list.Focus(state);
    }

    private bool IsInFooter(DependencyObject? source)
    {
        while (source != null) { if (ReferenceEquals(source, _footer)) return true; source = VisualTreeHelper.GetParent(source); }
        return false;
    }

    private List<Button> FooterButtons()
    {
        var buttons = new List<Button>();
        void Visit(DependencyObject element)
        {
            if (element is Button { IsEnabled: true, Visibility: Visibility.Visible } button && button.Tag is Action) buttons.Add(button);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Visit(VisualTreeHelper.GetChild(element, i));
        }
        if (_footer != null) Visit(_footer);
        return buttons;
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
        _list.SizeChanged -= OnListSizeChanged;
        _list.RemoveHandler(UIElement.PointerWheelChangedEvent, _wheelHandler);
        if (_footer != null) _footer.SizeChanged -= OnFooterSizeChanged;
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
    [DllImport("user32.dll")] private static extern short GetKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
