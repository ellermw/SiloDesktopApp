using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;

namespace SiloPlayer.Views.Dialogs;

/// <summary>Responsive right-side section editor with a persistent scroll area and footer.</summary>
public static class SectionEditorSheet
{
    public static async Task<bool> ShowAsync(XamlRoot root, string title, FrameworkElement content, string saveLabel, Func<string?> validate, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<bool>();
        var popup = new Popup { XamlRoot = root, IsLightDismissEnabled = false };
        var overlay = new Grid { Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(150, 0, 0, 0)), Width = root.Size.Width, Height = root.Size.Height };
        var sheet = new Grid { Width = Math.Min(512, root.Size.Width), HorizontalAlignment = HorizontalAlignment.Right, Background = (Brush)Application.Current.Resources["AppBackgroundBrush"], Padding = new Thickness(24), RowSpacing = 18 };
        sheet.RowDefinitions.Add(new() { Height = GridLength.Auto }); sheet.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); sheet.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new TextBlock { Text = title, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        sheet.Children.Add(header);
        var scroller = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroller, 1); sheet.Children.Add(scroller);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = saveLabel, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
        var footer = new StackPanel { Spacing = 8, Children = { error, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, save } } } }; Grid.SetRow(footer, 2); sheet.Children.Add(footer);
        void Close(bool result) { popup.IsOpen = false; completion.TrySetResult(result); }
        cancel.Click += (_, _) => Close(false);
        save.Click += (_, _) => { var message = validate(); if (message == null) Close(true); else error.Text = message; };
        overlay.Tapped += (_, args) => { if (ReferenceEquals(args.OriginalSource, overlay)) Close(false); };
        var escape = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, args) => { Close(false); args.Handled = true; }; sheet.KeyboardAccelerators.Add(escape);
        overlay.Children.Add(sheet); popup.Child = overlay;
        void Resize(XamlRoot sender, XamlRootChangedEventArgs args) { overlay.Width = root.Size.Width; overlay.Height = root.Size.Height; sheet.Width = Math.Min(512, root.Size.Width); }
        root.Changed += Resize;
        popup.Closed += (_, _) => completion.TrySetResult(false);
        using var cancellation = cancellationToken.Register(() => overlay.DispatcherQueue.TryEnqueue(() => Close(false)));
        popup.IsOpen = true; save.Focus(FocusState.Programmatic);
        try { return await completion.Task; }
        finally { root.Changed -= Resize; popup.IsOpen = false; popup.Child = null; scroller.Content = null; }
    }
}
