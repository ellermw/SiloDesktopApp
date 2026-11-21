using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using T = SiloPlayer.Core.Services.Admin.AdminAppearanceOverlayText;

namespace SiloPlayer.Controls;

/// <summary>BadgeImagesControl and OverlayImageLibrary (official 66b836f82).</summary>
internal sealed partial class AdminAppearanceOverlayEditor
{
    internal const int MaxImageNameLength = 64;
    private readonly StackPanel _library = new() { Spacing = 16 };
    private readonly StackPanel _uploadArea = new() { Spacing = 12 };
    private readonly LogsWrapPanel _uploadRow = new() { Spacing = 12 };
    private readonly LogsWrapPanel _libraryRows = new() { Spacing = 8 };
    private readonly Dictionary<string, (Grid Row, TextBlock Name, Button Delete)> _libraryEntries = new(StringComparer.Ordinal);
    private readonly Button _choose = new();
    private readonly StackPanel _pendingName = new() { Spacing = 4, Visibility = Visibility.Collapsed };
    private readonly TextBox _name = new() { Width = 224, MaxLength = 128 };
    private readonly Button _upload = new();
    private readonly TextBlock _libraryError = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly TextBlock _libraryHelp = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Opacity = .7 };
    private readonly TextBlock _needsStorage = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, Opacity = .7 };
    private bool _pending;

    /// <summary>The badge-image library; the page shows it in its own group while the server supports styles.</summary>
    internal FrameworkElement LibraryPanel => _library;
    /// <summary>The admin asked to pick a file. The page owns the picker and the bytes.</summary>
    public event Action? ChooseImageRequested;
    /// <summary>The admin asked to upload the picked file under this trimmed name.</summary>
    public event Action<string>? UploadRequested;
    /// <summary>The admin asked to delete this library image. The page confirms first.</summary>
    public event Action<string>? DeleteRequested;

    private void BuildLibrary()
    {
        _choose.Content = T.T("web.settings.overlays.library.choose");
        AutomationProperties.SetAutomationId(_choose, "AppearanceOverlayChoose");
        AutomationProperties.SetHelpText(_choose, T.T("web.settings.overlays.library.fileLabel"));
        _choose.Click += (_, _) => { if (!_binding && _inputs.LibraryEditable && _inputs.UploadsAvailable) ChooseImageRequested?.Invoke(); };
        _pendingName.Children.Add(new TextBlock { Text = T.T("web.settings.overlays.library.name"), FontSize = 12, Opacity = .7 });
        _pendingName.Children.Add(_name);
        AutomationProperties.SetName(_name, T.T("web.settings.overlays.library.name"));
        AutomationProperties.SetAutomationId(_name, "AppearanceOverlayImageName");
        _name.TextChanged += (_, _) => SynchronizeUpload();
        _upload.Content = T.T("web.settings.overlays.library.upload");
        _upload.Visibility = Visibility.Collapsed;
        _upload.VerticalAlignment = VerticalAlignment.Bottom;
        AutomationProperties.SetAutomationId(_upload, "AppearanceOverlayUpload");
        _upload.Click += (_, _) =>
        {
            var name = _name.Text.Trim();
            if (_binding || !CanUpload(name)) return;
            UploadRequested?.Invoke(name);
        };
        _uploadRow.Children.Add(_choose); _uploadRow.Children.Add(_pendingName); _uploadRow.Children.Add(_upload);
        _uploadArea.Children.Add(_uploadRow);
        _libraryError.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];
        AutomationProperties.SetAutomationId(_libraryError, "AppearanceOverlayLibraryError");
        AutomationProperties.SetLiveSetting(_libraryError, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _uploadArea.Children.Add(_libraryError);
        _libraryHelp.Text = T.T("web.settings.overlays.library.help");
        _uploadArea.Children.Add(_libraryHelp);
        _needsStorage.Text = T.T("web.settings.overlays.library.needsStorage");
        AutomationProperties.SetAutomationId(_needsStorage, "AppearanceOverlayNeedsStorage");
        _library.Children.Add(_uploadArea); _library.Children.Add(_needsStorage); _library.Children.Add(_libraryRows);
        AutomationProperties.SetAutomationId(_library, "AppearanceOverlayLibrary");
    }

    /// <summary>A file was picked: offer its name, cut to the official 64 characters, for editing.</summary>
    internal void ShowPendingUpload(string name)
    {
        _pending = true;
        _libraryError.Visibility = Visibility.Collapsed;
        _name.Text = new string(name.EnumerateRunes().Take(MaxImageNameLength).SelectMany(rune => rune.ToString()).ToArray());
        SynchronizeLibrary();
    }

    internal void ClearPendingUpload()
    {
        _pending = false;
        _name.Text = "";
        SynchronizeLibrary();
    }

    /// <summary>Shows a picker refusal such as the official "Pick an image of 4 MB or less.", or clears it.</summary>
    internal void ShowLibraryError(string? message)
    {
        _libraryError.Text = message ?? "";
        _libraryError.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
    }

    internal bool HasPendingUpload => _pending;
    internal string PendingName => _name.Text;

    private bool CanUpload(string name) => _pending && _inputs.LibraryEditable && _inputs.UploadsAvailable &&
        name.EnumerateRunes().Count() is > 0 and <= MaxImageNameLength;

    private void SynchronizeUpload() => _upload.IsEnabled = CanUpload(_name.Text.Trim());

    private void SynchronizeLibrary()
    {
        _uploadArea.Visibility = _inputs.UploadsAvailable ? Visibility.Visible : Visibility.Collapsed;
        _needsStorage.Visibility = _inputs.UploadsAvailable ? Visibility.Collapsed : Visibility.Visible;
        if (!_inputs.UploadsAvailable && _pending) { _pending = false; _name.Text = ""; }
        _choose.Content = T.T(_pending ? "web.settings.overlays.library.pickAnother" : "web.settings.overlays.library.choose");
        _choose.IsEnabled = _inputs.LibraryEditable;
        _pendingName.Visibility = _upload.Visibility = _pending ? Visibility.Visible : Visibility.Collapsed;
        _name.IsEnabled = _inputs.LibraryEditable;
        SynchronizeUpload();
        var references = _images.Select(image => image.Ref).ToHashSet(StringComparer.Ordinal);
        foreach (var reference in _libraryEntries.Keys.Where(reference => !references.Contains(reference)).ToArray())
        {
            _libraryRows.Children.Remove(_libraryEntries[reference].Row); _libraryEntries.Remove(reference);
        }
        foreach (var image in _images)
        {
            if (!_libraryEntries.TryGetValue(image.Ref, out var entry))
            {
                var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14 };
                var delete = new Button { Content = new FontIcon { Glyph = "", FontSize = 16 }, Padding = new(8), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) };
                var reference = image.Ref;
                delete.Click += (_, _) =>
                {
                    // The page captures an admin lease and confirms before the API runs.
                    if (!_binding && _inputs.LibraryEditable && _images.Any(current => current.Ref == reference)) DeleteRequested?.Invoke(reference);
                };
                AutomationProperties.SetAutomationId(delete, "AppearanceOverlayDelete-" + reference);
                var card = new Grid { Width = 240, ColumnSpacing = 8, Padding = new(8), CornerRadius = new(8), BorderThickness = new(1), BorderBrush = AdminSettingsUi.Brush("BorderBrush") };
                card.ColumnDefinitions.Add(new() { Width = new(64) });
                card.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                card.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                card.Children.Add(Thumbnail(reference)); card.Children.Add(name); card.Children.Add(delete);
                Grid.SetColumn(name, 1); Grid.SetColumn(delete, 2);
                entry = (card, name, delete);
                _libraryEntries.Add(reference, entry); _libraryRows.Children.Add(entry.Row);
            }
            entry.Name.Text = image.Name;
            entry.Delete.IsEnabled = _inputs.LibraryEditable;
            ToolTipService.SetToolTip(entry.Name, image.Name);
            AutomationProperties.SetName(entry.Delete, T.T("web.settings.overlays.library.deleteLabel", ("name", image.Name)));
        }
        _libraryRows.Visibility = _images.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Library images are drawn through the card renderer, which reads the re-encoded
    // WebP bytes through the overlay service; a listed URL is never fetched as text.
    private Border Thumbnail(string reference) => new()
    {
        Width = 64, Height = 40, Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 38, 49, 67)), CornerRadius = new(6), Padding = new(4),
        Child = OverlayBadgeRenderer.Build("", "resolution", new(true, OverlayPosition.TopLeft)
        { ValueImages = new Dictionary<string, string>(StringComparer.Ordinal) { ["preview"] = reference } }, "minimal", 1.5, valueKey: "preview", images: _service),
    };

    private sealed class ImagePicker : ComboBox
    {
        private readonly AdminAppearanceOverlayEditor _editor;
        private readonly string _none;
        private bool _sync;
        internal ImagePicker(AdminAppearanceOverlayEditor editor, string label, string none, Action<string?> changed)
        {
            _editor = editor; _none = none;
            MinWidth = 150; HorizontalAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(this, label);
            SelectionChanged += (_, _) =>
            {
                if (!_sync && !editor._binding && editor.CanEdit && editor.SupportsStyles && IsEnabled)
                    changed((SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } value ? value : null);
            };
        }

        internal void Synchronize(string? selected)
        {
            _sync = true;
            try
            {
                var wanted = _editor._images.Select(image => (Reference: image.Ref, Name: image.Name)).ToList();
                // A ref the library no longer holds still shows, so it can be cleared.
                if (selected != null && wanted.All(image => image.Reference != selected)) wanted.Add((selected, T.T("web.settings.overlays.images.removed")));
                var current = Items.Cast<ComboBoxItem>().ToDictionary(item => (string)item.Tag, StringComparer.Ordinal);
                var valid = wanted.Select(image => image.Reference).Append("").ToHashSet(StringComparer.Ordinal);
                foreach (var item in Items.Cast<ComboBoxItem>().Where(item => !valid.Contains((string)item.Tag)).ToArray()) Items.Remove(item);
                if (!current.ContainsKey("")) Items.Insert(0, new ComboBoxItem { Tag = "", Content = _none });
                foreach (var (reference, name) in wanted)
                {
                    var listed = _editor._images.Any(image => image.Ref == reference);
                    if (!current.TryGetValue(reference, out var option))
                    {
                        option = new ComboBoxItem { Tag = reference };
                        Items.Add(option);
                    }
                    option.Content = listed ? Row(reference, name) : name;
                }
                SelectedItem = Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == (selected ?? ""));
            }
            finally { _sync = false; }
        }

        private StackPanel Row(string reference, string name)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(_editor.Thumbnail(reference));
            content.Children.Add(new TextBlock { Text = name, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            return content;
        }
    }

    private sealed class BadgeImageEditor : StackPanel
    {
        private readonly AdminAppearanceOverlayEditor _editor;
        private readonly OverlayDef _definition;
        private readonly ImagePicker _icon;
        private readonly StackPanel _values = new() { Spacing = 8 };
        private readonly Dictionary<string, (Grid Root, ImagePicker Picker, Button Remove)> _valueRows = new(StringComparer.Ordinal);
        private readonly StackPanel _addRow = new() { Spacing = 8 };
        private readonly AutoSuggestBox _key = new() { MaxWidth = 280 };
        private readonly ImagePicker _newImage;
        private readonly Button _add = new();
        private string? _newReference;
        private string[] _suggestions = [];
        private bool _editing;

        internal BadgeImageEditor(AdminAppearanceOverlayEditor editor, OverlayDef definition)
        {
            _editor = editor; _definition = definition; Spacing = 10; Width = 300;
            Children.Add(Heading(T.T("web.settings.overlays.images.iconImage")));
            _icon = new ImagePicker(editor, T.T("web.settings.overlays.images.iconImageLabel", ("badge", T.Badge(definition.Id, "label"))),
                T.T("web.settings.overlays.images.builtInIcon"), reference => editor.SetItem(definition.Id, item => item with { Image = reference }));
            AutomationProperties.SetAutomationId(_icon, "AppearanceOverlayIcon-" + definition.Id);
            Children.Add(_icon);
            _newImage = new ImagePicker(editor, T.T("web.settings.overlays.images.newValueImage"), T.T("web.settings.overlays.images.pickImage"), reference => { _newReference = reference; UpdateAdd(); });
            AutomationProperties.SetAutomationId(_newImage, "AppearanceOverlayNewValueImage-" + definition.Id);
            if (!HasValueKey(definition.Id)) return;
            Children.Add(Heading(T.T("web.settings.overlays.images.valueImages")));
            Children.Add(Description(T.T("web.settings.overlays.images.valueImagesHelp")));
            Children.Add(_values); Children.Add(_addRow);
            _key.PlaceholderText = T.T("web.settings.overlays.images.value");
            // 64 Unicode code points occupy at most 128 UTF-16 units. The
            // semantic limit below is in code points, like the server schema.
            _key.TextBoxStyle = new Style(typeof(TextBox));
            _key.TextBoxStyle.Setters.Add(new Setter(TextBox.MaxLengthProperty, 128));
            AutomationProperties.SetName(_key, _key.PlaceholderText);
            AutomationProperties.SetAutomationId(_key, "AppearanceOverlayValueKey-" + definition.Id);
            _key.TextChanged += (_, _) => { UpdateSuggestions(); UpdateAdd(); };
            _key.SuggestionChosen += (_, args) => { _key.Text = args.SelectedItem?.ToString() ?? ""; UpdateAdd(); };
            _add.Content = new FontIcon { Glyph = "", FontSize = 14 };
            AutomationProperties.SetName(_add, T.T("web.settings.overlays.images.addValueImage"));
            AutomationProperties.SetAutomationId(_add, "AppearanceOverlayAddValue-" + definition.Id);
            _add.Click += (_, _) =>
            {
                if (!CanAdd()) return;
                var key = _key.Text.Trim(); var reference = _newReference!;
                Assign(key, reference); ClearPending();
            };
            _addRow.Children.Add(_key); _addRow.Children.Add(_newImage); _addRow.Children.Add(_add);
        }

        private OverlayItemConfig Config => _editor._prefs.Items[_definition.Id];

        internal void SetEditing(bool editable)
        {
            _editing = editable;
            _icon.IsEnabled = _newImage.IsEnabled = _key.IsEnabled = editable;
            foreach (var row in _valueRows.Values) row.Picker.IsEnabled = row.Remove.IsEnabled = editable;
            UpdateAdd();
        }

        internal void Synchronize()
        {
            _icon.Synchronize(Config.Image);
            if (!HasValueKey(_definition.Id)) return;
            var entries = Config.ValueImages ?? new Dictionary<string, string>();
            foreach (var key in _valueRows.Keys.Where(key => !entries.ContainsKey(key)).ToArray())
            { _values.Children.Remove(_valueRows[key].Root); _valueRows.Remove(key); }
            foreach (var (key, reference) in entries.Take(256))
            {
                if (!_valueRows.TryGetValue(key, out var row))
                {
                    var picker = new ImagePicker(_editor, T.T("web.settings.overlays.images.imageFor", ("value", key)), T.T("web.settings.overlays.images.noImage"), value => Assign(key, value));
                    var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Padding = new(6) };
                    AutomationProperties.SetName(remove, T.T("web.settings.overlays.images.removeFor", ("value", key)));
                    remove.Click += (_, _) => Assign(key, null);
                    var contents = new StackPanel { Spacing = 4 };
                    contents.Children.Add(new TextBlock { Text = key, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 }); contents.Children.Add(picker);
                    row = (Field(contents, remove), picker, remove);
                    _valueRows.Add(key, row); _values.Children.Add(row.Root);
                }
                row.Picker.Synchronize(reference);
                row.Picker.IsEnabled = row.Remove.IsEnabled = _editing;
            }
            _newImage.Synchronize(_newReference);
            _addRow.Visibility = entries.Count < 32 ? Visibility.Visible : Visibility.Collapsed;
            UpdateSuggestions(); UpdateAdd();
        }

        private void Assign(string key, string? reference)
        {
            if (!_editing) return;
            _editor.SetItem(_definition.Id, item =>
            {
                var values = item.ValueImages?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
                if (reference == null) values.Remove(key);
                else if (values.ContainsKey(key) || values.Count < 32) values[key] = reference;
                // An emptied map leaves the item (official setValueImages).
                return item with { ValueImages = values.Count > 0 ? values : null };
            });
        }

        private bool CanAdd() => _editing && _editor.CanEdit && _editor.SupportsStyles && Config.Enabled &&
            _key.Text.Trim().EnumerateRunes().Count() is > 0 and <= 64 && _newReference != null &&
            (Config.ValueImages?.Count ?? 0) < 32;

        private void UpdateAdd() => _add.IsEnabled = CanAdd();
        private void UpdateSuggestions()
        {
            var next = Suggestions(_definition.Id).Where(value => Config.ValueImages?.ContainsKey(value) != true &&
                value.Contains(_key.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (_suggestions.SequenceEqual(next)) return;
            _suggestions = next; _key.ItemsSource = next;
        }

        internal void ClearPending() { _key.Text = ""; _newReference = null; _newImage.Synchronize(null); UpdateAdd(); }

        // Official registry: every overlay with getValueKey offers per-value images.
        private static bool HasValueKey(string id) => id is "resolution" or "hdr" or "resolution_hdr" or "resolution_hdr_audio" or "audio" or "audio_channels" or "video_codec" or
            "container" or "aspect_ratio" or "release_type" or "edition" or "content_rating" or "original_language" or "studio" or "network" or "show_status";

        private static string[] Suggestions(string id) => id switch
        {
            "resolution" => ["2160p", "1080p", "720p", "480p"],
            "hdr" => ["DV", "DV HDR10", "DV HDR10+", "HDR10", "HDR10+", "HLG", "HDR"],
            "audio" => ["Atmos", "TrueHD", "DTS-HD", "DTS", "EAC3", "AC3", "AAC", "FLAC"],
            "video_codec" => ["H.265", "H.264", "AV1", "VP9"],
            "release_type" => ["REMUX", "BluRay", "WEB-DL", "WEBRip", "HDTV", "DVD"],
            "show_status" => ["Returning", "Ended", "Cancelled", "Upcoming"],
            _ => [],
        };
    }
}
