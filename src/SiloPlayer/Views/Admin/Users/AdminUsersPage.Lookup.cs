using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    // The list loads in bounded pages. The one search box also asks the server's exact identity filter when what was typed matches no loaded account and more
    // pages exist, so an account that is not in the loaded pages can still be found. The answer is shown only while the search still holds the text it was
    // asked for, and a newer search cancels the one before it.
    private CancellationTokenSource? _lookup;
    private string _lookupText = "";
    private List<JsonObject> _lookupRows = [];

    private IEnumerable<JsonObject> ExactExtras()
    {
        if (_lookupText.Length == 0 || _lookupText != _search.Trim()) return [];
        var loaded = Rows("users").Select(user => user.Text("id")).ToHashSet(StringComparer.Ordinal);
        return _lookupRows.Where(user => !loaded.Contains(user.Text("id")));
    }

    private void QueueExactLookup()
    {
        _lookup?.Cancel(); _lookup = null;
        var text = _search.Trim();
        if (text.Length == 0 || text.Length > 320 || !Feature("exact_identity_filter") || _rows.GetValueOrDefault("users") is not { Complete: false }) return;
        if (FilteredUsers(withLookup: false).Count > 0) return;
        var source = _lookup = new CancellationTokenSource();
        _ = LookUpAsync(text, _generation, source);
    }

    private async Task LookUpAsync(string text, long generation, CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(450, source.Token);
            if (!_workspace.IsCurrent(generation)) return;
            var path = "/api/v2/admin/users?identity=" + Uri.EscapeDataString(text);
            var page = await _workspace.ReadAsync(generation, (lease, token) =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, source.Token);
                return _api.PageAsync(lease, path, 25, ct: linked.Token);
            });
            if (source.IsCancellationRequested || !_workspace.IsCurrent(generation) || _search.Trim() != text) return;
            _lookupText = text; _lookupRows = page.Items.ToList();
            RefreshUsers();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* a lookup that failed shows the same "no matches" a search with no answer shows */ }
    }
}
