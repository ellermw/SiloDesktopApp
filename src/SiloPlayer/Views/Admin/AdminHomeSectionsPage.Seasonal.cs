using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminHomeSectionsPage
{
    private AdminHomeSeasonalCatalog? _seasonalCatalog;
    private void SeasonalEditor(StackPanel panel)
    {
        var host = new StackPanel { Spacing = 12 }; panel.Children.Add(host);
        var revision = _dialogRevision; var pickerRevision = _pickerRevision; var token = _editorReads!.Token;
        if (_seasonalCatalog != null) RenderSeasonal(host, _seasonalCatalog); else _ = Load();
        async Task Load()
        {
            host.Children.Clear(); host.Children.Add(U.Text(T("web.home.addRow.seasonal.loading"), 13, true));
            try
            {
                var catalog = await _api.SeasonalAsync(_editorOwner!, token);
                if (catalog.Themes.Count > 100 || catalog.Themes.Any(theme => theme.Vibes.Count > 50) || catalog.MaxCustomHolidays is < 0 or > 100) throw new InvalidDataException();
                if (!EditorCurrent(revision) || pickerRevision != _pickerRevision) return;
                _seasonalCatalog = catalog; RenderSeasonal(host, catalog);
            }
            catch (Exception)
            {
                if (!EditorCurrent(revision) || pickerRevision != _pickerRevision) return;
                host.Children.Clear(); host.Children.Add(U.Text(T("web.home.addRow.seasonal.failed"), 13, true));
                var retry = Action("Retry", "HomeSectionSeasonalRetry"); retry.Click += async (_, _) => await Load(); host.Children.Add(retry);
            }
        }
    }
    private void RenderSeasonal(StackPanel host, AdminHomeSeasonalCatalog catalog)
    {
        host.Children.Clear(); var draft = _draft!; var config = draft.Config;
        var family = Value(config, "theme") == "family_movie_night" || config["enabled_themes"] is JsonArray { Count: 1 } enabledThemes && enabledThemes[0]?.ToString() == "family_movie_night";
        var chosen = (config["enabled_themes"] as JsonArray)?.Select(node => node!.ToString()).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        if (chosen.Count == 0 && Value(config, "theme") is { Length: > 0 } legacy) chosen.Add(legacy);
        void SetMap(string mapKey, string key, JsonNode? value)
        {
            var map = config[mapKey] as JsonObject;
            if (map == null) config[mapKey] = map = new();
            if (value == null) map.Remove(key); else map[key] = value;
            Dirty();
        }
        void CommitChosen()
        {
            var array = new JsonArray(); foreach (var key in chosen) array.Add(key);
            SetConfig("enabled_themes", array); config["theme"] = ""; config["mode"] = "";
            foreach (var mapKey in new[] { "theme_titles", "vibes", "theme_dates" })
                if (config[mapKey] is JsonObject map) foreach (var key in map.Select(entry => entry.Key).Where(key => !chosen.Contains(key)).ToArray()) map.Remove(key);
        }
        host.Children.Add(U.Text(T(family ? "web.home.addRow.seasonal.vibe" : "web.home.addRow.seasonal.holidays"), 15));
        foreach (var theme in catalog.Themes.Where(theme => family ? theme.Key == "family_movie_night" : theme.Key != "family_movie_night"))
        {
            var on = family || chosen.Contains(theme.Key); var row = new StackPanel { Spacing = 8 };
            if (!family)
            {
                var enabled = new CheckBox { Content = theme.Name + " · " + theme.When, IsChecked = on };
                enabled.Click += (_, _) => { if (enabled.IsChecked == true) chosen.Add(theme.Key); else chosen.Remove(theme.Key); CommitChosen(); RenderSeasonal(host, catalog); };
                row.Children.Add(enabled);
            }
            if (on)
            {
                var vibes = config["vibes"] as JsonObject;
                if (theme.Vibes.Count > 1) Field(row, T("web.home.addRow.seasonal.vibe"), Select("HomeSectionVibe_" + theme.Key,
                    theme.Vibes.Select(vibe => (vibe.Key, vibe.Name)), vibes == null ? theme.Vibes[0].Key : Value(vibes, theme.Key), value => SetMap("vibes", theme.Key, JsonValue.Create(value))));
                if (!family)
                {
                    var titles = config["theme_titles"] as JsonObject;
                    Field(row, "Row name during " + theme.Name, Input("Row name", "HomeSectionThemeName_" + theme.Key, titles == null ? "" : Value(titles, theme.Key), value => SetMap("theme_titles", theme.Key, string.IsNullOrWhiteSpace(value) ? null : JsonValue.Create(value.Trim()))));
                    if (theme.DatesEditable)
                    {
                        var window = (config["theme_dates"] as JsonObject)?[theme.Key] as JsonObject;
                        var start = window == null ? "" : Value(window, "start"); var end = window == null ? "" : Value(window, "end");
                        void Update() => SetMap("theme_dates", theme.Key, start == "" && end == "" ? null : new JsonObject { ["start"] = start, ["end"] = end });
                        Field(row, "Starts (MM-DD)", Input("Starts", "HomeSectionThemeStart_" + theme.Key, start, value => { start = value; Update(); }, 5));
                        Field(row, "Ends (MM-DD)", Input("Ends", "HomeSectionThemeEnd_" + theme.Key, end, value => { end = value; Update(); }, 5));
                        row.Children.Add(U.Text("Leave empty for the usual dates (" + theme.When + ").", 12, true));
                    }
                }
            }
            host.Children.Add(row);
        }
        if (!family)
        {
            host.Children.Add(U.Text(T("web.home.addRow.seasonal.inSeasonNote"), 13, true));
            var holidays = config["custom_holidays"] as JsonArray ?? new();
            host.Children.Add(U.Text(T("web.home.addRow.seasonal.yourHolidays"), 15));
            for (var index = 0; index < holidays.Count; index++)
            {
                var at = index; if (holidays[index] is not JsonObject holiday) continue;
                var row = new StackPanel { Spacing = 8 };
                foreach (var (key, label, max) in new[] { ("name", "Name", 200), ("start", "Starts (MM-DD)", 5), ("end", "Ends (MM-DD)", 5), ("list_url", "Public MDBList list", 2000) })
                    Field(row, label, Input(label, "HomeSectionHoliday_" + at + "_" + key, Value(holiday, key), value => { holiday[key] = value; Dirty(); }, max));
                var remove = Action("Remove holiday", "HomeSectionHolidayRemove_" + at); remove.Click += (_, _) => { holidays.RemoveAt(at); Dirty(); RenderSeasonal(host, catalog); }; row.Children.Add(remove); host.Children.Add(row);
            }
            var add = Action(T("web.home.addRow.seasonal.addHoliday"), "HomeSectionHolidayAdd"); add.IsEnabled = holidays.Count < catalog.MaxCustomHolidays;
            add.Click += (_, _) => { if (config["custom_holidays"] == null) config["custom_holidays"] = holidays; holidays.Add(new JsonObject { ["name"] = "", ["start"] = "", ["end"] = "", ["list_url"] = "" }); Dirty(); RenderSeasonal(host, catalog); }; host.Children.Add(add);
        }
        ConfigSelect(host, T("web.home.addRow.seasonal.picksFrom"), "source", [("", T("web.home.addRow.seasonal.sourceRecommendations")), ("lists", T("web.home.addRow.seasonal.sourceLists"))]);
    }
}
