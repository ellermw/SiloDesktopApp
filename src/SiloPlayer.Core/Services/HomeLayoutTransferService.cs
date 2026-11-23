using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public sealed class HomeLayoutTransferService(SettingsApi settings, CatalogApi catalog, CollectionsApi collections, AuthApi account)
{
    private ApiRequestContext? _previewContext;
    private HomeLayoutPlan? _previewPlan;
    public int AppliedPages { get; private set; }
    /// <summary>
    /// After <see cref="ApplyAsync"/> threw: the page that did not save, or null when the Hide watched items setting
    /// (<see cref="FailedHideWatched"/>) did. The import stops at the first failure, so earlier pages stay saved.
    /// </summary>
    public string? FailedPage { get; private set; }
    public bool FailedHideWatched { get; private set; }
    private void Check(ApiRequestContext context, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); if (!settings.IsCurrentContext(context)) throw new OperationCanceledException("The selected server or profile changed."); }
    public async Task<HomeLayoutFile> ExportAsync(CancellationToken ct = default)
    {
        var context = settings.CaptureContext();
        var identity = await settings.GetServerIdentityAsync(ct); Check(context, ct);
        var libraries = await catalog.GetLibrariesAsync(ct); Check(context, ct);
        var file = new HomeLayoutFile { ServerId = identity.ServerId,
            Libraries = libraries.Select(l => new HomeLayoutLibrary { Id = l.Id, Name = l.Name, Type = l.Type }).ToList() };
        var values = await settings.GetEffectiveSettingsAsync(["home.hide_watched_items"], ct); Check(context, ct);
        var hide = values.Settings.FirstOrDefault();
        if (hide?.Source == "profile" && bool.TryParse(hide.EffectiveValue, out var hidden)) file.HideWatchedItems = hidden;
        foreach (var page in new[] { new HomeLayoutPage() }.Concat(libraries.Select(l => new HomeLayoutPage { Scope = "library", LibraryId = l.Id })))
        {
            var libraryId = page.LibraryId?.ToString(CultureInfo.InvariantCulture);
            var stored = await settings.GetProfileSectionsAsync(page.Scope, libraryId, ct); Check(context, ct);
            page.Overrides = stored.Overrides.Select(HomeLayoutTransfer.ToWrite).ToList();
            // The tokens are optional: without them the page still exports, and an import of it offers no rows to hide.
            page.ServerSectionTokens = await ReadBaselineAsync(page.Scope, libraryId, ct) is { } baseline ? baseline.SectionTokens : null;
            Check(context, ct);
            file.Pages.Add(page);
        }
        HomeLayoutTransfer.Serialize(file); return file;
    }
    // A baseline that cannot be read is no baseline: the request failure is not the viewer's to fix, but a canceled request still ends the whole operation.
    private async Task<ProfileSectionBaseline?> ReadBaselineAsync(string scope, string? libraryId, CancellationToken ct)
    {
        try { return await settings.GetProfileSectionBaselineAsync(scope, libraryId, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { ct.ThrowIfCancellationRequested(); return null; }
    }
    public async Task<HomeLayoutPlan> PreviewAsync(string text, CancellationToken ct = default)
    {
        var file = HomeLayoutTransfer.Parse(text); var context = settings.CaptureContext(); _previewContext = null;
        var identity = await settings.GetServerIdentityAsync(ct); Check(context, ct);
        var libraries = await catalog.GetLibrariesAsync(ct); Check(context, ct);
        var recipes = await settings.GetRecipeCatalogAsync(ct); Check(context, ct);
        var personal = await collections.GetCollectionsAsync(ct); Check(context, ct);
        // Profile references are queried through the same selected server/client authority.
        var profiles = await account.GetProfilesAsync(ct); Check(context, ct);
        var target = new HomeLayoutTarget(identity.ServerId,
            libraries.Select(l => new HomeLayoutLibrary { Id = l.Id, Name = l.Name, Type = l.Type }).ToArray(),
            recipes.Categories.Values.SelectMany(r => r).GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.First().AdminOnly),
            true,
            personal.Collections.Select(c => c.Id).ToHashSet(), profiles.Profiles.Select(p => p.Id).ToHashSet());
        var plan = HomeLayoutTransfer.Plan(file, target);
        await OfferRowsToHideAsync(plan, context, ct);
        _previewContext = context; _previewPlan = plan; return plan;
    }
    // Only a same-server page whose export recorded the server's rows can tell which rows are new; a file without them
    // imports as it did before. A page whose baseline can't be read offers nothing, so one failed read never blocks the rest.
    private async Task OfferRowsToHideAsync(HomeLayoutPlan plan, ApiRequestContext context, CancellationToken ct)
    {
        if (!plan.SameServer) return;
        var pages = plan.Pages.Concat(plan.UnappliedPages).Where(page => page.ServerSectionTokens is { Count: > 0 }).ToList();
        plan.PagesWithTokens = pages.Count;
        foreach (var page in pages)
        {
            var baseline = await ReadBaselineAsync(page.Scope, page.LibraryId?.ToString(CultureInfo.InvariantCulture), ct); Check(context, ct);
            if (baseline == null) continue;
            var key = HomeLayoutBaseline.PageKey(page.Scope, page.LibraryId);
            foreach (var row in HomeLayoutBaseline.RowsAddedSinceExport(page, baseline))
                plan.HideOptions.Add(new() { Key = key + "/" + row.Id, PageKey = key, PageLabel = page.Label, Row = row });
        }
    }
    public async Task ApplyAsync(HomeLayoutPlan plan, CancellationToken ct = default)
    {
        if (!ReferenceEquals(plan, _previewPlan)) throw new InvalidOperationException("Apply the previewed layout only.");
        var context = _previewContext ?? throw new InvalidOperationException("Preview the layout before applying it."); Check(context, ct);
        AppliedPages = 0; FailedPage = null; FailedHideWatched = false;
        foreach (var page in plan.Pages.Concat(plan.CheckedUnappliedPages).ToList())
        {
            try { await ApplyPageAsync(plan, page, context, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { FailedPage = page.Label; throw; }
        }
        if (plan.HideWatchedItems.HasValue)
        {
            try
            {
                Check(context, ct);
                await settings.SetContractSettingValueAsync(context, "home.hide_watched_items", "profile", plan.HideWatchedItems.Value, ct: ct);
                Check(context, ct); settings.NotifyLocalizedSettingChanged("home.hide_watched_items");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { FailedHideWatched = true; throw; }
        }
    }
    private Task ApplyPageAsync(HomeLayoutPlan plan, HomeLayoutPage page, ApiRequestContext context, CancellationToken ct)
        => settings.RunSectionMutationAsync(async () =>
        {
            Check(context, ct);
            var libraryId = page.LibraryId?.ToString(CultureInfo.InvariantCulture);
            var hideIds = plan.SameServer ? plan.HideIds(page) : [];
            if (hideIds.Count > 0)
            {
                // The fresh read only drops rows removed since the preview; when it fails, the preview's choices stand.
                var baseline = await ReadBaselineAsync(page.Scope, libraryId, ct); Check(context, ct);
                if (baseline != null)
                {
                    var visible = baseline.VisibleSections.Select(row => row.Id).ToHashSet();
                    hideIds = hideIds.Where(visible.Contains).ToList();
                }
            }
            // A hide-only page with nothing left to hide has nothing to save.
            if (!page.ReplaceExisting && hideIds.Count == 0) return;
            var existing = await settings.GetProfileSectionsAsync(page.Scope, libraryId, ct); Check(context, ct);
            var overrides = HomeLayoutMerge.Build(page, existing.Overrides, plan.SameServer, hideIds);
            if (overrides.Count > HomeLayoutTransfer.MaximumPageOverrides)
                throw new InvalidDataException(HomeLayoutTransfer.T("web.home.layout.transfer.pageTooLarge",
                    $"this page would have more than {HomeLayoutTransfer.MaximumPageOverrides} saved section changes", ("max", HomeLayoutTransfer.MaximumPageOverrides)));
            Check(context, ct);
            await settings.UpdateProfileSectionsAsync(new() { Scope = page.Scope, LibraryId = libraryId, Overrides = overrides }, ct);
            Check(context, ct); AppliedPages++;
        }, ct);
}
