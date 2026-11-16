using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Views;

namespace SiloPlayer;

public sealed partial class MainWindow
{
    private readonly HashSet<(long Owner, long Language, int Library, bool Collections)> _requestedSidebarPinTitles = [];

    private string PinLabel(int? libraryId, string type, string id, string fallback) =>
        libraryId is int owner && !_viewModel.Libraries.Any(library => library.Id == owner)
            ? fallback : _catalogApi.ResolvePinLabel(libraryId, type, id, fallback);

    public string ResolveMenuPinLabel(PrimaryMenuItem item) => PinLabel(item.LibraryId, item.Type,
        item.Type == "section" ? item.SectionId ?? "" : item.CollectionId ?? "", item.Label ?? "");

    private void OnPinTitlesChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (_windowClosed || !CanExposeAuthenticatedNavigation) return;
        QueueSidebarPublication();
        EnsureSidebarPinTitles();
        if (ContentFrame.Content is SettingsPage settings) settings.RefreshInterfacePinLabels();
        if (ContentFrame.Content is LibraryPage library) library.RefreshPinnedCollectionTitles();
    });

    private void OnPinMetadataLanguageChanged(long generation) => OnPinTitlesChanged();

    private void EnsureSidebarPinTitles()
    {
        if (!_criticalContentLoaded || !CanExposeAuthenticatedNavigation || !_viewModel.HasCurrentLibrarySnapshot) return;
        var wanted = new HashSet<(int Library, bool Collections)>();
        foreach (var library in _viewModel.Libraries)
            if (_sidebarPins.TryGetValue(library.Id.ToString(), out var pins))
                foreach (var pin in pins)
                    wanted.Add((library.Id, pin.Type == "collection"));
        if (_sidebarCustomizationReady && _uiCustomizationService.IsSupported)
        foreach (var item in (_uiCustomizationService.PrimaryMenu?.Items ?? []).Concat(_uiCustomizationService.Shortcuts.Items))
            if (item.Type is "section" or "collection" && item.LibraryId is int owner && _viewModel.Libraries.Any(library => library.Id == owner))
                wanted.Add((owner, item.Type == "collection"));
        var language = _apiClient.LocalizedReads.Generation;
        _requestedSidebarPinTitles.RemoveWhere(pin => pin.Owner != _sidebarRetirementGeneration || pin.Language != language ||
            !wanted.Contains((pin.Library, pin.Collections)));
        foreach (var pin in wanted)
            if (_requestedSidebarPinTitles.Add((_sidebarRetirementGeneration, language, pin.Library, pin.Collections)))
                _ = RunShellWorkAsync("pin_titles", () => _catalogApi.EnsureLibraryPinTitlesAsync(pin.Library, pin.Collections));
    }
}
