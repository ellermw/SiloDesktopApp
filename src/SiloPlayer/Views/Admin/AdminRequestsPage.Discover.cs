using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using J = SiloPlayer.Core.Services.Admin.AdminRequestJson;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminRequestsPage
{
    private AdminRequestDocument? _layout;
    private async Task BuildDiscoverAsync(MonitorAuthorityLease owner, long generation)
    {
        var layout = await _api.GetLayoutAsync(owner);
        if (!Current(owner) || generation != _generation) return;
        _layout = layout; RenderDiscover();
    }
    private void RenderDiscover()
    {
        if (_layout == null) return;
        _content.Children.Clear(); var layout = _layout; var rows = J.Items(layout.Value, "rows").OfType<JsonObject>().ToArray();
        Header(S("web.admin.requests.discoverPage.title"), S("web.admin.requests.discoverPage.subtitle"),
            Button("Add row", "AdminDiscoverAdd", AddDiscoverRowAsync, true),
            Button(S("web.admin.requests.discoverPage.restore.confirm"), "AdminDiscoverRestore", async () =>
            {
                if (!await ConfirmAsync(S("web.admin.requests.discoverPage.restore.title"), S("web.admin.requests.discoverPage.restore.description") + "\n\n" + S("web.admin.requests.discoverPage.restore.builtInsBack") + "\n" + S("web.admin.requests.discoverPage.restore.addedDeleted") + "\n" + S("web.admin.requests.discoverPage.restore.followsDefaults") + "\n\n" + S("web.admin.requests.discoverPage.restore.cannotUndo"), S("web.admin.requests.discoverPage.restore.confirm"))) return;
                await WriteAsync(lease => _api.DeleteDocumentAsync(lease, layout), ReconcileCurrentAsync);
            }));
        if (rows.Length == 0) _content.Children.Add(Card(U.Text("No rows. Add a row or restore the default layout.", 14, true)));
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index]; var at = index;
            var title = J.Text(row, "title"); var id = J.Text(row, "id");
            var toggle = Toggle(title, "AdminDiscoverEnabled-" + id, J.Flag(row, "enabled")); var owner = Owner;
            toggle.Toggled += async (_, _) =>
            {
                if (!Current(owner)) return;
                var changed = rows.Select(r => AdminRequestRules.RowBody(r)).ToArray(); changed[at]["enabled"] = toggle.IsOn;
                await SaveLayoutAsync(layout, changed);
            };
            var controls = Row(Button(S("web.admin.settings.requests.shared.edit"), "AdminDiscoverEdit-" + id, () => EditDiscoverRowAsync(id)),
                Button("↑", "AdminDiscoverUp-" + id, () => MoveDiscoverAsync(layout, rows, at, -1)),
                Button("↓", "AdminDiscoverDown-" + id, () => MoveDiscoverAsync(layout, rows, at, 1)),
                Button(S("web.admin.settings.requests.shared.delete"), "AdminDiscoverDelete-" + id, async () =>
                {
                    if (await ConfirmAsync("Delete row?", "Remove “" + title + "” from the Discover page?", "Delete")) await SaveLayoutAsync(layout, rows.Where((_, i) => i != at));
                }));
            ((Button)controls.Children[1]).IsEnabled = at > 0; ((Button)controls.Children[2]).IsEnabled = at < rows.Length - 1;
            _content.Children.Add(Card(Stack(toggle, U.Text(DiscoverDescription(row), 13, true), controls)));
        }
    }
    private static string DiscoverDescription(JsonObject row)
    {
        var kind = J.Text(row, "type"); var label = KindLabel(kind);
        if (row["chart"] is JsonObject chart) label += " · " + J.Text(chart, "preset") + " · " + J.Text(chart, "media_type");
        if (row["discover"] is JsonObject filter) label += " · " + J.Text(filter, "media_type") + " · " + J.Text(filter, "sort_by");
        if (row["list"] is JsonObject list) label += " · " + SafeListUrl(J.Text(list, "url")) + " · " + J.Text(list, "order");
        if (row["source"] is JsonObject source) label += " · " + J.Text(source, "state") + " · " + J.Number(source, "entries") + " entries · " + J.Text(source, "refreshed_at") + (J.Text(source, "error").Length > 0 ? " · " + SafeDisplay(J.Text(source, "error")) : "");
        if (J.Flag(row, "hide_available")) label += " · " + S("web.admin.requests.discover.missingTitlesOnly");
        return label;
    }
    private static string SafeListUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Host + uri.AbsolutePath : value.All(char.IsAsciiDigit) ? value : "Public list";
    private static string KindLabel(string kind) => S("web.admin.requests.discover.kind." + (kind switch { "tmdb_chart" => "tmdbChart", "tmdb_discover" => "tmdbFilter", "tmdb_list" => "tmdbList", _ => kind }) + ".label");
    private async Task MoveDiscoverAsync(AdminRequestDocument layout, JsonObject[] rows, int index, int change)
    { var to = index + change; if (to < 0 || to >= rows.Length) return; var reordered = rows.ToArray(); (reordered[index], reordered[to]) = (reordered[to], reordered[index]); await SaveLayoutAsync(layout, reordered); }
    private async Task SaveLayoutAsync(AdminRequestDocument layout, IEnumerable<JsonObject> rows)
    {
        var owner = Owner; var body = new JsonObject { ["rows"] = new JsonArray(rows.Select(row => (JsonNode)AdminRequestRules.RowBody(row)).ToArray()) };
        // A save that was not made leaves the last canonical layout on screen, never the unsaved switch or order.
        if (!await WriteAsync(async lease => { _layout = await _api.SaveDocumentAsync(lease, layout, body); }, ReconcileCurrentAsync) && Current(owner)) RenderDiscover();
    }
    private async Task AddDiscoverRowAsync()
    {
        var layout = _layout; if (layout == null) return;
        if (J.Items(layout.Value, "rows").Count >= J.Number(layout.Value, "max_rows", 30)) { Notice("The Discover layout is full. Remove a row before adding another."); return; }
        var stock = StockRows().Where(stockRow => !J.Items(layout.Value, "rows").Any(row => J.Text(row, "id") == J.Text(stockRow, "id"))).ToArray();
        var choices = AdminRequestRules.RowTypes.Take(4).Select(kind => (kind, KindLabel(kind))).Concat(stock.Select(row => ("stock:" + J.Text(row, "id"), "Bring back · " + J.Text(row, "title"))));
        var pick = Select("AdminDiscoverKind", choices, "tmdb_chart"); string? selected = null;
        await EditAsync("Add row", Stack(U.Text(S("web.admin.requests.rowDialog.newRow"), 16), pick, U.Text(S("web.admin.requests.rowDialog.bringBackHelp"), 12, true)), _ => { selected = Selected(pick); return Task.CompletedTask; }, "Next", mutates: false);
        if (selected == null || Owner is not { IsCurrent: true }) return;
        var row = selected.StartsWith("stock:", StringComparison.Ordinal) ? (JsonObject)stock.Single(row => J.Text(row, "id") == selected[6..]).DeepClone() : NewDiscoverRow(selected);
        await EditDiscoverAsync(layout, row, true);
    }
    private async Task EditDiscoverRowAsync(string id)
    {
        if (Owner is not { IsCurrent: true } owner) return;
        var layout = await _api.GetLayoutAsync(owner); if (!Current(owner)) return; _layout = layout;
        var row = J.Items(layout.Value, "rows").OfType<JsonObject>().SingleOrDefault(row => J.Text(row, "id") == id);
        if (row != null) await EditDiscoverAsync(layout, (JsonObject)row.DeepClone(), false);
    }
    private async Task EditDiscoverAsync(AdminRequestDocument layout, JsonObject row, bool creating)
    {
        var form = new RequestForm(AdminRequestRules.RowBody(row)); var type = J.Text(row, "type"); var id = J.Text(row, "id");
        var nameInput = form.Text("title", "Name", "AdminDiscoverName", maximum: 80, required: true);
        form.Flag("enabled", "Shown", "AdminDiscoverShown");
        if (type is "studios" or "networks" or "genres") form.Panel.Children.Add(U.Text(KindLabel(type), 13, true));
        else
        {
            form.Number("limit", "Titles shown", "AdminDiscoverLimit", 0, 100, false);
            form.Flag("hide_available", S("web.admin.requests.discover.missingTitlesOnly"), "AdminDiscoverHideAvailable");
        }
        if (row["chart"] is JsonObject chart)
        {
            var chartDraft = (JsonObject)chart.DeepClone(); var chartForm = new RequestForm(chartDraft);
            var builtIn = StockRows().Any(stock => J.Text(stock, "id") == id && stock["chart"] != null);
            if (builtIn) form.Panel.Children.Add(U.Text(S("web.admin.requests.rowDialog.builtInKeepsChart"), 13, true));
            else
            {
                chartForm.Choice("preset", "Chart", "AdminDiscoverChart", new[] { "trending", "popular", "top_rated", "now_playing", "upcoming", "airing_today", "on_the_air" }.Select(value => (value, value.Replace('_', ' '))));
                chartForm.Choice("media_type", "Media type", "AdminDiscoverChartMedia", [("movie", "Movies"), ("series", "Series"), ("all", "Movies and series")]);
                chartForm.Choice("time_window", "Trending window", "AdminDiscoverChartWindow", [("day", "Day"), ("week", "Week")]);
                form.Panel.Children.Add(chartForm.Panel);
                form.Readers.Add(() => { var value = chartForm.Read(); var preset = J.Text(value, "preset"); var media = J.Text(value, "media_type"); if (preset != "trending") value.Remove("time_window"); if (media == "all" && preset != "trending" || media == "series" && preset is "now_playing" or "upcoming" || media == "movie" && preset is "airing_today" or "on_the_air") throw new ArgumentException("Choose a chart for this media type."); form.Data["chart"] = value; });
            }
        }
        if (row["discover"] is JsonObject filter)
        {
            var filterForm = DiscoverFilterForm((JsonObject)filter.DeepClone()); form.Panel.Children.Add(filterForm.Panel); form.Readers.Add(() => form.Data["discover"] = filterForm.Read());
        }
        if (row["list"] is JsonObject list)
        {
            var listForm = new RequestForm((JsonObject)list.DeepClone());
            var link = listForm.Text("url", type == "mdblist" ? "MDBList link" : S("web.admin.requests.rowDialog.tmdbListLink"), "AdminDiscoverListUrl", maximum: 2048, required: true);
            if (type == "mdblist")
            {
                if (J.Flag(_capabilities, "discover_list_order")) listForm.Choice("order", "Order", "AdminDiscoverListOrder", [("", "List order"), ("newest_added", "Newest added"), ("newest_released", "Newest released")]);
                var picks = Stack(); var search = Input("", "AdminDiscoverListSearch", "Search MDBList"); search.MaxLength = 200;
                var sourcesRevision = 0;
                async Task Sources(string kind)
                {
                    var owner = Owner; var revision = ++sourcesRevision; if (owner == null) return;
                    var token = _dialogReads?.Token ?? owner.Token;
                    if (kind == "search") { var capabilities = await _api.ListSourcesAsync(owner, "capabilities", ct: token); if (!Current(owner) || revision != sourcesRevision || _dialog == null) return; if (!J.Flag(capabilities, "mdblist_search")) { picks.Children.Clear(); picks.Children.Add(U.Text("MDBList search is not enabled on this server. Choose a popular list or paste a public link.", 13, true)); return; } }
                    var result = await _api.ListSourcesAsync(owner, kind, search.Text, token); if (!Current(owner) || revision != sourcesRevision || _dialog == null) return;
                    picks.Children.Clear();
                    var items = kind == "templates" ? J.Items(result, "categories").SelectMany(category => J.Items(category, "templates")).OfType<JsonObject>().Where(item => J.Text(item, "source") == "mdblist").Take(100).ToArray() : J.Items(result).OfType<JsonObject>().Take(100).ToArray();
                    if (items.Length == 0) picks.Children.Add(U.Text("No lists found.", 13, true));
                    foreach (var item in items)
                    {
                        var url = kind == "templates" ? J.Text(item["mdblist"], "url") : J.Text(item, "url"); var title = kind == "templates" ? J.Text(item, "title") : J.Text(item, "name");
                        if (url.Length > 0) picks.Children.Add(Button(title, "AdminDiscoverPick-" + J.Text(item, "id"), () => { link.Text = url; nameInput.Text = title; return Task.CompletedTask; }));
                    }
                }
                search.TextChanged += (_, _) => sourcesRevision++;
                listForm.Panel.Children.Add(Stack(search, Row(Button("Search", "AdminDiscoverListSearchGo", () => Sources("search")), Button("Popular lists", "AdminDiscoverListTop", () => Sources("templates"))), picks));
            }
            form.Panel.Children.Add(listForm.Panel); form.Readers.Add(() => form.Data["list"] = listForm.Read());
        }
        if (type is not ("studios" or "networks" or "genres"))
        {
            var preview = Stack();
            form.Panel.Children.Add(Button("Preview", "AdminDiscoverPreview", async () =>
            {
                var owner = Owner; if (owner == null) return; var input = form.Read(); var signature = input.ToJsonString(); var result = await _api.PreviewRowAsync(owner, input, _dialogReads?.Token ?? owner.Token); if (!Current(owner) || _dialog == null || signature != form.Read().ToJsonString()) return;
                preview.Children.Clear(); foreach (var title in J.Items(result, "results").Take(20)) preview.Children.Add(U.Text(J.Text(title, "title") + " · " + J.Text(title, "year"), 13));
            })); form.Panel.Children.Add(preview);
        }
        await EditAsync(creating ? "Add row" : "Edit row", form.Panel, async lease =>
        {
            var changed = form.Read(); var rows = J.Items(layout.Value, "rows").OfType<JsonObject>().Select(AdminRequestRules.RowBody).ToList();
            var position = rows.FindIndex(value => J.Text(value, "id") == id && id.Length > 0); if (position < 0) rows.Add(changed); else rows[position] = changed;
            _layout = await _api.SaveDocumentAsync(lease, layout, new JsonObject { ["rows"] = new JsonArray(rows.Select(value => (JsonNode)value).ToArray()) });
        }, reload: () => creating ? AddDiscoverRowAsync() : EditDiscoverRowAsync(id));
        if (Owner is { IsCurrent: true }) await ReloadAsync();
    }
    private RequestForm DiscoverFilterForm(JsonObject filter)
    {
        var form = new RequestForm(filter); var media = J.Text(filter, "media_type");
        var mediaSelect = form.Choice("media_type", "Media type", "AdminDiscoverFilterMedia", [("movie", "Movies"), ("series", "Series")]);
        var sortSelect = form.Choice("sort_by", "Order", "AdminDiscoverFilterSort", new[] { ("popularity.desc", "Most popular"), ("vote_average.desc", "Highest rated"), ("vote_count.desc", "Most voted"), ("first_air_date.desc", "Newest (series)"), ("primary_release_date.desc", "Newest (movies)"), ("first_air_date.asc", "Oldest (series)"), ("primary_release_date.asc", "Oldest (movies)"), ("revenue.desc", "Highest grossing") });
        form.Values("genre_ids", "Genres", "AdminDiscoverGenres", Genres(media), true);
        form.Values("exclude_genre_ids", "Exclude genres", "AdminDiscoverExcludeGenres", Genres(media), true);
        form.Values("keyword_ids", "TMDB keywords", "AdminDiscoverKeywords", [], true);
        form.Values("company_ids", "Studios", "AdminDiscoverCompanies", [], true);
        var networks = form.Values("network_ids", "Networks", "AdminDiscoverNetworks", [], true);
        var region = form.Text("watch_region", "Region", "AdminDiscoverRegion", maximum: 2); if (region.Text.Length == 0) region.Text = "US";
        var providers = Stack(); var providerForm = new RequestForm(filter); long providerRevision = 0; var providerRegion = region.Text.Trim();
        // Saved services belong to one media type and region; changing either drops them, so none is saved unseen.
        void DropProviders() { form.Data.Remove("watch_provider_ids"); providerForm = new RequestForm(filter); }
        form.Panel.Children.Add(Button("Streaming services", "AdminDiscoverProvidersLoad", async () =>
        {
            var owner = Owner; var revision = ++providerRevision; if (owner == null) return; var choices = await _api.WatchProvidersAsync(owner, J.Text(form.Read(), "media_type"), region.Text, _dialogReads?.Token ?? owner.Token); if (!Current(owner) || revision != providerRevision || _dialog == null) return;
            providers.Children.Clear(); providerForm = new RequestForm(filter); providerForm.Values("watch_provider_ids", "Streaming services", "AdminDiscoverProviders", choices.Select(item => (J.Text(item, "id"), J.Text(item, "name"))), true); providers.Children.Add(providerForm.Panel);
        })); form.Panel.Children.Add(providers); form.Readers.Add(() => providerForm.Read());
        var more = new RequestForm(filter);
        more.Choice("original_language", "Original language", "AdminDiscoverLanguage", new[] { ("", "Any language") }.Concat(Languages()));
        more.Values("origin_countries", "Countries of origin", "AdminDiscoverCountries", Countries());
        more.Number("year_from", "From year", "AdminDiscoverYearFrom", 1, 9999); more.Number("year_to", "To year", "AdminDiscoverYearTo", 1, 9999);
        more.Number("vote_average_gte", "Minimum TMDB score", "AdminDiscoverScore", 0, 10, integer: false); more.Number("vote_count_gte", "Minimum votes", "AdminDiscoverVotes");
        more.Number("runtime_gte", "Shortest runtime (minutes)", "AdminDiscoverRuntimeFrom"); more.Number("runtime_lte", "Longest runtime (minutes)", "AdminDiscoverRuntimeTo");
        var ratings = new[] { "", "G", "PG", "PG-13", "R", "NC-17", "TV-Y", "TV-Y7", "TV-G", "TV-PG", "TV-14", "TV-MA" };
        var ratingSelect = more.Choice("certification_lte", "Highest content rating", "AdminDiscoverRating", ratings.Select(rating => (rating, rating.Length == 0 ? "No rating limit" : rating)));
        form.Panel.Children.Add(new Expander { Header = "More options", Content = more.Panel, HorizontalAlignment = HorizontalAlignment.Stretch });
        form.Readers.Add(() => more.Read());
        void AdaptMedia()
        {
            var selectedMedia = Selected(mediaSelect); var series = selectedMedia == "series";
            if (selectedMedia != media)
            {
                media = selectedMedia;
                form.UpdateChoices("genre_ids", Genres(media), true); form.UpdateChoices("exclude_genre_ids", Genres(media), true);
                form.UpdateChoices("network_ids", [], true); ratingSelect.SelectedIndex = 0; DropProviders();
            }
            networks.Visibility = series ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in sortSelect.Items.OfType<ComboBoxItem>())
            {
                var value = item.Tag?.ToString() ?? ""; item.IsEnabled = series ? !value.StartsWith("primary_release_date", StringComparison.Ordinal) && value != "revenue.desc" : !value.StartsWith("first_air_date", StringComparison.Ordinal);
            }
            if (sortSelect.SelectedItem is ComboBoxItem selected && !selected.IsEnabled)
            {
                var value = Selected(sortSelect).Replace(series ? "primary_release_date" : "first_air_date", series ? "first_air_date" : "primary_release_date", StringComparison.Ordinal);
                sortSelect.SelectedItem = sortSelect.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.IsEnabled && Equals(item.Tag, value)) ?? sortSelect.Items[0];
            }
            // A saved rating outside the list cannot be matched to a media type, so it stays selectable instead of being dropped on open.
            foreach (var item in ratingSelect.Items.OfType<ComboBoxItem>()) { var value = item.Tag?.ToString() ?? ""; item.IsEnabled = value.Length == 0 || !ratings.Contains(value) || series == value.StartsWith("TV-", StringComparison.Ordinal); }
            if (ratingSelect.SelectedItem is ComboBoxItem rating && !rating.IsEnabled) ratingSelect.SelectedIndex = 0;
            providerRevision++; providers.Children.Clear();
        }
        mediaSelect.SelectionChanged += (_, _) => AdaptMedia(); AdaptMedia();
        region.TextChanged += (_, _) =>
        {
            providerRevision++; providers.Children.Clear();
            if (region.Text.Trim() == providerRegion) return;
            providerRegion = region.Text.Trim(); DropProviders();
        };
        form.Readers.Add(() => { if (Selected(mediaSelect) != "series") form.Data.Remove("network_ids"); });
        return form;
    }
    private static JsonObject NewDiscoverRow(string type) => type switch
    {
        "tmdb_chart" => JsonNode.Parse("{\"type\":\"tmdb_chart\",\"title\":\"Trending Movies\",\"enabled\":true,\"limit\":20,\"chart\":{\"preset\":\"trending\",\"media_type\":\"movie\",\"time_window\":\"week\"}}")!.AsObject(),
        "tmdb_discover" => JsonNode.Parse("{\"type\":\"tmdb_discover\",\"title\":\"\",\"enabled\":true,\"limit\":20,\"discover\":{\"media_type\":\"movie\",\"sort_by\":\"popularity.desc\"}}")!.AsObject(),
        _ => new JsonObject { ["type"] = type, ["title"] = "", ["enabled"] = true, ["limit"] = 20, ["list"] = new JsonObject { ["url"] = "" } },
    };
    private static IEnumerable<JsonObject> StockRows()
    {
        foreach (var (id, title, preset, media) in new[] { ("trending_movies", "Trending Movies", "trending", "movie"), ("trending_series", "Trending Series", "trending", "series"), ("popular_movies", "Popular Movies", "popular", "movie"), ("popular_series", "Popular Series", "popular", "series"), ("upcoming_movies", "Upcoming Movies", "upcoming", "movie"), ("on_air_series", "On Air Series", "on_the_air", "series") })
        {
            var chart = new JsonObject { ["preset"] = preset, ["media_type"] = media }; if (preset == "trending") chart["time_window"] = "week";
            yield return new JsonObject { ["id"] = id, ["type"] = "tmdb_chart", ["title"] = title, ["enabled"] = true, ["limit"] = 20, ["chart"] = chart };
        }
        foreach (var type in new[] { "studios", "networks", "genres" }) yield return new JsonObject { ["id"] = type, ["type"] = type, ["title"] = char.ToUpperInvariant(type[0]) + type[1..], ["enabled"] = true };
    }
}
