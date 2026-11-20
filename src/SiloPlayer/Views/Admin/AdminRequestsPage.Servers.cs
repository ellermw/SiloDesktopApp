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
    private static readonly HashSet<string> RoutingOwned = ["is_default", "is_default_4k", "is_4k", "anime_enabled", "anime_root_folder", "anime_quality_profile_id", "anime_tags"];
    private IEnumerable<(string Installation, JsonObject Capability, string Label)> RouterTypes() => _plugins.OfType<JsonObject>().SelectMany(plugin =>
        J.Items(plugin, "capabilities").OfType<JsonObject>().Where(capability => J.Text(capability, "type") == "request_router.v1" || J.Text(capability, "id") == "request_router.v1")
            .Select(capability => (J.Text(plugin, "id"), capability, J.Text(capability, "display_name") is { Length: > 0 } name ? name : J.Text(plugin, "plugin_id"))));
    private JsonObject? RouterCapability(JsonObject server) => RouterTypes().FirstOrDefault(entry => entry.Installation == J.Text(server, "installation_id") && J.Text(entry.Capability, "id") == J.Text(server, "capability_id")).Capability;
    private static bool Is4K(JsonNode? config) => J.Flag(config, "is_4k") || J.Flag(config, "is_default_4k") || J.Text(config, "is_4k") == "true" || J.Text(config, "is_default_4k") == "true";
    private static string ServerKind(JsonObject server) => J.Text(server["plugin_config"], "service_kind").ToLowerInvariant();
    private static bool ServerMedia(JsonObject server, string media) => ServerKind(server) switch { "radarr" => media == "movie", "sonarr" => media == "series", _ => J.Items(server, "supported_media_types").Count == 0 || J.Items(server, "supported_media_types").Any(node => node?.ToString() == media) };
    private static bool ServerTier(JsonObject server, string tier) => ServerKind(server) is not ("radarr" or "sonarr") || Is4K(server["plugin_config"]) == (tier == "uhd");
    private void RenderServers()
    {
        _settingsServers.Children.Clear();
        _settingsServers.Children.Add(U.Text(S("web.admin.settings.requests.servers.label"), 20));
        _settingsServers.Children.Add(U.Text(S("web.admin.settings.requests.servers.description"), 13, true));
        if (_serverLoadError != null || _pluginLoadError != null)
        {
            _settingsServers.Children.Add(U.Text(_serverLoadError ?? _pluginLoadError!, 13, true));
            _settingsServers.Children.Add(Button("Try again", "AdminRequestServersRetry", RefreshConfigurationAsync)); return;
        }
        var choices = RouterTypes().ToArray();
        var add = Button(S("web.admin.settings.requests.servers.addServer"), "AdminRequestServerAdd", () => EditServerAsync(null), true); add.IsEnabled = choices.Length > 0;
        _settingsServers.Children.Add(add);
        if (choices.Length == 0) _settingsServers.Children.Add(U.Text(S("web.admin.settings.requests.servers.noPlugin"), 13, true));
        if (_servers.Count == 0) _settingsServers.Children.Add(U.Text(S("web.admin.settings.requests.servers.empty"), 14, true));
        foreach (var document in _servers)
        {
            var server = document.Value; var id = J.Text(server, "id"); var capability = RouterCapability(server);
            var ready = J.Flag(server, "enabled") && J.Text(server, "installation_id").Length > 0 && (J.Flag(server, "has_api_key") || J.Flag(capability?["request_router"], "api_key_optional"));
            var state = !J.Flag(server, "enabled") ? S("web.admin.settings.requests.servers.disabled") : !ready ? S("web.admin.settings.requests.servers.needsSetup") : J.Text(server, "last_check_error").Length > 0 ? S("web.admin.settings.requests.servers.unreachable") : S("web.admin.settings.requests.serverEditor.testConnected");
            var kind = ServerKind(server); var name = J.Text(server, "name");
            var panel = Stack(U.Text(name, 18), U.Text((kind.Length > 0 ? char.ToUpperInvariant(kind[0]) + kind[1..] : J.Text(capability, "display_name")) + " · " + state, 13, true));
            if (J.Text(server, "last_check_error").Length > 0) panel.Children.Add(U.Text(SafeDisplay(J.Text(server, "last_check_error")), 12, true));
            foreach (var usage in RouteUses(id)) panel.Children.Add(U.Text(usage, 12, true));
            panel.Children.Add(Button(S("web.admin.settings.requests.shared.edit"), "AdminRequestServerEdit-" + id, () => EditServerAsync(id)));
            _settingsServers.Children.Add(Card(panel));
        }
    }
    private IEnumerable<string> RouteUses(string id) => (_routes?.Rows ?? []).Select(document => document.Value)
        .Where(route => J.Text(route["hd"], "integration_id") == id || J.Text(route["uhd"], "integration_id") == id)
        .Select(route => (J.Flag(route, "is_fallback") ? S("web.admin.settings.requests.shared.everythingElse") : J.Text(route, "name")) + " · " + J.Text(route, "media_type"));
    private string DeleteBlockers(string id)
    {
        if (!J.Flag(_capabilities, "routing")) return "";
        if (_routes == null || _routing == null) return "Reload routing before deleting this server.";
        var standard = J.Text(_routing.Value, "mode") == "standard"; var routes = _routes.Rows.Select(document => document.Value).ToArray();
        return string.Join(", ", routes.Where(route =>
        {
            if (J.Text(route["hd"], "integration_id") != id && J.Text(route["uhd"], "integration_id") != id) return false;
            if (!J.Flag(route, "is_fallback")) return true;
            if (standard) return false;
            var media = J.Text(route, "media_type");
            var onlyThis = new[] { J.Text(route["hd"], "integration_id"), J.Text(route["uhd"], "integration_id") }.All(value => value.Length == 0 || value == id);
            var noRules = !routes.Any(value => J.Text(value, "media_type") == media && !J.Flag(value, "is_fallback"));
            var lastOfKind = !_servers.Any(server => J.Text(server.Value, "id") != id && ServerKind(server.Value) == (media == "movie" ? "radarr" : "sonarr"));
            return !(onlyThis && noRules && lastOfKind);
        }).Select(route => J.Flag(route, "is_fallback") ? "Everything else · " + J.Text(route, "media_type") : J.Text(route, "name")));
    }
    private async Task RefreshConfigurationAsync()
    {
        var owner = Owner; var generation = _generation; if (!Current(owner)) return;
        ClearReadNotice();
        try
        {
            var servers = await _api.ListIntegrationsAsync(owner!);
            if (!Current(owner) || generation != _generation) return; _servers = servers; _serverLoadError = null;
            if (_pluginLoadError != null) { var plugins = await _api.ListPluginsAsync(owner!); if (!Current(owner) || generation != _generation) return; _plugins = plugins; _pluginLoadError = null; }
            if (J.Flag(_capabilities, "routing"))
            {
                var routing = await _api.GetRoutingAsync(owner!); var routes = await _api.ListRoutesAsync(owner!);
                if (!Current(owner) || generation != _generation) return; _routing = routing; _routes = routes; RenderRouting();
            }
            RenderServers();
        }
        catch (Exception error) { if (Current(owner)) Notice(AdminRequestRules.Error(error), true); throw; }
    }
    private async Task EditServerAsync(string? id)
    {
        var owner = Owner; if (!Current(owner)) return;
        var current = id == null ? null : await _api.GetIntegrationAsync(owner!, id);
        if (!Current(owner)) return;
        if (current != null) ClearReadNotice();
        var data = current?.Value ?? new JsonObject { ["name"] = "", ["enabled"] = true, ["base_url"] = "", ["supported_media_types"] = new JsonArray(), ["plugin_config"] = new JsonObject() };
        var types = RouterTypes().ToArray(); if (types.Length == 0) { Notice(S("web.admin.settings.requests.servers.noPlugin")); return; }
        var form = new RequestForm(data); form.Text("name", S("web.admin.settings.requests.serverEditor.name"), "AdminRequestServerName", maximum: 200);
        form.Flag("enabled", S("web.admin.settings.requests.serverEditor.enabled"), "AdminRequestServerEnabled");
        var savedType = J.Text(data, "installation_id") + ":" + J.Text(data, "capability_id");
        var typeChoices = types.Select(entry => (Value: entry.Installation + ":" + J.Text(entry.Capability, "id"), entry.Label)).ToList();
        // A saved server whose plugin type is no longer offered keeps that type selected, so it is never re-pointed at another plugin unasked.
        var missing = current != null && !typeChoices.Any(choice => choice.Value == savedType);
        if (missing) typeChoices.Add((savedType, "Unavailable plugin"));
        var type = Select("AdminRequestServerType", typeChoices, savedType);
        if (missing) foreach (var item in type.Items.OfType<ComboBoxItem>().Where(item => Equals(item.Tag, savedType))) item.IsEnabled = false;
        if (type.SelectedItem == null) type.SelectedIndex = 0;
        var used = id != null && RouteUses(id).Any(); type.IsEnabled = !used;
        form.Panel.Children.Add(Field(S("web.admin.settings.requests.serverEditor.type"), type, used ? S("web.admin.settings.requests.serverEditor.typeLocked", new { routes = string.Join(", ", RouteUses(id!)) }) : null));
        var address = form.Text("base_url", S("web.admin.settings.requests.serverEditor.url"), "AdminRequestServerUrl");
        if (Uri.TryCreate(address.Text, UriKind.Absolute, out var storedUri) && (storedUri.UserInfo.Length > 0 || storedUri.Query.Length > 0)) address.Text = "";
        var key = new PasswordBox { PlaceholderText = J.Flag(data, "has_api_key") ? S("web.admin.settings.requests.serverEditor.apiKeySaved") : "API key", PasswordRevealMode = PasswordRevealMode.Hidden };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(key, "AdminRequestServerApiKey"); _secrets.Add(key);
        form.Panel.Children.Add(Field(S("web.admin.settings.requests.serverEditor.apiKey"), key));
        var configHost = Stack(); var configData = data["plugin_config"] as JsonObject ?? new JsonObject();
        var config = new RequestForm((JsonObject)configData.DeepClone()); var options = new JsonObject();
        var fourK = Toggle(S("web.admin.settings.requests.serverEditor.uhdServer"), "AdminRequestServer4K", Is4K(configData));
        form.Panel.Children.Add(fourK); form.Panel.Children.Add(U.Text(S("web.admin.settings.requests.serverEditor.uhdServerDescription"), 12, true)); form.Panel.Children.Add(configHost);
        long connectionRevision = 0; var checking = false;
        bool Unavailable() => missing && Selected(type) == savedType;
        (string Installation, JsonObject Capability, string Label) Entry() => Unavailable()
            ? throw new ArgumentException(used ? "This server's plugin is unavailable. Change the routes that use this server, then choose another type." : "This server's plugin is unavailable. Choose another type before saving or testing.")
            : types.First(entry => entry.Installation + ":" + J.Text(entry.Capability, "id") == Selected(type));
        void RebuildConfig()
        {
            configHost.Children.Clear();
            if (Unavailable()) { configHost.Children.Add(U.Text("This server's plugin is unavailable, so its settings cannot be shown.", 13, true)); fourK.Visibility = Visibility.Collapsed; return; }
            config = PluginForm(Entry().Capability, config.Data, options, false, lockKind: used); configHost.Children.Add(config.Panel);
            config.Changed += () => connectionRevision++;
            fourK.Visibility = J.Text(config.Data, "service_kind") is "radarr" or "sonarr" ? Visibility.Visible : Visibility.Collapsed;
        }
        type.SelectionChanged += (_, _) => { connectionRevision++; config = new RequestForm(new JsonObject()); options = new(); RebuildConfig(); };
        address.TextChanged += (_, _) => connectionRevision++; key.PasswordChanged += (_, _) => connectionRevision++;
        RebuildConfig();
        JsonObject Body(bool probe = false)
        {
            var entry = Entry(); var body = form.Read();
            config.PreserveSavedSecrets = current != null && entry.Installation == J.Text(data, "installation_id") && J.Text(entry.Capability, "id") == J.Text(data, "capability_id") &&
                address.Text.Trim() == J.Text(current.Value, "base_url").Trim() && J.Text(config.Data, "service_kind") == J.Text(configData, "service_kind");
            var cfg = probe ? config.Snapshot() : config.Read();
            if (fourK.Visibility == Visibility.Visible) { cfg["is_4k"] = fourK.IsOn; cfg.Remove("is_default"); cfg.Remove("is_default_4k"); }
            body["installation_id"] = entry.Installation; body["capability_id"] = J.Text(entry.Capability, "id"); body["plugin_config"] = cfg;
            var serviceKind = J.Text(cfg, "service_kind");
            if (used && serviceKind != J.Text(configData, "service_kind")) throw new ArgumentException("Routing uses this server. Change its routes before changing the service type.");
            body["supported_media_types"] = serviceKind == "radarr" ? new JsonArray("movie") : serviceKind == "sonarr" ? new JsonArray("series") : data["supported_media_types"]?.DeepClone() ?? new JsonArray();
            if (key.Password.Trim().Length > 0) body["api_key_ref"] = key.Password.Trim(); else body.Remove("api_key_ref");
            if (!J.Flag(entry.Capability["request_router"], "base_url_optional") && address.Text.Trim().Length == 0) throw new ArgumentException("URL is required for this server type.");
            if (!J.Flag(entry.Capability["request_router"], "api_key_optional") && !J.Flag(data, "has_api_key") && key.Password.Trim().Length == 0) throw new ArgumentException("An API key is required for this server type.");
            return body;
        }
        var testResult = U.Text("", 13, true);
        form.Panel.Children.Add(Button(S("web.admin.settings.requests.serverEditor.test"), "AdminRequestServerTest", async () =>
        {
            if (checking) return; checking = true; var revision = connectionRevision; testResult.Text = S("web.admin.settings.requests.serverEditor.testing");
            try
            {
                var result = await _api.LoadOptionsAsync(owner!, id ?? "new", Body(true), _dialogReads?.Token ?? owner!.Token);
                if (!Current(owner) || revision != connectionRevision || _dialog == null) return;
                options = result["options"] as JsonObject ?? new();
                var service = J.Items(options, "service_kind").SingleOrDefault();
                if (service != null)
                {
                    var detected = J.Text(service, "value"); var stored = J.Text(config.Data, "service_kind");
                    if (used && stored.Length > 0 && stored != detected) { testResult.Text = "This address answers as a different server type. Change the routing that uses this server first."; return; }
                    config.Data["service_kind"] = detected;
                }
                RefreshPluginOptions(config, Entry().Capability, options, false);
                fourK.Visibility = J.Text(config.Data, "service_kind") is "radarr" or "sonarr" ? Visibility.Visible : Visibility.Collapsed;
                testResult.Text = S("web.admin.settings.requests.serverEditor.testConnected") + " · " + J.Items(options, "quality_profile_id").Count + " quality profiles · " + J.Items(options, "root_folder").Count + " root folders";
            }
            catch (Exception error) { if (Current(owner) && revision == connectionRevision) testResult.Text = AdminRequestRules.Error(error); }
            finally { checking = false; }
        })); form.Panel.Children.Add(testResult);
        var delete = false;
        if (current != null)
        {
            var blockers = DeleteBlockers(id!); var remove = Button(S("web.admin.settings.requests.shared.delete"), "AdminRequestServerDelete", () => { delete = true; _dialog?.Hide(); return Task.CompletedTask; });
            remove.IsEnabled = blockers.Length == 0; form.Panel.Children.Add(remove);
            if (blockers.Length > 0) form.Panel.Children.Add(U.Text(S("web.admin.settings.requests.serverEditor.deleteBlocked", new { routes = blockers }), 13, true));
        }
        await EditAsync(id == null ? S("web.admin.settings.requests.serverEditor.addTitle") : S("web.admin.settings.requests.serverEditor.editTitle", new { server = J.Text(data, "name") }), form.Panel, async lease =>
        {
            if (checking) throw new ArgumentException("Wait for the connection test to finish.");
            if (current == null) await _api.CreateIntegrationAsync(lease, Body()); else await _api.SaveDocumentAsync(lease, current, Body());
        }, reload: id == null ? RefreshConfigurationAsync : () => EditServerAsync(id));
        if (delete && current != null && Current(owner) && await ConfirmAsync(S("web.admin.settings.requests.serverEditor.deleteTitle", new { server = J.Text(data, "name") }), S("web.admin.settings.requests.serverEditor.deleteDescription"), S("web.admin.settings.requests.shared.delete")))
            await WriteAsync(lease => _api.DeleteDocumentAsync(lease, current));
        if (Current(owner)) await RefreshConfigurationAsync();
    }

    /// <summary>Renders every declared plugin control (or JSON-schema property) without a JSON editor or readable secrets.</summary>
    private RequestForm PluginForm(JsonObject capability, JsonObject input, JsonObject options, bool overrides, JsonObject? serverConfig = null, bool lockKind = false)
    {
        var config = new RequestForm((JsonObject)input.DeepClone());
        var schema = J.Items(capability, "config_schema").FirstOrDefault();
        var descriptor = schema?["admin_form"] as JsonObject;
        JsonObject? jsonSchema = null;
        try { jsonSchema = JsonNode.Parse(J.Text(schema, "json_schema")) as JsonObject; } catch (System.Text.Json.JsonException) { }
        var fields = J.Items(descriptor, "fields").OfType<JsonObject>().ToArray();
        if (fields.Length == 0 && jsonSchema?["properties"] is JsonObject properties)
            fields = properties.Select(pair => new JsonObject { ["key"] = pair.Key, ["label"] = J.Text(pair.Value, "title") is { Length: > 0 } title ? title : pair.Key.Replace('_', ' '), ["control"] = J.Text(pair.Value, "type") switch { "boolean" => "SWITCH", "integer" or "number" => "NUMBER", _ => "TEXT" } }).ToArray();
        var conditional = new List<(FrameworkElement[] Elements, Func<bool> Visible)>();
        foreach (var field in fields)
        {
            var key = J.Text(field, "key"); if (RoutingOwned.Contains(key) || overrides && key == "service_kind") continue;
            if (!overrides && !config.Data.ContainsKey(key) && field["default_value"] != null) config.Data[key] = field["default_value"]!.DeepClone();
            var label = J.Text(field, "label"); if (label.Length == 0) label = key;
            var before = config.Readers.Count;
            var draftStart = config.DraftReaders.Count;
            var hostStart = config.Panel.Children.Count;
            var control = J.Text(field, "control"); var secret = J.Flag(field, "secret") || control == "PASSWORD";
            if (overrides && secret) continue;
            var propertySchema = jsonSchema?["properties"]?[key];
            var numeric = NumericSchema(propertySchema);
            if (secret)
            {
                var savedSecret = config.Data[key]?.DeepClone();
                config.Data.Remove(key);
                var password = new PasswordBox { PlaceholderText = "Leave blank to keep the saved value", PasswordRevealMode = PasswordRevealMode.Hidden }; _secrets.Add(password);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(password, "AdminRequestPlugin-" + key);
                password.PasswordChanged += (_, _) => config.NotifyChange();
                config.Panel.Children.Add(Field(label, password, J.Text(field, "description")));
                config.DraftReaders.Add(() =>
                {
                    if (password.Password.Length > 0) config.Data[key] = password.Password;
                    else if (config.PreserveSavedSecrets && savedSecret != null) config.Data[key] = savedSecret.DeepClone();
                    else config.Data.Remove(key);
                });
            }
            else if (control == "SWITCH" && !overrides) config.Flag(key, label, "AdminRequestPlugin-" + key, J.Text(field, "description"));
            else if (control is "SELECT" or "MULTI_SELECT" || control == "SWITCH" && overrides)
            {
                var choices = J.Items(options, key).Count > 0 ? J.Items(options, key) : J.Items(field, "options");
                var pairs = choices.Select(option => (J.Text(option, "value"), J.Text(option, "label"))).ToList();
                if (control == "SWITCH") pairs = [("true", "On"), ("false", "Off")];
                if (control == "MULTI_SELECT") config.Values(key, label, "AdminRequestPlugin-" + key, pairs, numeric: NumericSchema(propertySchema?["items"]), maximum: 100);
                else
                {
                    var existing = J.Text(config.Data, key); if (existing.Length > 0 && !pairs.Any(p => p.Item1 == existing)) pairs.Add((existing, existing));
                    var select = config.Choice(key, label, "AdminRequestPlugin-" + key, new[] { ("", overrides ? "Server default" : "Choose…") }.Concat(pairs), J.Text(field, "description"));
                    config.DraftReaders.Add(() => { var value = Selected(select); if (value.Length > 0 && numeric && double.TryParse(value, CultureInfo.InvariantCulture, out var number)) config.Data[key] = number; else if (value.Length > 0 && control == "SWITCH") config.Data[key] = value == "true"; });
                }
            }
            else if (control == "NUMBER") config.Number(key, label, "AdminRequestPlugin-" + key,
                J.Flag(field["validation"], "has_min") ? J.FloatingNumber(field["validation"], "min", double.MinValue) : double.MinValue,
                J.Flag(field["validation"], "has_max") ? J.FloatingNumber(field["validation"], "max", double.MaxValue) : double.MaxValue,
                integer: J.Text(propertySchema, "type") == "integer" || J.Items(propertySchema, "type").Any(value => value?.ToString() == "integer") && !J.Items(propertySchema, "type").Any(value => value?.ToString() == "number"));
            else
            {
                var text = config.Text(key, label, "AdminRequestPlugin-" + key, J.Text(field, "description"), maximum: J.Number(field["validation"], "max_length", 4096));
                if (control == "TEXTAREA" || J.Flag(field, "multiline")) { text.AcceptsReturn = true; text.TextWrapping = TextWrapping.Wrap; text.MinHeight = 90; }
            }
            var readers = config.Readers.Skip(before).ToArray(); config.Readers.RemoveRange(before, config.Readers.Count - before);
            bool Visible()
            {
                var conditions = J.Items(field, "show_when");
                return conditions.All(condition => J.Items(condition, "equals").Any(value => value?.ToString() == J.Text(serverConfig ?? config.Data, J.Text(condition, "field"))));
            }
            var controls = config.Panel.Children.Skip(hostStart).OfType<FrameworkElement>().ToArray();
            if (lockKind && key == "service_kind")
                for (var index = 0; index < controls.Length; index++)
                {
                    var element = controls[index]; var at = config.Panel.Children.IndexOf(element); config.Panel.Children.RemoveAt(at);
                    var disabled = new ContentControl { Content = element, IsEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    config.Panel.Children.Insert(at, disabled); controls[index] = disabled;
                }
            foreach (var element in controls) element.Visibility = Visible() ? Visibility.Visible : Visibility.Collapsed;
            conditional.Add((controls, Visible));
            var draftReaders = config.DraftReaders.Skip(draftStart).ToArray(); config.DraftReaders.RemoveRange(draftStart, config.DraftReaders.Count - draftStart);
            config.DraftReaders.Add(() => { if (secret || Visible()) foreach (var read in draftReaders) read(); });
            config.Readers.Add(() =>
            {
                if (!Visible()) return;
                foreach (var read in readers) read();
                if (!overrides && J.Flag(field, "required") && !secret && (!config.Data.ContainsKey(key) || config.Data[key]?.ToString().Length == 0)) throw new ArgumentException(label + " is required.");
                if (config.Data[key] is JsonValue && !secret)
                {
                    var value = config.Data[key]!.ToString(); var validation = field["validation"];
                    var minimum = J.Number(validation, "min_length"); var maximum = J.Number(validation, "max_length", int.MaxValue);
                    if (value.Length < minimum || maximum > 0 && value.Length > maximum) throw new ArgumentException(label + " has an invalid length.");
                    var pattern = J.Text(validation, "pattern");
                    if (pattern.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(value, pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw new ArgumentException(label + " does not match the required format.");
                }
            });
        }
        config.Changed += () => { foreach (var (elements, visible) in conditional) foreach (var element in elements) element.Visibility = visible() ? Visibility.Visible : Visibility.Collapsed; };
        if (fields.Length == 0) config.Panel.Children.Add(U.Text(S("web.admin.settings.requests.serverEditor.noPluginSettings"), 13, true));
        return config;
    }
    private static bool NumericSchema(JsonNode? schema) => J.Text(schema, "type") is "integer" or "number" || J.Items(schema, "type").Any(value => value?.ToString() is "integer" or "number");
    private static void RefreshPluginOptions(RequestForm config, JsonObject capability, JsonObject options, bool overrides)
    {
        var descriptor = J.Items(capability, "config_schema").FirstOrDefault()?["admin_form"];
        foreach (var field in J.Items(descriptor, "fields"))
        {
            var key = J.Text(field, "key"); var choices = J.Items(options, key);
            if (choices.Count == 0 || J.Text(field, "control") is not ("SELECT" or "MULTI_SELECT")) continue;
            if (!overrides && J.Text(field, "control") == "SELECT" && J.Flag(field, "dynamic_options") && J.Text(config.Data, key).Length == 0)
                config.Data[key] = choices[0]?["value"]?.DeepClone();
            config.UpdateChoices(key, choices.Select(choice => (J.Text(choice, "value"), J.Text(choice, "label"))));
        }
    }
}
