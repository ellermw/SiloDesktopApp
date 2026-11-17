using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

internal sealed class AdminUsersAvatarPicker : IDisposable
{
    private readonly Func<string, CancellationToken, Task<byte[]>> _read;
    private readonly CancellationToken _owner;
    private readonly ComboBox _style = new() { Header = "Preset style", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 40 };
    private readonly GridView _choices = new() { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 270, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Image _preview = new() { Width = 96, Height = 96, Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _fallback = new() { Text = "Initials fallback", TextWrapping = TextWrapping.Wrap };
    private readonly Button _load = new() { Content = "Load graphical previews", MinHeight = 40 };
    private readonly Dictionary<string, Image> _images = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cancel;
    private Task _pending = Task.CompletedTask;
    private long _generation;
    private int _batch;
    private bool _disposed;
    internal StackPanel Content { get; } = new() { Spacing = 8 };
    internal string? Reference => ((_choices.SelectedItem as GridViewItem)?.Tag as string) is { Length: > 0 } selected ? selected : null;

    internal AdminUsersAvatarPicker(string? initial, Func<string, CancellationToken, Task<byte[]>> read, CancellationToken owner)
    {
        _read = read; _owner = owner;
        foreach (var style in AdminUserAvatars.Styles) _style.Items.Add(style);
        _style.SelectedItem = AdminUserAvatars.Style(initial);
        _choices.SelectionChanged += (_, _) => { if (!_disposed) UpdatePreview(); };
        AutomationProperties.SetName(_choices, "Profile avatar presets");
        AutomationProperties.SetName(_preview, "Selected profile avatar preview");
        Content.Children.Add(_preview); Content.Children.Add(_fallback); Content.Children.Add(_style);
        Content.Children.Add(_choices); Content.Children.Add(_load);
        var next = new Button { Content = "Refresh preset choices", MinHeight = 40 };
        Content.Children.Add(next); Content.Children.Add(_status);
        Content.Children.Add(new TextBlock { Text = "Previews load only on request, sequentially, for this visible batch. DiceBear receives only the preset seed, never account credentials. Existing uploaded avatars are retained without fetching their private URL.", TextWrapping = TextWrapping.Wrap });
        Refresh(initial);
        _style.SelectionChanged += (_, _) => { _batch = 0; Refresh(Reference); };
        next.Click += (_, _) => { _batch = (_batch + 1) % 32; Refresh(Reference); };
        _load.Click += async (_, _) => await LoadAsync();
        Content.Unloaded += (_, _) => Cancel();
    }
    private void Cancel()
    {
        _generation++;
        _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
    }
    private void Refresh(string? selected)
    {
        Cancel(); _choices.Items.Clear(); _images.Clear(); _preview.Source = null;
        void Add(string reference, string label)
        {
            var image = new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform };
            var tile = new StackPanel { Spacing = 2 };
            tile.Children.Add(image); tile.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 96 });
            var choice = new GridViewItem { Content = tile, Tag = reference, Width = 110, MinHeight = 104 };
            AutomationProperties.SetName(choice, label);
            _choices.Items.Add(choice);
            if (reference.Length != 0) _images[reference] = image;
        }
        Add("", "Initials fallback");
        foreach (var option in AdminUserAvatars.Batch(_style.SelectedItem as string ?? "identicon", _batch)) Add(option.Reference, option.Label);
        foreach (var index in Enumerable.Range(1, 8)) Add("preset:avatar-" + index, "Legacy avatar " + index);
        if (!string.IsNullOrEmpty(selected) && !_images.ContainsKey(selected)) Add(selected, "Keep existing avatar");
        _choices.SelectedItem = _choices.Items.OfType<GridViewItem>().FirstOrDefault(item => (string)item.Tag == selected) ?? _choices.Items[0];
        _status.Text = "Graphical previews have not been loaded for this batch.";
        _load.IsEnabled = !_disposed && !_owner.IsCancellationRequested;
        UpdatePreview();
    }
    private void UpdatePreview()
    {
        _preview.Source = Reference is { } reference && _images.TryGetValue(reference, out var image) ? image.Source : null;
        _fallback.Visibility = _preview.Source == null ? Visibility.Visible : Visibility.Collapsed;
        _fallback.Text = Reference == null ? "Initials fallback" : "Preset selected — graphical preview not loaded";
    }
    private async Task LoadAsync()
    {
        if (_disposed || _owner.IsCancellationRequested) return;
        Cancel(); _cancel = CancellationTokenSource.CreateLinkedTokenSource(_owner); _cancel.CancelAfter(TimeSpan.FromSeconds(40));
        var token = _cancel.Token; var generation = _generation; var prior = _pending;
        _load.IsEnabled = false;
        _pending = PopulateAsync(prior, generation, token);
        await _pending;
    }
    private bool Current(long generation, CancellationToken token) => !_disposed && generation == _generation && !_owner.IsCancellationRequested && !token.IsCancellationRequested;
    private async Task PopulateAsync(Task prior, long generation, CancellationToken token)
    {
        try
        {
            await prior;
            if (!Current(generation, token)) return;
            _status.Text = "Loading this preset batch…";
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
                UpdatePreview();
            }
            if (Current(generation, token)) _status.Text = "Graphical previews loaded. Select a tile to preview and use that avatar.";
        }
        catch (OperationCanceledException) { if (!_disposed && generation == _generation && !_owner.IsCancellationRequested) _status.Text = "Preview loading stopped; loaded tiles are retained. Retry explicitly if needed."; }
        catch (Exception) { if (Current(generation, token)) _status.Text = "Some previews are unavailable. Loaded tiles and the selected preset are retained; retry explicitly if needed."; }
        finally { if (!_disposed && generation == _generation) _load.IsEnabled = !_owner.IsCancellationRequested; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Cancel(); _preview.Source = null;
        foreach (var image in _images.Values) image.Source = null;
        _images.Clear(); _choices.Items.Clear();
    }
}
