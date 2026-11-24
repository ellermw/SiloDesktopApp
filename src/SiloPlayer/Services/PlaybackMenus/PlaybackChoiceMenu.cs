using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;

namespace SiloPlayer.Services.PlaybackMenus;

internal sealed record PlaybackMenuChoice(string Id, string Title, string Metadata,
    IReadOnlyList<string> Badges, bool IsCurrent = false, bool IsEnabled = true,
    string DisabledReason = "", bool CloseOnInvoke = true, bool IsPending = false)
{
    // Presentation only (player-hud.css panels). Ids, selection and enablement stay above.
    /// <summary>Upper-case group label drawn above this row ("Source file").</summary>
    internal string Section { get; init; } = "";
    /// <summary>Hairline drawn above this row, before its section label.</summary>
    internal bool Divider { get; init; }
    /// <summary>Secondary text at the end of the title line ("8 Mbps", "SRT · Embedded").</summary>
    internal string Trailing { get; init; } = "";
    /// <summary>Leading icon for an action row; such rows have no check column.</summary>
    internal string Icon { get; init; } = "";
    /// <summary>A switch at the end of the row (Fill screen, Playback info).</summary>
    internal bool? Switch { get; init; }
    /// <summary>Reserve the leading check column, so chosen and other rows line up.</summary>
    internal bool Check { get; init; } = true;
    /// <summary>Accent-coloured word after the secondary line ("Now").</summary>
    internal string Accent { get; init; } = "";
    /// <summary>One line only; long titles are cut with an ellipsis (chapters).</summary>
    internal bool SingleLine { get; init; }
    /// <summary>Detail the row reads out but does not draw (an audio track's bitrate).</summary>
    internal string Spoken { get; init; } = "";
    /// <summary>The row's least height; 2.5rem for a choice, 4rem for a chapter.</summary>
    internal double MinHeight { get; init; } = 40;
    /// <summary>A short grey explanation rather than a choice (why the version is locked).</summary>
    internal bool Note { get; init; }
    /// <summary>The row is a labelled segmented group (playback speed) rather than one choice.</summary>
    internal IReadOnlyList<PlaybackMenuSegment> Segments { get; init; } = [];
}

internal sealed record PlaybackMenuSegment(string Id, string Label, string Name, bool IsCurrent, bool IsEnabled);

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
    private ListViewItem? _hovered;
    private bool _keyboardCursor;
    private readonly bool _segmented;
    private readonly TextBlock? _headingValue;
    private readonly TextBlock _heading;
    private Func<string>? _localizedTitle;
    private Func<IReadOnlyList<PlaybackMenuChoice>?>? _localizedChoices;
    private IDisposable? _localization;
    private long _textGeneration = AppStrings.Current.Generation;

    internal PlaybackChoiceMenu(MpvVideoWindow owner, PlaybackMenuAnchor anchor, string title,
        IReadOnlyList<PlaybackMenuChoice> choices, Func<bool> isCurrent,
        Action<string> selected, Action<PlaybackMenuCloseReason> closed, Action<DependencyObject>? applyTypography = null, FrameworkElement? footer = null,
        Func<PlaybackMenuChoice, FrameworkElement?>? leadingContent = null, bool allowDisabledTraversal = false, bool segmented = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count == 0) throw new ArgumentException("A menu requires a choice.", nameof(choices));
        _owner = owner; _anchor = anchor; _choices = choices;
        _isCurrent = isCurrent; _selected = selected; _closed = closed;
        _applyTypography = applyTypography; _footer = footer; _leadingContent = leadingContent;
        _allowDisabledTraversal = allowDisabledTraversal;
        _segmented = segmented;
        _dismiss = () => Close(PlaybackMenuCloseReason.OutsidePointer);
        _list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection(),
        };
        if (_allowDisabledTraversal) _list.IsTabStop = true;
        // The rows draw their own hover, chosen and keyboard states (.hud-item),
        // so the list's selection bar and fills stay out of the picture.
        _list.Resources["ListViewItemSelectionIndicatorVisualEnabled"] = false;
        _list.Resources["ListViewItemDisabledThemeOpacity"] = 1d; // rows dim their own text
        foreach (var key in new[] { "ListViewItemBackgroundPointerOver", "ListViewItemBackgroundPressed", "ListViewItemBackgroundSelected",
            "ListViewItemBackgroundSelectedPointerOver", "ListViewItemBackgroundSelectedPressed", "ListViewItemBackgroundSelectedDisabled" })
            _list.Resources[key] = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        if (_segmented)
        {
            _list.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal' MaximumRowsOrColumns='10'/></ItemsPanelTemplate>");
            _list.Padding = new Thickness(16, 0, 16, 8);
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        }
        AutomationProperties.SetName(_list, title + " choices");
        BuildChoices();
        // .hud-panel: near-black glass, hairline edge, 1rem corners; .hud-panel-header.
        var heading = _heading = new TextBlock { Text = title, FontSize = _segmented ? 14 : 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(16, 10, 16, 6), Foreground = PlaybackMenuStyle.Paper, TextTrimming = TextTrimming.CharacterEllipsis };
        _root = new Grid
        {
            RequestedTheme = ElementTheme.Dark, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 16, 19)),
            BorderBrush = PlaybackMenuStyle.Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(PlaybackMenuStyle.PanelRadius),
            Padding = new Thickness(0, 6, 0, 6),
        };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (_segmented)
        {
            var header = new Grid(); header.Children.Add(heading);
            _headingValue = new TextBlock { Text = choices.FirstOrDefault(c => c.IsCurrent)?.Title ?? "", FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 10, 16, 6), Foreground = PlaybackMenuStyle.Paper };
            header.Children.Add(_headingValue); _root.Children.Add(header);
        }
        else _root.Children.Add(heading);
        Grid.SetRow(_list, 1); _root.Children.Add(_list);
        if (_footer != null)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_footer, 2); _root.Children.Add(_footer);
        }
        // Menu text follows the interface language; the popup's placement and
        // the video surface it belongs to keep their left-to-right geometry.
        AppLocalization.ApplyDirection(_root);
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
        _list.SelectionChanged += OnListSelectionChanged;
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

    // Action menus have no current choice. Keep their opening presentation
    // neutral while retaining selection for subsequent keyboard navigation.
    internal bool SelectOnOpen { get; init; } = true;

    /// <summary>The panel's width in the stylesheet's pixels (22rem unless the menu sets its own).</summary>
    internal double PanelWidth { get; init; } = 352;

    private void BuildChoices()
    {
        foreach (var choice in _choices) _list.Items.Add(BuildChoice(choice));
    }

    // ── Rows (.hud-item) ───────────────────────────────────────────────────
    // [check or icon] [thumbnail/number] title + secondary lines [value, tags, switch]
    private static FrameworkElement BuildDefaultContent(PlaybackMenuChoice choice, FrameworkElement? leading = null)
    {
        if (choice.Note)
            return new TextBlock { Text = choice.Title, FontSize = 12, Foreground = PlaybackMenuStyle.Ash, TextWrapping = TextWrapping.Wrap,
                MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 19, Margin = new Thickness(6, 0, 6, 0) };
        var row = new Grid { ColumnSpacing = 12, Opacity = choice.IsEnabled ? 1 : .45 };
        var column = 0;
        void Place(FrameworkElement element, bool fill = false)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = fill ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetColumn(element, column++); row.Children.Add(element);
        }
        if (choice.Icon.Length > 0)
            Place(new Border { Width = 20, VerticalAlignment = VerticalAlignment.Center,
                Child = SiloPlayer.Controls.WebUiIcon.Create(choice.Icon, 20, PlaybackMenuStyle.Ash, 1.75) });
        else if (choice.Check)
            Place(new Border { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center,
                Child = choice.IsCurrent ? SiloPlayer.Controls.WebUiIcon.Create("check", 18, PlaybackMenuStyle.Lamp(), 2.5) : null });
        if (leading != null) Place(leading);

        var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = choice.Title, FontSize = 15, Foreground = PlaybackMenuStyle.Paper,
            FontWeight = choice.IsCurrent ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            TextWrapping = choice.SingleLine ? TextWrapping.NoWrap : TextWrapping.Wrap,
            MaxLines = choice.SingleLine ? 1 : 2, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (choice.Trailing.Length > 0)
        {
            // The value sits at the end of the title line and never gives way to a long title.
            var line = new Grid { ColumnSpacing = 12 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var value = PlaybackMenuStyle.Secondary(choice.Trailing); value.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(value, 1); line.Children.Add(title); line.Children.Add(value);
            copy.Children.Add(line);
        }
        else copy.Children.Add(title);
        var tags = choice.Badges.Where(text => text.Length > 0).ToArray();
        var secondary = choice.Metadata.Length > 0 || choice.Accent.Length > 0;
        if (secondary)
        {
            var text = PlaybackMenuStyle.Secondary("", wrap: tags.Length == 0 && !choice.SingleLine);
            if (choice.Metadata.Length > 0) text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = choice.Metadata });
            if (choice.Accent.Length > 0)
            {
                if (choice.Metadata.Length > 0) text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " · " });
                text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = choice.Accent, Foreground = PlaybackMenuStyle.Lamp() });
            }
            if (tags.Length == 0) copy.Children.Add(text);
            else
            {
                // Tags follow the secondary text on its line ("E-AC-3 · 5.1 · English  Default").
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                text.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(text);
                foreach (var tag in tags) line.Children.Add(PlaybackMenuStyle.Chip(tag));
                copy.Children.Add(line);
            }
        }
        if (!choice.IsEnabled && choice.DisabledReason.Length > 0)
            copy.Children.Add(PlaybackMenuStyle.Secondary(choice.DisabledReason, wrap: true));
        Place(copy, fill: true);

        if (!secondary && tags.Length > 0)
        {
            var end = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            foreach (var tag in tags) end.Children.Add(PlaybackMenuStyle.Chip(tag));
            Place(end);
        }
        if (choice.IsPending)
        {
            var pending = PlaybackMenuStyle.Secondary(PendingText); pending.VerticalAlignment = VerticalAlignment.Center;
            Place(pending);
        }
        if (choice.Switch is { } on) Place(PlaybackMenuStyle.Switch(on));
        return row;
    }

    // A labelled segmented group inside the list (the settings panel's Speed).
    private FrameworkElement BuildSegmentedGroup(PlaybackMenuChoice choice)
    {
        var group = new StackPanel { Spacing = 8, Opacity = choice.IsEnabled ? 1 : .45 };
        var head = new Grid();
        head.Children.Add(PlaybackMenuStyle.Label(choice.Title));
        var value = PlaybackMenuStyle.Label(choice.IsPending ? PendingText : choice.Metadata);
        value.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; value.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(value); group.Children.Add(head);
        group.Children.Add(PlaybackMenuStyle.Segmented(choice.Segments, id =>
        {
            if (!_finished && _isCurrent()) _selected(id);
        }));
        return group;
    }

    private static Border? Surface(ListViewItem item) => item.Content switch
    {
        Panel host when host.Children.Count > 0 => host.Children[^1] as Border,
        Border segment => segment,
        _ => null,
    };

    // Hover belongs to the pointer and the outlined cursor to the keyboard;
    // a chosen row is marked by its check and weight, never by a fill.
    private void RefreshRowStates()
    {
        if (_finished) return;
        foreach (var item in _list.Items.OfType<ListViewItem>())
        {
            if (Surface(item) is not { } surface) continue;
            var group = _choices.FirstOrDefault(choice => choice.Id == item.Tag as string)?.Segments.Count > 0;
            var cursor = _keyboardCursor && ReferenceEquals(_list.SelectedItem, item);
            var hover = !_keyboardCursor && !group && !_segmented && item.IsEnabled && ReferenceEquals(_hovered, item);
            if (!_segmented) surface.Background = hover || (cursor && !group) ? PlaybackMenuStyle.Hover : PlaybackMenuStyle.Clear;
            surface.BorderBrush = cursor ? PlaybackMenuStyle.Lamp() : PlaybackMenuStyle.Clear;
        }
    }

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        _hovered = sender as ListViewItem; _keyboardCursor = false; RefreshRowStates();
    }

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(_hovered, sender)) _hovered = null;
        RefreshRowStates();
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs args) => RefreshRowStates();

    private ListViewItem BuildChoice(PlaybackMenuChoice choice)
    {
        var name = string.Join(", ", new[] { choice.Title, choice.Metadata, choice.Spoken, choice.Trailing, string.Join(", ", choice.Badges), choice.Accent,
            choice.Switch is { } state ? (state ? "On" : "Off") : "",
            choice.IsCurrent ? SelectedText : "", choice.IsPending ? PendingText : "", choice.DisabledReason }.Where(s => s.Length > 0));
        ListViewItem item;
        if (_segmented)
        {
            // The standalone Speed surface: one .hud-segment per list item, so the
            // native containers keep keyboard and automation ownership.
            item = new ListViewItem { Tag = choice.Id, IsEnabled = choice.IsEnabled,
                MinWidth = 0, Width = 35, MinHeight = 0, Height = 40, Padding = new Thickness(1, 0, 1, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch, UseSystemFocusVisuals = false,
                Content = new Border { CornerRadius = new CornerRadius(7), Height = 36, BorderThickness = new Thickness(2), BorderBrush = PlaybackMenuStyle.Clear,
                    Background = choice.IsCurrent ? PlaybackMenuStyle.Paper : PlaybackMenuStyle.Rgb(18, 255, 255, 255),
                    Child = new TextBlock { Text = choice.Title.TrimEnd('×'), FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = choice.IsCurrent ? PlaybackMenuStyle.Ink : PlaybackMenuStyle.Ash,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold } } };
        }
        else
        {
            var group = choice.Segments.Count > 0;
            var content = group ? BuildSegmentedGroup(choice) : BuildDefaultContent(choice, _leadingContent?.Invoke(choice));
            // The 2px edge is the keyboard outline; it is always there so nothing moves when it lights.
            var surface = new Border
            {
                Margin = new Thickness(6, 0, 6, 0), Padding = group ? new Thickness(8, 4, 8, 6) : new Thickness(8, 6, 8, 6), MinHeight = choice.MinHeight,
                CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2),
                BorderBrush = PlaybackMenuStyle.Clear, Background = PlaybackMenuStyle.Clear, Child = content,
            };
            var host = new StackPanel();
            if (choice.Divider) host.Children.Add(PlaybackMenuStyle.Divider());
            if (choice.Section.Length > 0) host.Children.Add(PlaybackMenuStyle.Section(choice.Section));
            host.Children.Add(surface);
            item = new ListViewItem { Content = host, Tag = choice.Id, IsEnabled = choice.IsEnabled, Padding = new Thickness(0), MinHeight = 0,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, UseSystemFocusVisuals = false };
        }
        AutomationProperties.SetName(item, name);
        ToolTipService.SetToolTip(item, _segmented ? name : null);
        item.Loaded += OnRowLoaded;
        item.PointerEntered += OnRowPointerEntered; item.PointerExited += OnRowPointerExited;
        return item;
    }

    // Spoken state words. "Pending" is native-only (no catalog equivalent).
    private static string SelectedText => AppStrings.Get("web.player.quality.selected", fallback: "Selected");
    private const string PendingText = "Pending";

    /// <summary>
    /// Re-renders this open menu's text when the interface language changes:
    /// heading, rows, spoken names and direction update in place. Selection,
    /// scroll, focus, the request lease and the playback owner are untouched;
    /// a retired or closed menu ignores the change.
    /// </summary>
    internal void Localize(Func<string> title, Func<IReadOnlyList<PlaybackMenuChoice>?>? choices = null)
    {
        if (_finished) return;
        _localizedTitle = title; _localizedChoices = choices;
        _localization?.Dispose();
        _localization = AppLocalization.Register(_root, Relocalize);
    }

    private void Relocalize()
    {
        if (_finished || !_isCurrent()) return;
        var generation = AppStrings.Current.Generation;
        if (generation == _textGeneration) return;
        _textGeneration = generation;
        AppLocalization.ApplyDirection(_root);
        if (_localizedTitle is { } title)
        {
            var text = title();
            _heading.Text = text;
            _window.Title = "Silo Desktop Player — " + text;
            AutomationProperties.SetName(_list, text + " choices");
        }
        if (_localizedChoices?.Invoke() is { Count: > 0 } choices) ApplyChoices(choices, relocalized: true);
        else if (_shown) QueueLayout(placement: true);
    }

    internal void UpdateChoices(IReadOnlyList<PlaybackMenuChoice> choices) => ApplyChoices(choices, relocalized: false);

    // One reflected/internal entry point stays UpdateChoices; this carries the language-change mode.
    private void ApplyChoices(IReadOnlyList<PlaybackMenuChoice> choices, bool relocalized)
    {
        if (_finished || !_isCurrent() || choices.Count == 0) return;
        // A language change refreshes every row: spoken state words are not part of the choice data.
        bool Same(PlaybackMenuChoice a, PlaybackMenuChoice b) => !relocalized &&
            a.Id == b.Id && a.Title == b.Title && a.Metadata == b.Metadata && a.Badges.SequenceEqual(b.Badges) &&
            a.IsCurrent == b.IsCurrent && a.IsEnabled == b.IsEnabled && a.DisabledReason == b.DisabledReason &&
            a.CloseOnInvoke == b.CloseOnInvoke && a.IsPending == b.IsPending &&
            a.Section == b.Section && a.Divider == b.Divider && a.Trailing == b.Trailing && a.Icon == b.Icon &&
            a.Switch == b.Switch && a.Check == b.Check && a.Accent == b.Accent && a.SingleLine == b.SingleLine && a.Note == b.Note && a.MinHeight == b.MinHeight && a.Spoken == b.Spoken &&
            a.Segments.SequenceEqual(b.Segments);
        if (_choices.Count == choices.Count && _choices.Zip(choices).All(pair => Same(pair.First, pair.Second))) return;
        var sameIds = _choices.Count == choices.Count && _choices.Zip(choices).All(pair => pair.First.Id == pair.Second.Id);
        // A live refresh is not navigation. Leave scrollbar/touch offsets alone,
        // including layout queued by updated row sizes; explicit navigation
        // resumes selection-following in Input.
        if (sameIds) _keepSelectionVisible = false;
        var previous = _choices; _choices = choices;
        if (_headingValue != null) _headingValue.Text = choices.FirstOrDefault(c => c.IsCurrent)?.Title ?? "";
        void UpdateContainer(ListViewItem row, PlaybackMenuChoice choice)
        {
            var replacement = BuildChoice(choice);
            var content = replacement.Content; replacement.Content = null;
            replacement.Loaded -= OnRowLoaded;
            replacement.PointerEntered -= OnRowPointerEntered; replacement.PointerExited -= OnRowPointerExited;
            row.Content = content; row.IsEnabled = replacement.IsEnabled;
            AutomationProperties.SetName(row, AutomationProperties.GetName(replacement));
            ToolTipService.SetToolTip(row, ToolTipService.GetToolTip(replacement));
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
            foreach (ListViewItem row in _list.Items) DetachRow(row);
            _hovered = null;
            _list.Items.Clear(); BuildChoices();
            _list.SelectedIndex = selected == null && !SelectOnOpen ? -1
                : Math.Max(0, choices.ToList().FindIndex(choice => choice.Id == selected));
            if (_shown) ScrollSelectionIntoView();
        }
        RefreshRowStates();
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
        _list.SelectedIndex = SelectOnOpen ? selected : -1;
        if (!SelectOnOpen) _list.IsTabStop = true;
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
        var width = Math.Max(1, Math.Min(bounds.Right - bounds.Left, (int)Math.Ceiling(PanelWidth * scale)));
        // Measure the real wrapped rows, typography and visible footer. A fixed
        // allowance per choice made short menus mostly empty and let a collapsed
        // error footer give Quality the taller Subtitle menu cap. Measure at the
        // tallest size the popup may take, never unbounded: an infinite viewport
        // on the live list can discard the scroll offset a refresh must keep.
        // .hud-popover: at most 32rem tall, and it leaves 10rem of the player free.
        // A panel with controls under its list (subtitles) keeps room for them.
        var surface = Math.Max(1, bounds.Bottom - bounds.Top) / scale;
        var cap = Math.Min(surface, Math.Max(_footer?.Visibility == Visibility.Visible ? 300d : 160d, Math.Min(512d, surface - 160d)));
        _root.Measure(new Windows.Foundation.Size(width / scale, cap));
        var height = Math.Max(1, Math.Min(bounds.Bottom - bounds.Top,
            (int)Math.Ceiling(Math.Min(cap, _root.DesiredSize.Height) * scale)));
        var right = video.Left + (int)(_anchor.Right * (video.Right - video.Left));
        // The panel rises 0.75rem above its rail button, its end edge on the button's.
        var top = video.Top + (int)(_anchor.Top * (video.Bottom - video.Top)) - height - (int)(12 * scale);
        var left = Math.Clamp(right - width, bounds.Left, Math.Max(bounds.Left, bounds.Right - width));
        top = Math.Clamp(top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height));
        var position = _window.AppWindow.Position; var size = _window.AppWindow.Size;
        if (position.X != left || position.Y != top || size.Width != width || size.Height != height)
        {
            _window.AppWindow.MoveAndResize(new RectInt32(left, top, width, height));
            // The system rounds a window by 8px at most. Cut the window to the
            // panel's own 1rem corners so nothing square shows outside its outline.
            var diameter = (int)Math.Round(2 * PlaybackMenuStyle.PanelRadius * scale);
            var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
            if (region != IntPtr.Zero && SetWindowRgn(Hwnd, region, true) == 0) DeleteObject(region);
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs args)
    {
        // WinUI reports Content for an explicit item container, for pointer
        // clicks as well as UIA Invoke. Resolve only our exact owned row.
        var item = _list.Items.OfType<ListViewItem>()
            .FirstOrDefault(row => ReferenceEquals(row, args.ClickedItem) || ReferenceEquals(row.Content, args.ClickedItem));
        if (item?.Tag is not string id) return;
        // A segmented group's own buttons take the pointer; its label is not a target.
        if (_pointerSelection && _choices.FirstOrDefault(choice => choice.Id == id)?.Segments.Count > 0) return;
        Select(id, _pointerSelection);
    }

    private void DetachRow(ListViewItem row)
    {
        row.Loaded -= OnRowLoaded;
        row.PointerEntered -= OnRowPointerEntered; row.PointerExited -= OnRowPointerExited;
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
        if (action is "home" or "end" or "up" or "left" or "down" or "right")
        {
            _keepSelectionVisible = true;
            // From here the keyboard drives the menu: show where it is.
            if (!_keyboardCursor) { _keyboardCursor = true; RefreshRowStates(); }
        }
        if (action == "activate")
        {
            if (_footerIndex >= 0)
            {
                var controls = FooterButtons();
                if (_footerIndex < controls.Count && controls[_footerIndex].Tag is Action callback) callback();
            }
            else if (_list.SelectedItem is ListViewItem { Tag: string id }) Select(id);
            else if (!SelectOnOpen && _loaded.Task.IsCompletedSuccessfully &&
                _choices.FirstOrDefault(choice => choice.IsEnabled) is { } first) Select(first.Id);
            return;
        }
        var current = _list.SelectedIndex;
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
            "up" or "left" => current < 0 ? _list.Items.Count - 1 : Math.Max(0, current - 1),
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
        else if (_allowDisabledTraversal || _list.SelectedIndex < 0) _list.Focus(state);
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
            if (element is UIElement { Visibility: Visibility.Collapsed }) return; // a hidden tool row hides its button
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
        _localization?.Dispose(); _localization = null;
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
        _list.SelectionChanged -= OnListSelectionChanged;
        _list.SizeChanged -= OnListSizeChanged;
        _list.RemoveHandler(UIElement.PointerWheelChangedEvent, _wheelHandler);
        if (_footer != null) _footer.SizeChanged -= OnFooterSizeChanged;
        foreach (ListViewItem row in _list.Items) DetachRow(row);
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
        if (_list.SelectedItem == null) return;
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

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
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
