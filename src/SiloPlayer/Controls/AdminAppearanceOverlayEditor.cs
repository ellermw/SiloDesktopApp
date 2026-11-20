using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using T = SiloPlayer.Core.Services.Admin.AdminAppearanceOverlayText;

namespace SiloPlayer.Controls;

/// <summary>
/// A retained control tree for server-default overlay staging. The owner saves
/// Changed documents through the admin batch; this control never saves a profile.
/// </summary>
internal sealed partial class AdminAppearanceOverlayEditor : StackPanel
{
    private readonly CardOverlayService _service;
    private CardOverlayPrefs _prefs = CardOverlayService.ParseEditorDocument(BuiltInDefaults);
    private string _document = BuiltInDefaults;
    private bool _binding = true;
    private bool _enabled;
    private bool _editable;
    private int _revision;
    private bool _uploadsAvailable;
    private bool? _imageLibraryEditable;
    private ApiIdentityContext? _owner;
    private IReadOnlySet<string> _shownRatings = new HashSet<string>(StringComparer.Ordinal);
    private IReadOnlyList<OverlayLibraryImage> _images = [];
    private readonly StackPanel _defaults = new() { Spacing = 18 };
    private readonly Dictionary<string, OverlayRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StackPanel> _categories = new(StringComparer.Ordinal);
    private readonly Grid _poster = new() { Width = 185, Height = 277.5, Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 38, 49, 67)) };
    private readonly TextBlock _placeholder = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = .4, FontSize = 12 };
    private readonly CardWatchStatusIndicator _watchStatus = new();
    private readonly ProgressBar _download = new() { Minimum = 0, Maximum = 100, Value = 43, Height = 4, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
    private CardBadgeHosts _preview = null!;
    private readonly List<StackPanel> _previewPanels = [];
    private readonly ComboBox _variant = new() { MinWidth = 160 };

    internal static string BuiltInDefaults => CardOverlayService.BuiltInEditorDefaults;
    internal static string DefaultsForRevision(int manifestRevision) => CardOverlayService.BuiltInEditorDefaultsForRevision(manifestRevision);
    public event Action<string>? Changed;
    public event Action? UploadRequested;
    public event Action<string>? DeleteRequested;

    public AdminAppearanceOverlayEditor(CardOverlayService service)
    {
        _service = service;
        Spacing = 20;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, "AdminAppearanceOverlayEditor");
        Children.Add(_defaults);
        _defaults.Children.Add(Heading(T.T("web.admin.settings.appearance.overlays.style.label")));
        _defaults.Children.Add(Description(T.T("web.admin.settings.appearance.overlays.style.description")));
        BuildLook();
        BuildPreview();
        foreach (var category in new[] { "tech", "ratings", "metadata", "ribbons" })
        {
            var group = new StackPanel { Spacing = 8 };
            group.Children.Add(Heading(T.T("web.settings.overlays.category." + category + ".title")));
            _categories[category] = group;
            foreach (var definition in OverlayRegistry.All.Where(definition => Category(definition.Id) == category))
            {
                var row = new OverlayRow(this, definition);
                _rows.Add(definition.Id, row);
                group.Children.Add(row.Root);
            }
            _defaults.Children.Add(group);
        }
        BuildLibrary();
        Unloaded += (_, _) =>
        {
            if (IsLoaded) return;
            _editable = false;
            _imageLibraryEditable = false;
            foreach (var row in _rows.Values) row.Retire();
        };
        _binding = false;
        Synchronize();
    }

    /// <summary>Call with the parent's lease-checked overlay-config and ratings-capability readback.</summary>
    public void SetCapabilities(bool imageUploadsAvailable, IReadOnlySet<string> shownRatingSources)
    {
        _uploadsAvailable = imageUploadsAvailable;
        _shownRatings = shownRatingSources.ToHashSet(StringComparer.Ordinal);
        Synchronize();
    }

    /// <summary>Image operations remain independent of an environment-managed defaults setting.</summary>
    public void SetImageLibraryEditable(bool editable)
    {
        _imageLibraryEditable = editable;
        Synchronize();
    }

    /// <summary>The same responsive poster widths used by the official preview.</summary>
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

    public void Bind(string document, bool enabled, bool editable, int manifestRevision, IReadOnlyList<OverlayLibraryImage> images)
    {
        var owner = _service.CaptureImageOwner();
        if (_owner is { } previous && previous != owner)
        {
            foreach (var row in _rows.Values) row.Retire();
            _shownRatings = new HashSet<string>(StringComparer.Ordinal);
            _uploadsAvailable = false;
            _imageLibraryEditable = null;
        }
        _owner = owner;
        _document = string.IsNullOrWhiteSpace(document) || document == BuiltInDefaults ? DefaultsForRevision(manifestRevision) : document;
        _prefs = CardOverlayService.ParseEditorDocument(_document);
        _enabled = enabled;
        _editable = editable;
        _revision = manifestRevision;
        // The official library is capped at 256. Reject malformed identities and
        // duplicate rows locally; never turn returned URLs into network requests.
        _images = images.Where(image => SiloApiClient.IsOverlayImageRef(image.Ref)).DistinctBy(image => image.Ref).Take(256).ToArray();
        Synchronize();
    }

    private bool CurrentOwner => _owner is { } owner && _service.IsCurrentImageOwner(owner);
    private bool CanEdit => _enabled && _editable && CurrentOwner;
    private bool CanEditLibrary => (_imageLibraryEditable ?? _editable) && CurrentOwner;
    private bool Advanced => _revision >= 25;
    private bool Offered(string id) => CardOverlayService.EditorOffersOverlay(id, _revision, _shownRatings);

    private void Synchronize()
    {
        var wasBinding = _binding;
        _binding = true;
        try
        {
            _defaults.Opacity = _enabled ? 1 : .5;
            _variant.IsEnabled = CanEdit;
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
        var json = CardOverlayService.SerializeEditorDocument(next, _revision);
        var previous = CardOverlayService.SerializeEditorDocument(_prefs, _revision);
        if (json == previous) return;
        _document = json;
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
        var contents = new StackPanel { Spacing = 12 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(Heading(T.T("web.admin.settings.appearance.overlays.preview")));
        foreach (var id in new[] { "movie", "show", "requested" })
            _variant.Items.Add(new ComboBoxItem { Tag = id, Content = T.T("web.common.overlays.previewVariant." + id) });
        _variant.SelectedIndex = 0;
        AutomationProperties.SetName(_variant, T.T("web.common.overlays.previewSample"));
        AutomationProperties.SetAutomationId(_variant, "AppearancePreviewVariant");
        _variant.SelectionChanged += (_, _) => { if (!_binding) UpdatePreview(); };
        Grid.SetColumn(_variant, 1);
        header.Children.Add(_variant);
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
        contents.Children.Add(new Border { Child = _poster, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new(12), BorderThickness = new(1), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DimGray) });
        _defaults.Children.Add(new Border { Child = contents, Padding = new(12), CornerRadius = new(12), BorderThickness = new(1), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DimGray) });
    }

    private void UpdatePreview()
    {
        var variant = (_variant.SelectedItem as ComboBoxItem)?.Tag as string ?? "movie";
        _placeholder.Text = T.T("web.common.overlays.previewPlaceholder." + variant);
        var geometry = CardOverlayGeometry.ForPoster(_poster.Width, _prefs.Preset);
        _watchStatus.Update(variant switch
        {
            "movie" => new UserState { Played = true },
            "show" => new UserState { Played = false, WatchedCount = 5, UnplayedCount = 3 },
            _ => null,
        }, geometry);
        var download = variant == "requested" && _prefs.Items["request_status"].Enabled;
        _download.Visibility = download ? Visibility.Visible : Visibility.Collapsed;
        foreach (var panel in _previewPanels)
        {
            panel.Spacing = geometry.StackGap;
            panel.Margin = new(geometry.EdgeInset, geometry.EdgeInset, geometry.EdgeInset,
                geometry.EdgeInset + (panel.VerticalAlignment == VerticalAlignment.Bottom && download ? CardOverlayGeometry.ProgressBarClearance : 0));
            panel.MaxWidth = _poster.Width - 2 * geometry.EdgeInset;
        }
        if (_revision < 25 && _watchStatus.Visibility == Visibility.Visible)
            _previewPanels[1].Margin = new(geometry.EdgeInset, geometry.EdgeInset + geometry.WatchStatusSize + geometry.StackGap, geometry.EdgeInset, geometry.EdgeInset);
        if (_watchStatus.Visibility == Visibility.Visible)
            _previewPanels[0].MaxWidth = Math.Max(0, _poster.Width - 2 * geometry.EdgeInset - _watchStatus.ReservedWidth - geometry.StackGap);
        var data = Sample(variant);
        var listed = _images.Select(image => image.Ref).ToHashSet(StringComparer.Ordinal);
        var previewItems = _prefs.Items.ToDictionary(pair => pair.Key, pair => pair.Value with
        {
            Enabled = pair.Value.Enabled && Offered(pair.Key),
            Image = listed.Contains(pair.Value.Image ?? "") ? pair.Value.Image : null,
            ValueImages = pair.Value.ValueImages?.Where(image => listed.Contains(image.Value)).ToDictionary(image => image.Key, image => image.Value, StringComparer.Ordinal),
        }, StringComparer.Ordinal);
        _preview.Show(CardBadgeHosts.ForData(data, _service, geometry.Scale, _prefs with { Items = previewItems }));
    }

    private static OverlayData Sample(string variant)
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

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Description(string text) => new() { Text = text, FontSize = 13, Opacity = .7, TextWrapping = TextWrapping.Wrap };

    private static Grid Field(FrameworkElement label, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8, Padding = new(0, 8, 0, 8) };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(label); grid.Children.Add(control);
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
        private readonly Button _image = new() { Content = new FontIcon { Glyph = "\uEB9F", FontSize = 16 }, Padding = new(8) };
        private readonly Flyout _flyout = new();
        private BadgeImageEditor? _images;
        private int _positionRevision = -1;

        internal OverlayRow(AdminAppearanceOverlayEditor editor, OverlayDef definition)
        {
            _editor = editor; Definition = definition;
            var label = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var name = T.Badge(definition.Id, "label");
            label.Children.Add(Heading(name)); label.Children.Add(Description(T.Badge(definition.Id, "description")));
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(_position); controls.Children.Add(_image); controls.Children.Add(_toggle);
            AutomationProperties.SetName(_position, T.T("web.admin.settings.appearance.overlays.position", ("label", name)));
            AutomationProperties.SetName(_toggle, T.T("web.admin.settings.appearance.overlays.show", ("label", name)));
            AutomationProperties.SetName(_image, T.T("web.admin.settings.appearance.overlays.images", ("label", name)));
            AutomationProperties.SetAutomationId(_toggle, "AppearanceOverlayToggle-" + definition.Id);
            AutomationProperties.SetAutomationId(_position, "AppearanceOverlayPosition-" + definition.Id);
            AutomationProperties.SetAutomationId(_image, "AppearanceOverlayImages-" + definition.Id);
            _toggle.Toggled += (_, _) => editor.SetItem(definition.Id, item => item with { Enabled = _toggle.IsOn });
            _position.SelectionChanged += (_, _) =>
            {
                if (editor._binding || !editor._prefs.Items[definition.Id].Enabled || (_position.SelectedItem as ComboBoxItem)?.Tag is not OverlayPosition position) return;
                editor.SetItem(definition.Id, item => item with { Position = position });
            };
            _image.Flyout = _flyout;
            _flyout.Opening += (_, _) =>
            {
                if (_images != null) return;
                _images = new BadgeImageEditor(editor, definition);
                _flyout.Content = new ScrollViewer { Content = _images, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                editor.Synchronize();
            };
            _flyout.Closed += (_, _) =>
            {
                // Like the official popover, a closed image editor releases its
                // choice containers. Ordinary Bind never replaces an open editor.
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
            if (_positionRevision != (editor.Advanced ? 25 : 24))
            {
                _positionRevision = editor.Advanced ? 25 : 24;
                _position.Items.Clear();
                foreach (var position in editor.Advanced
                    ? new[] { OverlayPosition.TopLeft, OverlayPosition.LeftEdge, OverlayPosition.RightEdge, OverlayPosition.BottomLeft, OverlayPosition.BottomRight }
                    : new[] { OverlayPosition.TopLeft, OverlayPosition.TopRight, OverlayPosition.BottomLeft, OverlayPosition.BottomRight })
                    _position.Items.Add(new ComboBoxItem { Tag = position, Content = PositionLabel(position) });
            }
            var display = CardOverlayService.PositionForRevision(config.Position, editor._revision);
            _position.SelectedItem = _position.Items.Cast<ComboBoxItem>().First(item => (OverlayPosition)item.Tag == display);
            _position.IsEnabled = editor.CanEdit && config.Enabled;
            _image.Visibility = editor.Advanced && (editor._images.Count > 0 || config.Image != null || config.ValueImages != null) ? Visibility.Visible : Visibility.Collapsed;
            _image.IsEnabled = editor.CanEdit && config.Enabled;
            _image.Opacity = config.Image != null || config.ValueImages?.Count > 0 ? 1 : .65;
            if (_images != null) { _images.SetEditing(editor.CanEdit && config.Enabled && editor.Advanced); _images.Synchronize(); }
        }

        internal void Retire() { _flyout.Hide(); _images?.ClearPending(); }

        private static string PositionLabel(OverlayPosition position) => position == OverlayPosition.TopRight ? "Top Right" :
            T.T("web.settings.overlays.position." + (position switch
            {
                OverlayPosition.TopLeft => "topLeft", OverlayPosition.LeftEdge => "leftEdge", OverlayPosition.RightEdge => "rightEdge",
                OverlayPosition.BottomLeft => "bottomLeft", _ => "bottomRight",
            }));
    }
}
