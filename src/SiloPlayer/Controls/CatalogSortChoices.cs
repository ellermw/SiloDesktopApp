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
    internal sealed record Choice(object? Label, string Value, string? TitleKey = null);
    internal static Choice[] Capture(ComboBox combo) => combo.Items.OfType<ComboBoxItem>()
        .Select(item => new Choice(item.Content, item.Tag as string ?? "", SiloPlayer.Services.Localized.GetContent(item))).ToArray();

    internal static string Apply(ComboBox combo, IReadOnlyList<Choice> choices, IReadOnlySet<string> shownSources,
        string? scope, string? selected, bool keepSavedEditorSort = false, bool preserveItems = false)
    {
        var keptField = keepSavedEditorSort ? Field(selected) : null;
        var available = choices.Where(choice => Field(choice.Value).Length == 0 ||
            (CatalogRatingSortPolicy.IsAvailable(Field(choice.Value), shownSources, keptField)
                && CatalogRatingSortPolicy.AppliesToScope(Field(choice.Value), scope))).ToArray();
        // Selection and save-state refreshes must not replace the rows under
        // an open popup while the user is navigating with the keyboard.
        if (!preserveItems || combo.Items.Count != available.Length ||
            !combo.Items.OfType<ComboBoxItem>().Select(item => item.Tag as string).SequenceEqual(available.Select(choice => choice.Value)))
        {
            combo.Items.Clear();
            foreach (var choice in available)
                combo.Items.Add(MediaItemCardActions.Localize(new ComboBoxItem { Content = choice.Label, Tag = choice.Value }, content: choice.TitleKey));
            // New items do not load while the dropdown is closed: apply their keys before one is selected.
            SiloPlayer.Services.Localized.RefreshItems(combo);
        }
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, selected))
            ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        return (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    }
    private static string Field(string? value) => (value ?? "").Split(':', 2)[0];
}
