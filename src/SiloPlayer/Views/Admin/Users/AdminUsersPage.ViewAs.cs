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
    private async Task BeginViewAsAsync()
    {
        var generation = _generation;
        var targetId = _review!.User.Text("id");
        var targetName = _review.User.Text("username");
        var result = await ConfirmAsync("View as " + targetName, "Start a separate in-memory session for this account. The regular desktop account is never overwritten. Return to admin ends this session.", new(AdminUsersOperation.ViewAs));
        if (result == null) return;
        try
        {
            if (result.Body == null || !_workspace.IsCurrent(generation)) return;
            _preview?.Dispose();
            _previewName = targetName; _previewReturn = _location.Href;
            _preview = new(result.ViewAsBaseUrl!, targetId, result.Body, _workspace.Token, () => _workspace.IsCurrent(generation));
            result.Body.Remove("access_token"); result.Body.Remove("refresh_token");
            await PreviewReadAsync("/api/v2/profiles", "profiles");
        }
        finally { await result.DiscardViewAsAsync(); }
    }
    private void RenderPreviewHeader()
    {
        _body.Children.Clear(); _body.Children.Add(_notice);
        _body.Children.Add(Text("VIEWING AS " + _previewName + " — separate, temporary session", 24));
        _body.Children.Add(Text("Return location: " + _previewReturn));
        _body.Children.Add(Action("Return to admin", EndViewAsAsync, nested: true));
    }
    private async Task PreviewReadAsync(string path, string kind, string? cursor = null, HashSet<string>? seen = null)
    {
        var preview = _preview;
        if (preview == null) return;
        _previewRead?.Cancel(); _previewRead?.Dispose(); _previewRead = CancellationTokenSource.CreateLinkedTokenSource(_workspace.Token);
        var token = _previewRead.Token; var generation = _generation;
        seen ??= new(StringComparer.Ordinal);
        bool Current() => !token.IsCancellationRequested && ReferenceEquals(preview, _preview) && _workspace.IsCurrent(generation);
        void ReadFailed(Exception error)
        {
            if (!Current()) return;
            RenderPreviewHeader(); ShowError(error);
            _body.Children.Add(Action("Retry scoped read", () => PreviewReadAsync(path, kind, cursor, seen)));
        }
        RenderPreviewHeader(); _notice.Text = "Loading temporary-session preview…";
        try
        {
            if (cursor != null && (seen.Count >= 9 || seen.Contains(cursor))) throw new InvalidDataException("Scoped page bound reached; reopen or narrow the resource.");
            var requestPath = kind == "catalog" || cursor != null ? SiloPlayer.Core.Services.Admin.AdminPaging.WithPage(path, 25, cursor) : path;
            var response = await preview.ReadAsync(requestPath, token);
            if (!Current()) return;
            var items = kind == "home" ? response["sections"] as JsonArray ?? [] : response.Items();
            if (items.Count > AdminUsersApi.MaxRows) throw new InvalidDataException("Scoped collection exceeded its retention bound.");
            string? nextCursor = null;
            if (response["page"] is JsonObject page && page.Flag("has_more"))
            {
                nextCursor = page.Text("next_cursor");
                if (!SiloPlayer.Core.Services.Admin.AdminPaging.IsValidCursor(nextCursor) || items.Count == 0) throw new InvalidDataException("Invalid scoped continuation.");
                if (nextCursor == cursor || seen.Contains(nextCursor)) throw new InvalidDataException("Repeated scoped continuation; reopen this resource.");
            }
            // Only accepted responses consume a continuation or its page budget.
            if (cursor != null) seen.Add(cursor);
            _notice.Text = "";
            if (kind != "profiles")
            {
                var navigation = Inline();
                navigation.Children.Add(Action("Profiles", () => PreviewReadAsync("/api/v2/profiles", "profiles")));
                navigation.Children.Add(Action("Home layout", () => PreviewReadAsync("/api/v2/home/layout", "home")));
                navigation.Children.Add(Action("Libraries", () => PreviewReadAsync("/api/v2/libraries", "libraries")));
                _body.Children.Add(navigation);
                var query = new TextBox { Header = "Search this profile catalog", MaxLength = 320 };
                _body.Children.Add(query); _body.Children.Add(Action("Search", () => PreviewReadAsync("/api/v2/catalog?q=" + Uri.EscapeDataString(query.Text.Trim()), "catalog")));
            }
            if (kind == "detail")
            {
                ObjectCard("Visible item", response, ["id", "title", "name", "type", "year", "overview", "description", "genres", "content_rating"]);
                _body.Children.Add(Text("This isolated view-as surface verifies account/profile catalog access without replacing the app playback identity. Playback remains outside this scoped surface."));
                return;
            }
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
                        "libraries" => "/api/v2/catalog?library_id=" + AdminUsersApi.Segment(row.Text("id")),
                        "home" => "/api/v2/catalog?source=section&scope=home&section_id=" + AdminUsersApi.Segment(row.Text("id")),
                        _ => "/api/v2/catalog/items/" + AdminUsersApi.Segment(row.Text("id"))
                    };
                    card.Children.Add(Action("Open", () => PreviewReadAsync(next, kind is "libraries" or "home" ? "catalog" : "detail")));
                }
                _body.Children.Add(Card(kind == "profiles" ? "Target account profile" : "Visible catalog", card));
            }
            if (nextCursor != null)
                _body.Children.Add(Action("Next scoped page (replaces retained rows)", () => PreviewReadAsync(path, kind, nextCursor, seen), seen.Count < 9));
        }
        catch (OperationCanceledException error) { ReadFailed(new TimeoutException("The temporary-session read timed out. Retry or return to admin to end this session.", error)); }
        catch (Exception error) { ReadFailed(error); }
    }
    private async Task EndViewAsAsync()
    {
        var generation = _generation;
        // Retire rendered actions before cancellation can resume their handlers. Only the new load owns busy state.
        ++_loadRevision; _actionBusy = true;
        var preview = _preview; _preview = null; _previewRead?.Cancel();
        try { if (preview != null) await preview.EndAsync(_workspace.Token); }
        catch (Exception) { if (_workspace.IsCurrent(generation)) _notice.Text = "The temporary sign-out outcome is unknown; the local view-as credentials were discarded without replay."; }
        finally { preview?.Dispose(); }
        RequireGeneration(generation);
        await LoadAsync(_location);
    }
}
