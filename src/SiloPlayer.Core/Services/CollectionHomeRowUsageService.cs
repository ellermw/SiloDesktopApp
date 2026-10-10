using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>Where a personal collection appears among this profile's resolved Home and library rows.</summary>
public sealed class CollectionHomeRowUsageService(CatalogApi catalog, SettingsApi settings)
{
    public async Task<IReadOnlyList<CollectionHomeRowUsage>> GetAsync(string collectionId, CancellationToken ct = default)
    {
        var context = settings.CaptureContext();
        var libraries = await catalog.GetLibrariesAsync(ct);
        if (!settings.IsCurrentContext(context)) throw new OperationCanceledException("The selected profile changed.", ct);
        var pages = new[] { (Scope: "home", Id: (int?)null, Name: "Home") }
            .Concat(libraries.Select(library => (Scope: "library", Id: (int?)library.Id, Name: library.Name))).ToList();
        using var budget = new SemaphoreSlim(4);
        var tasks = pages.Select(async page =>
        {
            await budget.WaitAsync(ct);
            try
            {
                if (!settings.IsCurrentContext(context)) throw new OperationCanceledException("The selected profile changed.", ct);
                var resolved = await settings.GetProfileSectionSettingsAsync(page.Scope, page.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
                return resolved.Sections.Where(row => row.SectionType == "collection" && row.Config != null && row.Config.TryGetValue("user_collection_id", out var id)
                        && string.Equals(id?.ToString(), collectionId, StringComparison.Ordinal))
                    .Select(row => new CollectionHomeRowUsage(page.Scope, page.Id, page.Name, row.Id, row.Title, row.Hidden)).ToList();
            }
            finally { budget.Release(); }
        }).ToArray();
        var result = await Task.WhenAll(tasks);
        if (!settings.IsCurrentContext(context)) throw new OperationCanceledException("The selected profile changed.", ct);
        return result.SelectMany(rows => rows).ToList();
    }
}
