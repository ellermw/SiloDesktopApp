using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersFormKit;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The preset avatars of the profile editor (official <c>ProfileEditorDialog</c>): five style cards, the choices of the style shown with a "More options" shuffle, and the
/// legacy avatars. The previews are read as the choices appear, one after another, by the owned reader (the preset seed is all that leaves the app).
/// </summary>
internal sealed class AdminUsersAvatarPicker : IDisposable
{
    private readonly Func<string, CancellationToken, Task<byte[]>> _read;
    private readonly CancellationToken _owner;
    private string _style;
    private readonly GridView _choices = new() { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 232, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _showing = Text("", 12, true);
    private readonly Dictionary<string, Image> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _styleButtons = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cancel;
    private Task _pending = Task.CompletedTask;
    private long _generation;
    private int _batch;
    private bool _disposed, _syncing, _started, _running;

    internal StackPanel Content { get; } = new() { Spacing = 12 };
    /// <summary>The selected preset reference; null keeps initials.</summary>
    internal string? Reference => ((_choices.SelectedItem as GridViewItem)?.Tag as string) is { Length: > 0 } selected ? selected : null;
    internal ImageSource? PreviewSource => Reference is { } reference && _images.TryGetValue(reference, out var image) ? image.Source : null;
    internal string? SelectedLabel => (_choices.SelectedItem as GridViewItem) is { Tag: string { Length: > 0 } } item ? AutomationProperties.GetName(item) : null;
    internal event Action? Changed;

    private static string S(string key, string fallback, params (string Name, object? Value)[] args) =>
        AppStrings.Get(key, args.Length == 0 ? null : args.ToDictionary(pair => pair.Name, pair => pair.Value), fallback);

    private static string StyleKey(string style) => style switch { "bottts-neutral" => "botttsNeutral", "fun-emoji" => "funEmoji", "pixel-art-neutral" => "pixelArtNeutral", _ => style };
    private static string StyleLabel(string style) => S("web.profiles.helpers.avatar.style." + StyleKey(style) + ".label", style);
    private static string StyleSummary(string style) => S("web.profiles.helpers.avatar.style." + StyleKey(style) + ".summary", "");

    private static readonly string[] LegacyNames = ["skyFox", "cometCat", "starBear", "neonPup", "orbitBunny", "solarOwl", "novaGecko", "pixelPenguin"];
    private static readonly string[] LegacyFallback = ["Sky Fox", "Comet Cat", "Star Bear", "Neon Pup", "Orbit Bunny", "Solar Owl", "Nova Gecko", "Pixel Penguin"];

    internal AdminUsersAvatarPicker(string? initial, Func<string, CancellationToken, Task<byte[]>> read, CancellationToken owner)
    {
        _read = read; _owner = owner;
        _style = AdminUserAvatars.Style(initial);
        var title = Text(S("web.profiles.editor.presets", "Preset avatars"), 14);
        var hint = Text(S("web.profiles.editor.presetsHint", ""), 12, true);
        var heading = new StackPanel { Spacing = 2 }; heading.Children.Add(title); heading.Children.Add(hint);
        Content.Children.Add(heading);
        var styles = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        foreach (var style in AdminUserAvatars.Styles)
        {
            var label = Text(StyleLabel(style), 14); label.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
            var body = new StackPanel { Spacing = 4 }; body.Children.Add(label); body.Children.Add(Text(StyleSummary(style), 12, true));
            var button = new Button
            {
                Content = body, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(12, 10, 12, 10), CornerRadius = new(16),
                Style = (Style)Application.Current.Resources["GhostButtonStyle"], BorderThickness = new(1),
            };
            AutomationProperties.SetAutomationId(button, "AvatarStyle_" + style); AutomationProperties.SetName(button, StyleLabel(style));
            var chosen = style;
            button.Click += (_, _) => { if (_syncing || chosen == _style) return; _style = chosen; _batch = 0; Refresh(null); };
            _styleButtons[style] = button; styles.Children.Add(button);
        }
        AdaptiveColumns(styles);
        Content.Children.Add(styles);
        var more = ActionButton(S("web.profiles.editor.moreOptions", "More options"), "refresh-cw", height: 32);
        more.Click += (_, _) => { _batch = (_batch + 1) % 32; Refresh(Reference); };
        var showingRow = new SplitRow(_showing, more) { MinStart = 192, Gap = 12, RowGap = 8, CenterVertically = true };
        Content.Children.Add(showingRow);
        AutomationProperties.SetName(_choices, S("web.profiles.editor.presets", "Preset avatars"));
        _choices.SelectionChanged += (_, _) => { if (!_syncing && !_disposed) Changed?.Invoke(); };
        Content.Children.Add(_choices);
        Refresh(initial);
        // The previews are read when the picker is shown, as the web shows them; the reads run one after another and stop with the window.
        Content.Loaded += async (_, _) => { _started = true; if (!_running && _images.Values.Any(image => image.Source == null)) await LoadAsync(); };
    }

    private void Cancel()
    {
        _generation++;
        _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
    }

    private void PaintStyles()
    {
        foreach (var (style, button) in _styleButtons)
        {
            var on = style == _style;
            button.BorderBrush = on ? Brush("AccentBrush") : Tint("BorderBrush", 0.8);
            button.Background = on ? Tint("AccentBrush", 0.08) : null;
            AutomationProperties.SetItemStatus(button, on ? "selected" : "");
        }
    }

    private void Refresh(string? selected)
    {
        Cancel(); _syncing = true; _choices.Items.Clear(); _images.Clear();
        PaintStyles();
        void Add(string reference, string label, bool initials = false)
        {
            var image = new Image { Width = 56, Height = 56, Stretch = Stretch.Uniform };
            var tile = new Border { Child = image, Width = 64, Height = 64, CornerRadius = new(12), Padding = new(4) };
            if (initials) tile.Child = new TextBlock { Text = "Aa", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("SecondaryTextBrush") };
            var choice = new GridViewItem { Content = tile, Tag = reference, Width = 80, Height = 80, Padding = new(4) };
            AutomationProperties.SetName(choice, label);
            ToolTipService.SetToolTip(choice, label);
            _choices.Items.Add(choice);
            if (reference.Length != 0) _images[reference] = image;
        }
        Add("", S("web.profiles.editor.initialsFallback", "Initials fallback"), true);
        var batch = AdminUserAvatars.Batch(_style, _batch);
        foreach (var option in batch) Add(option.Reference, option.Label);
        for (var index = 0; index < 8; index++) Add("preset:avatar-" + (index + 1), S("web.profiles.helpers.avatar.legacy." + LegacyNames[index], LegacyFallback[index]));
        if (!string.IsNullOrEmpty(selected) && !_images.ContainsKey(selected)) Add(selected, S("web.profiles.editor.customUpload", "Custom upload"));
        _choices.SelectedItem = _choices.Items.OfType<GridViewItem>().FirstOrDefault(item => (string)item.Tag == selected) ?? _choices.Items[0];
        _showing.Text = S("web.profiles.editor.presetsShowing", $"Showing {batch.Count} {StyleSummary(_style).ToLowerInvariant()} presets.", ("count", batch.Count), ("count_text", batch.Count), ("summary", StyleSummary(_style)));
        _syncing = false;
        Changed?.Invoke();
        if (_started) _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_disposed || _owner.IsCancellationRequested) return;
        Cancel(); _cancel = CancellationTokenSource.CreateLinkedTokenSource(_owner); _cancel.CancelAfter(TimeSpan.FromSeconds(40));
        var token = _cancel.Token; var generation = _generation; var prior = _pending;
        _pending = PopulateAsync(prior, generation, token);
        await _pending;
    }

    private bool Current(long generation, CancellationToken token) => !_disposed && generation == _generation && !_owner.IsCancellationRequested && !token.IsCancellationRequested;

    private async Task PopulateAsync(Task prior, long generation, CancellationToken token)
    {
        _running = true;
        try
        {
            await prior;
            if (!Current(generation, token)) return;
            foreach (var pair in _images.ToArray())
            {
                if (pair.Value.Source != null) continue;
                byte[] bytes;
                try { bytes = await _read(pair.Key, token); }
                catch (ArgumentException) { continue; }
                if (!Current(generation, token)) return;
                using var stream = new MemoryStream(bytes);
                using var randomAccess = stream.AsRandomAccessStream();
                var source = new SvgImageSource { RasterizePixelWidth = 128, RasterizePixelHeight = 128 };
                var decode = source.SetSourceAsync(randomAccess);
                using var decodeCancellation = token.Register(decode.Cancel);
                var status = await decode;
                if (!Current(generation, token)) return;
                if (status != SvgImageSourceLoadStatus.Success) throw new InvalidDataException("Preset SVG could not be rendered.");
                pair.Value.Source = source;
                if (pair.Key == Reference) Changed?.Invoke();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* a preview that cannot load leaves its tile empty; the choice still works */ }
        finally { if (generation == _generation) _running = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Cancel();
        foreach (var image in _images.Values) image.Source = null;
        _images.Clear(); _choices.Items.Clear();
    }
}
