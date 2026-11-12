using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private void BuildAdvancedOverlayLook()
    {
        AdvancedOverlayLookHost.Children.Clear();
        if (!_cardOverlayService.AdvancedAvailable || _cardOverlayDraft is null || !IsCurrentCardOverlayDraft()) return;
        var current = _cardOverlayDraft.Style ?? new OverlayStyle();
        var owner = _cardOverlayService.CaptureImageOwner();
        void Set(Func<OverlayStyle, OverlayStyle> change)
        {
            if (_cardOverlayDraft is null || !IsCurrentCardOverlayDraft() || !_cardOverlayService.AdvancedAvailable || !_cardOverlayService.IsCurrentImageOwner(owner)) return;
            var style = _cardOverlayDraft.Style ?? new();
            var next = change(style);
            // WinUI raises TextChanged for a colour box's initial text. An unchanged
            // look must not save and turn server defaults into a profile override.
            if (next == style) return;
            _cardOverlayDraft = _cardOverlayDraft with { Style = next };
            UpdateCardOverlayPreview(); ScheduleCardOverlaySave();
        }
        AdvancedOverlayLookHost.Children.Add(new TextBlock { Text = "Customize look", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        void Number(string label, double? value, double minimum, double maximum, Action<double?> changed, bool integral = false)
        {
            var box = new NumberBox { Header = label, Minimum = minimum, Maximum = maximum, Value = value ?? double.NaN, SmallChange = integral ? 1 : .25, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            var normalizing = false;
            box.ValueChanged += (_, args) =>
            {
                if (normalizing) return;
                double? next = double.IsFinite(args.NewValue) ? args.NewValue : null;
                if (integral && next.HasValue)
                {
                    next = Math.Clamp(Math.Round(next.Value, MidpointRounding.AwayFromZero), minimum, maximum);
                    // Normalize typed fractions as well as spinner input before scheduling a save.
                    if (box.Value != next.Value)
                    {
                        normalizing = true;
                        try { box.Value = next.Value; }
                        finally { normalizing = false; }
                    }
                }
                changed(next);
            };
            AdvancedOverlayLookHost.Children.Add(box);
        }
        Number("Size (0.75–2)", current.Size, .75, 2, value => Set(style => style with { Size = value }));
        Number("Border width (0–3)", current.BorderWidth, 0, 3, value => Set(style => style with { BorderWidth = value }), integral: true);
        void Choice(string label, string? selected, string[] options, Action<string?> changed)
        {
            var box = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch };
            box.Items.Add(new ComboBoxItem { Content = "Preset default", Tag = "" });
            foreach (var option in options) box.Items.Add(new ComboBoxItem { Content = option, Tag = option });
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == (selected ?? "")) ?? box.Items[0];
            box.SelectionChanged += (_, _) => changed((box.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } value ? value : null);
            AdvancedOverlayLookHost.Children.Add(box);
        }
        Choice("Shape", current.Shape, ["pill", "rounded", "square"], value => Set(style => style with { Shape = value }));
        Choice("Font weight", current.FontWeight, ["regular", "semibold", "extrabold"], value => Set(style => style with { FontWeight = value }));
        void Color(string label, string? selected, bool alpha, Action<string?> changed)
        {
            var box = new TextBox { Header = label, Text = selected ?? "", PlaceholderText = alpha ? "#RRGGBBAA (blank uses preset)" : "#RRGGBB (blank uses preset)", MaxLength = alpha ? 9 : 7 };
            box.TextChanged += (_, _) =>
            {
                var text = box.Text.Trim();
                if (text.Length == 0) changed(null);
                else if (text.Length == (alpha ? 9 : 7) && OverlayColor.Parse(text) is not null) changed(text);
            };
            AdvancedOverlayLookHost.Children.Add(box);
        }
        Color("Background", current.Background, true, value => Set(style => style with { Background = value }));
        Color("Text color", current.TextColor, false, value => Set(style => style with { TextColor = value }));
        Color("Border color", current.BorderColor, true, value => Set(style => style with { BorderColor = value }));
        Choice("Uppercase", current.Uppercase?.ToString().ToLowerInvariant(), ["true", "false"], value => Set(style => style with { Uppercase = value is null ? null : value == "true" }));
        Choice("Shadow", current.Shadow?.ToString().ToLowerInvariant(), ["true", "false"], value => Set(style => style with { Shadow = value is null ? null : value == "true" }));
        var reset = new Button { Content = "Use preset look" };
        reset.Click += (_, _) => { Set(_ => new OverlayStyle()); BuildAdvancedOverlayLook(); };
        AdvancedOverlayLookHost.Children.Add(reset);
    }

    private Button BuildOverlayImagesButton(string id)
    {
        var button = new Button { Content = "Images", Padding = new Thickness(8, 4, 8, 4) };
        var flyout = new Flyout();
        var owner = _cardOverlayService.CaptureImageOwner();
        button.Flyout = flyout;
        flyout.Opening += (_, _) =>
        {
            if (_cardOverlayDraft is null || !IsCurrentCardOverlayDraft() || !_cardOverlayService.IsCurrentImageOwner(owner)) return;
            var contents = new StackPanel { Spacing = 12, Width = 280 };
            contents.Children.Add(new TextBlock { Text = "Uploaded badge images", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            if (_cardOverlayService.Images.Count == 0) contents.Children.Add(new TextBlock { Text = "No uploaded images. Ask your server administrator to add images.", TextWrapping = TextWrapping.Wrap });
            ComboBox Picker(string label, string? selected, Action<string?> changed)
            {
                var picker = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch };
                picker.Items.Add(new ComboBoxItem { Content = "None (plain badge)", Tag = "" });
                foreach (var image in _cardOverlayService.Images)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(PosterCard.BuildAdvancedBadge("Sample", "resolution", new(true, OverlayPosition.TopLeft) { Image = image.Ref }, "minimal", images: _cardOverlayService));
                    row.Children.Add(new TextBlock { Text = image.Name.Length > 64 ? image.Name[..64] : image.Name, VerticalAlignment = VerticalAlignment.Center });
                    picker.Items.Add(new ComboBoxItem { Content = row, Tag = image.Ref });
                }
                if (selected is not null && !picker.Items.Cast<ComboBoxItem>().Any(item => (string)item.Tag == selected))
                    picker.Items.Add(new ComboBoxItem { Content = "Stored image (unavailable)", Tag = selected });
                picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == (selected ?? "")) ?? picker.Items[0];
                picker.SelectionChanged += (_, _) => changed((picker.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } value ? value : null);
                return picker;
            }
            var config = _cardOverlayDraft.Items[id];
            contents.Children.Add(Picker("Image beside the label", config.Image, reference =>
            {
                if (_cardOverlayDraft is not null && _cardOverlayService.IsCurrentImageOwner(owner)) UpdateCardOverlayItem(id, _cardOverlayDraft.Items[id] with { Image = reference });
            }));
            var values = config.ValueImages ?? new Dictionary<string, string>();
            foreach (var pair in values.Take(32))
            {
                var key = pair.Key;
                contents.Children.Add(Picker("Value: " + key, pair.Value, reference => Assign(key, reference)));
            }
            if (id is "resolution" or "hdr" or "resolution_hdr" or "resolution_hdr_audio" or "audio" or "audio_channels" or "video_codec" or "container" or "aspect_ratio" or "release_type" or "edition" or "content_rating" or "original_language" or "studio" or "network" or "show_status")
            {
                var keyBox = new TextBox { Header = "Exact API value for a whole-badge image", PlaceholderText = "e.g. 2160p or DV HDR10", MaxLength = 64 };
                contents.Children.Add(keyBox);
                string? selectedImage = null;
                var add = new Button { Content = "Add value image", IsEnabled = false };
                void UpdateAdd() => add.IsEnabled = selectedImage is not null && !string.IsNullOrWhiteSpace(keyBox.Text)
                    && _cardOverlayDraft is not null && (_cardOverlayDraft.Items[id].ValueImages?.Count ?? 0) < 32
                    && _cardOverlayDraft.Items[id].ValueImages?.ContainsKey(keyBox.Text.Trim()) != true;
                var picker = Picker("Image for this value", null, reference => { selectedImage = reference; UpdateAdd(); });
                keyBox.TextChanged += (_, _) => UpdateAdd();
                add.Click += (_, _) =>
                {
                    if (!add.IsEnabled || selectedImage is null || !_cardOverlayService.IsCurrentImageOwner(owner)) return;
                    var key = keyBox.Text.Trim();
                    Assign(key, selectedImage);
                    contents.Children.Insert(contents.Children.IndexOf(keyBox), Picker("Value: " + key, selectedImage, reference => Assign(key, reference)));
                    keyBox.Text = ""; picker.SelectedIndex = 0;
                };
                contents.Children.Add(picker); contents.Children.Add(add);
            }
            void Assign(string key, string? reference)
            {
                if (_cardOverlayDraft is null || !_cardOverlayService.IsCurrentImageOwner(owner)) return;
                var current = _cardOverlayDraft.Items[id];
                var map = current.ValueImages?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
                if (reference is null) map.Remove(key); else if (map.Count < 32 || map.ContainsKey(key)) map[key] = reference;
                UpdateCardOverlayItem(id, current with { ValueImages = map });
            }
            flyout.Content = new ScrollViewer { Content = contents, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        };
        return button;
    }
}

