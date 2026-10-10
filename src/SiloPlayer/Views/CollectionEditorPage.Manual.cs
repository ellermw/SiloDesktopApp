using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Converters;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private Border BuildManualItemRow(CollectionItem item, int index)
    {
        var row = new Grid { ColumnSpacing = 10, Padding = new(0, 8, 0, 8) };
        foreach (var width in new[] { new GridLength(28), new GridLength(24), new GridLength(36), new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(ActualWidth < 1024 ? 44 : 36) }) row.ColumnDefinitions.Add(new() { Width = width });
        var canMove = ViewModel.CanReorderManualItems && !ViewModel.IsManualMutationPending;
        var handle = new Button { Content = WebUiIcon.Create("grip-vertical", 18), Width = 28, Height = 36, MinHeight = 0, MinWidth = 0, Padding = new(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0), CornerRadius = new(8), Visibility = canMove ? Visibility.Visible : Visibility.Collapsed };
        AutomationProperties.SetName(handle, "Move " + item.Title);
        Windows.Foundation.Point? dragOrigin = null;
        var dragging = false;
        var pointerTarget = index;
        Border? highlighted = null;
        void ClearPointerGesture()
        {
            if (highlighted != null) highlighted.BorderBrush = CurrentBrush("BorderBrush");
            highlighted = null; dragging = false; dragOrigin = null;
        }
        // Keep reordering inside this editor. Button consumes pointer events,
        // and OS drag/drop can finish without delivering a row drop.
        handle.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) =>
        {
            var point = args.GetCurrentPoint(ManualItemsPanel);
            if (!canMove || !point.Properties.IsLeftButtonPressed) return;
            dragOrigin = point.Position; pointerTarget = ViewModel.ManualItems.IndexOf(item);
            handle.CapturePointer(args.Pointer);
        }), true);
        handle.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) =>
        {
            var point = args.GetCurrentPoint(ManualItemsPanel);
            if (!canMove || dragOrigin is not { } origin || !point.Properties.IsLeftButtonPressed ||
                (!dragging && Math.Abs(point.Position.X - origin.X) + Math.Abs(point.Position.Y - origin.Y) < 4)) return;
            dragging = true; args.Handled = true;
            var dropRows = ManualItemsPanel.Children.OfType<Border>().ToArray();
            for (var rowIndex = 0; rowIndex < dropRows.Length; rowIndex++)
            {
                var dropRow = dropRows[rowIndex];
                var top = dropRow.TransformToVisual(ManualItemsPanel).TransformPoint(new()).Y;
                if (point.Position.Y < top || point.Position.Y >= top + dropRow.ActualHeight) continue;
                pointerTarget = rowIndex;
                if (highlighted != null) highlighted.BorderBrush = CurrentBrush("BorderBrush");
                highlighted = dropRow; highlighted.BorderBrush = CurrentBrush("AccentBrush");
                AutomationProperties.SetHelpText(handle, $"Position {pointerTarget + 1}. Release to drop.");
                break;
            }
        }), true);
        handle.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(async (_, args) =>
        {
            var shouldMove = dragging;
            var destination = pointerTarget;
            ClearPointerGesture(); handle.ReleasePointerCapture(args.Pointer);
            if (!shouldMove) return;
            args.Handled = true;
            await ViewModel.MoveManualItemAsync(ViewModel.ManualItems.IndexOf(item), destination);
        }), true);
        handle.PointerCanceled += (_, _) => ClearPointerGesture();
        // Button releases capture before bubbling PointerReleased. Defer
        // cleanup so that release can commit the gesture first.
        handle.PointerCaptureLost += (_, _) => DispatcherQueue.TryEnqueue(ClearPointerGesture);
        var picked = false; var target = index;
        // Button consumes Space before ordinary routed handlers receive it.
        handle.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler(async (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Space)
            {
                args.Handled = true;
                if (picked) { picked = false; await ViewModel.MoveManualItemAsync(ViewModel.ManualItems.IndexOf(item), target); }
                else { picked = true; target = ViewModel.ManualItems.IndexOf(item); }
            }
            else if (picked && args.Key == Windows.System.VirtualKey.Escape) { picked = false; args.Handled = true; }
            else if (picked && args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down)
            {
                args.Handled = true;
                target = Math.Clamp(target + (args.Key == Windows.System.VirtualKey.Up ? -1 : 1), 0, Math.Max(0, ViewModel.ManualItems.Count - 1));
                AutomationProperties.SetHelpText(handle, $"Position {target + 1}. Space drops; Escape cancels.");
            }
        }), true);
        row.Children.Add(handle);
        var position = new TextBlock { Text = (index + 1).ToString(), FontSize = 13, Foreground = CurrentBrush("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(position, 1); row.Children.Add(position);
        var cover = new Border { Width = 36, Height = 54, CornerRadius = new(6), Background = CurrentBrush("SurfaceBrush") };
        if (!string.IsNullOrWhiteSpace(item.PosterUrl)) cover.Child = new Image { Stretch = Stretch.UniformToFill, Source = (ImageSource)new UrlToImageSourceConverter().Convert(item.PosterUrl, typeof(ImageSource), null!, "") };
        Grid.SetColumn(cover, 2); row.Children.Add(cover);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        copy.Children.Add(new TextBlock { Text = item.Title ?? item.ContentId ?? item.MediaItemId, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        copy.Children.Add(new TextBlock { Text = string.Join(" · ", new[] { item.Year > 0 ? item.Year.ToString() : null, item.Type == "series" ? "Series" : "Movie" }.Where(value => value != null)), FontSize = 12.5, Foreground = CurrentBrush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(copy, 3); row.Children.Add(copy);
        var remove = new Button { Content = WebUiIcon.Create("x", 16), Width = ActualWidth < 1024 ? 44 : 36, Height = ActualWidth < 1024 ? 44 : 36, MinHeight = 0, MinWidth = 0, Padding = new(0), Style = (Style)Application.Current.Resources["GhostButtonStyle"], IsEnabled = !ViewModel.IsManualMutationPending };
        AutomationProperties.SetName(remove, "Remove " + item.Title); remove.Click += async (_, _) => await ViewModel.RemoveManualItemCommand.ExecuteAsync(item); Grid.SetColumn(remove, 5); row.Children.Add(remove);
        var border = new Border { Child = row, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0, 0, 0, 1), BorderBrush = CurrentBrush("BorderBrush") };
        return border;
    }
}
