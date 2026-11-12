using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

internal static partial class TitleAllowancesNativeFixture
{
    private static async Task AccessPagingRaces(StackPanel parent, Wire wire, SiloApiClient client, AuthService auth)
    {
        auth.SelectProfile("primary", "fixture-proof", new() { Id = "primary", Name = "Primary", IsPrimary = true });
        foreach (var change in new[] { "revoke", "add", "collection" })
        {
            wire.RaceMode = true; wire.RaceRevision = wire.RaceHeld = 0; wire.RaceCollectionRemoved = false;
            wire.RaceTitles.Clear(); wire.RaceTitles.UnionWith(["movie:one", "movie:two"]);
            wire.RaceGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.RaceHoldTitles = wire.RaceHoldCollections = false;
            AllowedTitlesSection? section = null;
            var owner = new TitleAllowanceSession(client, auth.SelectedProfile, () => section?.IsLoaded == true);
            section = new AllowedTitlesSection(owner, "above"); parent.Children.Add(section);
            Task? oldRead = null;
            try
            {
                await Until(() => section.IsLoaded); await section.LoadAsync();
                Button Action(string id, string text) => Descendants(section).OfType<Button>().Single(b => Equals(b.Tag, id) && Equals(b.Content, text));
                if (change == "collection")
                {
                    wire.RaceHoldCollections = true; oldRead = section.LoadAsync();
                    await Until(() => wire.RaceHeld == 1);
                    Click(Action("paused", "Stop allowing"));
                    await Until(() => wire.RaceRevision == 1);
                    await Until(() => Descendants(section).OfType<TextBlock>().Any(t => t.Text == "No collections allowed."));
                }
                else
                {
                    wire.RaceHoldTitles = true;
                    Click(Descendants(section).OfType<Button>().Single(b => Equals(b.Content, "Show more")));
                    await Until(() => wire.RaceHeld == 1);
                    if (change == "revoke")
                    {
                        Click(Action("movie:one", "Stop allowing"));
                        await Until(() => wire.RaceRevision == 1);
                        await Until(() => !Descendants(section).OfType<Button>().Any(b => Equals(b.Tag, "movie:one")));
                    }
                    else
                    {
                        var search = Descendants(section).OfType<TextBox>().Single(); search.Text = "New title";
                        await Until(() => Descendants(section).OfType<Button>().Any(b => Equals(b.Tag, "series:new")));
                        Click(Action("series:new", "Allow"));
                        await Until(() => wire.RaceRevision == 1);
                        await Until(() => Descendants(section).OfType<Button>().Any(b => Equals(b.Tag, "series:new") && Equals(b.Content, "Stop allowing")));
                    }
                }
                wire.RaceGate.TrySetResult(); if (oldRead != null) await oldRead;
                await Task.Delay(100);
                var ids = Descendants(section).OfType<Button>().Where(b => Equals(b.Content, "Stop allowing")).Select(b => b.Tag as string).ToArray();
                if (change == "revoke") Check(!ids.Contains("movie:one") && ids.Contains("movie:two"), "Old paging response restored a revoked allowance.");
                if (change == "add") Check(ids.Contains("series:new"), "Old paging response lost the confirmed new allowance.");
                if (change == "collection") Check(!ids.Contains("paused"), "Old collection read restored a revoked collection.");
                Program.Log("PASS allowance post-write refresh retires held pre-write " + change + " response.");
            }
            finally { wire.RaceGate.TrySetResult(); parent.Children.Remove(section); wire.RaceMode = false; await Task.Delay(80); }
        }
    }

    private sealed partial class Wire
    {
        internal bool RaceMode, RaceHoldTitles, RaceHoldCollections, RaceCollectionRemoved;
        internal int RaceRevision, RaceHeld;
        internal TaskCompletionSource RaceGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly HashSet<string> RaceTitles = [];
        private async Task<HttpResponseMessage> RaceResponseAsync(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            var collections = path.Contains("allowed-collections");
            if (request.Method != HttpMethod.Get)
            {
                ++RaceRevision;
                if (collections) RaceCollectionRemoved = true;
                else { var id = Uri.UnescapeDataString(path.Split('/')[6]); if (request.Method == HttpMethod.Put) RaceTitles.Add(id); else RaceTitles.Remove(id); }
                return new(HttpStatusCode.NoContent);
            }
            if (path == "/api/v2/catalog") return Json(new { items = new[] { new { content_id = "series:new", title = "New title", type = "series", year = 2026 } } });
            if (collections)
            {
                var items = RaceCollectionRemoved ? Array.Empty<object>() : new object[] { new { collection_id = "paused", name = "Old collection", item_count = 2, live = true } };
                if (RaceHoldCollections) { RaceHoldCollections = false; ++RaceHeld; await RaceGate.Task; }
                return Json(new { items });
            }
            var more = request.RequestUri.Query.Contains("cursor=");
            var titles = (more ? RaceTitles.Skip(1) : RaceRevision == 0 ? RaceTitles.Take(1) : RaceTitles).ToArray();
            var hasMore = !more && RaceRevision == 0;
            var response = Json(new { items = titles.Select(id => new { content_id = id, title = id, type = id.StartsWith("movie") ? "movie" : "series", year = 2026 }).ToArray(), page = new { has_more = hasMore, next_cursor = "race+cursor" } });
            if (more && RaceHoldTitles) { RaceHoldTitles = false; ++RaceHeld; await RaceGate.Task; }
            return response;
        }
    }
}
