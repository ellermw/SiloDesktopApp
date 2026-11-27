using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Services;
using Windows.Storage;
using Windows.System;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private async void ReviewArtworkCache_Click(object sender, RoutedEventArgs e)
    {
        ReviewArtworkCacheButton.IsEnabled = false;
        ArtworkCacheReviewStatus.Text = L("native.settings.artworkCache.checking", "Checking for old cache files…");
        try
        {
            var review = await App.Services.GetRequiredService<ImageService>().ReviewLegacyArtworkStorageAsync();
            ArtworkCacheReviewStatus.Text = "";
            if (!IsLoaded || DevicesPanel.Visibility != Visibility.Visible || XamlRoot is not { } root) return;
            if (review is not { } storage)
            {
                ArtworkCacheReviewStatus.Text = L("native.settings.artworkCache.notLinked", "The artwork cache is not using a linked folder.");
                return;
            }

            var size = Count(storage.CandidateBytes);
            var estimate = L("native.settings.artworkCache.estimate",
                $"Files that look like old artwork cache data in this folder: {size} bytes. This estimate includes only readable, matching files and does not prove they belong to Silo.",
                Args(("size", size)));
            var caution = L("native.settings.artworkCache.caution",
                $"To reclaim space, close Silo and remove only files you know came from its old artwork cache. Do not remove the shared folder or the active cache ({storage.ActiveDirectoryName}).",
                Args(("active_directory", storage.ActiveDirectoryName)));
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = L("native.settings.artworkCache.dialogTitle", "Review old artwork cache files"),
                Content = new TextBlock { Text = $"{storage.TargetPath}\n\n{estimate}\n\n{caution}", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                PrimaryButtonText = L("native.settings.artworkCache.openFolder", "Open folder for manual cleanup"),
                CloseButtonText = L("native.settings.artworkCache.close", "Close"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && IsLoaded)
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(storage.TargetPath);
                if (!await Launcher.LaunchFolderAsync(folder)) throw new IOException("Could not open the artwork cache folder.");
            }
        }
        catch (OperationCanceledException) { ArtworkCacheReviewStatus.Text = ""; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            ArtworkCacheReviewStatus.Text = L("native.settings.artworkCache.unavailable", "Could not review or open the linked artwork cache folder.");
        }
        finally { ReviewArtworkCacheButton.IsEnabled = true; }
    }
}
