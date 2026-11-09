using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Api;

public partial class CatalogApi
{
    /// <summary>Index the same title-ordered query as GetCatalogAsync; older servers have no index.</summary>
    public async Task<CatalogLettersResponse?> GetCatalogLettersAsync(
        int libraryId, string? order = null, string? genre = null, string? studio = null,
        string? contentRating = null, string? country = null, string? resolution = null,
        string? audioLanguage = null, string? yearMin = null, string? yearMax = null, string? type = null,
        IReadOnlyList<QueryRule>? extraRules = null, string extraRulesMatch = "all",
        IReadOnlyList<QueryGroup>? queryGroups = null, string queryGroupsMatch = "all", CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var language = client.LocalizedReads.Generation;
        var groups = BuildCatalogRuleGroups(studio, country, resolution, audioLanguage, extraRules,
            extraRulesMatch, queryGroups, queryGroupsMatch);
        var match = queryGroups is null ? "all" : queryGroupsMatch;
        groups = WithCatalogBaseFilters(groups, match, genre, contentRating, yearMin, yearMax);
        var body = new Dictionary<string, object?>
        {
            ["library_id"] = libraryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["order"] = order == "desc" ? "desc" : "asc", ["groups"] = groups,
            ["match"] = match == "any" ? "any" : "all",
        };
        if (!string.IsNullOrWhiteSpace(type)) body["type"] = type;
        // Freeze editable rules before the first await, so an old request can
        // never acquire newly edited filters under its earlier ownership key.
        var frozenBody = System.Text.Json.JsonSerializer.SerializeToElement(body, BrowseV2.Json);
        var capabilities = await GetSearchRuleCapabilitiesAsync(context, ct);
        ct.ThrowIfCancellationRequested();
        if (!capabilities.LetterIndex) return null;
        try
        {
            var response = await client.SendRequestAsync<CatalogLettersResponse>(context, HttpMethod.Post,
                "/api/v2/catalog/letters", frozenBody, ct);
            ct.ThrowIfCancellationRequested();
            client.LocalizedReads.ThrowIfChanged(language);
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Catalog letter authority changed.", ct);
            response.Validate();
            return response;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Catalog letter index failed after the server advertised letter_index=true.", ex);
        }
    }

    private static List<QueryGroup> BuildCatalogRuleGroups(string? studio, string? country, string? resolution,
        string? audioLanguage, IReadOnlyList<QueryRule>? extraRules, string extraRulesMatch,
        IReadOnlyList<QueryGroup>? queryGroups, string queryGroupsMatch)
    {
        var groups = queryGroups?.Where(group => group.Rules.Count > 0).ToList() ?? [];
        var rules = new List<QueryRule>();
        void Add(string field, string? value) { if (!string.IsNullOrWhiteSpace(value)) rules.Add(new() { Field = field, Op = "is", Value = value }); }
        Add("studio", studio); Add("country", country); Add("resolution", resolution); Add("audio_language", audioLanguage);
        if (extraRules is not null) rules.AddRange(extraRules);
        if (rules.Count == 0) return groups;
        var additional = new QueryGroup { Match = extraRulesMatch == "any" ? "any" : "all", Rules = rules };
        if (groups.Count == 0 || queryGroupsMatch != "any") return groups.Concat(new[] { additional }).ToList();

        // Supplemental filters are ANDed with the structured expression, even
        // when its top-level groups (or the additional rules) use OR.
        static IEnumerable<List<QueryRule>> Branches(QueryGroup group) => group.Match == "any"
            ? group.Rules.Select(rule => new List<QueryRule> { rule })
            : new[] { group.Rules };
        return groups.SelectMany(group => Branches(group).SelectMany(branch =>
            Branches(additional).Select(extra => new QueryGroup
            {
                Match = "all", AdditionalProperties = group.AdditionalProperties,
                Rules = branch.Concat(extra).ToList(),
            }))).ToList();
    }

    private static List<QueryGroup> WithCatalogBaseFilters(List<QueryGroup> groups, string match,
        string? genre, string? contentRating, string? yearMin, string? yearMax)
    {
        var rules = new List<QueryRule>();
        void Add(string field, string op, string? value) { if (value is not null) rules.Add(new() { Field = field, Op = op, Value = value }); }
        Add("genre", "is", genre); Add("content_rating", "is", contentRating);
        Add("year", "gte", yearMin); Add("year", "lte", yearMax);
        if (rules.Count == 0) return groups;
        if (match == "any" && groups.Count > 0)
            return groups.SelectMany(group => group.Match == "any"
                ? group.Rules.Select(rule => new QueryGroup { Match = "all", Rules = new[] { rule }.Concat(rules).ToList() })
                : new[] { new QueryGroup { Match = "all", Rules = group.Rules.Concat(rules).ToList() } }).ToList();
        return groups.Concat(new[] { new QueryGroup { Match = "all", Rules = rules } }).ToList();
    }
}
