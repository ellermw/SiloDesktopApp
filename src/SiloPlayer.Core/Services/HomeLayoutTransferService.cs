using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public sealed class HomeLayoutTransferService(SettingsApi settings, CatalogApi catalog, CollectionsApi collections, AuthApi account, AuthService auth)
{
    private ApiRequestContext? _previewContext;
    private HomeLayoutPlan? _previewPlan;
    public int AppliedPages { get; private set; }
    public bool KeptSavedChanges { get; private set; }
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
            var stored = await settings.GetProfileSectionsAsync(page.Scope, page.LibraryId?.ToString(CultureInfo.InvariantCulture), ct); Check(context, ct);
            page.Overrides = stored.Overrides.Select(HomeSectionWritePolicy.FromRaw).ToList();
            foreach (var row in page.Overrides) row.Id = null;
            if (page.Overrides.Count > 0) file.Pages.Add(page);
        }
        HomeLayoutTransfer.Serialize(file); return file;
    }
    public async Task<HomeLayoutPlan> PreviewAsync(string text, CancellationToken ct = default)
    {
        var file = HomeLayoutTransfer.Parse(text); var context = settings.CaptureContext(); _previewContext = null;
        var identity = await settings.GetServerIdentityAsync(ct); Check(context, ct);
        var libraries = await catalog.GetLibrariesAsync(ct); Check(context, ct);
        var recipes = await settings.GetRecipeCatalogAsync(ct); Check(context, ct);
        var flags = await settings.GetSectionFlagsAsync(ct); Check(context, ct);
        var personal = await collections.GetCollectionsAsync(ct); Check(context, ct);
        // Profile references are queried through the same selected server/client authority.
        var profiles = await account.GetProfilesAsync(ct); Check(context, ct);
        var target = new HomeLayoutTarget(identity.ServerId,
            libraries.Select(l => new HomeLayoutLibrary { Id = l.Id, Name = l.Name, Type = l.Type }).ToArray(),
            recipes.Categories.Values.SelectMany(r => r).GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.First().AdminOnly),
            auth.CurrentUser?.Role == "admin" || flags.AllowProfileCustomSections,
            personal.Collections.Select(c => c.Id).ToHashSet(), profiles.Profiles.Select(p => p.Id).ToHashSet());
        var plan = HomeLayoutTransfer.Plan(file, target); _previewContext = context; _previewPlan = plan; return plan;
    }
    public async Task ApplyAsync(HomeLayoutPlan plan, CancellationToken ct = default)
    {
        if (!ReferenceEquals(plan, _previewPlan)) throw new InvalidOperationException("Apply the previewed layout only.");
        var context = _previewContext ?? throw new InvalidOperationException("Preview the layout before applying it."); Check(context, ct);
        AppliedPages = 0;
        KeptSavedChanges = false;
        foreach (var page in plan.Pages)
        {
            await settings.RunSectionMutationAsync(async () =>
            {
            Check(context, ct);
            var libraryId = page.LibraryId?.ToString(CultureInfo.InvariantCulture);
            var existing = await settings.GetProfileSectionsAsync(page.Scope, libraryId, ct); Check(context, ct);
            var keep = existing.Overrides.Where(row => !string.IsNullOrEmpty(row.SectionId)
                && HomeSectionWritePolicy.IsTrakt(HomeSectionWritePolicy.FromRaw(row).Config)).Select(row => row.SectionId).ToHashSet();
            if (plan.SameServer)
            {
                var view = await settings.GetProfileSectionSettingsAsync(page.Scope, libraryId, ct); Check(context, ct);
                foreach (var row in view.Sections) if (!row.IsCustom && HomeSectionWritePolicy.IsTrakt(row.Config)) keep.Add(row.Id);
            }
            async Task SaveAsync()
            {
                var overrides = HomeLayoutMerge.Build(page, existing.Overrides, plan.SameServer, keep);
                if (overrides.Count > 500) throw new InvalidDataException("This page would have more than 500 saved section changes.");
                Check(context, ct);
                await settings.UpdateProfileSectionsAsync(new() { Scope = page.Scope, LibraryId = libraryId, Overrides = overrides }, ct);
            }
            try { await SaveAsync(); }
            catch (ApiException ex) when (ex.StatusCode == 422 && plan.SameServer && existing.Overrides.Any(row => !string.IsNullOrEmpty(row.SectionId)))
            {
                Check(context, ct);
                foreach (var row in existing.Overrides) if (!string.IsNullOrEmpty(row.SectionId)) keep.Add(row.SectionId);
                await SaveAsync(); KeptSavedChanges = true;
            }
            Check(context, ct); AppliedPages++;
            }, ct);
        }
        if (plan.HideWatchedItems.HasValue)
        { Check(context, ct); await settings.PutSettingAsync("home.hide_watched_items", plan.HideWatchedItems.Value ? "true" : "false", ct); Check(context, ct); }
    }
}
