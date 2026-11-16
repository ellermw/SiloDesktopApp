using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private AdminUserViewAs? _preview;
    private CancellationTokenSource? _previewRead;
    private string _previewName = "";
    private string _previewReturn = "";
    private readonly Dictionary<string, HashSet<string>> _previewCursors = new(StringComparer.Ordinal);
    private async Task BeginViewAsAsync()
    {
        var generation = _generation;
        var result = await ConfirmAsync("View as " + _review!.User.Text("username"), "Start a separate in-memory session for this account. The regular desktop account is never overwritten. Return to admin ends this session.", new(AdminUsersOperation.ViewAs));
        if (result?.Body == null || !_workspace.IsCurrent(generation)) return;
        _preview?.Dispose();
        _previewName = _review.User.Text("username"); _previewReturn = _location.Href; _previewCursors.Clear();
        _preview = new(App.Services.GetRequiredService<SiloApiClient>().BaseUrl, _review.User.Text("id"), result.Body, _workspace.Token, () => _workspace.IsCurrent(generation));
        result.Body.Remove("access_token"); result.Body.Remove("refresh_token");
        await PreviewReadAsync("/api/v2/profiles", "profiles");
    }
    private async Task PreviewReadAsync(string path, string kind)
    {
        var preview = _preview;
        if (preview == null) return;
        _previewRead?.Cancel(); _previewRead?.Dispose(); _previewRead = CancellationTokenSource.CreateLinkedTokenSource(_workspace.Token);
        var token = _previewRead.Token; var generation = _generation;
        try
        {
            var response = await preview.ReadAsync(path, token);
            if (token.IsCancellationRequested || !ReferenceEquals(preview, _preview) || !_workspace.IsCurrent(generation)) return;
            _body.Children.Clear(); _body.Children.Add(_notice);
            _body.Children.Add(Text("VIEWING AS " + _previewName + " — separate, temporary session", 24));
            _body.Children.Add(Text("Return location: " + _previewReturn));
            _body.Children.Add(Action("Return to admin", EndViewAsAsync));
            if (kind != "profiles")
            {
                var navigation = Inline();
                navigation.Children.Add(Action("Profiles", () => PreviewReadAsync("/api/v2/profiles", "profiles")));
                navigation.Children.Add(Action("Home layout", () => PreviewReadAsync("/api/v2/home/layout", "home")));
                navigation.Children.Add(Action("Libraries", () => PreviewReadAsync("/api/v2/libraries", "libraries")));
                _body.Children.Add(navigation);
                var query = new TextBox { Header = "Search this profile catalog", MaxLength = 320 };
                _body.Children.Add(query); _body.Children.Add(Action("Search", () => PreviewReadAsync("/api/v2/catalog?limit=25&q=" + Uri.EscapeDataString(query.Text.Trim()), "catalog")));
            }
            if (kind == "detail")
            {
                ObjectCard("Visible item", response, ["id", "title", "name", "type", "year", "overview", "description", "genres", "content_rating"]);
                _body.Children.Add(Text("This isolated view-as surface verifies account/profile catalog access without replacing the app playback identity. Playback remains outside this scoped surface."));
                return;
            }
            var items = kind == "home" ? response["sections"] as JsonArray ?? [] : response.Items();
            if (items.Count > AdminUsersApi.MaxRows) throw new InvalidDataException("Scoped collection exceeded its retention bound.");
            foreach (var row in items.OfType<JsonObject>())
            {
                var card = Panel(); card.Children.Add(Text(row.Text("title", row.Text("name", row.Text("id"))), 18));
                if (kind == "profiles")
                {
                    card.Children.Add(Text($"Primary {row.Flag("is_primary")} · Child {row.Flag("is_child")} · PIN protected {row.Flag("has_pin")}"));
                    var pin = new PasswordBox { Header = "Profile PIN (only when protected)", Visibility = row.Flag("has_pin") ? Visibility.Visible : Visibility.Collapsed, MaxLength = 72 };
                    card.Children.Add(pin);
                    card.Children.Add(Action("Select this target profile", async () =>
                    {
                        await preview.SelectProfileAsync(row.Text("id"), row.Flag("has_pin") ? pin.Password : null, token);
                        pin.Password = ""; RequireGeneration(generation);
                        if (!ReferenceEquals(preview, _preview)) throw new OperationCanceledException();
                        await PreviewReadAsync("/api/v2/home/layout", "home");
                    }));
                }
                else
                {
                    var next = kind switch
                    {
                        "libraries" => "/api/v2/catalog?limit=25&library_id=" + AdminUsersApi.Segment(row.Text("id")),
                        "home" => "/api/v2/catalog?limit=25&source=section&scope=home&section_id=" + AdminUsersApi.Segment(row.Text("id")),
                        _ => "/api/v2/catalog/items/" + AdminUsersApi.Segment(row.Text("id"))
                    };
                    card.Children.Add(Action("Open", () => PreviewReadAsync(next, kind is "libraries" or "home" ? "catalog" : "detail")));
                }
                _body.Children.Add(Card(kind == "profiles" ? "Target account profile" : "Visible catalog", card));
            }
            if (response["page"] is JsonObject page && page.Flag("has_more"))
            {
                var cursor = page.Text("next_cursor");
                var origin = path.Split("&cursor=", StringSplitOptions.None)[0];
                if (!SiloPlayer.Core.Services.Admin.AdminPaging.IsValidCursor(cursor) || items.Count == 0) throw new InvalidDataException("Invalid scoped continuation.");
                if (!_previewCursors.TryGetValue(origin, out var seen))
                {
                    if (_previewCursors.Count >= 32) _previewCursors.Clear();
                    _previewCursors[origin] = seen = new(StringComparer.Ordinal);
                }
                if (seen.Contains(cursor)) throw new InvalidDataException("Repeated scoped continuation; reopen this resource.");
                _body.Children.Add(Action("Next scoped page (replaces retained rows)", () =>
                {
                    if (seen.Count >= 9 || !seen.Add(cursor)) throw new InvalidDataException("Scoped page bound reached; reopen or narrow the resource.");
                    return PreviewReadAsync(SiloPlayer.Core.Services.Admin.AdminPaging.WithPage(origin, 25, cursor), kind);
                }, seen.Count < 9));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (ReferenceEquals(preview, _preview) && _workspace.IsCurrent(generation)) { ShowError(error); if (!_body.Children.OfType<Button>().Any()) _body.Children.Add(Action("Return to admin", EndViewAsAsync)); } }
    }
    private async Task EndViewAsAsync()
    {
        var generation = _generation;
        var preview = _preview; _preview = null; _previewRead?.Cancel();
        try { if (preview != null) await preview.EndAsync(_workspace.Token); }
        catch (Exception) { if (_workspace.IsCurrent(generation)) _notice.Text = "The temporary sign-out outcome is unknown; the local view-as credentials were discarded without replay."; }
        finally { preview?.Dispose(); }
        RequireGeneration(generation);
        await LoadAsync(_location);
    }
}
