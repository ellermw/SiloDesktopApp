using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Services;

namespace SiloPlayer.Helpers;

/// <summary>Adapts the shipped WinUI dialog template while retaining its real command buttons and keyboard behavior.</summary>
internal static class EditorDialogPresentation
{
    internal static void Configure(ContentDialog dialog, double width, Thickness padding)
    {
        dialog.FontFamily = (FontFamily)Application.Current.Resources["ThemeFontFamily"];
        dialog.Background = (Brush)Application.Current.Resources["AppBackgroundBrush"];
        dialog.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
        dialog.BorderThickness = new Thickness(1); dialog.CornerRadius = new CornerRadius(12);
        dialog.Resources["ContentDialogMaxWidth"] = width;
        dialog.Resources["ContentDialogMinWidth"] = 0d;
        dialog.Resources["ContentDialogPadding"] = padding;
        dialog.Resources["ContentDialogTitleMargin"] = new Thickness(0, 0, 0, 16);
        dialog.Resources["ContentDialogTopOverlay"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        dialog.Resources["ContentDialogSeparatorThickness"] = new Thickness(0);
        dialog.Resources["OverlayCornerRadius"] = new CornerRadius(12);
        // Dialogs render outside the shell's mirrored root; follow the interface direction while open.
        Localized.SetDirection(dialog, true);
        AppLocalization.Register(dialog, () => RefreshKeyedDescendants(dialog));
    }

    internal static Button CornerClose(ContentDialog dialog)
    {
        var close = new Button { Content = WebUiIcon.Create("x", 16, (Brush)Application.Current.Resources["SecondaryTextBrush"]),
            Width = 24, Height = 24, MinHeight = 0, MinWidth = 0, Padding = new Thickness(4),
            Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, "Close"); close.KeyedName("web.ui.close");
        close.Click += (_, _) => dialog.Hide(); return close;
    }

    internal static void PlaceCornerClose(ContentDialog dialog, Grid? header, Button close)
    {
        // A negative header margin clips the icon in WinUI's title presenter.
        // Put the corner action inside the full dialog surface instead.
        var background = Descendants<Border>(dialog).FirstOrDefault(border => border.Name == "BackgroundElement");
        if (background?.Child is not FrameworkElement content) return;
        header?.Children.Remove(close);
        Grid overlay;
        if (content is Grid grid && grid.Name == "EditorCornerCloseOverlay") overlay = grid;
        else
        {
            background.Child = null;
            overlay = new Grid { Name = "EditorCornerCloseOverlay" }; overlay.Children.Add(content); background.Child = overlay;
        }
        if (!overlay.Children.Contains(close)) overlay.Children.Add(close);
        Grid.SetColumn(close, 0); Grid.SetRow(close, 0);
        Grid.SetColumnSpan(close, Math.Max(1, overlay.ColumnDefinitions.Count));
        Grid.SetRowSpan(close, Math.Max(1, overlay.RowDefinitions.Count));
        close.Margin = new Thickness(16); close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
    }

    internal static void ReflowCommands(ContentDialog dialog, bool narrow, Thickness padding, Button? reset = null, double buttonHeight = 32, bool stackNarrow = false)
    {
        // The reference dialog reserves32px above and below a constrained viewport.
        var background = Descendants<Border>(dialog).FirstOrDefault(border => border.Name == "BackgroundElement");
        if (background is not null) background.MaxHeight = Math.Max(160, (dialog.XamlRoot?.Size.Height ?? 900) - 64);
        var commands = Descendants<Grid>(dialog).FirstOrDefault(grid => grid.Name == "CommandSpace");
        if (commands is null) return;
        var primary = commands.Children.OfType<Button>().First(button => button.Name == "PrimaryButton");
        var cancel = commands.Children.OfType<Button>().First(button => button.Name == "CloseButton");
        primary.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        cancel.Style = (Style)Application.Current.Resources["GhostButtonStyle"];
        // The ContentDialog template supplies local command colors; use the current shared theme.
        primary.Background = (Brush)Application.Current.Resources["AccentBrush"];
        primary.Foreground = (Brush)Application.Current.Resources["AccentForegroundBrush"];
        cancel.Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        foreach (var button in new[] { primary, cancel })
        {
            button.MinWidth = 0; button.MinHeight = buttonHeight; button.Height = buttonHeight; button.FontSize = buttonHeight == 36 ? 14 : 13;
            button.Padding = new Thickness(buttonHeight == 36 ? 16 : 12, 4, buttonHeight == 36 ? 16 : 12, 4); button.CornerRadius = new CornerRadius(8);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        cancel.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"]; cancel.BorderThickness = new Thickness(1);
        commands.Padding = padding; commands.Background = dialog.Background;
        commands.BorderBrush = new SolidColorBrush(((SolidColorBrush)Application.Current.Resources["BorderBrush"]).Color) { Opacity = .1 };
        commands.BorderThickness = reset is null ? new Thickness(0) : new Thickness(0, 1, 0, 0);
        // Reuse the five named columns so the WinUI visibility states keep their template targets.
        commands.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        commands.ColumnDefinitions[1].Width = narrow ? new GridLength(8) : GridLength.Auto;
        commands.ColumnDefinitions[2].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(8);
        commands.ColumnDefinitions[3].Width = narrow ? new GridLength(0) : GridLength.Auto;
        commands.ColumnDefinitions[4].Width = new GridLength(0);
        Grid.SetColumn(cancel, narrow ? 0 : 1); Grid.SetColumn(primary, narrow ? 2 : 3);
        if (reset is null)
        {
            if (stackNarrow)
            {
                commands.RowDefinitions.Clear();
                commands.RowDefinitions.Add(new() { Height = GridLength.Auto });
                if (narrow) { commands.RowDefinitions.Add(new() { Height = new GridLength(8) }); commands.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
                Grid.SetRow(primary, 0); Grid.SetRow(cancel, narrow ? 2 : 0);
                if (narrow) { Grid.SetColumn(primary, 0); Grid.SetColumn(cancel, 0); Grid.SetColumnSpan(primary, 5); Grid.SetColumnSpan(cancel, 5); }
                else { Grid.SetColumnSpan(primary, 1); Grid.SetColumnSpan(cancel, 1); }
            }
            return;
        }
        if (reset.Parent is Panel parent && !ReferenceEquals(parent, commands)) parent.Children.Remove(reset);
        if (!commands.Children.Contains(reset)) commands.Children.Add(reset);
        while (commands.RowDefinitions.Count < 2) commands.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetRow(reset, narrow ? 1 : 0); Grid.SetColumn(reset, 0); Grid.SetColumnSpan(reset, narrow ? 5 : 1);
        reset.Margin = new Thickness(0, narrow ? 8 : 0, 0, 0);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
    }

    // Keyed retained text: the literal English already assigned is the fallback, and the
    // catalog text replaces it on load and after every interface-language change.
    // Key only fixed labels; text that code later rewrites uses AppStrings.Get instead.
    internal static T Keyed<T>(this T element, string key) where T : FrameworkElement
    {
        if (element is TextBlock or MenuFlyoutItem or MenuFlyoutSubItem) Localized.SetText(element, key);
        else if (element is ContentControl) Localized.SetContent(element, key);
        else throw new ArgumentException("Keyed text is not supported on " + element.GetType().Name + ".");
        return element;
    }
    /// <summary>
    /// The text to show for a failed request: v2 problem details through the catalog
    /// (<see cref="ApiProblemText"/>), other failures as their message. Machine handling
    /// keeps branching on codes and status, never on this text.
    /// </summary>
    internal static string DisplayMessage(this Exception exception)
        => exception is ApiException api ? ApiProblemText.Display(api) : exception.Message;

    /// <summary>
    /// Re-applies keyed text and content under an open dialog. A dialog's own locale
    /// registration survives its template relocation; a child's Loaded/Unloaded order
    /// can leave the child unsubscribed while it is still shown, so the dialog refresh
    /// re-reads its keyed descendants (title presenter and content alike).
    /// </summary>
    internal static void RefreshKeyedDescendants(DependencyObject root)
    {
        foreach (var element in Descendants<FrameworkElement>(root))
        {
            switch (element)
            {
                case TextBlock text when Localized.GetText(text) is { Length: > 0 } key: text.Text = AppStrings.Get(key, fallback: text.Text); break;
                // A flyout owner's content is never rewritten here (doing so can stop its flyout opening).
                case ContentControl control when control is not Button { Flyout: not null }
                    && Localized.GetContent(control) is { Length: > 0 } key && control.Content is string current:
                    control.Content = AppStrings.Get(key, fallback: current); break;
            }
            // Modal template relocation can also detach a field's own locale
            // subscription. Refresh keyed field metadata from the open dialog;
            // the editable value remains untouched.
            if (element is TextBox input)
            {
                if (Localized.GetPlaceholder(input) is { Length: > 0 } placeholderKey)
                    input.PlaceholderText = AppStrings.Get(placeholderKey, fallback: input.PlaceholderText);
                if (Localized.GetHeader(input) is { Length: > 0 } headerKey)
                    input.Header = AppStrings.Get(headerKey, fallback: input.Header as string);
            }
            if (Localized.GetName(element) is { Length: > 0 } nameKey)
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, AppStrings.Get(nameKey));
        }
    }

    internal static T KeyedHeader<T>(this T element, string key) where T : FrameworkElement { Localized.SetHeader(element, key); return element; }
    internal static T KeyedPlaceholder<T>(this T element, string key) where T : FrameworkElement { Localized.SetPlaceholder(element, key); return element; }
    internal static T KeyedName<T>(this T element, string key) where T : FrameworkElement { Localized.SetName(element, key); return element; }
    internal static T KeyedToolTip<T>(this T element, string key) where T : FrameworkElement { Localized.SetToolTip(element, key); return element; }
    internal static ContentDialog KeyedDialog(this ContentDialog dialog, string? title = null, string? primary = null, string? secondary = null, string? close = null)
    {
        if (title != null) Localized.SetTitle(dialog, title);
        if (primary != null) Localized.SetPrimaryButton(dialog, primary);
        if (secondary != null) Localized.SetSecondaryButton(dialog, secondary);
        if (close != null) Localized.SetCloseButton(dialog, close);
        Localized.SetDirection(dialog, true);
        return dialog;
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
