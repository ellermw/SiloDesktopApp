using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private string? _lastHeaderState;
    private string? _lastLookState;
    private Border HeaderTag(string text, string? icon = null, bool shared = false)
    {
        var color = shared ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 125, 211, 252)) : CurrentBrush("SecondaryTextBrush");
        var body = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (icon != null) body.Children.Add(WebUiIcon.Create(icon, 14, color));
        body.Children.Add(new TextBlock { Text = text, FontSize = 12, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold, Foreground = color });
        return new Border { Child = body, Padding = new(8, 2, 8, 2), CornerRadius = new(6), Background = shared
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(38, 14, 165, 233)) : CurrentBrush("SurfaceBrush") };
    }

    private void UpdateHeaderActions(bool phone)
    {
        var isDirty = _draftBaseline != null && CaptureDraft().Any(pair => _draftBaseline.GetValueOrDefault(pair.Key) != pair.Value);
        var state = $"{phone}|{ViewModel.CollectionId}|{ViewModel.CollectionType}|{ViewModel.IsEditing}|{ViewModel.IsShared}|{ViewModel.IsSaving}|{ViewModel.IsReadOnly}|{isDirty}|{!string.IsNullOrWhiteSpace(ViewModel.CurrentPosterUrl)}|{ViewModel.Capabilities?.Imports}";
        if (state == _lastHeaderState) return;
        _lastHeaderState = state;
        _headerTags.Children.Clear();
        _headerTags.Children.Add(HeaderTag(_kindTag.Text, ViewModel.CollectionType switch { "manual" => "list-ordered", "smart" => "wand-sparkles", _ => "refresh-cw" }));
        if (ViewModel.IsShared) _headerTags.Children.Add(HeaderTag("Shared", "users", true));
        if (!ViewModel.IsEditing) _headerTags.Children.Add(HeaderTag("Not created yet"));
        var hasPoster = !string.IsNullOrWhiteSpace(ViewModel.CurrentPosterUrl);
        _currentHeader.ColumnDefinitions[0].Width = hasPoster ? GridLength.Auto : new(0);
        _currentHeader.ColumnSpacing = hasPoster ? 20 : 0;
        Grid.SetRow(_headerActions, phone ? 1 : 0);
        _currentHeader.RowSpacing = phone && ViewModel.IsEditing ? 12 : 0;
        Grid.SetColumn(_headerActions, phone ? (hasPoster ? 0 : 1) : 2);
        Grid.SetColumnSpan(_headerActions, phone && hasPoster ? 2 : 1);
        _headerActions.Children.Clear(); _headerActions.Visibility = ViewModel.IsEditing ? Visibility.Visible : Visibility.Collapsed;
        if (!ViewModel.IsEditing || ViewModel.CollectionId == null) return;
        var id = ViewModel.CollectionId;
        var open = new Button { Content = "Open", Style = (Style)Application.Current.Resources["OutlineButtonStyle"], MinHeight = 32, Padding = new(12, 0, 12, 0) };
        open.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
            { CollectionId = id, Title = ViewModel.Name, IsUserCollection = true });
        _headerActions.Children.Add(open);
        var menu = new MenuFlyout();
        if (ViewModel.IsImportedCollection && ViewModel.Capabilities?.Imports != false)
        {
            var dirty = _draftBaseline != null && CaptureDraft().Any(pair => _draftBaseline.GetValueOrDefault(pair.Key) != pair.Value);
            var sync = new MenuFlyoutItem { Text = "Sync now", IsEnabled = !ViewModel.IsSaving && !dirty };
            if (dirty) ToolTipService.SetToolTip(sync, "Save changes before syncing.");
            sync.Click += async (_, _) => await ViewModel.SyncNowCommand.ExecuteAsync(null); menu.Items.Add(sync); menu.Items.Add(new MenuFlyoutSeparator());
        }
        var delete = new MenuFlyoutItem { Text = "Delete…", IsEnabled = !ViewModel.IsSaving && !ViewModel.IsReadOnly };
        delete.Click += async (_, _) =>
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Delete " + ViewModel.Name + "?", Content = "The collection will be removed. Its titles stay in your libraries.", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await ViewModel.DeleteCommand.ExecuteAsync(null);
        };
        menu.Items.Add(delete);
        var more = new Button { Content = "⋯", Flyout = menu, Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(0), Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(more, "More actions"); _headerActions.Children.Add(more);
    }

    private void UpdateLookPresentation(bool phone)
    {
        var state = $"{phone}|{_lookOpen}|{ViewModel.PosterFileName}|{ViewModel.PosterSourceUrl}|{ViewModel.CurrentPosterUrl}|{ViewModel.CurrentPosterIsCollage}|{ViewModel.RemovePosterOnSave}";
        if (_lastLookState == state) return;
        _lastLookState = state;
        _lookToggle.Padding = _lookOpen ? new(0) : new(16, 16, phone ? 16 : 24, 16);
        // Collapsed templates can have logical children without a visual Parent.
        // Release the previous row's owned controls before replacing its content.
        RemoveFrom(_lookToggle.Content, _lookCover);
        RemoveFrom(_lookToggle.Content, _lookSummary);
        if (_lookOpen) { _lookToggle.Content = "Look · Done"; return; }
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _lookCover.Child = _lookThumbnail; _lookThumbnail.Source = ViewModel.RemovePosterOnSave ? null : ImportedPosterPreview.Source ?? _headerPoster.Source;
        if (_lookThumbnail.Source == null) _lookCover.Child = new TextBlock { Text = "▦", FontSize = 20, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Foreground = CurrentBrush("SecondaryTextBrush") };
        _lookCover.Background = CurrentBrush("SurfaceBrush");
        if (_lookCover.Parent is Panel oldCoverParent) oldCoverParent.Children.Remove(_lookCover);
        row.Children.Add(_lookCover);
        _lookSummary.Text = ViewModel.PosterFileName != null ? "Poster: uploaded image" : !string.IsNullOrWhiteSpace(ViewModel.PosterSourceUrl) ? "Poster: image from a link" : !ViewModel.RemovePosterOnSave && !ViewModel.CurrentPosterIsCollage && _headerPoster.Source != null ? "Poster: custom image" : "Poster: a collage of its titles";
        _lookSummary.Foreground = CurrentBrush("SecondaryTextBrush");
        if (_lookSummary.Parent is Panel oldSummaryParent) oldSummaryParent.Children.Remove(_lookSummary);
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = "Look", FontSize = 15, FontWeight = FontWeights.SemiBold }); labels.Children.Add(_lookSummary);
        Grid.SetColumn(labels, 1); row.Children.Add(labels);
        if (!phone)
        {
            var change = new Border { Height = 32, VerticalAlignment = VerticalAlignment.Center, Padding = new(12, 0, 12, 0), CornerRadius = new(10), BorderThickness = new(1), BorderBrush = CurrentBrush("BorderBrush"),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = {
                    new TextBlock { Text = "Change", FontSize = 13.5, FontWeight = FontWeights.Medium }, WebUiIcon.Create("chevron-down", 14, CurrentBrush("SecondaryTextBrush")) } } };
            Grid.SetColumn(change, 2); row.Children.Add(change);
        }
        _lookToggle.Content = row;
    }
}
