using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using SiloPlayer.Controls;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminCollectionsPage
{
    private readonly SemaphoreSlim _imageSlots = new(4, 4);
    private FrameworkElement Poster(string url, double width, string name)
    {
        var height = width * 1.5;
        var placeholder = Text("▤", 24, true); placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        var image = new Image { Width = width, Height = height, Stretch = Stretch.UniformToFill };
        var face = new Grid { Width = width, Height = height, Children = { placeholder, image } };
        var border = new Border { Child = face, CornerRadius = new(8), Width = width, Height = height, Background = AdminMonitorUi.Solid(AdminMonitorUi.Border) };
        Identify(border, "CollectionPoster", name);
        var owner = Controller.Owner; var started = false; var retired = new CancellationTokenSource();
        border.Unloaded += (_, _) => { retired.Cancel(); image.Source = null; };
        border.EffectiveViewportChanged += async (_, args) =>
        {
            var viewport = args.EffectiveViewport;
            if (started || string.IsNullOrEmpty(url) || !Current(owner) || viewport.Width <= 0 || viewport.Height <= 0 || viewport.Y >= height || viewport.Y + viewport.Height <= 0) return;
            started = true; var acquired = false;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner!.Token, retired.Token);
            try
            {
                await _imageSlots.WaitAsync(linked.Token); acquired = true;
                var bytes = await _api.ArtworkBytesAsync(owner, url, linked.Token);
                if (!Current(owner) || !border.IsLoaded || linked.IsCancellationRequested) return;
                using var stream = new MemoryStream(bytes); using var source = stream.AsRandomAccessStream();
                var bitmap = new BitmapImage { DecodePixelWidth = 256, DecodePixelHeight = 384 };
                await bitmap.SetSourceAsync(source);
                if (Current(owner) && border.IsLoaded && !linked.IsCancellationRequested) { image.Source = bitmap; placeholder.Visibility = Visibility.Collapsed; }
            }
            catch (Exception) { /* Keep the placeholder. Never print signed image URLs or make a broken poster fail the page. */ }
            finally { if (acquired) _imageSlots.Release(); }
        };
        return border;
    }
    private static StackPanel Stack(double spacing = 12) => new() { Spacing = spacing, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static LogsWrapPanel Wrap() => new() { Spacing = 10, RowSpacing = 10 };
    private static TextBlock Text(string text, double size = 14, bool muted = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        FontWeight = size >= 20 ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = AdminMonitorUi.Solid(muted ? AdminMonitorUi.Muted : AdminMonitorUi.Foreground)
    };
    private static Border Card(UIElement child) => new()
    {
        Child = child, Padding = new(16), CornerRadius = new(18), BorderThickness = new(1),
        BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border), Background = AdminMonitorUi.Solid(AdminMonitorUi.Surface),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static void Identify(DependencyObject control, string id, string name) { AutomationProperties.SetAutomationId(control, id); AutomationProperties.SetName(control, name); }
    private Button ActionButton(string title, string id, Func<Task> action, bool enabled = true)
    {
        var button = new Button { Content = title, MinHeight = 36, Padding = new(12, 7, 12, 7), IsEnabled = enabled, CornerRadius = new(8) };
        Identify(button, id, title);
        button.Click += async (_, _) =>
        {
            var owner = Controller.Owner;
            try { await action(); }
            catch (Exception error) { if (Current(owner)) Notice(SiloPlayer.Core.Services.Admin.AdminCollectionsController.Message(error)); }
        };
        return button;
    }
    private ComboBox Select(string id, IEnumerable<(string Value, string Label)> options, string current, Func<string, Task> changed)
    {
        var select = new ComboBox { MinWidth = 145, MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Stretch };
        Identify(select, id, id.Replace("Collections", ""));
        foreach (var (value, label) in options) select.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        if (current.Length > 0 && !select.Items.OfType<ComboBoxItem>().Any(item => (string)item.Tag == current)) select.Items.Add(new ComboBoxItem { Content = current, Tag = current });
        select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == current) ?? select.Items.OfType<ComboBoxItem>().FirstOrDefault();
        select.SelectionChanged += async (_, _) =>
        {
            var owner = Controller.Owner;
            try { if (select.SelectedItem is ComboBoxItem { Tag: string value }) await changed(value); }
            catch (Exception error) { if (Current(owner)) Notice(SiloPlayer.Core.Services.Admin.AdminCollectionsController.Message(error)); }
        };
        return select;
    }
    private TextBox Input(StackPanel target, string label, string id, string value, Action<string> changed, bool multiline = false)
    {
        target.Children.Add(Text(label));
        var input = new TextBox { Text = value, MaxLength = multiline ? 16384 : 1024, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 90 : 36, HorizontalAlignment = HorizontalAlignment.Stretch };
        Identify(input, id, label); input.TextChanged += (_, _) => changed(input.Text); target.Children.Add(input); return input;
    }
    private async Task<bool?> ConfirmAsync(string title, string message, string primary, SiloPlayer.Core.Services.MonitorAuthorityLease owner)
    {
        if (_dialog != null || !Current(owner) || XamlRoot == null) return null;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = Text(message), PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        Identify(dialog, "CollectionsConfirm", title); _dialog = dialog;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary && Current(owner); }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (_dialog != null || XamlRoot == null) return ContentDialogResult.None;
        dialog.XamlRoot = XamlRoot; _dialog = dialog;
        try { return await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
}
