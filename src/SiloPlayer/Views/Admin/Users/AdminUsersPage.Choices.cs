using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<(string Id, string Name)>>> FormChoicesAsync(IEnumerable<string> fields, IReadOnlyDictionary<string, IReadOnlyList<(string Id, string Name)>>? supplied, long generation)
    {
        var result = new Dictionary<string, IReadOnlyList<(string Id, string Name)>>(StringComparer.Ordinal);
        foreach (var entry in supplied ?? new Dictionary<string, IReadOnlyList<(string Id, string Name)>>()) result[entry.Key] = entry.Value;
        if (fields.Contains("access_group_id") && Feature("access_groups"))
        {
            if (!_rows.ContainsKey("groups")) await RowsAsync("groups", "/api/v2/admin/access-groups", generation);
            RequireGeneration(generation);
            if (!_rows.ContainsKey("groups")) throw new InvalidOperationException("Access-group choices failed to load. Reload before editing this card.");
            result["access_group_id"] = Rows("groups").Select(row => (row.Text("id"), row.Text("name"))).ToArray();
        }
        foreach (var key in new[] { "library_ids", "allowed_library_ids" }.Where(fields.Contains))
        {
            if (!_rows.ContainsKey("library-choices")) await RowsAsync("library-choices", "/api/v2/libraries", generation, true);
            RequireGeneration(generation);
            if (!_rows.ContainsKey("library-choices")) throw new InvalidOperationException("Library metadata choices failed their bounded read. No catalog was prefetched. Reload before editing.");
            result[key] = Rows("library-choices").Select(row => (row.Text("id"), row.Text("name"))).ToArray();
        }
        if (fields.Contains("permissions")) result["permissions"] = new[] { ("metadata_curation", "Metadata curation"), ("marker_edit", "Edit skip markers") };
        return result;
    }
    private void AddChoiceContinuation(AdminUsersForm form, IEnumerable<string> fields)
    {
        if (fields.Contains("access_group_id") && _rows.GetValueOrDefault("groups") is { Complete: false })
            form.Content.Children.Add(Action("Load next bounded group-choice page", async () =>
            {
                var generation = _generation;
                await MoreAsync("groups");
                RequireGeneration(generation);
                form.UpdateChoices("access_group_id", Rows("groups").Select(row => (row.Text("id"), row.Text("name"))).ToArray());
            }, nested: true));
    }
}
