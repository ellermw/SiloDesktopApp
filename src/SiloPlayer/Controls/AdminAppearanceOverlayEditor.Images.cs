using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using T = SiloPlayer.Core.Services.Admin.AdminAppearanceOverlayText;

namespace SiloPlayer.Controls;

internal sealed partial class AdminAppearanceOverlayEditor
{
    private readonly StackPanel _library = new() { Spacing = 12 };
    private readonly LogsWrapPanel _libraryRows = new() { Spacing = 8 };
    private readonly Dictionary<string, (Grid Row, TextBlock Name, Button Delete)> _libraryEntries = new(StringComparer.Ordinal);
    private readonly Button _upload = new();
    private readonly TextBlock _libraryHelp = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Opacity = .7 };

    private void BuildLibrary()
    {
        _library.Children.Add(Heading(T.T("web.admin.settings.appearance.badgeImages.group")));
        _upload.Content = T.T("web.settings.overlays.library.choose");
        _upload.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(_upload, "AppearanceOverlayUpload");
        _upload.Click += (_, _) => { if (!_binding && CanEditLibrary && Advanced && _uploadsAvailable) UploadRequested?.Invoke(); };
        _library.Children.Add(_upload); _library.Children.Add(_libraryHelp); _library.Children.Add(_libraryRows);
        Children.Add(_library);
    }

    private void SynchronizeLibrary()
    {
        _library.Visibility = Advanced ? Visibility.Visible : Visibility.Collapsed;
        _upload.IsEnabled = CanEditLibrary;
        _upload.Visibility = _uploadsAvailable ? Visibility.Visible : Visibility.Collapsed;
        _libraryHelp.Text = T.T("web.settings.overlays.library." + (_uploadsAvailable ? "help" : "needsStorage"));
        var references = _images.Select(image => image.Ref).ToHashSet(StringComparer.Ordinal);
        foreach (var reference in _libraryEntries.Keys.Where(reference => !references.Contains(reference)).ToArray())
        {
            _libraryRows.Children.Remove(_libraryEntries[reference].Row); _libraryEntries.Remove(reference);
        }
        foreach (var image in _images)
        {
            if (!_libraryEntries.TryGetValue(image.Ref, out var entry))
            {
                var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var delete = new Button { Content = new FontIcon { Glyph = "\uE74D", FontSize = 16 }, Padding = new(8) };
                var reference = image.Ref;
                delete.Click += (_, _) =>
                {
                    // The owning page captures an admin lease and confirms before
                    // invoking the API. This event carries intent, never a write.
                    if (!_binding && CanEditLibrary && Advanced && _images.Any(current => current.Ref == reference)) DeleteRequested?.Invoke(reference);
                };
                AutomationProperties.SetAutomationId(delete, "AppearanceOverlayDelete-" + reference);
                var card = new Grid { Width = 260, ColumnSpacing = 8, Padding = new(8) };
                card.ColumnDefinitions.Add(new() { Width = new(64) });
                card.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                card.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                card.Children.Add(Thumbnail(reference)); card.Children.Add(name); card.Children.Add(delete);
                Grid.SetColumn(name, 1); Grid.SetColumn(delete, 2);
                entry = (card, name, delete);
                _libraryEntries.Add(reference, entry); _libraryRows.Children.Add(entry.Row);
            }
            entry.Name.Text = image.Name;
            entry.Delete.IsEnabled = CanEditLibrary;
            ToolTipService.SetToolTip(entry.Name, image.Name);
            AutomationProperties.SetName(entry.Delete, T.T("web.settings.overlays.library.deleteLabel", ("name", image.Name)));
        }
    }

    private Border Thumbnail(string reference) => new()
    {
        Width = 64, Height = 40, Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 38, 49, 67)), CornerRadius = new(6),
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
            SelectionChanged += (_, args) =>
            {
                foreach (var previous in args.RemovedItems.OfType<ComboBoxItem>()) ReleasePreview(previous);
                if (SelectedItem is ComboBoxItem current) ShowPreview(current);
                if (!_sync && !editor._binding && editor.CanEdit && editor.Advanced && IsEnabled)
                    changed((SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } value ? value : null);
            };
        }

        internal void Synchronize(string? selected)
        {
            _sync = true;
            try
            {
                var wanted = _editor._images.Select(image => (Reference: image.Ref, Name: image.Name)).ToList();
                if (selected != null && wanted.All(image => image.Reference != selected)) wanted.Add((selected, T.T("web.settings.overlays.images.removed")));
                var current = Items.Cast<ComboBoxItem>().ToDictionary(item => (string)item.Tag, StringComparer.Ordinal);
                var valid = wanted.Select(image => image.Reference).Append("").ToHashSet(StringComparer.Ordinal);
                foreach (var item in Items.Cast<ComboBoxItem>().Where(item => !valid.Contains((string)item.Tag)).ToArray()) Items.Remove(item);
                if (!current.ContainsKey("")) Items.Insert(0, new ComboBoxItem { Tag = "", Content = _none });
                foreach (var (reference, name) in wanted)
                {
                    if (current.TryGetValue(reference, out var existing))
                    {
                        if (!_editor._images.Any(image => image.Ref == reference)) existing.Content = name;
                        else if (existing.Content is StackPanel row && row.Children.LastOrDefault() is TextBlock text) text.Text = name;
                        else existing.Content = name;
                        continue;
                    }
                    var option = new ComboBoxItem { Tag = reference, Content = name };
                    option.Loaded += (_, _) => ShowPreview(option);
                    option.Unloaded += (_, _) => ReleasePreview(option);
                    Items.Add(option);
                }
                SelectedItem = Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == (selected ?? ""));
                if (SelectedItem is ComboBoxItem currentOption) ShowPreview(currentOption);
            }
            finally { _sync = false; }
        }

        private void ShowPreview(ComboBoxItem option)
        {
            if (option.Content is not string name || option.Tag is not string reference ||
                !_editor._images.Any(image => image.Ref == reference)) return;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(_editor.Thumbnail(reference));
            content.Children.Add(new TextBlock { Text = name, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            option.Content = content;
        }

        private void ReleasePreview(ComboBoxItem option)
        {
            if (option.IsLoaded || ReferenceEquals(option, SelectedItem)) return;
            if (option.Content is StackPanel content && content.Children.LastOrDefault() is TextBlock text) option.Content = text.Text;
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
            _add.Content = T.T("web.settings.overlays.images.addValueImage");
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
                    var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, Padding = new(6) };
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
                return item with { ValueImages = values };
            });
        }

        private bool CanAdd() => _editing && _editor.CanEdit && _editor.Advanced && Config.Enabled &&
            _key.Text.Trim().EnumerateRunes().Count() is > 0 and <= 64 && _newReference != null &&
            _editor._images.Any(image => image.Ref == _newReference) && (Config.ValueImages?.Count ?? 0) < 32;

        private void UpdateAdd() => _add.IsEnabled = CanAdd();
        private void UpdateSuggestions()
        {
            var next = Suggestions(_definition.Id).Where(value => Config.ValueImages?.ContainsKey(value) != true &&
                value.Contains(_key.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (_suggestions.SequenceEqual(next)) return;
            _suggestions = next; _key.ItemsSource = next;
        }

        internal void ClearPending() { _key.Text = ""; _newReference = null; _newImage.Synchronize(null); UpdateAdd(); }

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
