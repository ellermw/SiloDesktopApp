using SiloPlayer.Core.Models.Collections;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private string _syncedFilledName = "";
    private string _syncedFilledDescription = "";
    private string? _syncedFilledLimit;
    private string _syncedFilledSchedule = "";
    private string? _syncedPickedPosterUrl;
    private string? _syncedKeptFields;

    private void ApplySyncedPickFields(string name, string description, int? limit, string? schedule, string? poster = null)
    {
        // Follow the next pick only while the field still holds the preceding
        // pick's value. User-entered names, limits and schedules survive.
        var keepName = !string.IsNullOrWhiteSpace(ViewModel.Name) && ViewModel.Name != _syncedFilledName;
        var keepDescription = !string.IsNullOrWhiteSpace(ViewModel.Description) && ViewModel.Description != _syncedFilledDescription;
        if (!keepName) ViewModel.Name = name;
        if (!keepDescription) ViewModel.Description = description;
        // The catalog key of what was kept; worded when shown, so a language change rewords it.
        _syncedKeptFields = keepName && keepDescription ? "web.collections.copy.keptNameAndDescription"
            : keepName ? "web.collections.copy.keptName" : keepDescription ? "web.collections.copy.keptDescription" : null;
        var limitText = limit?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if ((string.IsNullOrWhiteSpace(ViewModel.MaxItemsText) ? null : ViewModel.MaxItemsText) == _syncedFilledLimit) ViewModel.MaxItemsText = limitText;
        if ((ViewModel.SyncSchedule ?? "") == _syncedFilledSchedule) ViewModel.SyncSchedule = schedule ?? "";
        _syncedFilledName = name; _syncedFilledDescription = description; _syncedFilledLimit = limitText; _syncedFilledSchedule = schedule ?? "";
        _syncedPickedPosterUrl = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>().ResolveServerUrl(poster);
        if (ViewModel.PosterFileBytes == null && string.IsNullOrWhiteSpace(ViewModel.PosterSourceUrl) && !ViewModel.RemovePosterOnSave)
        {
            ViewModel.CurrentPosterIsCollage = false;
            ViewModel.CurrentPosterUrl = _syncedPickedPosterUrl;
        }
        _titleLimit.Text = ViewModel.MaxItemsText ?? "";
        _newSchedule.SelectedItem = _newSchedule.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, ViewModel.SyncSchedule ?? ""));
    }

    private void ApplySyncedTemplateFields(CollectionTemplate template) => ApplySyncedPickFields(template.Title, template.Description,
        template.DefaultLimit > 0 ? template.DefaultLimit : null, SyncedNamedSchedule(template.DefaultSyncSchedule), template.PosterPath);

    private void ApplySyncedChartFields()
    {
        if (_pickingTemplate || !_newSynced || _syncedSource != "tmdb" || _chartPreset.SelectedItem is not ComboBoxItem chosen) return;
        var preset = chosen.Tag?.ToString(); var media = (_chartMedia.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var window = preset == "trending" ? (_chartWindow.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "day" : null;
        var template = _syncedImports?.TemplateGroups.SelectMany(group => group.Templates).FirstOrDefault(t => t.Source == "tmdb" && t.Tmdb is { } chart && chart.Preset == preset && chart.MediaType == media &&
            (preset != "trending" || (chart.TimeWindow ?? "day") == window));
        if (template != null) { ApplySyncedTemplateFields(template); return; }
        // This becomes the saved collection name: always English, from the chart's own value, never from the shown label.
        var title = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(ChartNameEnglish(preset));
        var name = string.Join(" ", new[] { title, media switch { "movie" => "Movies", "tv" => "TV Shows", _ => "" }, window switch { "week" => "This Week", "day" => "Today", _ => "" } }.Where(s => s.Length > 0));
        ApplySyncedPickFields(name, "", null, "");
    }
}
