using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.System;
using static SiloPlayer.Views.Admin.AdminLibrariesUi;
using P = SiloPlayer.Core.Services.Admin.AdminLibraryPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Add/Edit library (WebUI <c>LibraryEditorDialog</c> + <c>LibraryFormSections</c>): a modal panel
/// shown immediately (no slide), 768 × 640 at most and full-screen below 640 px, with section
/// tabs as a side rail from 640 px and a scrolling top strip below. Escape and Cancel close;
/// focus is trapped inside and returns to the opener. A failed save switches to the section
/// with the error. Inputs stay mounted while the draft re-renders so typing keeps focus.
/// </summary>
internal sealed class AdminLibraryEditorView : Grid
{
    private const double MaxWidthDialog = 768;
    private const double MaxHeightDialog = 640;

    private readonly AdminLibrariesPage _page;
    private readonly AdminLibrariesController _controller;
    private readonly Border _panel = new() { CornerRadius = new(16), BorderThickness = new(1) };
    private readonly Grid _layout = new() { TabFocusNavigation = KeyboardNavigationMode.Cycle };
    private readonly Border _typeTile = new() { Width = 40, Height = 40, CornerRadius = new(12), BorderThickness = new(1) };
    private readonly TextBlock _title = Text("", 18);
    private readonly TextBlock _description = Text("", 12, true);
    private readonly Button _close;
    private readonly Grid _body = new();
    private readonly StackPanel _tabs = new() { Spacing = 4 };
    private readonly ScrollViewer _tabScroll = new();
    private readonly ScrollViewer _contentScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _sectionHost = new() { Spacing = 20, Padding = new(24, 20, 24, 20) };
    private readonly TextBlock _sectionTitle = Text("", 14);
    private readonly TextBlock _sectionDescription = Text("", 12, true);
    private readonly ContentControl _sectionContent = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Grid _footer = new() { ColumnSpacing = 8, RowSpacing = 8 };
    private readonly TextBlock _footerError = Text("", 12);
    private readonly Button _cancel;
    private readonly Button _save;
    private readonly TextBox _name;
    private readonly List<PathField> _paths = [];
    private Control? _opener;
    private AdminLibraryEditorSection _section = AdminLibraryEditorSection.General;
    private AdminLibraryEditorSection? _broughtIntoView;
    private string? _renderedSectionKey;
    private bool _syncing;
    private bool _closed;
    private CancellationTokenRegistration _retirement;

    internal AdminLibraryEditorSession Session { get; }
    internal AdminLibraryTranslationSession? Prewarm { get; private set; }
    internal event Action? CloseRequested;

    internal AdminLibraryEditorView(AdminLibrariesPage page, AdminLibrariesController controller, AdminLibraryEditorSession session)
    {
        _page = page; _controller = controller; Session = session;
        AutomationProperties.SetAutomationId(this, "LibraryEditor");
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 0, 0, 0));
        var dismiss = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        Children.Add(dismiss);
        _panel.Background = Brush("AppBackgroundBrush");
        _panel.BorderBrush = Brush("BorderBrush");
        _panel.HorizontalAlignment = HorizontalAlignment.Center;
        _panel.VerticalAlignment = VerticalAlignment.Center;
        _panel.Child = _layout;
        Children.Add(_panel);
        AutomationProperties.SetName(_panel, T(session.State.IsEditing ? "web.admin.common.libraryEditor.dialog.editTitle" : "web.admin.common.libraryEditor.dialog.addTitle"));

        _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });

        // Header.
        var header = new Grid { ColumnSpacing = 12, Padding = new(24, 16, 16, 16), BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1) };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _typeTile.BorderBrush = Brush("BorderBrush"); _typeTile.Background = Tint("SurfaceBrush", 0.9);
        header.Children.Add(_typeTile);
        var titles = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _title.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(_title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        titles.Children.Add(_title);
        _description.TextTrimming = TextTrimming.CharacterEllipsis;
        titles.Children.Add(_description);
        Grid.SetColumn(titles, 1); header.Children.Add(titles);
        _close = IconButton("x", Core.Localization.AppStrings.Get("web.ui.close", fallback: "Close"), "LibraryEditorClose", 32, 16, Brush("SecondaryTextBrush"));
        _close.VerticalAlignment = VerticalAlignment.Top;
        _close.Click += (_, _) => CloseRequested?.Invoke();
        Grid.SetColumn(_close, 2); header.Children.Add(_close);
        _layout.Children.Add(header);

        // Body: tabs + scrolling section.
        _tabScroll.Content = _tabs;
        _sectionTitle.FontWeight = FontWeights.SemiBold;
        var sectionHead = new StackPanel { Spacing = 4 };
        sectionHead.Children.Add(_sectionTitle);
        sectionHead.Children.Add(_sectionDescription);
        _sectionHost.Children.Add(sectionHead);
        _sectionHost.Children.Add(_sectionContent);
        _contentScroll.Content = _sectionHost;
        _body.Children.Add(_tabScroll);
        _body.Children.Add(_contentScroll);
        Grid.SetRow(_body, 1); _layout.Children.Add(_body);

        // Footer.
        _footer.Padding = new(24, 16, 24, 16);
        _footer.BorderBrush = Brush("BorderBrush"); _footer.BorderThickness = new(0, 1, 0, 0);
        _footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); _footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _footerError.Foreground = Solid(Destructive); _footerError.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(_footerError, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
        AutomationProperties.SetAutomationId(_footerError, "LibraryEditorError");
        _footer.Children.Add(_footerError);
        _cancel = Action(Core.Localization.AppStrings.Get("common.cancel", fallback: "Cancel"), "LibraryEditorCancel", style: "GhostButtonStyle", height: 36);
        _cancel.Click += (_, _) => CloseRequested?.Invoke();
        Grid.SetColumn(_cancel, 1); _footer.Children.Add(_cancel);
        _save = Action("", "LibraryEditorSave", style: "AccentButtonStyle", height: 36);
        _save.Click += (_, _) => _ = SubmitAsync();
        Grid.SetColumn(_save, 2); _footer.Children.Add(_save);
        Grid.SetRow(_footer, 2); _layout.Children.Add(_footer);

        _name = new TextBox { Height = 36, FontSize = 14, CornerRadius = new(8), PlaceholderText = T("web.admin.common.libraryEditor.general.namePlaceholder") };
        AutomationProperties.SetName(_name, T("web.admin.common.libraryEditor.general.name"));
        AutomationProperties.SetAutomationId(_name, "LibraryEditorName");
        _name.Text = session.State.Name;
        _name.TextChanged += (_, _) => { if (!_syncing) Session.SetName(_name.Text); };
        _name.KeyDown += (_, args) => { if (args.Key == VirtualKey.Enter) { args.Handled = true; _ = SubmitAsync(); } };

        KeyDown += (_, args) => { if (args.Key == VirtualKey.Escape) { args.Handled = true; CloseRequested?.Invoke(); } };
        session.Changed += OnSessionChanged;
        // The editor dies with its owner: an authority change closes it at once (the draft is never carried over).
        _retirement = session.Lease.Token.Register(() => Ui(() => { if (!_closed) CloseRequested?.Invoke(); }));
        if (session.State.Library is { } library) StartTranslation(library.Id);
        ApplyViewport();
        Render();
    }

    private void StartTranslation(string libraryId)
    {
        if (Prewarm != null) return;
        Prewarm = new AdminLibraryTranslationSession(_controller, Session.Lease, libraryId, _controller.WatchedTranslationJobs);
        Prewarm.Changed += OnSessionChanged;
        _ = Prewarm.OpenAsync();
    }

    private void OnSessionChanged() => Ui(() => { if (!_closed) Render(); });

    /// <summary>Dispatcher callbacks must not escape into WinUI's native message loop; a failure is logged without data.</summary>
    private void Ui(Action action, Microsoft.UI.Dispatching.DispatcherQueuePriority priority = Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal) =>
        DispatcherQueue?.TryEnqueue(priority, () =>
        {
            try { action(); }
            catch (Exception ex)
            {
                Core.Services.LocalLog.AppendLine("admin_monitor.txt", $"library editor render failed: {ex.GetType().Name} (0x{ex.HResult:X8}) in {ex.TargetSite?.Name}");
            }
        });

    internal void Show(Control? opener)
    {
        _opener = opener;
        Ui(() => { if (_name.XamlRoot != null) _name.Focus(FocusState.Programmatic); });
    }

    internal void Detach()
    {
        _closed = true;
        CloseRequested = null;
        Session.Changed -= OnSessionChanged;
        _retirement.Dispose();
        if (Prewarm != null) { Prewarm.Changed -= OnSessionChanged; Prewarm.Dispose(); Prewarm = null; }
        foreach (var path in _paths) path.Dispose();
        _opener?.Focus(FocusState.Programmatic);
    }

    /// <summary>Owner-state changes the editor shows (seek-preview support, monitoring status).</summary>
    internal void RenderOwner(AdminLibrariesSnapshot _) => Render(); // The section key includes every owner-derived input.

    internal void ApplyViewport()
    {
        var root = XamlRoot?.Size ?? _page.XamlRoot?.Size ?? new Windows.Foundation.Size(1440, 900);
        var phone = root.Width < 640;
        if (phone)
        {
            _panel.Width = root.Width; _panel.Height = root.Height; _panel.CornerRadius = new(0); _panel.BorderThickness = new(0);
        }
        else
        {
            _panel.Width = Math.Min(MaxWidthDialog, root.Width - 32);
            _panel.Height = Math.Min(MaxHeightDialog, root.Height - 64);
            _panel.CornerRadius = new(16); _panel.BorderThickness = new(1);
        }
        _body.ColumnDefinitions.Clear(); _body.RowDefinitions.Clear();
        if (phone)
        {
            _body.RowDefinitions.Add(new() { Height = GridLength.Auto }); _body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            Grid.SetRow(_tabScroll, 0); Grid.SetColumn(_tabScroll, 0); Grid.SetRow(_contentScroll, 1); Grid.SetColumn(_contentScroll, 0);
            _tabs.Orientation = Orientation.Horizontal; _tabs.Padding = new(8, 6, 8, 6); _tabs.Width = double.NaN;
            _tabScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden; _tabScroll.HorizontalScrollMode = ScrollMode.Enabled;
            _tabScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled; _tabScroll.VerticalScrollMode = ScrollMode.Disabled;
            _tabScroll.BorderBrush = Brush("BorderBrush"); _tabScroll.BorderThickness = new(0, 0, 0, 1);
            _sectionHost.Padding = new(16, 20, 16, 20);
            _close.Width = _close.Height = 44;
        }
        else
        {
            _body.ColumnDefinitions.Add(new() { Width = new(176) }); _body.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            Grid.SetRow(_tabScroll, 0); Grid.SetColumn(_tabScroll, 0); Grid.SetRow(_contentScroll, 0); Grid.SetColumn(_contentScroll, 1);
            _tabs.Orientation = Orientation.Vertical; _tabs.Padding = new(12); _tabs.Width = 176;
            _tabScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; _tabScroll.HorizontalScrollMode = ScrollMode.Disabled;
            _tabScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; _tabScroll.VerticalScrollMode = ScrollMode.Enabled;
            _tabScroll.BorderBrush = Brush("BorderBrush"); _tabScroll.BorderThickness = new(0, 0, 1, 0);
            _sectionHost.Padding = new(24, 20, 24, 20);
            _close.Width = _close.Height = 32;
        }
        _phone = phone;
        _renderedSectionKey = null;
        if (!_closed) Render();
    }

    private bool _phone;

    // ── Render ───────────────────────────────────────────────────────────

    private static readonly (AdminLibraryEditorSection Section, string Icon, string Title, string Description)[] Sections =
    [
        (AdminLibraryEditorSection.General, "settings-2", "web.admin.common.libraryEditor.section.general", "web.admin.common.libraryEditor.section.generalDescription"),
        (AdminLibraryEditorSection.Folders, "folder-open", "web.admin.common.libraryEditor.section.folders", "web.admin.common.libraryEditor.section.foldersDescription"),
        (AdminLibraryEditorSection.Metadata, "database", "web.admin.common.libraryEditor.section.metadata", "web.admin.common.libraryEditor.section.metadataDescription"),
        (AdminLibraryEditorSection.Advanced, "sliders-horizontal", "web.admin.common.libraryEditor.section.advanced", "web.admin.common.libraryEditor.section.advancedDescription"),
    ];

    internal void Render()
    {
        var state = Session.State;
        if (state.Saved || state.Retired || !Session.IsCurrent) { CloseRequested?.Invoke(); return; }
        if (state.Library is { } library && Prewarm == null) StartTranslation(library.Id);
        _title.Text = T(state.IsEditing ? "web.admin.common.libraryEditor.dialog.editTitle" : "web.admin.common.libraryEditor.dialog.addTitle");
        _description.Text = state.IsEditing ? T("web.admin.common.libraryEditor.dialog.editDescription", ("name", state.Library!.Name)) : T("web.admin.common.libraryEditor.dialog.addDescription");
        ToolTipService.SetToolTip(_description, _description.Text);
        _typeTile.Child = new Border { Child = Icon(P.TypeMeta(state.Type).Icon, 20, Solid(Primary)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var visible = Sections.Where(entry => entry.Section != AdminLibraryEditorSection.Advanced || state.ShowAdvanced).ToArray();
        if (visible.All(entry => entry.Section != _section)) _section = AdminLibraryEditorSection.General;
        RenderTabs(state, visible);

        var current = visible.First(entry => entry.Section == _section);
        _sectionTitle.Text = T(current.Title);
        _sectionDescription.Text = T(current.Description);
        var key = SectionKey(state);
        if (key != _renderedSectionKey)
        {
            _renderedSectionKey = key;
            var focused = Focused(XamlRoot);
            var focusId = focused == null ? "" : AutomationProperties.GetAutomationId(focused);
            var keep = focused != null && (ReferenceEquals(focused, _name) || _paths.Any(path => ReferenceEquals(path.Box, focused)));
            _sectionContent.Content = _section switch
            {
                AdminLibraryEditorSection.General => General(state),
                AdminLibraryEditorSection.Folders => Folders(state),
                AdminLibraryEditorSection.Metadata => Metadata(state),
                _ => Advanced(state),
            };
            if (focusId.Length > 0 && !keep) Ui(() => AdminLibrariesPage.FindControl(_sectionContent, focusId)?.Focus(FocusState.Programmatic));
            else if (keep) Ui(() => { if (focused!.XamlRoot != null) focused.Focus(FocusState.Programmatic); });
        }
        _syncing = true;
        try { if (_name.Text != state.Name) _name.Text = state.Name; }
        finally { _syncing = false; }
        foreach (var path in _paths) path.SetEnabled(state.CanSave);
        _name.IsEnabled = state.CanSave;

        var errors = new[] { state.ShownNameError, state.ShownPathsError }.Where(error => error != null).ToArray();
        _footerError.Text = state.Failure ?? string.Join(" ", errors);
        _footerError.Visibility = _footerError.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(_footerError, 0); Grid.SetColumnSpan(_footerError, 1);
        _save.Content = T(state.Saving
            ? state.IsEditing ? "web.admin.common.libraryEditor.dialog.saving" : "web.admin.common.libraryEditor.dialog.creating"
            : state.IsEditing ? "web.admin.common.libraryEditor.dialog.saveChanges" : "web.admin.common.libraryEditor.dialog.create");
        AutomationProperties.SetName(_save, (string)_save.Content);
        _save.IsEnabled = state.CanSave;
        _cancel.IsEnabled = !state.Saving;
    }

    /// <summary>Structural inputs of the active section (text edits do not rebuild it).</summary>
    private string SectionKey(AdminLibraryEditorState state)
    {
        var owner = _controller.State;
        var translation = Prewarm;
        return _section switch
        {
            AdminLibraryEditorSection.General => string.Join("|", _section, _phone, state.Type, state.Enabled, state.IsEditing, state.Library?.Type, state.Library?.PosterUrl,
                state.PosterUploading, state.PosterDeleting, state.ShownNameError, state.CanSave),
            AdminLibraryEditorSection.Folders => string.Join("|", _section, _phone, state.Paths.Count, state.ShownPathsError, state.RealtimeMonitoring, state.CanSave,
                owner.Monitoring?.ServerEnabled, MonitoringEntry(state)?.State, MonitoringEntry(state)?.Detail, MonitoringEntry(state)?.Directories),
            AdminLibraryEditorSection.Metadata => string.Join("|", _section, _phone, state.Type, state.MetadataLanguage, state.AutoTranslateMetadata, state.BoxSetsEnabled, state.BoxSetMinMovies,
                state.BoxSetsInSearch, string.Join(",", state.TrailerKinds), state.ChainLoading, state.ChainFailed, state.ChainDirty, ChainKey(state), state.CanSave,
                translation?.Enabled, translation?.CapabilityLoading, translation?.CapabilityError, translation?.Busy, translation?.Starting, translation?.Cancelling, string.Join(",", translation?.ActiveJobs.Select(job => job.Id + job.Progress + job.ProgressMessage) ?? [])),
            _ => string.Join("|", _section, _phone, state.Type, state.ChapterThumbnailsEnabled, state.TrickplayEnabled, state.IntroDetectionEnabled, state.CanSave, ChapterSupported(state), TrickplaySupported(state)),
        };
    }

    private static string ChainKey(AdminLibraryEditorState state) =>
        string.Join(";", state.ActiveChains.Select(level => level.Key + "=" + string.Join(",", level.Value.Select(item => item.PluginInstallationId + ":" + item.CapabilityId + ":" + item.Enabled))));

    private void RenderTabs(AdminLibraryEditorState state, IReadOnlyList<(AdminLibraryEditorSection Section, string Icon, string Title, string Description)> visible)
    {
        var focusIndex = _tabs.Children.OfType<Button>().ToList().FindIndex(button => button.FocusState != FocusState.Unfocused);
        _tabs.Children.Clear();
        for (var index = 0; index < visible.Count; index++)
        {
            var entry = visible[index];
            var selected = entry.Section == _section;
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(Icon(entry.Icon, 16, selected ? Brush("PrimaryTextBrush") : Brush("SecondaryTextBrush")));
            var label = Text(T(entry.Title), 14); label.FontWeight = FontWeights.Medium; label.Foreground = selected ? Brush("PrimaryTextBrush") : Brush("SecondaryTextBrush");
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var hasError = entry.Section == AdminLibraryEditorSection.General && state.ShownNameError != null || entry.Section == AdminLibraryEditorSection.Folders && state.ShownPathsError != null;
            if (hasError) { var dot = new Border { Width = 6, Height = 6, CornerRadius = new(3), Background = Solid(Destructive), Margin = new(8, 0, 0, 0) }; Grid.SetColumn(dot, 2); row.Children.Add(dot); }
            else if (entry.Section == AdminLibraryEditorSection.Folders && state.FolderCount > 0)
            {
                var count = Mono(state.FolderCount.ToString(System.Globalization.CultureInfo.InvariantCulture), 10); count.Margin = new(8, 0, 0, 0);
                Grid.SetColumn(count, 2); row.Children.Add(count);
            }
            var tab = new Button
            {
                Content = row, HorizontalAlignment = _phone ? HorizontalAlignment.Left : HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Height = _phone ? 44 : double.NaN, MinHeight = _phone ? 44 : 36, Padding = new(12, 8, 12, 8), CornerRadius = new(8), BorderThickness = new(0),
                Style = (Style)Application.Current.Resources["GhostButtonStyle"], Background = selected ? Tint("MutedBrush", 0.8) : null,
            };
            AutomationProperties.SetName(tab, T(entry.Title));
            AutomationProperties.SetAutomationId(tab, "LibraryEditorTab_" + entry.Section);
            AutomationProperties.SetItemStatus(tab, selected ? "selected" : "");
            AutomationProperties.SetPositionInSet(tab, index + 1);
            AutomationProperties.SetSizeOfSet(tab, visible.Count);
            var section = entry.Section;
            tab.Click += (_, _) => Select(section);
            var position = index;
            tab.KeyDown += (_, args) =>
            {
                var step = (_phone ? args.Key is VirtualKey.Right : args.Key is VirtualKey.Down) ? 1 : (_phone ? args.Key is VirtualKey.Left : args.Key is VirtualKey.Up) ? -1 : 0;
                if (step == 0) return;
                args.Handled = true;
                var target = (position + step + visible.Count) % visible.Count;
                Select(visible[target].Section);
                Ui(() => { if (target < _tabs.Children.Count) (_tabs.Children[target] as Button)?.Focus(FocusState.Keyboard); });
            };
            _tabs.Children.Add(tab);
            if (selected && _broughtIntoView != entry.Section)
            {
                _broughtIntoView = entry.Section;
                Ui(() => { if (tab.XamlRoot != null && tab.IsLoaded) tab.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false }); });
            }
        }
        if (focusIndex >= 0 && focusIndex < _tabs.Children.Count) Ui(() => { if (focusIndex < _tabs.Children.Count) (_tabs.Children[focusIndex] as Button)?.Focus(FocusState.Keyboard); });
    }

    private void Select(AdminLibraryEditorSection section)
    {
        if (_section == section) return;
        _section = section;
        _renderedSectionKey = null;
        _contentScroll.ChangeView(null, 0, null, true);
        Render();
    }

    private async Task SubmitAsync()
    {
        var failing = await Session.SubmitAsync();
        if (failing is { } section) Ui(() => { Select(section); _renderedSectionKey = null; Render(); });
    }

    // ── General ──────────────────────────────────────────────────────────

    private StackPanel General(AdminLibraryEditorState state)
    {
        var stack = new StackPanel { Spacing = 20 };
        var name = new StackPanel { Spacing = 6 };
        name.Children.Add(Strong(T("web.admin.common.libraryEditor.general.name"), 13));
        Reparent(_name, name);
        _name.Height = _phone ? 44 : 36;
        if (state.ShownNameError is { } error) { var text = Text(error, 12); text.Foreground = Solid(Destructive); name.Children.Add(text); }
        stack.Children.Add(name);

        var type = new StackPanel { Spacing = 6 };
        type.Children.Add(Strong(T("web.admin.common.libraryEditor.general.type"), 13));
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        AutomationProperties.SetName(grid, T("web.admin.common.libraryEditor.general.typeGroup"));
        var columns = _phone ? 2 : 5;
        for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var row = 0; row < (P.Types.Count + columns - 1) / columns; row++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < P.Types.Count; index++)
        {
            var entry = P.Types[index];
            var selected = state.Type == entry.Value;
            var content = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            var icon = Icon(entry.Icon, 20, selected ? Solid(Primary) : Brush("SecondaryTextBrush")); icon.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(icon);
            var label = Text(entry.Label, _phone ? 12 : 11); label.FontWeight = FontWeights.Medium; label.TextAlignment = TextAlignment.Center; label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Foreground = selected ? Brush("PrimaryTextBrush") : Brush("SecondaryTextBrush");
            content.Children.Add(label);
            var radio = new RadioButton
            {
                Content = content, GroupName = "LibraryType", IsChecked = selected, IsEnabled = state.CanSave, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new(8, 12, 8, 12), CornerRadius = new(12), BorderThickness = new(1),
                Background = selected ? Solid(Primary, 0.10) : Tint("SurfaceBrush", 0.9), BorderBrush = selected ? Solid(Primary, 0.5) : Brush("BorderBrush"), MinWidth = 0,
            };
            radio.Template = RadioTileTemplate;
            AutomationProperties.SetName(radio, entry.Label);
            AutomationProperties.SetAutomationId(radio, "LibraryEditorType_" + entry.Value);
            var value = entry.Value;
            radio.Checked += (_, _) => { if (!_syncing) Session.SetType(value); };
            Grid.SetRow(radio, index / columns); Grid.SetColumn(radio, index % columns);
            grid.Children.Add(radio);
        }
        type.Children.Add(grid);
        if (state.Library != null && state.Type != state.Library.Type)
        {
            var warning = Text(T("web.admin.common.libraryEditor.general.typeChangeWarning"), 12); warning.Foreground = Solid(Warning);
            type.Children.Add(warning);
        }
        stack.Children.Add(type);

        var enabled = Switch(T("web.admin.common.libraryEditor.general.enabled"), "LibraryEditorEnabled", state.Enabled);
        enabled.IsEnabled = state.CanSave;
        enabled.Toggled += (_, _) => Session.SetEnabled(enabled.IsOn);
        stack.Children.Add(SettingCard(T("web.admin.common.libraryEditor.general.enabled"), T("web.admin.common.libraryEditor.general.enabledDescription"), enabled));
        if (state.Library != null) stack.Children.Add(Poster(state));
        return stack;
    }

    /// <summary>A radio "tile": the content in a bordered box (WebUI <c>role="radio"</c> button).</summary>
    private static readonly ControlTemplate RadioTileTemplate = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='RadioButton'>" +
        "<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' " +
        "CornerRadius='{TemplateBinding CornerRadius}' Padding='{TemplateBinding Padding}'>" +
        "<ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border></ControlTemplate>");

    private StackPanel Poster(AdminLibraryEditorState state)
    {
        var library = state.Library!;
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(Strong(T("web.admin.common.libraryEditor.poster.label"), 13));
        var row = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        if (!string.IsNullOrEmpty(library.PosterUrl))
        {
            var image = new Image { Height = 56, Width = 56 * 16 / 9d, Stretch = Stretch.UniformToFill };
            if (Uri.TryCreate(new Uri(App.Services.GetService(typeof(SiloApiClient)) is SiloApiClient client && client.BaseUrl.Length > 0 ? client.BaseUrl : "https://localhost/"), library.PosterUrl, out var uri))
                image.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
            AutomationProperties.SetName(image, T("web.admin.common.libraryEditor.poster.alt", ("name", library.Name)));
            row.Children.Add(new Border { Child = image, CornerRadius = new(4), BorderThickness = new(1), BorderBrush = Brush("BorderBrush") });
        }
        else
        {
            row.Children.Add(new Border
            {
                Height = 56, Width = 56 * 16 / 9d, CornerRadius = new(4), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), Background = Tint("MutedBrush", 0.3),
                Child = new Border { Child = Icon("image", 16, Tint("SecondaryTextBrush", 0.4)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        }
        var upload = Action(state.PosterUploading ? "..." : T(string.IsNullOrEmpty(library.PosterUrl) ? "web.admin.common.libraryEditor.poster.upload" : "web.admin.common.libraryEditor.poster.replace"),
            "LibraryEditorPosterUpload", height: _phone ? 44 : 32);
        upload.FontSize = 12;
        upload.IsEnabled = !state.PosterUploading && !state.PosterDeleting && Session.IsCurrent;
        upload.Click += async (_, _) => await PickPosterAsync();
        row.Children.Add(upload);
        if (!string.IsNullOrEmpty(library.PosterUrl))
        {
            var remove = IconButton("trash-2", T("web.admin.common.libraryEditor.poster.remove"), "LibraryEditorPosterRemove", _phone ? 44 : 32, 12, Brush("SecondaryTextBrush"));
            remove.IsEnabled = !state.PosterUploading && !state.PosterDeleting;
            remove.Click += (_, _) => _ = Session.DeletePosterAsync();
            row.Children.Add(remove);
        }
        stack.Children.Add(row);
        return stack;
    }

    /// <summary>Opens the image picker (JPEG, PNG, WebP), reads at most 10 MiB, then uploads under the editor's lease.</summary>
    private async Task PickPosterAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary };
            foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".webp" }) picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var file = await picker.PickSingleFileAsync();
            if (file == null || _closed || !Session.IsCurrent) return;
            var properties = await file.GetBasicPropertiesAsync();
            var mediaType = file.FileType.ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
            if (properties.Size == 0 || properties.Size > (ulong)AdminLibrariesApi.MaxPosterBytes)
            {
                _controller.Announce(T("admin.native.libraries.posterInvalid"), true);
                return;
            }
            using var stream = await file.OpenStreamForReadAsync();
            var bytes = await AdminLibraryEditorSession.ReadPosterAsync(stream, Session.Lease.Token);
            if (_closed || !Session.IsCurrent) return;
            await Session.UploadPosterAsync(bytes, mediaType);
        }
        catch (AdminLibraryPosterException)
        {
            if (!_closed && Session.IsCurrent) _controller.Announce(T("admin.native.libraries.posterInvalid"), true);
        }
        catch (OperationCanceledException) when (_closed || !Session.IsCurrent) { }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
        {
            _controller.Announce(T("web.admin.libraries.toast.posterUploadFailed"), true);
        }
    }

    // ── Folders ──────────────────────────────────────────────────────────

    private StackPanel Folders(AdminLibraryEditorState state)
    {
        var stack = new StackPanel { Spacing = 20 };
        var fields = new StackPanel { Spacing = 12 };
        var list = new StackPanel { Spacing = 8 };
        while (_paths.Count < state.Paths.Count) _paths.Add(new PathField(this, _paths.Count));
        while (_paths.Count > state.Paths.Count)
        {
            var removed = _paths[^1];
            if (_hosts.Remove(removed.Root, out var host)) host.Children.Remove(removed.Root);
            removed.Dispose();
            _paths.RemoveAt(_paths.Count - 1);
        }
        for (var index = 0; index < state.Paths.Count; index++)
        {
            var field = _paths[index];
            field.Sync(state.Paths[index], state.Paths.Count > 1, state.ShownPathsError != null, _phone);
            Reparent(field.Root, list);
        }
        fields.Children.Add(list);
        if (state.ShownPathsError is { } error) { var text = Text(error, 12); text.Foreground = Solid(Destructive); fields.Children.Add(text); }
        var buttons = new Grid { ColumnSpacing = 6 };
        var browse = Action(T("web.admin.common.libraryEditor.folders.browse"), "LibraryEditorBrowse", "folder-search", height: _phone ? 44 : 32);
        browse.IsEnabled = state.CanSave;
        browse.Click += async (_, _) =>
        {
            var selected = await _page.BrowseFoldersAsync(Session.Lease, Session.State.Paths);
            if (selected.Count > 0) Session.MergeBrowsedPaths(selected);
        };
        var add = Action(T("web.admin.common.libraryEditor.folders.addPath"), "LibraryEditorAddPath", "plus", height: _phone ? 44 : 32);
        add.IsEnabled = state.CanSave;
        add.Click += (_, _) => { Session.AddPath(); Ui(() => _paths.LastOrDefault()?.Box.Focus(FocusState.Programmatic)); };
        if (_phone)
        {
            buttons.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); buttons.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            browse.HorizontalAlignment = add.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else { buttons.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); buttons.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); buttons.HorizontalAlignment = HorizontalAlignment.Left; }
        buttons.Children.Add(browse); Grid.SetColumn(add, 1); buttons.Children.Add(add);
        fields.Children.Add(buttons);
        stack.Children.Add(fields);
        stack.Children.Add(Monitoring(state));
        return stack;
    }

    private AdminLibraryRealtimeMonitoringEntry? MonitoringEntry(AdminLibraryEditorState state) =>
        state.Library == null ? null : _controller.State.Monitoring?.Libraries.FirstOrDefault(entry => entry.LibraryId == state.Library.Id);

    /// <summary>The real-time monitoring switch and the server's status line for the saved switch.</summary>
    private Border Monitoring(AdminLibraryEditorState state)
    {
        var status = _controller.State.Monitoring;
        var serverOff = status?.ServerEnabled == false;
        var entry = MonitoringEntry(state);
        var unsaved = state.RealtimeMonitoring != (state.Library?.RealtimeMonitoring ?? true);
        UIElement? footer = null;
        if (serverOff)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush("SecondaryTextBrush") };
            var settings = new AdminRouteRequest(AdminRoute.LibrarySettings);
            foreach (var (segment, tag) in AdminLibrariesText.Segments(T("web.admin.common.libraryEditor.monitoring.serverOff")))
            {
                if (tag == "link" && AdminShellPage.CanOpen(settings))
                {
                    var link = new Microsoft.UI.Xaml.Documents.Hyperlink();
                    link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = segment, FontWeight = FontWeights.Medium });
                    link.Click += (_, _) => { if (_closed || !Session.IsCurrent) return; CloseRequested?.Invoke(); AdminShellPage.Open(settings); };
                    text.Inlines.Add(link);
                }
                else text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = segment, FontWeight = tag == "link" ? FontWeights.Medium : FontWeights.Normal });
            }
            if (!AdminShellPage.CanOpen(settings)) ToolTipService.SetToolTip(text, T("admin.native.libraries.settingsUnavailable"));
            footer = text;
        }
        else if (entry != null && !unsaved)
        {
            var line = Text(T("web.admin.common.libraryEditor.monitoringStatus.line", ("status", P.MonitoringStatusText(entry))), 12, !P.MonitoringNeedsAttention(entry));
            if (P.MonitoringNeedsAttention(entry)) line.Foreground = Solid(Warning);
            AutomationProperties.SetAutomationId(line, "LibraryEditorMonitoringStatus");
            footer = line;
        }
        var toggle = Switch(T("web.admin.common.libraryEditor.monitoring.title"), "LibraryEditorMonitoring", state.RealtimeMonitoring);
        toggle.IsEnabled = !serverOff && state.CanSave;
        toggle.Toggled += (_, _) => Session.SetRealtimeMonitoring(toggle.IsOn);
        return SettingCard(T("web.admin.common.libraryEditor.monitoring.title"), T("web.admin.common.libraryEditor.monitoring.description"), toggle, footer);
    }

    /// <summary>One folder path: the input with its inline completion (Tab accepts) and a remove button.</summary>
    private sealed class PathField : IDisposable
    {
        private readonly AdminLibraryEditorView _owner;
        private readonly TextBlock _ghost = new() { FontSize = 14, IsHitTestVisible = false, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center, Margin = new(13, 0, 12, 0) };
        private readonly Microsoft.UI.Xaml.Documents.Run _typed = new();
        private readonly Microsoft.UI.Xaml.Documents.Run _suffix = new();
        private readonly Button _remove;
        private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private CancellationTokenSource? _lookup;
        private string _completion = "";
        private bool _syncing;
        internal int Index { get; }
        internal Grid Root { get; } = new() { ColumnSpacing = 6 };
        internal TextBox Box { get; }

        internal PathField(AdminLibraryEditorView owner, int index)
        {
            _owner = owner; Index = index;
            Root.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); Root.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var icon = Icon("folder-open", 16, Tint("SecondaryTextBrush", 0.6)); icon.VerticalAlignment = VerticalAlignment.Center;
            Root.Children.Add(icon);
            var input = new Grid { FlowDirection = FlowDirection.LeftToRight };
            _typed.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            _suffix.Foreground = Brush("SecondaryTextBrush");
            _ghost.Inlines.Add(_typed); _ghost.Inlines.Add(_suffix);
            Box = new TextBox { Height = 36, FontSize = 14, CornerRadius = new(8), PlaceholderText = "/mnt/media/movies", Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            AutomationProperties.SetName(Box, T("web.admin.common.libraryEditor.section.folders") + " " + (index + 1));
            AutomationProperties.SetAutomationId(Box, "LibraryEditorPath_" + index);
            input.Children.Add(_ghost);
            input.Children.Add(Box);
            Grid.SetColumn(input, 1); Root.Children.Add(input);
            _remove = IconButton("trash-2", T("web.admin.common.libraryEditor.folders.remove"), "LibraryEditorRemovePath_" + index, 36, 14, Brush("SecondaryTextBrush"));
            _remove.Click += (_, _) => _owner.Session.RemovePath(Index);
            Grid.SetColumn(_remove, 2); Root.Children.Add(_remove);
            Box.TextChanged += (_, _) =>
            {
                if (_syncing) return;
                _owner.Session.UpdatePath(Index, Box.Text);
                SetCompletion("");
                _debounce.Stop(); _debounce.Start();
            };
            Box.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == VirtualKey.Tab && _completion.Length > 0 && !IsShiftDown())
                {
                    args.Handled = true;
                    var accepted = Box.Text.Trim() + _completion;
                    Box.Text = accepted; Box.SelectionStart = accepted.Length;
                }
            };
            _debounce.Tick += async (_, _) =>
            {
                _debounce.Stop();
                _lookup?.Cancel(); _lookup?.Dispose();
                _lookup = CancellationTokenSource.CreateLinkedTokenSource(_owner.Session.Lease.Token);
                var token = _lookup.Token;
                var value = Box.Text;
                try
                {
                    var suffix = await AdminPathCompletion.SuggestAsync(_owner._controller.Api, _owner.Session.Lease, value, token);
                    if (!token.IsCancellationRequested && Box.Text == value) SetCompletion(suffix);
                }
                catch (Exception) { /* Completion is a hint; a failed lookup shows none. */ }
            };
        }

        private static bool IsShiftDown() =>
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        private void SetCompletion(string suffix)
        {
            _completion = suffix;
            _typed.Text = suffix.Length > 0 ? Box.Text.Trim() : "";
            _suffix.Text = suffix;
            AutomationProperties.SetHelpText(Box, suffix.Length > 0 ? Box.Text.Trim() + suffix : "");
        }

        internal void Sync(string value, bool removable, bool invalid, bool phone)
        {
            _syncing = true;
            try { if (Box.Text != value) Box.Text = value; }
            finally { _syncing = false; }
            _remove.Visibility = removable ? Visibility.Visible : Visibility.Collapsed;
            Box.Height = phone ? 44 : 36; _remove.Width = _remove.Height = phone ? 44 : 36;
            Box.BorderBrush = invalid ? Solid(Destructive) : Brush("BorderBrush");
        }

        internal void SetEnabled(bool enabled) { Box.IsEnabled = enabled; _remove.IsEnabled = enabled; }

        public void Dispose()
        {
            _debounce.Stop();
            _lookup?.Cancel(); _lookup?.Dispose(); _lookup = null;
        }
    }

    // ── Metadata ─────────────────────────────────────────────────────────

    private StackPanel Metadata(AdminLibraryEditorState state)
    {
        var stack = new StackPanel { Spacing = 20 };
        var language = new StackPanel { Spacing = 6 };
        language.Children.Add(Strong(T("web.admin.common.libraryEditor.metadata.language"), 13));
        var picker = new ComboBox { Width = _phone ? double.NaN : 256, HorizontalAlignment = _phone ? HorizontalAlignment.Stretch : HorizontalAlignment.Left, Height = _phone ? 44 : 36, CornerRadius = new(8) };
        foreach (var option in P.MetadataLanguages()) picker.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Code });
        if (!picker.Items.Cast<ComboBoxItem>().Any(item => (string)item.Tag == state.MetadataLanguage))
            picker.Items.Add(new ComboBoxItem { Content = state.MetadataLanguage.Length > 0 ? state.MetadataLanguage : "—", Tag = state.MetadataLanguage });
        picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == state.MetadataLanguage);
        picker.IsEnabled = state.CanSave;
        AutomationProperties.SetName(picker, T("web.admin.common.libraryEditor.metadata.language"));
        AutomationProperties.SetAutomationId(picker, "LibraryEditorLanguage");
        picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is ComboBoxItem { Tag: string code }) Session.SetMetadataLanguage(code); };
        language.Children.Add(picker);
        language.Children.Add(Text(T("web.admin.common.libraryEditor.metadata.languageHelp"), 12, true));
        stack.Children.Add(language);

        var translate = Switch(T("web.admin.common.libraryEditor.metadata.autoTranslate"), "LibraryEditorAutoTranslate", state.AutoTranslateMetadata);
        translate.IsEnabled = state.CanSave;
        translate.Toggled += (_, _) => Session.SetAutoTranslate(translate.IsOn);
        stack.Children.Add(PlainSetting(T("web.admin.common.libraryEditor.metadata.autoTranslate"), T("web.admin.common.libraryEditor.metadata.autoTranslateHelp"), translate));
        if (state.Library != null && Prewarm is { } translation)
        {
            if (translation.Enabled) stack.Children.Add(TranslatePanel(translation));
            else if (translation.CapabilityLoading || translation.CapabilityError != null)
            {
                var failure = new StackPanel { Spacing = 8 };
                failure.Children.Add(Text(T("web.admin.common.libraryEditor.translate.title"), 14));
                if (translation.CapabilityError is { } error) failure.Children.Add(Text(error, 12, true));
                var retry = Action(Core.Localization.AppStrings.Get("common.retry", fallback: "Retry"), "LibraryEditorTranslationCapabilityRetry");
                retry.IsEnabled = !translation.CapabilityLoading && Session.IsCurrent;
                retry.Click += (_, _) => _ = translation.RetryCapabilityAsync();
                failure.Children.Add(retry);
                stack.Children.Add(failure);
            }
        }
        if (state.Support.BoxSets) stack.Children.Add(BoxSets(state));
        if (state.Support.Trailers) stack.Children.Add(TrailerKinds(state));
        if (state.ContentLevels.Count > 0) stack.Children.Add(Providers(state));
        return stack;
    }

    private static Grid PlainSetting(string title, string help, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(Strong(title, 13));
        text.Children.Add(Text(help, 12, true));
        grid.Children.Add(text);
        Grid.SetColumn(control, 1); grid.Children.Add(control);
        return grid;
    }

    private string? _translateTarget;

    /// <summary>WebUI <c>LibraryTranslatePanel</c>: prewarm missing translations for one language; Stop while one runs.</summary>
    private Border TranslatePanel(AdminLibraryTranslationSession translation)
    {
        var stack = new StackPanel { Spacing = 12 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(Icon("languages", 16, Brush("SecondaryTextBrush")));
        head.Children.Add(Strong(T("web.admin.common.libraryEditor.translate.title"), 14));
        stack.Children.Add(head);
        stack.Children.Add(Text(T("web.admin.common.libraryEditor.translate.description"), 12, true));
        if (translation.JobsLoading)
            stack.Children.Add(Text(Core.Localization.AppStrings.Get("common.loading", fallback: "Loading..."), 12, true));
        if (translation.JobsError is { } jobsError)
        {
            stack.Children.Add(Text(jobsError, 12));
            var retry = Action(Core.Localization.AppStrings.Get("common.retry", fallback: "Retry"), "LibraryEditorTranslationJobsRetry");
            retry.IsEnabled = !translation.JobsLoading && Session.IsCurrent;
            retry.Click += (_, _) => _ = translation.RetryJobsAsync();
            stack.Children.Add(retry);
        }
        var row = new LogsWrapPanel { Spacing = 12, RowSpacing = 8 };
        var field = new StackPanel { Spacing = 4 };
        field.Children.Add(Text(T("web.admin.common.libraryEditor.translate.language"), 12));
        var picker = new ComboBox { Width = _phone ? 280 : double.NaN, MinWidth = 180, Height = _phone ? 44 : 36, CornerRadius = new(6) };
        picker.Items.Add(new ComboBoxItem { Content = T("web.admin.common.libraryEditor.translate.selectLanguage"), Tag = "" });
        foreach (var option in P.TranslationLanguages()) picker.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Code });
        picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == (_translateTarget ?? "")) ?? picker.Items[0];
        picker.IsEnabled = !translation.Busy;
        AutomationProperties.SetName(picker, T("web.admin.common.libraryEditor.translate.language"));
        AutomationProperties.SetAutomationId(picker, "LibraryEditorTranslateLanguage");
        picker.SelectionChanged += (_, _) => _translateTarget = (picker.SelectedItem as ComboBoxItem)?.Tag as string;
        field.Children.Add(picker);
        row.Children.Add(field);
        var active = translation.ActiveJobs;
        var single = active.Count == 1 ? active[0] : null;
        if (single != null)
        {
            var stop = Action(T("web.admin.common.libraryEditor.translate.stop"), "LibraryEditorTranslateStop", height: _phone ? 44 : 32);
            stop.IsEnabled = !translation.Cancelling;
            stop.VerticalAlignment = VerticalAlignment.Bottom;
            stop.Click += (_, _) => _ = translation.CancelAsync(single.Id);
            row.Children.Add(stop);
        }
        else
        {
            var start = Action(T(translation.Starting ? "web.admin.common.libraryEditor.translate.starting" : "web.admin.common.libraryEditor.translate.translate"),
                "LibraryEditorTranslateStart", style: "AccentButtonStyle", height: _phone ? 44 : 32);
            start.IsEnabled = !translation.Busy;
            start.VerticalAlignment = VerticalAlignment.Bottom;
            start.Click += (_, _) => _ = translation.StartAsync(_translateTarget ?? "");
            row.Children.Add(start);
        }
        stack.Children.Add(row);
        foreach (var job in active)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var message = job.ProgressMessage.Length > 0 ? job.ProgressMessage : T("web.admin.common.libraryEditor.translate.queued");
            var status = job.FieldsTotal > 0 ? T("web.admin.common.libraryEditor.translate.statusWithPercent", ("status", message), ("percent", (int)Math.Round(job.Progress * 100))) : message;
            if (active.Count > 1) status = T("web.admin.common.libraryEditor.translate.statusWithLanguage", ("language", AdminSubtitlesText.LanguageName(job.TargetLanguage)), ("status", status));
            var text = Text(status, 12, true);
            AutomationProperties.SetLiveSetting(text, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            line.Children.Add(text);
            if (single == null)
            {
                var stop = Action(T("web.admin.common.libraryEditor.translate.stop"), "LibraryEditorTranslateStop_" + job.Id, style: "GhostButtonStyle", height: _phone ? 44 : 32);
                stop.IsEnabled = !translation.Cancelling;
                var id = job.Id;
                stop.Click += (_, _) => _ = translation.CancelAsync(id);
                line.Children.Add(stop);
            }
            stack.Children.Add(line);
        }
        return new Border { Child = stack, Padding = new(12), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), Background = Tint("MutedBrush", 0.3) };
    }

    private Border BoxSets(AdminLibraryEditorState state)
    {
        var stack = new StackPanel { Spacing = 12 };
        var toggle = Switch(T("web.admin.common.libraryEditor.boxSets.title"), "LibraryEditorBoxSets", state.BoxSetsEnabled);
        toggle.IsEnabled = state.CanSave;
        toggle.Toggled += (_, _) => Session.SetBoxSets(toggle.IsOn);
        stack.Children.Add(PlainSetting(T("web.admin.common.libraryEditor.boxSets.title"), T("web.admin.common.libraryEditor.boxSets.description"), toggle));
        if (state.BoxSetsEnabled)
        {
            var more = new StackPanel { Spacing = 12, Padding = new(0, 12, 0, 0), BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 1, 0, 0) };
            var size = new StackPanel { Spacing = 6 };
            size.Children.Add(Strong(T("web.admin.common.libraryEditor.boxSets.minMovies"), 13));
            var pills = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Padding = new(2), HorizontalAlignment = HorizontalAlignment.Left };
            var frame = new Border { Child = pills, CornerRadius = new(999), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(frame, T("web.admin.common.libraryEditor.boxSets.minMovies"));
            foreach (var count in P.BoxSetOptions(state.BoxSetMinMovies))
            {
                var selected = state.BoxSetMinMovies == count;
                var pill = new RadioButton
                {
                    Content = new TextBlock { Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 12, FontWeight = FontWeights.Medium, Foreground = selected ? Brush("AccentForegroundBrush") : Brush("SecondaryTextBrush") },
                    GroupName = "BoxSetSize", IsChecked = selected, IsEnabled = state.CanSave, MinWidth = _phone ? 44 : 0, MinHeight = _phone ? 44 : 0, Padding = new(12, 2, 12, 2),
                    CornerRadius = new(999), Background = selected ? Solid(Primary) : new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0), Template = RadioTileTemplate,
                };
                AutomationProperties.SetName(pill, count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                AutomationProperties.SetAutomationId(pill, "LibraryEditorBoxSetSize_" + count);
                var value = count;
                pill.Checked += (_, _) => Session.SetBoxSetMinMovies(value);
                pills.Children.Add(pill);
            }
            size.Children.Add(frame);
            size.Children.Add(Text(T("web.admin.common.libraryEditor.boxSets.minMoviesHelp"), 12, true));
            more.Children.Add(size);
            var inSearch = Switch(T("web.admin.common.libraryEditor.boxSets.inSearch"), "LibraryEditorBoxSetsSearch", state.BoxSetsInSearch);
            inSearch.IsEnabled = state.CanSave;
            inSearch.Toggled += (_, _) => Session.SetBoxSetsInSearch(inSearch.IsOn);
            more.Children.Add(PlainSetting(T("web.admin.common.libraryEditor.boxSets.inSearch"), T("web.admin.common.libraryEditor.boxSets.inSearchHelp"), inSearch));
            more.Children.Add(Text(T("web.admin.common.libraryEditor.boxSets.lookupNote"), 12, true));
            stack.Children.Add(more);
        }
        return new Border { Child = stack, Padding = new(14), CornerRadius = new(12), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), Background = Tint("SurfaceBrush", 0.9) };
    }

    private StackPanel TrailerKinds(AdminLibraryEditorState state)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(Strong(T("web.admin.common.libraryEditor.metadata.trailerKinds"), 13));
        stack.Children.Add(Text(T("web.admin.common.libraryEditor.metadata.trailerKindsHelp"), 12, true));
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        AutomationProperties.SetName(grid, T("web.admin.common.libraryEditor.metadata.trailerKindsGroup"));
        var columns = _phone ? 1 : 2;
        for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var kinds = P.ProviderTrailerKinds;
        for (var row = 0; row < (kinds.Count + columns - 1) / columns; row++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < kinds.Count; index++)
        {
            var kind = kinds[index];
            var check = state.TrailerKinds.Contains(kind);
            var box = new CheckBox { Content = P.TrailerKindLabel(kind), IsChecked = check, IsEnabled = state.CanSave, MinHeight = _phone ? 44 : 32 };
            AutomationProperties.SetAutomationId(box, "LibraryEditorTrailer_" + kind);
            box.Click += (_, _) => Session.ToggleTrailerKind(kind);
            var holder = new Border { Child = box, Padding = new(10, 0, 10, 0), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = check ? Brush("BorderBrush") : Tint("BorderBrush", 0.5), Background = check ? Tint("MutedBrush", 1) : Tint("MutedBrush", 0.3) };
            Grid.SetRow(holder, index / columns); Grid.SetColumn(holder, index % columns);
            grid.Children.Add(holder);
        }
        stack.Children.Add(grid);
        return stack;
    }

    private readonly HashSet<string> _collapsedLevels = new(StringComparer.Ordinal);

    /// <summary>WebUI provider priority: loading, the local metadata switch, per-level ordered remote providers, or the empty states.</summary>
    private StackPanel Providers(AdminLibraryEditorState state)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(Strong(T("web.admin.common.libraryEditor.metadata.providerPriority"), 13));
        var help = Text(T("web.admin.common.libraryEditor.metadata.providerPriorityHelp"), 12, true); help.Margin = new(0, 0, 0, 6);
        stack.Children.Add(help);
        Border Dashed(UIElement child) => new() { Child = child, Padding = new(16), CornerRadius = new(12), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), Background = Tint("SurfaceBrush", 0.9) };
        if (state.ChainFailed)
        {
            var failure = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
            failure.Children.Add(Text(T("web.admin.libraries.toast.providerChainFailed"), 12, true));
            var retry = Action(Core.Localization.AppStrings.Get("common.retry", fallback: "Retry"), "LibraryEditorProvidersRetry");
            retry.HorizontalAlignment = HorizontalAlignment.Center;
            retry.Click += (_, _) => _ = Session.RetryChainsAsync();
            failure.Children.Add(retry);
            stack.Children.Add(Dashed(failure));
            return stack;
        }
        if (state.ChainLoading)
        {
            var loading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
            loading.Children.Add(Spinning("loader", 14, Brush("SecondaryTextBrush")));
            loading.Children.Add(Text(T("web.admin.common.libraryEditor.metadata.loadingProviders"), 12, true));
            AutomationProperties.SetAutomationId(loading, "LibraryEditorProvidersLoading");
            stack.Children.Add(Dashed(loading));
            return stack;
        }
        if (!state.HasMetadataProviders)
        {
            var none = Text(T("web.admin.common.libraryEditor.metadata.noProviders"), 12, true); none.TextAlignment = TextAlignment.Center; none.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(Dashed(none));
            return stack;
        }
        if (state.HasLocalMetadata)
        {
            var local = Switch(T("web.admin.common.libraryEditor.metadata.localMetadata"), "LibraryEditorLocalMetadata", state.LocalMetadataEnabled);
            local.IsEnabled = state.CanSave;
            local.Toggled += (_, _) => Session.SetLocalMetadata(local.IsOn);
            var card = SettingCard(T("web.admin.common.libraryEditor.metadata.localMetadata"), T("web.admin.common.libraryEditor.metadata.localMetadataDescription"), local);
            card.Margin = new(0, 0, 0, 12);
            stack.Children.Add(card);
        }
        if (!state.HasRemoteProviders)
        {
            var none = Text(T("web.admin.common.libraryEditor.metadata.noRemoteProviders"), 12, true); none.TextAlignment = TextAlignment.Center; none.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(Dashed(none));
            return stack;
        }
        foreach (var level in state.ContentLevels)
        {
            var items = state.RemoteChains.TryGetValue(level, out var remote) ? remote : [];
            var collapsed = _collapsedLevels.Contains(level);
            var section = new StackPanel { Spacing = 4, Margin = new(0, 0, 0, 12) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            head.Children.Add(Icon(collapsed ? "chevron-right" : "chevron-down", 12, Solid(Primary)));
            var title = Text(Core.Localization.AppStrings.Uppercase(P.ContentLevelLabel(level)), 12); title.FontWeight = FontWeights.SemiBold; title.Foreground = Solid(Primary); title.CharacterSpacing = 100;
            head.Children.Add(title);
            var toggle = new Button { Content = head, Padding = new(0, 4, 0, 4), MinHeight = _phone ? 44 : 0, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
            AutomationProperties.SetName(toggle, P.ContentLevelLabel(level));
            AutomationProperties.SetAutomationId(toggle, "LibraryEditorLevel_" + level);
            AutomationProperties.SetItemStatus(toggle, collapsed ? "collapsed" : "expanded");
            toggle.Click += (_, _) => { if (!_collapsedLevels.Remove(level)) _collapsedLevels.Add(level); _renderedSectionKey = null; Render(); };
            section.Children.Add(toggle);
            if (!collapsed)
                for (var index = 0; index < items.Count; index++) section.Children.Add(ProviderRow(state, level, items, index));
            stack.Children.Add(section);
        }
        return stack;
    }

    private Border ProviderRow(AdminLibraryEditorState state, string level, IReadOnlyList<AdminProviderChainItem> items, int index)
    {
        var item = items[index];
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var check = new CheckBox { IsChecked = item.Enabled, IsEnabled = state.CanSave, MinHeight = _phone ? 44 : 28, Content = Mono(item.ProviderSlug, 12, !item.Enabled) };
        AutomationProperties.SetName(check, item.ProviderSlug);
        AutomationProperties.SetAutomationId(check, "LibraryEditorProvider_" + level + "_" + index);
        var position = index;
        check.Click += (_, _) => Session.ToggleProvider(level, position);
        grid.Children.Add(check);
        var moves = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var size = _phone ? 44 : 20;
        var up = IconButton("arrow-up", Core.Localization.AppStrings.Get("web.settings.interface.moveUp", new Dictionary<string, object?> { ["label"] = item.ProviderSlug }, "Move " + item.ProviderSlug + " up"),
            "LibraryEditorProviderUp_" + level + "_" + index, size, _phone ? 16 : 10);
        up.IsEnabled = index > 0 && state.CanSave;
        up.Click += (_, _) => { Session.MoveProvider(level, position, -1); FocusAfterMove(level, position - 1, "Up"); };
        var down = IconButton("arrow-down", Core.Localization.AppStrings.Get("web.settings.interface.moveDown", new Dictionary<string, object?> { ["label"] = item.ProviderSlug }, "Move " + item.ProviderSlug + " down"),
            "LibraryEditorProviderDown_" + level + "_" + index, size, _phone ? 16 : 10);
        down.IsEnabled = index < items.Count - 1 && state.CanSave;
        down.Click += (_, _) => { Session.MoveProvider(level, position, 1); FocusAfterMove(level, position + 1, "Down"); };
        moves.Children.Add(up); moves.Children.Add(down);
        Grid.SetColumn(moves, 1); grid.Children.Add(moves);
        var number = Mono((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 10); number.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(number, 2); grid.Children.Add(number);
        return new Border
        {
            Child = grid, Padding = new(10, _phone ? 2 : 6, 10, _phone ? 2 : 6), CornerRadius = new(6), BorderThickness = new(1),
            BorderBrush = item.Enabled ? Brush("BorderBrush") : Tint("BorderBrush", 0.5), Background = item.Enabled ? Tint("MutedBrush", 1) : Tint("MutedBrush", 0.3),
        };
    }

    /// <summary>Keeps keyboard focus on the moved provider's arrow (the row rebuilds after a move).</summary>
    private void FocusAfterMove(string level, int index, string direction) =>
        Ui(priority: Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, action: () =>
        {
            var target = AdminLibrariesPage.FindControl(_sectionContent, "LibraryEditorProvider" + direction + "_" + level + "_" + index)
                ?? AdminLibrariesPage.FindControl(_sectionContent, "LibraryEditorProvider_" + level + "_" + index);
            target?.Focus(FocusState.Keyboard);
        });

    // ── Advanced ─────────────────────────────────────────────────────────

    private bool ChapterSupported(AdminLibraryEditorState state) =>
        state.Library?.ChapterThumbnailsSupported ?? _controller.State.Libraries.FirstOrDefault()?.ChapterThumbnailsSupported ?? true;

    /// <summary>Null when the server offers no seek previews (the switch is then hidden).</summary>
    private bool? TrickplaySupported(AdminLibraryEditorState state)
    {
        if (state.Library != null) return state.Library.TrickplaySupported;
        if (_controller.State.Libraries.FirstOrDefault() is { } first) return first.TrickplaySupported;
        return _controller.State.Capabilities is { Trickplay: true } capabilities ? capabilities.TrickplaySupported : null;
    }

    private StackPanel Advanced(AdminLibraryEditorState state)
    {
        var stack = new StackPanel { Spacing = 12 };
        TextBlock Unavailable(string key) { var text = Text(T(key), 12); text.Foreground = Solid(Warning); return text; }
        if (state.Support.ChapterThumbnails)
        {
            var supported = ChapterSupported(state);
            var toggle = Switch(T("web.admin.common.libraryEditor.advanced.chapterThumbnails"), "LibraryEditorChapterThumbnails", state.ChapterThumbnailsEnabled);
            toggle.IsEnabled = supported && state.CanSave;
            toggle.Toggled += (_, _) => Session.SetChapterThumbnails(toggle.IsOn);
            stack.Children.Add(SettingCard(T("web.admin.common.libraryEditor.advanced.chapterThumbnails"), T("web.admin.common.libraryEditor.advanced.chapterThumbnailsDescription"), toggle,
                supported ? null : Unavailable("web.admin.common.libraryEditor.advanced.chapterThumbnailsUnavailable")));
        }
        if (state.Support.Trickplay && TrickplaySupported(state) is { } trickplaySupported)
        {
            var toggle = Switch(T("web.admin.common.libraryEditor.advanced.trickplay"), "LibraryEditorTrickplay", state.TrickplayEnabled);
            // An unsupported server still lets an admin turn an enabled switch off.
            toggle.IsEnabled = (trickplaySupported || state.TrickplayEnabled) && state.CanSave;
            toggle.Toggled += (_, _) => Session.SetTrickplay(toggle.IsOn);
            stack.Children.Add(SettingCard(T("web.admin.common.libraryEditor.advanced.trickplay"), T("web.admin.common.libraryEditor.advanced.trickplayDescription"), toggle,
                trickplaySupported ? null : Unavailable("web.admin.common.libraryEditor.advanced.trickplayUnavailable")));
        }
        if (state.Support.IntroDetection)
        {
            var (title, description) = P.MarkerDetectionCopy(state.Support);
            var toggle = Switch(title, "LibraryEditorIntroDetection", state.IntroDetectionEnabled);
            toggle.IsEnabled = state.CanSave;
            toggle.Toggled += (_, _) => Session.SetIntroDetection(toggle.IsOn);
            stack.Children.Add(SettingCard(title, description, toggle));
        }
        return stack;
    }

    // FrameworkElement.Parent is not reliable for code-built trees, so each reused control's host is tracked.
    private readonly Dictionary<FrameworkElement, Panel> _hosts = new(ReferenceEqualityComparer.Instance);

    /// <summary>Moves a control that stays mounted across section rebuilds (keeps focus and caret) into its new host.</summary>
    private void Reparent(FrameworkElement element, Panel target)
    {
        if (_hosts.TryGetValue(element, out var host) && !ReferenceEquals(host, target)) host.Children.Remove(element);
        else if (element.Parent is Panel parent && !ReferenceEquals(parent, target)) parent.Children.Remove(element);
        if (!target.Children.Contains(element)) target.Children.Add(element);
        _hosts[element] = target;
    }
}
