using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

/// <summary>Filters existing native sort choices using current server capability and media relevance.</summary>
internal static class CatalogSortChoices
{
    internal static async Task<IReadOnlySet<string>> LoadShownSourcesAsync(SiloPlayer.Core.Api.CatalogApi api)
    {
        try { return await api.GetShownRatingSourcesAsync(); }
        catch { return api.CachedShownRatingSources; }
    }
    internal sealed record Choice(object? Label, string Value);
    internal static Choice[] Capture(ComboBox combo) => combo.Items.OfType<ComboBoxItem>()
        .Select(item => new Choice(item.Content, item.Tag as string ?? "")).ToArray();

    internal static string Apply(ComboBox combo, IReadOnlyList<Choice> choices, IReadOnlySet<string> shownSources,
        string? scope, string? selected, bool keepSavedEditorSort = false)
    {
        var keptField = keepSavedEditorSort ? Field(selected) : null;
        combo.Items.Clear();
        foreach (var choice in choices)
        {
            var field = Field(choice.Value);
            if (field.Length > 0 && (!CatalogRatingSortPolicy.IsAvailable(field, shownSources, keptField)
                || !CatalogRatingSortPolicy.AppliesToScope(field, scope))) continue;
            var item = new ComboBoxItem { Content = choice.Label, Tag = choice.Value };
            combo.Items.Add(item);
            if (choice.Value == selected) combo.SelectedItem = item;
        }
        if (combo.SelectedItem == null && combo.Items.Count > 0) combo.SelectedIndex = 0;
        return (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    }
    private static string Field(string? value) => (value ?? "").Split(':', 2)[0];
}
