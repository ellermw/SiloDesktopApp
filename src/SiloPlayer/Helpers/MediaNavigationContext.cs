using Microsoft.UI.Xaml.Media;
using SiloPlayer.Views;

namespace SiloPlayer.Helpers;

public sealed record ItemDetailNavigationArgs(string ContentId, int? LibraryId = null);

public static class MediaNavigationContext
{
    public static int? LibraryId(DependencyObject source)
    {
        for (DependencyObject? node = source; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is LibraryPage library) return library.ViewModel.Library?.Id;
            if (node is CollectionBrowsePage collection) return collection.CurrentLibraryId;
            if (node is ItemDetailPage detail) return detail.ViewModel.LibraryId;
        }
        return null;
    }

    public static object Detail(string contentId, int? libraryId) => libraryId.HasValue
        ? new ItemDetailNavigationArgs(contentId, libraryId) : contentId;
}
