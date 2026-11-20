using System.Globalization;
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
    private string _routingMedia = "movie";
    private void RenderRouting()
    {
        _settingsRouting.Children.Clear(); if (_routing == null || _routes == null) return;
        var routing = _routing; var snapshot = _routes; var standard = J.Text(routing.Value, "mode") == "standard";
        var panel = Stack(U.Text(S("web.admin.settings.requests.routing.label"), 20), U.Text(S("web.admin.settings.requests.routing." + (standard ? "standardDescription" : "advancedDescription")), 13, true));
        var mode = Select("AdminRequestRoutingMode", [("standard", S("web.admin.settings.requests.routingMode.standard")), ("advanced", S("web.admin.settings.requests.routingMode.advanced"))], standard ? "standard" : "advanced");
        var reason = J.Text(routing.Value, "standard_unavailable_reason");
        if (reason.Length > 0)
        {
            foreach (var item in mode.Items.OfType<ComboBoxItem>().Where(item => Equals(item.Tag, "standard"))) item.IsEnabled = standard;
            panel.Children.Add(U.Text(S("web.admin.settings.requests.routingMode.standardUnavailable", new { reason = SafeDisplay(reason) }), 13, true));
        }
        var owner = Owner;
        mode.SelectionChanged += async (_, _) =>
        {
            if (!Current(owner) || _busy || Selected(mode) == J.Text(routing.Value, "mode")) return;
            await WriteAsync(async lease => { _routing = await _api.SaveDocumentAsync(lease, routing, new JsonObject { ["mode"] = Selected(mode) }); }, RefreshConfigurationAsync);
        };
        panel.Children.Add(Field(S("web.admin.settings.requests.routingMode.label"), mode));
        if (standard)
        {
            foreach (var media in new[] { "movie", "series" })
            {
                var destination = J.Items(routing.Value, "standard").FirstOrDefault(row => J.Text(row, "media_type") == media);
                var hd = ServerName(J.Text(destination, "hd_integration_id")); var uhd = ServerName(J.Text(destination, "uhd_integration_id"));
                panel.Children.Add(U.Text((media == "movie" ? "Movies" : "Series") + " → " + (hd.Length > 0 ? hd : "No server yet") + " · 4K → " + (uhd.Length > 0 ? uhd : "No 4K versions"), 14));
            }
            panel.Children.Add(U.Text(S("web.admin.settings.requests.routingMode.uhdNote"), 12, true));
            var paused = snapshot.Rows.Count(document => !J.Flag(document.Value, "is_fallback") && J.Flag(document.Value, "enabled"));
            if (paused > 0) panel.Children.Add(U.Text(S("web.admin.settings.requests.routingMode.pausedRules", Count(paused)), 13, true));
        }
        else
        {
            var media = Select("AdminRequestRoutingMedia", [("movie", "Movies"), ("series", "Series")], _routingMedia);
            media.SelectionChanged += (_, _) => { if (Current(owner)) { _routingMedia = Selected(media); RenderRouting(); } };
            panel.Children.Add(media);
            var rows = snapshot.Rows.Where(document => J.Text(document.Value, "media_type") == _routingMedia).OrderBy(document => J.Flag(document.Value, "is_fallback") ? int.MaxValue : J.Number(document.Value, "position")).ToArray();
            var fallbackReady = rows.Any(document => J.Flag(document.Value, "is_fallback") && J.Text(document.Value["hd"], "integration_id").Length > 0);
            var add = Button(S("web.admin.settings.requests.shared.addRule"), "AdminRequestRuleAdd", () => AddRuleAsync(_routingMedia), true);
            add.IsEnabled = fallbackReady; panel.Children.Add(add);
            if (!fallbackReady) panel.Children.Add(U.Text("Set an HD server for Everything else before adding a rule.", 13, true));
            var rules = rows.Where(document => !J.Flag(document.Value, "is_fallback")).ToArray();
            for (var index = 0; index < rows.Length; index++)
            {
                var document = rows[index]; var row = document.Value; var id = J.Text(row, "id"); var fallback = J.Flag(row, "is_fallback");
                var title = fallback ? S("web.admin.settings.requests.shared.everythingElse") : J.Text(row, "name");
                var card = Stack(U.Text(title, 18), U.Text(RouteSummary(row), 13, true));
                var actions = Row(Button(S("web.admin.settings.requests.shared.edit"), "AdminRequestRuleEdit-" + id, () => EditRuleAsync(id)));
                if (!fallback)
                {
                    var toggle = Toggle("Enabled", "AdminRequestRuleEnabled-" + id, J.Flag(row, "enabled"));
                    toggle.Toggled += async (_, _) =>
                    {
                        if (!Current(owner) || _busy) return;
                        await WriteAsync(async lease => { var canonical = await _api.GetRouteAsync(lease, id); var body = canonical.Value; body["enabled"] = toggle.IsOn; await _api.SaveDocumentAsync(lease, canonical, body); }, RefreshConfigurationAsync);
                    }; card.Children.Add(toggle);
                    var position = Array.IndexOf(rules, document);
                    var up = Button("↑", "AdminRequestRuleUp-" + id, () => MoveRuleAsync(snapshot, _routingMedia, rules, position, -1)); up.IsEnabled = position > 0;
                    var down = Button("↓", "AdminRequestRuleDown-" + id, () => MoveRuleAsync(snapshot, _routingMedia, rules, position, 1)); down.IsEnabled = position < rules.Length - 1;
                    actions.Children.Add(up); actions.Children.Add(down);
                    actions.Children.Add(Button(S("web.admin.settings.requests.shared.delete"), "AdminRequestRuleDelete-" + id, async () =>
                    {
                        var canonical = await _api.GetRouteAsync(owner!, id);
                        if (Current(owner) && await ConfirmAsync("Delete rule?", "Delete “" + J.Text(canonical.Value, "name") + "”? Requests that matched it will use the next matching rule.", "Delete"))
                            await WriteAsync(lease => _api.DeleteDocumentAsync(lease, canonical), RefreshConfigurationAsync);
                    }));
                }
                card.Children.Add(actions); panel.Children.Add(Card(card));
            }
        }
        panel.Children.Add(BuildRoutingPreview()); _settingsRouting.Children.Add(Card(panel));
    }
    private string ServerName(string id) => J.Text(_servers.FirstOrDefault(document => J.Text(document.Value, "id") == id)?.Value, "name");
    private string RouteSummary(JsonObject route)
    {
        var conditions = route["conditions"] as JsonObject;
        var summary = conditions == null || conditions.Count == 0 ? "Every request" : string.Join(" · ", conditions.Select(pair => pair.Key.Replace('_', ' ') + ": " + (pair.Value is JsonArray array ? string.Join(", ", array.Select(v => v?.ToString())) : pair.Value?.ToString())));
        return summary + "\nHD → " + (ServerName(J.Text(route["hd"], "integration_id")) is { Length: > 0 } hd ? hd : "Next rule") + " · 4K → " + (J.Flag(route, "skip_uhd") ? "No 4K copy" : ServerName(J.Text(route["uhd"], "integration_id")) is { Length: > 0 } uhd ? uhd : "Next rule");
    }
    private async Task MoveRuleAsync(AdminRequestRoutes snapshot, string media, AdminRequestDocument[] rules, int index, int change)
    {
        var to = index + change; if (to < 0 || to >= rules.Length) return;
        var ids = rules.Select(rule => J.Text(rule.Value, "id")).ToArray(); (ids[index], ids[to]) = (ids[to], ids[index]);
        await WriteAsync(lease => _api.ReorderRoutesAsync(lease, snapshot, media, ids), RefreshConfigurationAsync);
    }
    private async Task AddRuleAsync(string media)
    {
        var pick = Select("AdminRequestRulePreset", [("anime", S("web.admin.settings.requests.presets.anime.name")), ("kids", S("web.admin.settings.requests.presets.kids.name")), ("foreign", S("web.admin.settings.requests.presets.foreign.name")), ("custom", S("web.admin.settings.requests.presets.custom"))], "anime");
        string? preset = null;
        await EditAsync(S("web.admin.settings.requests.presets.title"), Stack(U.Text(S("web.admin.settings.requests.presets.description")), pick), _ => { preset = Selected(pick); return Task.CompletedTask; }, "Next", mutates: false);
        if (preset == null || Owner is not { IsCurrent: true }) return;
        var conditions = preset switch
        {
            "anime" => new JsonObject { ["anime"] = true },
            "kids" => new JsonObject { ["genre_ids"] = media == "series" ? new JsonArray(10751, 10762) : new JsonArray(10751), ["max_content_rating"] = "PG" },
            "foreign" => new JsonObject { ["exclude_original_languages"] = new JsonArray(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) },
            _ => new JsonObject(),
        };
        var data = new JsonObject { ["media_type"] = media, ["name"] = preset switch { "anime" => "Anime", "kids" => "Kids & family", "foreign" => "Foreign language", _ => "" }, ["enabled"] = true,
            ["conditions"] = conditions, ["hd"] = new JsonObject(), ["uhd"] = new JsonObject(), ["skip_uhd"] = false };
        await RuleEditorAsync(null, data, preset == "anime");
    }
    private async Task EditRuleAsync(string id)
    {
        var owner = Owner; if (!Current(owner)) return;
        var document = await _api.GetRouteAsync(owner!, id); if (Current(owner)) await RuleEditorAsync(document, document.Value, false);
    }
    private async Task RuleEditorAsync(AdminRequestDocument? current, JsonObject data, bool animePreset)
    {
        var media = J.Text(data, "media_type"); var fallback = J.Flag(data, "is_fallback"); var form = new RequestForm(data);
        if (!fallback)
        {
            form.Text("name", "Name", "AdminRequestRuleName", maximum: 100, required: true); form.Flag("enabled", "Enabled", "AdminRequestRuleEnabled");
            var conditions = ConditionsForm(data["conditions"] as JsonObject ?? new(), media); form.Panel.Children.Add(conditions.Panel); form.Readers.Add(() => form.Data["conditions"] = conditions.Read());
        }
        else { form.Panel.Children.Add(U.Text(S("web.admin.settings.requests.fallback.description", new { media_type = media }), 13, true)); form.Data["enabled"] = true; form.Data["conditions"] = new JsonObject(); form.Data["skip_uhd"] = false; }
        var hd = DestinationForm(data, "hd", media, fallback, animePreset); var uhd = DestinationForm(data, "uhd", media, fallback, animePreset);
        form.Panel.Children.Add(hd.Panel); form.Panel.Children.Add(uhd.Panel);
        form.Readers.Add(() => { hd.Read(); uhd.Read(); });
        await EditAsync(fallback ? S("web.admin.settings.requests.fallback.title", new { media_type = media }) : current == null ? S("web.admin.settings.requests.shared.addRule") : "Edit rule", form.Panel, async lease =>
        {
            var body = form.Read();
            if (!J.Flag(_capabilities, "routing_series_status") && body["conditions"] is JsonObject conditions && (conditions.ContainsKey("series_statuses") || conditions.ContainsKey("exclude_series_statuses"))) throw new ArgumentException("This server does not support series status conditions. Review this rule on a server that supports them.");
            if (current == null) await _api.CreateRouteAsync(lease, body); else await _api.SaveDocumentAsync(lease, current, body);
        }, reload: current == null ? RefreshConfigurationAsync : () => EditRuleAsync(J.Text(data, "id")));
        if (Owner is { IsCurrent: true }) await RefreshConfigurationAsync();
    }
    private RequestForm ConditionsForm(JsonObject data, string media)
    {
        var form = new RequestForm((JsonObject)data.DeepClone());
        form.Panel.Children.Add(U.Text(S("web.admin.settings.requests.conditions.heading"), 18)); form.Panel.Children.Add(U.Text(S("web.admin.settings.requests.conditions.description"), 12, true));
        var hosts = Stack(); var active = new HashSet<string>(StringComparer.Ordinal); var versions = new Dictionary<string, object>(StringComparer.Ordinal);
        var menu = new List<(string Value, string Label)> { ("anime", "Anime"), ("year", media == "series" ? "First aired" : "Release year"), ("max_content_rating", "Content rating") };
        var kinds = new List<(string Key, string Label, bool Numeric, IEnumerable<(string Value, string Label)> Choices)>
        {
            ("genre_ids", "Genre", true, Genres(media)), ("original_languages", "Original language", false, Languages()), ("origin_countries", "Country of origin", false, Countries()),
            ("keyword_ids", "TMDB keyword", true, []), ("requester_user_ids", "Requested by", true, _users.Select(user => (J.Text(user, "id"), J.Text(user, "username")))),
            (media == "series" ? "network_ids" : "company_ids", media == "series" ? "Network" : "Studio", true, []),
        };
        if (media == "series" && J.Flag(_capabilities, "routing_series_status")) kinds.Add(("series_statuses", "Status", false, new[] { "returning", "in_production", "upcoming", "ended", "cancelled" }.Select(status => (status, status.Replace('_', ' ')))));
        foreach (var kind in kinds) { menu.Add((kind.Key, kind.Label + " · is any of")); menu.Add(("exclude_" + kind.Key, kind.Label + " · is none of")); }
        var pick = Select("AdminRequestConditionKind", new[] { ("", S("web.admin.settings.requests.conditions.addCondition")) }.Concat(menu), "");
        void Add(string key)
        {
            if (key.Length == 0 || !active.Add(key)) return;
            if (!menu.Any(option => option.Value == key)) { active.Remove(key); return; }
            var version = new object(); versions[key] = version;
            var label = menu.FirstOrDefault(pair => pair.Value == key).Label ?? key;
            var child = new RequestForm(form.Data); var host = Stack();
            if (key == "anime")
            {
                var select = Select("AdminRequestConditionAnime", [("true", S("web.admin.settings.requests.conditions.isAnime")), ("false", S("web.admin.settings.requests.conditions.isNotAnime"))], form.Data["anime"] == null || J.Flag(form.Data, "anime") ? "true" : "false");
                child.Panel.Children.Add(select); child.Readers.Add(() => form.Data["anime"] = Selected(select) == "true");
            }
            else if (key == "year") { child.Number("year_from", "From year", "AdminRequestConditionYearFrom", 1, 9999); child.Number("year_to", "To year", "AdminRequestConditionYearTo", 1, 9999); }
            else if (key == "max_content_rating") child.Choice(key, "Highest content rating", "AdminRequestConditionRating", new[] { "G", "PG", "PG-13", "R", "NC-17", "TV-Y", "TV-Y7", "TV-G", "TV-PG", "TV-14", "TV-MA" }.Select(rating => (rating, rating)));
            else
            {
                var field = key.StartsWith("exclude_", StringComparison.Ordinal) ? key[8..] : key;
                var kind = kinds.FirstOrDefault(kind => kind.Key == field);
                child.Values(key, label, "AdminRequestCondition-" + key, kind.Choices ?? [], kind.Numeric, 100);
            }
            var remove = Button("Remove condition", "AdminRequestConditionRemove-" + key, () =>
            {
                active.Remove(key); versions.Remove(key); if (key == "year") { form.Data.Remove("year_from"); form.Data.Remove("year_to"); } else form.Data.Remove(key);
                hosts.Children.Remove(host); return Task.CompletedTask;
            });
            host.Children.Add(child.Panel); host.Children.Add(remove); hosts.Children.Add(host);
            form.Readers.Add(() => { if (versions.TryGetValue(key, out var current) && ReferenceEquals(current, version)) child.Read(); });
        }
        foreach (var key in form.Data.Select(pair => pair.Key).ToArray()) Add(key is "year_from" or "year_to" ? "year" : key);
        pick.SelectionChanged += (_, _) => { Add(Selected(pick)); pick.SelectedIndex = 0; };
        form.Panel.Children.Add(pick); form.Panel.Children.Add(hosts); return form;
    }
    private RequestForm DestinationForm(JsonObject route, string tier, string media, bool fallback, bool animePreset)
    {
        var form = new RequestForm(route); var destination = route[tier] as JsonObject ?? new();
        var selectedId = J.Text(destination, "integration_id");
        var eligible = _servers.Select(document => document.Value).Where(server => J.Flag(server, "enabled") && ServerMedia(server, media) && ServerTier(server, tier)).ToArray();
        var entries = new List<(string Value, string Label)> { ("", fallback ? tier == "uhd" ? "No 4K versions" : "Choose a server" : "Use the next matching rule") };
        entries.AddRange(eligible.Select(server => (J.Text(server, "id"), J.Text(server, "name"))));
        if (!fallback && tier == "uhd") entries.Add(("skip", "Don't make a 4K copy"));
        if (selectedId.Length > 0 && !eligible.Any(server => J.Text(server, "id") == selectedId)) entries.Add((selectedId, ServerName(selectedId) + " · unavailable"));
        var select = Select("AdminRequestDestination-" + tier, entries, tier == "uhd" && J.Flag(route, "skip_uhd") ? "skip" : selectedId);
        form.Panel.Children.Add(Field(tier == "hd" ? "HD version" : "4K version", select));
        var host = Stack(); form.Panel.Children.Add(host); var options = new JsonObject(); var overrides = new RequestForm(destination["overrides"] as JsonObject ?? new());
        var previous = selectedId; long generation = 0;
        void Render()
        {
            host.Children.Clear(); var id = Selected(select); var server = _servers.FirstOrDefault(document => J.Text(document.Value, "id") == id)?.Value;
            if (server == null) return;
            var capability = RouterCapability(server); if (capability == null) { host.Children.Add(U.Text("The server's plugin is unavailable. Choose another server.", 13, true)); return; }
            overrides = PluginForm(capability, overrides.Data, options, true, server["plugin_config"] as JsonObject);
            host.Children.Add(overrides.Panel);
            host.Children.Add(Button("Load server settings", "AdminRequestDestinationOptions-" + tier, async () =>
            {
                var owner = Owner; var revision = ++generation; if (!Current(owner)) return;
                var result = await _api.LoadOptionsAsync(owner!, id, new JsonObject(), _dialogReads?.Token ?? owner!.Token); if (!Current(owner) || revision != generation || id != Selected(select) || _dialog == null) return;
                options = result["options"] as JsonObject ?? new(); RefreshPluginOptions(overrides, capability, options, true);
            }));
        }
        select.SelectionChanged += (_, _) =>
        {
            generation++;
            if (Selected(select) != previous)
            {
                overrides = new RequestForm(new()); options = new(); previous = Selected(select);
                var server = _servers.FirstOrDefault(document => J.Text(document.Value, "id") == previous)?.Value;
                if (animePreset && media == "series" && server != null && ServerKind(server) == "sonarr") overrides.Data["series_type"] = "anime";
            }
            Render();
        };
        Render();
        form.Readers.Add(() =>
        {
            var id = Selected(select);
            if (id == "skip") { route[tier] = new JsonObject(); route["skip_uhd"] = true; return; }
            if (tier == "uhd") route["skip_uhd"] = false;
            if (id.Length == 0) { route[tier] = new JsonObject(); return; }
            if (!eligible.Any(server => J.Text(server, "id") == id)) throw new ArgumentException("Choose an enabled server that serves this media type and quality tier.");
            var values = overrides.Read(); route[tier] = new JsonObject { ["integration_id"] = id }; if (values.Count > 0) route[tier]!["overrides"] = values;
        }); return form;
    }
    private FrameworkElement BuildRoutingPreview()
    {
        var media = Select("AdminRequestPreviewMedia", [("movie", "Movies"), ("series", "Series")], _routingMedia);
        var query = Input("", "AdminRequestPreviewSearch", "Search by title"); query.MaxLength = 200;
        var requester = Input("", "AdminRequestPreviewRequester", "Account ID (optional)"); requester.MaxLength = 10;
        var results = Stack(); var preview = Stack(); long revision = 0; long previewRevision = 0; var owner = Owner;
        var search = Button("Search", "AdminRequestPreviewSearchGo", async () =>
        {
            var expected = ++revision; var titles = await _api.SearchRouteTitlesAsync(owner!, Selected(media), query.Text);
            if (!Current(owner) || expected != revision) return; results.Children.Clear(); preview.Children.Clear();
            if (titles.Count == 0) results.Children.Add(U.Text(S("web.admin.settings.requests.preview.noMatches"), 13, true));
            foreach (var title in titles)
            {
                var item = title!;
                results.Children.Add(Button(J.Text(item, "title") + " · " + J.Text(item, "year"), "AdminRequestPreviewTitle-" + J.Number(item, "tmdb_id"), async () =>
                {
                    if (expected != revision) return;
                    var selection = ++previewRevision; preview.Children.Clear();
                    var requestedBy = requester.Text.Trim();
                    try
                    {
                        var result = await _api.PreviewRouteAsync(owner!, J.Text(item, "media_type"), J.Number(item, "tmdb_id"), requestedBy.Length == 0 ? null : requestedBy);
                        if (!Current(owner) || expected != revision || selection != previewRevision || requester.Text.Trim() != requestedBy) return;
                        var rendered = await RoutePreviewAsync(result, owner!, owner!.Token);
                        if (!Current(owner) || expected != revision || selection != previewRevision || requester.Text.Trim() != requestedBy) return; preview.Children.Clear(); preview.Children.Add(rendered);
                    }
                    // A superseded selection's late failure says nothing about the preview now on screen.
                    catch (Exception) when (!Current(owner) || expected != revision || selection != previewRevision || requester.Text.Trim() != requestedBy) { }
                }));
            }
        });
        query.TextChanged += (_, _) => revision++; media.SelectionChanged += (_, _) => revision++; requester.TextChanged += (_, _) => { previewRevision++; preview.Children.Clear(); };
        return Stack(U.Text(S("web.admin.settings.requests.preview.heading"), 18), U.Text(S("web.admin.settings.requests.preview.caption"), 12, true), media, query, requester, search, results, preview);
    }
    private async Task<FrameworkElement> RoutePreviewAsync(JsonObject result, MonitorAuthorityLease lease, CancellationToken ct)
    {
        var names = new Dictionary<string, (JsonArray Fields, JsonObject Options)>(StringComparer.Ordinal);
        var tiers = J.Items(result, "tiers").OfType<JsonObject>().ToArray();
        var ids = tiers.Where(tier => tier["overrides"] is JsonObject { Count: > 0 }).Select(tier => J.Text(tier, "integration_id")).Where(id => id.Length > 0).Distinct().Take(2).ToArray();
        var plugins = (JsonArray)_plugins.DeepClone();
        if (ids.Length > 0 && plugins.Count == 0)
        {
            try { plugins = await _api.ListPluginsAsync(lease, ct); }
            catch (Exception) when (lease.IsCurrent && !ct.IsCancellationRequested) { }
            lease.ThrowIfRetired(); ct.ThrowIfCancellationRequested();
        }
        foreach (var id in ids)
        {
            JsonObject? server = _servers.FirstOrDefault(document => J.Text(document.Value, "id") == id)?.Value;
            try { server ??= (await _api.GetIntegrationAsync(lease, id, ct)).Value; }
            catch (Exception) when (lease.IsCurrent && !ct.IsCancellationRequested) { }
            lease.ThrowIfRetired(); ct.ThrowIfCancellationRequested();
            var plugin = plugins.FirstOrDefault(item => J.Text(item, "id") == J.Text(server, "installation_id"));
            var capability = J.Items(plugin, "capabilities").FirstOrDefault(item => J.Text(item, "id") == J.Text(server, "capability_id"));
            var fields = J.Items(J.Items(capability, "config_schema").FirstOrDefault()?["admin_form"], "fields");
            var options = new JsonObject();
            if (tiers.Where(tier => J.Text(tier, "integration_id") == id).Any(tier => tier["overrides"] is JsonObject overrides && overrides.Any(pair => pair.Key is "quality_profile_id" or "tags" or "metadata_language")))
            {
                try { options = (await _api.LoadOptionsAsync(lease, id, new JsonObject(), ct))["options"] as JsonObject ?? new(); }
                catch (Exception) when (lease.IsCurrent && !ct.IsCancellationRequested) { }
                lease.ThrowIfRetired(); ct.ThrowIfCancellationRequested();
            }
            names[id] = (fields, options);
        }
        var panel = Stack();
        foreach (var tier in tiers)
        {
            panel.Children.Add(U.Text((J.Text(tier, "quality") == "uhd" ? "4K" : J.Text(tier, "quality")) + " → " + J.Text(tier, "integration_name") + " · " + J.Text(tier, "route_name") + " · " + SafeDisplay(J.Text(tier, "note")), 14));
            if (tier["overrides"] is not JsonObject overrides) continue;
            names.TryGetValue(J.Text(tier, "integration_id"), out var resolved);
            foreach (var pair in overrides)
            {
                if (pair.Value == null || pair.Value is JsonArray { Count: 0 }) continue;
                var field = resolved.Fields?.FirstOrDefault(item => J.Text(item, "key") == pair.Key);
                if (J.Flag(field, "secret") || J.Text(field, "control") == "PASSWORD" || new[] { "password", "credential", "secret", "token", "api_key" }.Any(word => pair.Key.Contains(word, StringComparison.OrdinalIgnoreCase))) continue;
                var labelKey = pair.Key switch { "root_folder" => "rootFolder", "quality_profile_id" => "qualityProfile", "tags" => "tags", "series_type" => "seriesType", "minimum_availability" => "minimumAvailability", "search_on_add" => "searchOnAdd", "season_folder" => "seasonFolder", _ => null };
                var label = labelKey != null ? S("web.admin.settings.requests.model.override." + labelKey) : J.Text(field, "label") is { Length: > 0 } declared ? declared : pair.Key.Replace('_', ' ');
                string ValueLabel(JsonNode? value)
                {
                    if (value is JsonArray values) return string.Join(", ", values.Select(ValueLabel));
                    if (value is JsonValue boolean && boolean.TryGetValue<bool>(out var flag)) return S("web.admin.settings.requests.model.override." + (flag ? "on" : "off"));
                    var raw = value?.ToString() ?? "";
                    var choices = resolved.Options?[pair.Key] as JsonArray ?? J.Items(field, "options");
                    return SafeDisplay(pair.Key == "root_folder" ? raw : choices.FirstOrDefault(option => J.Text(option, "value") == raw) is { } choice ? J.Text(choice, "label") : raw);
                }
                panel.Children.Add(U.Text(label + ": " + ValueLabel(pair.Value), 13, true));
            }
        }
        var trace = Stack();
        if (result["facts"] is JsonObject facts)
            foreach (var pair in facts) trace.Children.Add(U.Text(pair.Key.Replace('_', ' ') + ": " + (pair.Value is JsonArray values ? string.Join(", ", values.Select(value => value?.ToString())) : pair.Value?.ToString()), 12, true));
        foreach (var rule in J.Items(result, "rules")) trace.Children.Add(U.Text(J.Text(rule, "route_name") + " · HD: " + J.Text(rule, "hd") + " · 4K: " + J.Text(rule, "uhd") + "\n" + string.Join(", ", J.Items(rule, "unmet_conditions").Select(node => node?.ToString())), 12, true));
        panel.Children.Add(new Expander { Header = S("web.admin.settings.requests.preview.howDecided"), Content = trace, HorizontalAlignment = HorizontalAlignment.Stretch });
        return panel;
    }
}
