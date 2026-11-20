using System.Text.Json;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public sealed partial class AdminHomeSectionsApi
{
    private const string Root = "/api/v2/admin/sections";

    private async Task<ApiResponse<T>> SendAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path,
        object? body = null, string? etag = null, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        return await client.AdminHomeSectionsSendAsync<T>(lease, method, path, body, etag, deadline.Token).ConfigureAwait(false);
    }

    public async Task<AdminHomeSectionCapabilities> CapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        (await SendAsync<AdminHomeSectionCapabilities>(lease, HttpMethod.Get, Root + "/capabilities", ct: ct).ConfigureAwait(false)).Body;

    public async Task<AdminHomeSectionCatalog> CatalogAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var value = (await SendAsync<AdminHomeSectionCatalog>(lease, HttpMethod.Get, "/api/v2/sections/recipes", ct: ct).ConfigureAwait(false)).Body;
        if (value.Categories == null || value.Categories.Count > 50 || value.Categories.Any(group => group == null || group.Recipes == null || group.Recipes.Count > 100 ||
            group.Recipes.Any(recipe => recipe == null || string.IsNullOrWhiteSpace(recipe.Type) || recipe.Presets == null || recipe.Presets.Count > 100 || recipe.Presets.Any(preset => preset == null || preset.DefaultParams == null))))
            throw new InvalidDataException("Invalid row catalog.");
        return value;
    }

    public async Task<AdminHomeSectionSession> GetAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default, AdminHomeSectionScope? scope = null)
    {
        var response = await SendAsync<AdminHomeSection>(lease, HttpMethod.Get, RowPath(id), ct: ct).ConfigureAwait(false);
        ValidateRow(response.Body, id, scope ?? AdminHomeSectionScope.Home);
        if (!AdminValidators.IsStrong(response.ETag)) throw new InvalidDataException("A strong row version is required. Reload the row.");
        return new(response.Body, response.ETag!, lease);
    }

    public Task<AdminHomeSection> CreateAsync(MonitorAuthorityLease lease, AdminHomeSectionDraft draft, int position, bool webOnlySupported,
        CancellationToken ct = default, AdminHomeSectionScope? scope = null)
    {
        scope ??= AdminHomeSectionScope.Home;
        draft.Validate();
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        var body = draft.Body(webOnlySupported); body["scope"] = scope.Kind; body["position"] = position;
        _ = scope.Query;
        if (scope.LibraryId != null) body["library_id"] = scope.LibraryId;
        return MutationAsync(async () =>
        {
            var response = await SendAsync<AdminHomeSection>(lease, HttpMethod.Post, Root, body, ct: ct).ConfigureAwait(false);
            ValidateRow(response.Body, scope: scope); return response.Body;
        }, lease);
    }

    public Task<AdminHomeSection> SaveAsync(MonitorAuthorityLease lease, AdminHomeSectionSession session, AdminHomeSectionDraft draft,
        bool webOnlySupported = false, CancellationToken ct = default)
    {
        RequireSession(lease, session); draft.Validate();
        return MutationAsync(async () =>
        {
            var response = await SendAsync<AdminHomeSection>(lease, HttpMethod.Patch, RowPath(session.Row.Id), draft.Body(webOnlySupported), session.ETag, ct).ConfigureAwait(false);
            ValidateRow(response.Body, session.Row.Id, new(session.Row.Scope, session.Row.LibraryId)); return response.Body;
        }, lease);
    }

    public Task DeleteAsync(MonitorAuthorityLease lease, AdminHomeSectionSession session, CancellationToken ct = default)
    {
        RequireSession(lease, session);
        return MutationAsync(async () =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
            await client.AdminHomeSectionsDeleteAsync(lease, RowPath(session.Row.Id), session.ETag, deadline.Token).ConfigureAwait(false);
            return true;
        }, lease);
    }

    public Task BulkCopyAsync(MonitorAuthorityLease lease, AdminHomeSectionDraft draft, IReadOnlyList<string> libraryIds, CancellationToken ct = default)
    {
        draft.Validate();
        if (libraryIds.Count is < 1 or > 100 || libraryIds.Distinct().Count() != libraryIds.Count || libraryIds.Any(id => !long.TryParse(id, out var number) || number <= 0 || !id.All(char.IsAsciiDigit)))
            throw new ArgumentException("Choose between 1 and 100 distinct library pages.");
        if (!draft.CanCopy) throw new ArgumentException("This row cannot be copied to other library pages.");
        var body = draft.Body(false); body["scope"] = "library"; body["library_ids"] = libraryIds.ToArray(); body["featured"] = false;
        return MutationAsync(async () =>
        {
            var result = await SendAsync<AdminHomeBulkResult>(lease, HttpMethod.Post, Root + "/bulk", body, ct: ct).ConfigureAwait(false);
            if (result.Body.Created != libraryIds.Count) throw new InvalidDataException("The server did not confirm every copy.");
            return true;
        }, lease);
    }

    public Task RestoreAsync(MonitorAuthorityLease lease, AdminHomeSectionsSnapshot snapshot, bool resetProfiles, CancellationToken ct = default)
    {
        if (!ReferenceEquals(snapshot.Lease, lease)) throw new MonitorAuthorityRetiredException();
        return MutationAsync(async () =>
        {
            await SendAsync<AdminHomeSectionOrder>(lease, HttpMethod.Put, Root + "/defaults" + snapshot.Scope.Query,
                new { reset_profiles = resetProfiles }, snapshot.ETag, ct).ConfigureAwait(false);
            return true;
        }, lease);
    }

    public async Task<AdminHomeSectionPreview> PreviewAsync(MonitorAuthorityLease lease, AdminHomeSectionDraft draft, CancellationToken ct = default, AdminHomeSectionScope? scope = null)
    {
        draft.Validate();
        var body = new Dictionary<string, object?> { ["section_type"] = draft.SectionType, ["config"] = draft.Config.DeepClone(), ["item_limit"] = Math.Min(12, draft.ItemLimit) };
        if (scope?.LibraryId != null) { _ = scope.Query; body["library_id"] = scope.LibraryId; }
        var result = (await SendAsync<AdminHomeSectionPreview>(lease, HttpMethod.Post, Root + "/preview", body, ct: ct).ConfigureAwait(false)).Body;
        if (result.Items.Count > 12 || result.Collections.Count > 50 || result.TotalCount < 0) throw new InvalidDataException("Invalid preview.");
        return result;
    }

    /// <summary>Explicit, single-page pickers. Never walks a catalog; the UI replaces the 50-row window on Next.</summary>
    public async Task<AdminHomeSectionChoices> ChoicesAsync(MonitorAuthorityLease lease, string kind, string query = "", string? cursor = null,
        CancellationToken ct = default)
    {
        if (cursor != null && !AdminPaging.IsValidCursor(cursor)) throw new ArgumentException("Invalid picker cursor.");
        var path = kind switch
        {
            "collections" => "/api/v2/admin/collections",
            "libraries" => "/api/v2/libraries",
            "titles" => "/api/v2/catalog",
            _ => throw new ArgumentException("Unknown picker.")
        };
        if (query.Length > 200) throw new ArgumentException("Search is too long.");
        // The pinned metadata endpoints have no paging/search parameters. Do not invent them.
        // Their response is byte-bounded by the shared transport and capped at 2,000 metadata choices.
        // The view searches and pages that bounded window locally. Media searches are server-paged.
        if (kind == "titles")
        {
            path = AdminPaging.WithPage(path, 50, cursor);
            if (query.Length > 0) path += "&q=" + Uri.EscapeDataString(query);
        }
        else if (cursor != null) throw new ArgumentException("Metadata choices do not support server cursors.");
        var result = (await SendAsync<AdminHomeSectionChoices>(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false)).Body;
        if (result.Items == null || result.Items.Count > (kind == "titles" ? 50 : 2000) || result.Items.Any(item => string.IsNullOrWhiteSpace(item.Identity)) ||
            result.Items.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count() != result.Items.Count ||
            (kind == "titles" && (result.Page is not { } page || (page.HasMore && (result.Items.Count == 0 || !AdminPaging.IsValidCursor(page.NextCursor) || page.NextCursor == cursor)) ||
            (!page.HasMore && !string.IsNullOrEmpty(page.NextCursor))))) throw new InvalidDataException("Invalid picker page.");
        return result;
    }

    public async Task<AdminHomeSectionChoice> CollectionByIdAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200) throw new ArgumentException("Enter a collection ID.");
        var choice = (await SendAsync<AdminHomeSectionChoice>(lease, HttpMethod.Get, "/api/v2/admin/collections/" + Uri.EscapeDataString(id), ct: ct).ConfigureAwait(false)).Body;
        if (choice.Id != id || choice.Visibility != "visible") throw new InvalidDataException("Collection identity or visibility changed.");
        return choice;
    }

    public Task<byte[]> PosterAsync(MonitorAuthorityLease lease, string url, CancellationToken ct = default) =>
        client.AdminLibraryGetPosterAsync(lease, url, ct);

    public async Task<AdminHomeSeasonalCatalog> SeasonalAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        (await SendAsync<AdminHomeSeasonalCatalog>(lease, HttpMethod.Get, "/api/v2/sections/seasonal-themes", ct: ct).ConfigureAwait(false)).Body;

    public async Task<AdminHomeRuleCapabilities> RuleCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var rules = (await SendAsync<AdminHomeRuleCapabilities>(lease, HttpMethod.Get, "/api/v2/catalog/search/capabilities", ct: ct).ConfigureAwait(false)).Body;
        try
        {
            var ratings = (await SendAsync<SiloPlayer.Core.Models.Catalog.RatingsCapability>(lease, HttpMethod.Get, "/api/v2/capabilities/ratings", ct: ct).ConfigureAwait(false)).Body;
            if (ratings.State == "available") rules.ShownRatingSources = ratings.Sources.Select(source => source.Source).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception) when (lease.IsCurrent && !ct.IsCancellationRequested) { }
        lease.ThrowIfRetired(); return rules;
    }

    public async Task<IReadOnlyList<AdminHomeRuleSuggestion>> RuleValuesAsync(MonitorAuthorityLease lease, string field, string kind,
        string query, string mediaScope, IReadOnlyList<string> libraryIds, AdminHomeRuleCapabilities? caps, CancellationToken ct = default)
    {
        if (query.Length > 200 || libraryIds.Count > 1000) throw new ArgumentException("Refine the value search.");
        var scope = string.IsNullOrEmpty(mediaScope) ? "" : "&type=" + Uri.EscapeDataString(mediaScope);
        if (caps?.FacetValueSearch == true && libraryIds.Count > 0) scope += "&library_ids=" + Uri.EscapeDataString(string.Join(',', libraryIds));
        if (kind == "person")
        {
            if (caps?.PeopleMediaScope != true || string.IsNullOrWhiteSpace(query)) return [];
            var result = (await SendAsync<AdminHomeSectionChoices>(lease, HttpMethod.Get,
                "/api/v2/catalog/people?limit=20&q=" + Uri.EscapeDataString(query) + (mediaScope == "" ? "" : "&media_scope=" + Uri.EscapeDataString(mediaScope)), ct: ct).ConfigureAwait(false)).Body;
            if (result.Items.Count > 20) throw new InvalidDataException("Too many people suggestions.");
            return result.Items.Select(person => new AdminHomeRuleSuggestion(person.Name, person.Name)).ToArray();
        }
        if (kind == "language")
        {
            var result = (await SendAsync<SiloPlayer.Core.Models.Catalog.CatalogFiltersResponse>(lease, HttpMethod.Get,
                "/api/v2/catalog/filters?source=query" + scope + (field == "original_language" ? "&skip_technical=true" : ""), ct: ct).ConfigureAwait(false)).Body;
            var values = field == "original_language" ? result.OriginalLanguages : field == "audio_language" ? result.AudioLanguages : result.SubtitleLanguages;
            if (values.Count > 2000) throw new InvalidDataException("Too many language choices.");
            return values.Select(value =>
            {
                string label; try { label = System.Globalization.CultureInfo.GetCultureInfo(value).DisplayName; } catch (System.Globalization.CultureNotFoundException) { label = value; }
                return new AdminHomeRuleSuggestion(value, label);
            }).Where(value => value.Value.Contains(query, StringComparison.OrdinalIgnoreCase) || value.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Take(50).ToArray();
        }
        if (kind != "facet" || QueryFieldCatalog.Get(field)?.Kind != "facet") return [];
        if (caps?.FacetValueSearch != true && string.IsNullOrWhiteSpace(query)) return [];
        var facets = (await SendAsync<HomeRuleFacetValues>(lease, HttpMethod.Get,
            "/api/v2/catalog/filters/search?source=query&facet=" + Uri.EscapeDataString(field) + "&q=" + Uri.EscapeDataString(query) + "&limit=50" + scope, ct: ct).ConfigureAwait(false)).Body;
        var matches = caps?.FacetValueSearch == true ? facets.Values.Select(value => value.Value).ToArray() : facets.Matches.ToArray();
        if (matches.Length > 50) throw new InvalidDataException("Too many facet suggestions.");
        return matches.Select(value => new AdminHomeRuleSuggestion(value, value)).ToArray();
    }
    private sealed class HomeRuleFacetValues { public List<string> Matches { get; set; } = []; public List<HomeRuleFacetValue> Values { get; set; } = []; }
    private sealed class HomeRuleFacetValue { public string Value { get; set; } = ""; }

    private static string RowPath(string id) => Root + "/" + Uri.EscapeDataString(
        !string.IsNullOrWhiteSpace(id) && id.Length <= 200 ? id : throw new ArgumentException("Invalid row identity."));

    private static void ValidateRow(AdminHomeSection row, string? id = null, AdminHomeSectionScope? scope = null)
    {
        if (row == null) throw new InvalidDataException("Missing row.");
        scope ??= new(row.Scope, row.LibraryId);
        _ = scope.Query;
        if (row == null || string.IsNullOrWhiteSpace(row.Id) || (id != null && id != row.Id) || !scope.Matches(row))
            throw new InvalidDataException("The row identity or page changed. Reload and review.");
    }

    private static void RequireSession(MonitorAuthorityLease lease, AdminHomeSectionSession session)
    {
        lease.ThrowIfRetired();
        if (!ReferenceEquals(session.Owner, lease)) throw new MonitorAuthorityRetiredException();
        ValidateRow(session.Row); AdminValidators.RequireStrong(session.ETag);
    }

    internal static async Task<T> MutationAsync<T>(Func<Task<T>> send, MonitorAuthorityLease lease)
    {
        lease.ThrowIfRetired();
        try { var result = await send().ConfigureAwait(false); lease.ThrowIfRetired(); return result; }
        catch (OperationCanceledException) when (!lease.IsCurrent) { throw; }
        catch (ApiException ex) when (ex.StatusCode == 412) { throw new AdminHomeSectionsConflictException(); }
        catch (ApiException ex) when (ex.StatusCode is >= 400 and < 500 && ex.StatusCode != 408) { throw; }
        catch (Exception) { throw new AdminHomeSectionsUnknownException(); }
    }
}

public sealed class AdminHomeSectionsConflictException() : Exception("Rows changed on the server. Reload and review before saving.");
public sealed class AdminHomeSectionsUnknownException() : Exception("The outcome is unknown. Reload and review the server rows before making another change. The request was not replayed.");
public sealed class AdminHomeBulkResult { public int Created { get; set; } }
