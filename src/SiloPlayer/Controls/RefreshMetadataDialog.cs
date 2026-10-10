using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Controls;

/// <summary>Current WebUI refresh chooser; dispatch survives ordinary corner dismissal.</summary>
public sealed class RefreshMetadataDialog : ContentDialog
{
    private readonly Func<string, Task> _onConfirm;
    private readonly Button _quickButton;
    private readonly Button _completeButton;
    private readonly Button _cancelButton;
    private readonly List<(FrameworkElement Icon, ProgressRing Spinner)> _choiceIndicators = [];
    private readonly ScrollViewer _scroller;
    private readonly StackPanel _root;
    private bool _pending;
    private bool _dismissed;

    public RefreshMetadataDialog(Func<string, Task> onConfirm)
    {
        _onConfirm = onConfirm;
        EditorDialogPresentation.Configure(this, 512, new Thickness(24));
        Resources["ContentDialogTitleMargin"] = new Thickness(0, 0, 0, 8);
        var title = new Grid { ColumnSpacing = 24 };
        title.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        title.Children.Add(new TextBlock { Text = "Refresh Metadata", FontSize = 18, Height = 18, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold });
        var close = CornerCloseButton(); close.Content = WebUiIcon.Create("x", 16, Brush("PrimaryTextBrush"));
        close.Width = close.Height = 16; close.Margin = new Thickness(0, -8, -8, 0); close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, _) => Dismiss(); Grid.SetColumn(close, 1); title.Children.Add(close); Title = title;

        _root = new StackPanel { Spacing = 16 };
        _root.Children.Add(new TextBlock
        {
            Text = "Choose whether to refresh the existing item or rebuild it from the files on disk.",
            FontSize = 14, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap,
        });
        _quickButton = BuildChoice("refresh-cw", "Quick Refresh", "Keep the current item and refresh metadata using the existing scan scope.", "quick");
        _completeButton = BuildChoice("rotate-ccw", "Complete Refresh", "Clear the current match, re-scan, and rebuild the item from disk context. This can recreate the item with a new ID or type.", "complete");
        _root.Children.Add(new StackPanel { Spacing = 12, Children = { _quickButton, _completeButton } });
        _cancelButton = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)Application.Current.Resources["OutlineButtonStyle"] };
        _cancelButton.Click += (_, _) => { if (!_pending) Dismiss(); };
        _root.Children.Add(_cancelButton);
        _scroller = new ScrollViewer { Content = _root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = _scroller;
        var escape = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, args) => { Dismiss(); args.Handled = true; }; KeyboardAccelerators.Add(escape);
        Opened += (_, _) => { _dismissed = false; EditorDialogPresentation.PlaceCornerClose(this, title, close); Reflow(); if (XamlRoot != null) XamlRoot.Changed += RootChanged; };
        Closed += (_, _) => { _dismissed = true; if (XamlRoot != null) XamlRoot.Changed -= RootChanged; };
    }
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Reflow();
    private void Reflow()
    {
        if (XamlRoot == null) return;
        var alignment = XamlRoot.Size.Width < 640 ? TextAlignment.Center : TextAlignment.Left;
        if (Title is Grid header && header.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock heading) heading.TextAlignment = alignment;
        if (_root.Children[0] is TextBlock description) description.TextAlignment = alignment;
        _root.Width = Math.Min(462, Math.Max(0, XamlRoot.Size.Width - 82));
        if (Title is Grid title) title.Width = _root.Width;
        var maxHeight = Math.Max(100, XamlRoot.Size.Height - 64);
        Resources["ContentDialogMaxHeight"] = maxHeight;
        var shell = EditorDialogPresentation.Descendants<Border>(this).FirstOrDefault(border => border.Name == "BackgroundElement");
        if (shell != null) shell.MaxHeight = maxHeight;
        _scroller.MaxHeight = Math.Max(60, maxHeight - 100);
    }
    private void Dismiss() { _dismissed = true; Hide(); }
    internal static Button CornerCloseButton()
    {
        var close = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 16 }, Style = (Style)Application.Current.Resources["GhostButtonStyle"], Padding = new Thickness(0), MinWidth = 16, MinHeight = 16, Width = 20, Height = 20, Opacity = .7 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, "Close"); ToolTipService.SetToolTip(close, "Close"); return close;
    }
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private Button BuildChoice(string glyph, string title, string description, string mode)
    {
        var icon = WebUiIcon.Create(glyph, 20, Brush("SecondaryTextBrush")); icon.Margin = new Thickness(0, 2, 0, 0); icon.VerticalAlignment = VerticalAlignment.Top;
        var spinner = new ProgressRing { Width = 20, Height = 20, IsActive = false, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var copy = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = title, FontSize = 14, Height = 20, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold }, new TextBlock { Text = description, FontSize = 14, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap } } };
        var grid = new Grid { ColumnSpacing = 12 }; grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Grid { Children = { icon, spinner } }); Grid.SetColumn(copy, 1); grid.Children.Add(copy);
        var button = new Button { Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16), Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        button.PointerEntered += (_, _) => { if (!_pending) button.Background = Brush("SurfaceHoverBrush"); };
        button.PointerExited += (_, _) => button.Background = Brush("SurfaceBrush");
        _choiceIndicators.Add((icon, spinner)); button.Click += async (_, _) => await RunAsync(mode); return button;
    }
    private async Task RunAsync(string mode)
    {
        if (_pending) return;
        _pending = true; SetPending(true);
        try { await _onConfirm(mode); if (!_dismissed) Hide(); }
        catch (Exception ex) { App.Services.GetService<ToastService>()?.Error(ex.Message); }
        finally { _pending = false; SetPending(false); }
    }
    private void SetPending(bool pending)
    {
        _quickButton.IsEnabled = _completeButton.IsEnabled = _cancelButton.IsEnabled = !pending;
        _quickButton.Opacity = _completeButton.Opacity = pending ? .6 : 1;
        foreach (var (icon, spinner) in _choiceIndicators) { icon.Visibility = pending ? Visibility.Collapsed : Visibility.Visible; spinner.Visibility = pending ? Visibility.Visible : Visibility.Collapsed; spinner.IsActive = pending; }
    }
}
