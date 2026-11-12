using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Helpers;

/// <summary>
/// The confirmation both the Collections card menu and the collection page's More actions menu use before a personal
/// collection is deleted. The dialog stays open, with the error under the text, until the delete succeeds or is cancelled.
/// </summary>
internal static class CollectionDeleteDialog
{
    /// <param name="delete">Deletes the collection. Returns null when it is gone, or the message to show when it could not be deleted.</param>
    /// <returns>True when the collection was deleted.</returns>
    internal static async Task<bool> ShowAsync(XamlRoot? xamlRoot, string name, bool shared, Func<Task<string?>> delete)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = CollectionHeroActions.DeleteDescription(shared), TextWrapping = TextWrapping.Wrap });
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = CollectionHeroActions.DeleteTitle(name),
            Content = content,
            PrimaryButtonText = "Delete",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            XamlRoot = xamlRoot,
            DefaultButton = ContentDialogButton.Close
        };
        var deleted = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.PrimaryButtonText = "Deleting...";
                error.Visibility = Visibility.Collapsed;
                string? failure;
                try { failure = await delete(); }
                catch (Exception ex) { failure = "The collection could not be deleted: " + ex.Message; }
                if (failure == null) { deleted = true; args.Cancel = false; return; }
                error.Text = failure;
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                dialog.PrimaryButtonText = "Delete";
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
        return deleted;
    }
}
