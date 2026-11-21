using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using T = SiloPlayer.Core.Services.Admin.AdminAppearanceOverlayText;

namespace SiloPlayer.Controls;

/// <summary>What the Appearance page binds into the overlay editor; never a write.</summary>
internal sealed record AdminAppearanceEditorInputs(
    string Document,
    bool OverlaysEnabled,
    bool Editable,
    bool SupportsStyles,
    IReadOnlyList<OverlayLibraryImage> Images,
    bool UploadsAvailable,
    IReadOnlySet<string> ShownRatingSources,
    bool LibraryEditable);

/// <summary>
/// The server-default card overlay editor of official AppearanceSettings.tsx (Silo
/// Server 66b836f82): badge style, the live sample preview and every offered
/// overlay row with its position, images and switch. A retained control tree:
/// <see cref="Bind"/> only synchronizes values and never raises <see cref="Changed"/>.
/// The owner stages Changed documents in its settings batch; this control never
/// saves anything and never touches the viewer's profile.
/// </summary>
internal sealed partial class AdminAppearanceOverlayEditor : StackPanel
{
    private static readonly OverlayPosition[] Positions =
        [OverlayPosition.TopLeft, OverlayPosition.LeftEdge, OverlayPosition.RightEdge, OverlayPosition.BottomLeft, OverlayPosition.BottomRight];
    private static readonly string[] Variants = ["movie", "show", "requested"];

    private readonly CardOverlayService _service;
    private CardOverlayPrefs _prefs = CardOverlayService.ParseEditorDocument(BuiltInDefaults);
    private bool _binding = true;
    private AdminAppearanceEditorInputs _inputs = new(BuiltInDefaults, true, false, false, [], false, new HashSet<string>(), false);
    private IReadOnlyList<OverlayLibraryImage> _images = [];
    private readonly StackPanel _look = new() { Spacing = 8 };
    private readonly Border _previewFrame = new() { Padding = new(12), CornerRadius = new(12), BorderThickness = new(1) };
    private readonly StackPanel _categoriesHost = new() { Spacing = 18 };
    private readonly Dictionary<string, OverlayRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StackPanel> _categories = new(StringComparer.Ordinal);
    private readonly Grid _poster = new() { Width = 160, Height = 240 };
    private readonly TextBlock _placeholder = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, CharacterSpacing = 100 };
    private readonly CardWatchStatusIndicator _watchStatus = new();
    private readonly ProgressBar _download = new() { Minimum = 0, Maximum = 100, Value = 43, Height = 4, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
    private readonly List<StackPanel> _previewPanels = [];
    private readonly Dictionary<string, ToggleButton> _variantButtons = new(StringComparer.Ordinal);
    private CardBadgeHosts _preview = null!;
    private string _variant = "movie";

    /// <summary>The official BUILT_IN_OVERLAY_DEFAULTS.</summary>
    internal static string BuiltInDefaults => CardOverlayService.BuiltInEditorDefaults;
    /// <summary>Raised with the staged defaults.card_overlays text, only for an edit the admin made.</summary>
    public event Action<string>? Changed;

    public AdminAppearanceOverlayEditor(CardOverlayService service)
    {
        _service = service;
        Spacing = 16;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, "AdminAppearanceOverlayEditor");
        _look.Children.Add(Heading(T.T("web.admin.settings.appearance.overlays.style.label")));
        _look.Children.Add(Description(T.T("web.admin.settings.appearance.overlays.style.description")));
        BuildLook();
        Children.Add(_look);
        BuildPreview();
        Children.Add(_previewFrame);
        foreach (var category in new[] { "tech", "ratings", "metadata", "ribbons" })
        {
            var group = new StackPanel { Spacing = 4 };
            group.Children.Add(Subheading(T.T("web.settings.overlays.category." + category + ".title")));
            _categories[category] = group;
            foreach (var definition in OverlayRegistry.All.Where(definition => Category(definition.Id) == category))
            {
                var row = new OverlayRow(this, definition);
                _rows.Add(definition.Id, row);
                group.Children.Add(row.Root);
            }
            _categoriesHost.Children.Add(group);
        }
        Children.Add(_categoriesHost);
        BuildLibrary();
        Unloaded += (_, _) =>
        {
            if (IsLoaded) return;
            foreach (var row in _rows.Values) row.Retire();
            ClearPendingUpload();
        };
        _binding = false;
        Synchronize();
    }

    /// <summary>Synchronizes the retained controls to the page state. Never raises Changed.</summary>
    public void Bind(AdminAppearanceEditorInputs inputs)
    {
        _inputs = inputs;
        _prefs = CardOverlayService.ParseEditorDocument(string.IsNullOrWhiteSpace(inputs.Document) ? BuiltInDefaults : inputs.Document);
        // The official library holds at most 256. Malformed identities and duplicate
        // rows are dropped locally; listed URLs are never fetched from their text.
        _images = inputs.Images.Where(image => SiloApiClient.IsOverlayImageRef(image.Ref)).DistinctBy(image => image.Ref).Take(256).ToArray();
        Synchronize();
    }

    /// <summary>The preview draws at the width a home-row card draws at for the admin's own poster size.</summary>
    public void SetPreviewPresentation(string posterSize, double viewportWidth)
    {
        var (small, medium, large) = posterSize switch
        {
            "compact" => (120d, 140d, 160d), "large" => (170d, 195d, 220d), _ => (140d, 160d, 185d),
        };
        var width = viewportWidth >= 1024 ? large : viewportWidth >= 640 ? medium : small;
        if (_poster.Width == width) return;
        _poster.Width = width; _poster.Height = width * 1.5;
        UpdatePreview();
    }

    internal string Variant => _variant;
    private bool CanEdit => _inputs.Editable && _inputs.OverlaysEnabled;
    private bool SupportsStyles => _inputs.SupportsStyles;
    private bool Offered(string id) => CardOverlayService.EditorOffersOverlay(id, _inputs.ShownRatingSources);

    private void Synchronize()
    {
        var wasBinding = _binding;
        _binding = true;
        try
        {
            // The official page makes the style, preview and rows inert at half opacity
            // while badges are off; the badge-image library stays usable.
            var inert = !_inputs.OverlaysEnabled;
            foreach (var part in new FrameworkElement[] { _look, _previewFrame, _categoriesHost })
            {
                part.Opacity = inert ? .5 : 1;
                part.IsHitTestVisible = !inert;
            }
            foreach (var (variant, button) in _variantButtons)
            {
                button.IsChecked = variant == _variant;
                button.IsEnabled = !inert;
            }
            SynchronizeLook();
            foreach (var row in _rows.Values) row.Synchronize();
            foreach (var (category, panel) in _categories)
                panel.Visibility = _rows.Values.Any(row => Category(row.Definition.Id) == category && Offered(row.Definition.Id)) ? Visibility.Visible : Visibility.Collapsed;
            SynchronizeLibrary();
            UpdatePreview();
        }
        finally { _binding = wasBinding; }
    }

    private void Stage(CardOverlayPrefs next)
    {
        if (_binding || !CanEdit) return;
        var json = CardOverlayService.SerializeEditorDocument(next);
        if (json == CardOverlayService.SerializeEditorDocument(_prefs)) return;
        _prefs = CardOverlayService.ParseEditorDocument(json);
        Synchronize();
        Changed?.Invoke(json);
    }

    private void SetItem(string id, Func<OverlayItemConfig, OverlayItemConfig> update)
    {
        if (_binding || !CanEdit || !Offered(id)) return;
        var items = new Dictionary<string, OverlayItemConfig>(_prefs.Items, StringComparer.Ordinal);
        items[id] = update(items[id]);
        Stage(_prefs with { Items = items });
    }

    private void BuildPreview()
    {
        _previewFrame.BorderBrush = AdminSettingsUi.Brush("BorderBrush");
        var contents = new StackPanel { Spacing = 12 };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new TextBlock { Text = T.T("web.admin.settings.appearance.overlays.preview").ToUpperInvariant(), FontSize = 12, Opacity = .7, CharacterSpacing = 60, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(title);
        var toggle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        AutomationProperties.SetName(toggle, T.T("web.common.overlays.previewSample"));
        foreach (var variant in Variants)
        {
            var button = new ToggleButton { Content = T.T("web.common.overlays.previewVariant." + variant), CornerRadius = new(14), Padding = new(12, 4, 12, 4), FontSize = 12 };
            AutomationProperties.SetAutomationId(button, "AppearancePreviewVariant-" + variant);
            TintWhenChecked(button);
            // Local view state only, never staged: the sample the preview stands in for.
            OnToggle(button, () =>
            {
                _variant = variant;
                UpdatePreview();
            });
            _variantButtons[variant] = button;
            toggle.Children.Add(button);
        }
        Grid.SetColumn(toggle, 1);
        header.Children.Add(toggle);
        contents.Children.Add(header);
        _poster.Children.Add(_placeholder);
        StackPanel Host(HorizontalAlignment horizontal, VerticalAlignment vertical)
        {
            var panel = new StackPanel { HorizontalAlignment = horizontal, VerticalAlignment = vertical, IsHitTestVisible = false };
            _previewPanels.Add(panel); _poster.Children.Add(panel); return panel;
        }
        _preview = new(Host(HorizontalAlignment.Left, VerticalAlignment.Top), Host(HorizontalAlignment.Right, VerticalAlignment.Top),
            Host(HorizontalAlignment.Left, VerticalAlignment.Bottom), Host(HorizontalAlignment.Right, VerticalAlignment.Bottom),
            Host(HorizontalAlignment.Left, VerticalAlignment.Center), Host(HorizontalAlignment.Right, VerticalAlignment.Center));
        _poster.Children.Add(_download);
        _poster.Children.Add(_watchStatus);
        AutomationProperties.SetAutomationId(_poster, "AppearanceOverlayPreview");
        contents.Children.Add(new Border
        {
            Child = _poster, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new(12), BorderThickness = new(1),
            BorderBrush = AdminSettingsUi.Brush("BorderBrush"), Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .12),
        });
        _previewFrame.Child = contents;
    }

    private void UpdatePreview()
    {
        _placeholder.Text = T.T("web.common.overlays.previewPlaceholder." + _variant).ToUpperInvariant();
        _placeholder.Opacity = .3;
        var geometry = CardOverlayGeometry.ForPoster(_poster.Width, _prefs.Preset);
        _watchStatus.Update(_variant switch
        {
            "movie" => new UserState { Played = true },
            "show" => new UserState { Played = false, WatchedCount = 5, UnplayedCount = 3 },
            _ => null,
        }, geometry);
        // requestDownloadBarPercent: the bar belongs to an enabled request_status badge.
        var download = _variant == "requested" && _prefs.Items["request_status"].Enabled;
        _download.Visibility = download ? Visibility.Visible : Visibility.Collapsed;
        foreach (var panel in _previewPanels)
        {
            panel.Spacing = geometry.StackGap;
            panel.Margin = new(geometry.EdgeInset, geometry.EdgeInset, geometry.EdgeInset,
                geometry.EdgeInset + (panel.VerticalAlignment == VerticalAlignment.Bottom && download ? CardOverlayGeometry.ProgressBarClearance : 0));
            panel.MaxWidth = _poster.Width - 2 * geometry.EdgeInset;
        }
        if (_watchStatus.Visibility == Visibility.Visible)
            _previewPanels[0].MaxWidth = Math.Max(0, _poster.Width - 2 * geometry.EdgeInset - _watchStatus.ReservedWidth - geometry.StackGap);
        var listed = _images.Select(image => image.Ref).ToHashSet(StringComparer.Ordinal);
        // Cards drop image refs the library no longer lists (withoutUnlistedImages).
        var previewItems = _prefs.Items.ToDictionary(pair => pair.Key, pair => pair.Value with
        {
            Enabled = pair.Value.Enabled && Offered(pair.Key),
            Image = listed.Contains(pair.Value.Image ?? "") ? pair.Value.Image : null,
            ValueImages = pair.Value.ValueImages?.Where(image => listed.Contains(image.Value)).ToDictionary(image => image.Key, image => image.Value, StringComparer.Ordinal),
        }, StringComparer.Ordinal);
        _preview.Show(CardBadgeHosts.ForData(Sample(_variant), _service, geometry.Scale, _prefs with { Items = previewItems }));
    }

    /// <summary>The official OVERLAY_PREVIEW_SAMPLES (lib/overlays/sample-data.ts).</summary>
    internal static OverlayData Sample(string variant)
    {
        if (variant == "requested") return new() { RatingTmdb = 8.1, ContentRating = "PG-13", Year = 2026,
            RequestStatus = T.T("web.common.overlays.sampleDownloading", ("percent", 43)) };
        var show = variant == "show";
        return new()
        {
            Resolution = show ? "1080p" : "2160p", Hdr = show ? "HDR10" : "DV HDR10", Audio = "Atmos", AudioChannels = "7.1", VideoCodec = "H.265",
            Container = "MKV", AspectRatio = "2.39:1", ReleaseType = show ? null : "REMUX", Edition = show ? null : "Extended", MultiAudio = true, MultiSub = true,
            RatingImdb = 8.7, RatingTmdb = 8.5, RatingRtCritic = 96, RatingRtAudience = 92, ContentRating = "PG-13", AdvisoryAge = 13,
            Year = 2024, Runtime = show ? 55 : 148, OriginalLanguage = "EN", Studio = show ? null : "A24", Network = show ? "HBO" : null,
            ShowStatus = show ? "returning" : null, AvailabilityHave = show ? 2 : null, AvailabilityTotal = show ? 3 : null,
        };
    }

    private static string Category(string id) => id switch
    {
        "rating_imdb" or "rating_tmdb" or "rating_rt" or "rating_rt_audience" or "content_rating" or "advisory_age" => "ratings",
        "year" or "runtime" or "original_language" or "studio" or "network" => "metadata",
        "show_status" or "request_status" or "availability" => "ribbons",
        _ => "tech",
    };

    /// <summary>A toggle used as one choice of a group: any user toggle runs the choice, then the group re-selects from state.</summary>
    private void OnToggle(ToggleButton button, Action choose)
    {
        void Handler(object sender, RoutedEventArgs args)
        {
            if (_binding) return;
            choose();
            Synchronize();
        }
        button.Checked += Handler;
        button.Unchecked += Handler;
    }

    /// <summary>The official selected card: a primary border over a faint primary tint, text unchanged.</summary>
    internal static void TintWhenChecked(ToggleButton button)
    {
        var tint = AdminMonitorUi.Solid(AdminMonitorUi.Primary, .1);
        var border = AdminMonitorUi.Solid(AdminMonitorUi.Primary);
        foreach (var key in new[] { "ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed" }) button.Resources[key] = tint;
        foreach (var key in new[] { "ToggleButtonBorderBrushChecked", "ToggleButtonBorderBrushCheckedPointerOver", "ToggleButtonBorderBrushCheckedPressed" }) button.Resources[key] = border;
        foreach (var key in new[] { "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed" }) button.Resources[key] = AdminSettingsUi.Brush("PrimaryTextBrush");
        button.BorderThickness = new(1);
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Description(string text) => new() { Text = text, FontSize = 13, Opacity = .7, TextWrapping = TextWrapping.Wrap };
    private static FrameworkElement Subheading(string text) => AdminSettingsUi.Subheading(text);

    private static Grid Field(FrameworkElement label, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8, Padding = new(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(label); grid.Children.Add(control);
        Grid.SetColumn(control, 1);
        grid.SizeChanged += (_, _) =>
        {
            var stacked = grid.ActualWidth < 620;
            Grid.SetColumn(control, stacked ? 0 : 1); Grid.SetRow(control, stacked ? 1 : 0);
            Grid.SetColumnSpan(control, stacked ? 2 : 1); Grid.SetColumnSpan(label, stacked ? 2 : 1);
            control.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        };
        return grid;
    }

    private sealed class OverlayRow
    {
        private readonly AdminAppearanceOverlayEditor _editor;
        internal OverlayDef Definition { get; }
        internal Grid Root { get; }
        private readonly ToggleSwitch _toggle = new() { OnContent = "", OffContent = "", MinWidth = 0 };
        private readonly ComboBox _position = new() { Width = 140 };
        private readonly Button _image = new() { Content = new FontIcon { Glyph = "", FontSize = 16 }, Padding = new(8), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) };
        private readonly Flyout _flyout = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        private BadgeImageEditor? _images;

        internal OverlayRow(AdminAppearanceOverlayEditor editor, OverlayDef definition)
        {
            _editor = editor; Definition = definition;
            var label = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var name = T.Badge(definition.Id, "label");
            label.Children.Add(Heading(name)); label.Children.Add(Description(T.Badge(definition.Id, "description")));
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(_position); controls.Children.Add(_image); controls.Children.Add(_toggle);
            foreach (var position in Positions)
                _position.Items.Add(new ComboBoxItem { Tag = position, Content = PositionLabel(position) });
            AutomationProperties.SetName(_position, T.T("web.admin.settings.appearance.overlays.position", ("label", name)));
            AutomationProperties.SetName(_toggle, T.T("web.admin.settings.appearance.overlays.show", ("label", name)));
            AutomationProperties.SetName(_image, T.T("web.admin.settings.appearance.overlays.images", ("label", name)));
            AutomationProperties.SetAutomationId(_toggle, "AppearanceOverlayToggle-" + definition.Id);
            AutomationProperties.SetAutomationId(_position, "AppearanceOverlayPosition-" + definition.Id);
            AutomationProperties.SetAutomationId(_image, "AppearanceOverlayImages-" + definition.Id);
            _toggle.Toggled += (_, _) =>
            {
                if (editor._binding || _toggle.IsOn == editor._prefs.Items[definition.Id].Enabled) return;
                editor.SetItem(definition.Id, item => item with { Enabled = _toggle.IsOn });
            };
            _position.SelectionChanged += (_, _) =>
            {
                if (editor._binding || !editor._prefs.Items[definition.Id].Enabled || (_position.SelectedItem as ComboBoxItem)?.Tag is not OverlayPosition position) return;
                if (position == Display(editor._prefs.Items[definition.Id].Position)) return;
                editor.SetItem(definition.Id, item => item with { Position = position });
            };
            _image.Flyout = _flyout;
            _flyout.Opening += (_, _) =>
            {
                if (_images != null) return;
                _images = new BadgeImageEditor(editor, definition);
                _flyout.Content = new ScrollViewer { Content = _images, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                Synchronize();
            };
            _flyout.Closed += (_, _) =>
            {
                // Like the official popover, a closed image editor releases its
                // controls and any unfinished new value. Bind never replaces an open one.
                _flyout.Content = null;
                _images = null;
            };
            Root = Field(label, controls);
        }

        internal void Synchronize()
        {
            var editor = _editor;
            var config = editor._prefs.Items[Definition.Id];
            Root.Visibility = editor.Offered(Definition.Id) ? Visibility.Visible : Visibility.Collapsed;
            _toggle.IsOn = config.Enabled;
            _toggle.IsEnabled = editor.CanEdit;
            var display = Display(config.Position);
            _position.SelectedItem = _position.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (OverlayPosition)item.Tag == display);
            _position.IsEnabled = editor.CanEdit && config.Enabled;
            var assigned = config.Image != null || config.ValueImages?.Count > 0;
            _image.Visibility = editor.SupportsStyles && (editor._images.Count > 0 || assigned) ? Visibility.Visible : Visibility.Collapsed;
            _image.IsEnabled = editor.CanEdit && config.Enabled;
            _image.Foreground = assigned ? AdminSettingsUi.Accent : AdminMonitorUi.Solid(AdminMonitorUi.Muted);
            if (_image.Visibility != Visibility.Visible || !config.Enabled) _flyout.Hide();
            if (_images != null) { _images.SetEditing(editor.CanEdit && config.Enabled && editor.SupportsStyles); _images.Synchronize(); }
        }

        internal void Retire() { _flyout.Hide(); _images?.ClearPending(); }

        // A stored top-right badge sits on the right-edge rail (official parsePosition).
        private static OverlayPosition Display(OverlayPosition position) => position == OverlayPosition.TopRight ? OverlayPosition.RightEdge : position;

        private static string PositionLabel(OverlayPosition position) => T.T("web.settings.overlays.position." + (position switch
        {
            OverlayPosition.TopLeft => "topLeft", OverlayPosition.LeftEdge => "leftEdge", OverlayPosition.RightEdge => "rightEdge",
            OverlayPosition.BottomLeft => "bottomLeft", _ => "bottomRight",
        }));
    }
}
