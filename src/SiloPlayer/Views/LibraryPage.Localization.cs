namespace SiloPlayer.Views;

public sealed partial class LibraryPage
{
    private void RefreshLibraryLocalization()
    {
        ViewModel.RefreshLocalization();
        UpdateMultiSelectLabels();
        UpdateCountDisplay();
        UpdateActiveFilterBadges();
        if (ViewModel.Library is not { } library) return;
        var suppressed = _suppressFilterEvents;
        _suppressFilterEvents = true;
        try
        {
            UpdateSortOptions(library.Type, ViewModel.SelectedSort, ViewModel.SelectedType);
            ConfigureBrowseTypeSelector(library.Type, ViewModel.SelectedType);
            ConfigureFilterSections(library.Type);
        }
        finally { _suppressFilterEvents = suppressed; }
    }
}
