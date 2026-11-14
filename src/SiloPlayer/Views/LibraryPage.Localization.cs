namespace SiloPlayer.Views;

public sealed partial class LibraryPage
{
    private void RefreshLibraryLocalization()
    {
        RefreshInlineChoices();
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

    private void RefreshInlineChoices()
    {
        var previous = _suppressFilterEvents;
        _suppressFilterEvents = true;
        try
        {
            SiloPlayer.Services.Localized.RefreshItems(OrderComboBox);
            SiloPlayer.Services.Localized.RefreshItems(AudiobookGroupSortComboBox);
            SiloPlayer.Services.Localized.RefreshItems(MediaTypeComboBox);
            SiloPlayer.Services.Localized.RefreshItems(DecadeComboBox);
            SiloPlayer.Services.Localized.RefreshItems(MatchStatusComboBox);
            SiloPlayer.Services.Localized.RefreshItems(WatchStatusComboBox);
        }
        finally { _suppressFilterEvents = previous; }
    }
}
