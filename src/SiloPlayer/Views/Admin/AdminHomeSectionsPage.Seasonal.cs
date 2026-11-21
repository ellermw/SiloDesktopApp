using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>The seasonal row's holidays, own dates, custom MDBList holidays and picks source (official SeasonalFields).</summary>
public sealed partial class AdminHomeSectionsPage
{
    private AdminHomeSeasonalCatalog? _seasonalCatalog;
    private const string FamilyNight = "family_movie_night";

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
                var retry = Action(T("web.home.rows.reloadRows"), "HomeSectionSeasonalRetry"); retry.Click += async (_, _) => await Load(); host.Children.Add(retry);
            }
        }
    }

    private static Dictionary<string, string> StringMap(JsonNode? node)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is JsonObject obj) foreach (var pair in obj) if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text)) map[pair.Key] = text;
        return map;
    }
    private static JsonObject? MapNode(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var list = entries.ToList(); if (list.Count == 0) return null;
        var node = new JsonObject(); foreach (var (key, value) in list) node[key] = value; return node;
    }
    private static AdminHomeSeasonalVibe ChosenVibe(AdminHomeSeasonalTheme theme, IReadOnlyDictionary<string, string> vibes) =>
        theme.Vibes.FirstOrDefault(vibe => vibes.TryGetValue(theme.Key, out var chosen) && vibe.Key == chosen) ?? theme.Vibes[0];

    private void RenderSeasonal(StackPanel host, AdminHomeSeasonalCatalog catalog)
    {
        host.Children.Clear(); var draft = _draft!; var config = draft.Config;
        var variant = AdminHomeSectionsRules.VariantOf("seasonal_themed", config);
        // Holidays, a legacy single-holiday row and Family movie night have fields; a row matching no variant has none.
        if (variant == "se_family_movie_night") { FamilyNightFields(host, catalog); return; }
        if (variant != "se_auto" && !(variant == null && config["theme"] is JsonValue { } legacy && legacy.TryGetValue<string>(out _))) return;
        HolidayFields(host, catalog);
    }

    private void SourceField(StackPanel host)
    {
        var source = Value(_draft!.Config, "source") == "lists" ? "lists" : "recommendations";
        var hint = U.Text(T(source == "lists" ? "web.home.addRow.seasonal.sourceListsHint" : "web.home.addRow.seasonal.sourceRecommendationsHint"), 12, true);
        var box = new StackPanel { Spacing = 6 };
        box.Children.Add(Select("HomeSection_source", [("recommendations", T("web.home.addRow.seasonal.sourceRecommendations")), ("lists", T("web.home.addRow.seasonal.sourceLists"))], source,
            value => { SetConfig("source", value == "recommendations" ? null : JsonValue.Create(value)); hint.Text = T(value == "lists" ? "web.home.addRow.seasonal.sourceListsHint" : "web.home.addRow.seasonal.sourceRecommendationsHint"); }));
        box.Children.Add(hint); Field(host, T("web.home.addRow.seasonal.picksFrom"), box);
    }

    private void FamilyNightFields(StackPanel host, AdminHomeSeasonalCatalog catalog)
    {
        var theme = catalog.Themes.FirstOrDefault(entry => entry.Key == FamilyNight);
        if (theme != null && theme.Vibes.Count > 1) VibeSelect(host, theme, ChosenVibe(theme, StringMap(_draft!.Config["vibes"])), key => SetConfig("vibes", new JsonObject { [FamilyNight] = key }), T("web.home.addRow.seasonal.vibe"));
        SourceField(host);
    }

    private void VibeSelect(StackPanel host, AdminHomeSeasonalTheme theme, AdminHomeSeasonalVibe current, Action<string> changed, string label)
    {
        var description = U.Text(current.Description, 12, true);
        var select = Select("HomeSectionVibe_" + theme.Key, theme.Vibes.Select(vibe => (vibe.Key, vibe.Name)), current.Key, key =>
        { changed(key); description.Text = theme.Vibes.FirstOrDefault(vibe => vibe.Key == key)?.Description ?? ""; });
        AutomationProperties.SetName(select, T("web.home.addRow.seasonal.vibeFor", new { holiday = theme.Name }));
        var box = new StackPanel { Spacing = 4 }; box.Children.Add(select); box.Children.Add(description); Field(host, label, box);
    }

    private void HolidayFields(StackPanel host, AdminHomeSeasonalCatalog catalog)
    {
        var draft = _draft!; var config = draft.Config;
        var themes = catalog.Themes.Where(theme => theme.Key != FamilyNight).ToList();
        var enabled = (config["enabled_themes"] as JsonArray)?.Select(node => node?.ToString() ?? "").Where(key => key != "").ToList()
            ?? (Value(config, "theme") is { Length: > 0 } legacyKey ? [legacyKey] : []);
        var titles = StringMap(config["theme_titles"]); var vibes = StringMap(config["vibes"]);
        var dates = new Dictionary<string, (string Start, string End)>(StringComparer.Ordinal);
        if (config["theme_dates"] is JsonObject windows)
            foreach (var pair in windows) if (pair.Value is JsonObject window) dates[pair.Key] = (Value(window, "start"), Value(window, "end"));
        var custom = config["custom_holidays"] as JsonArray;
        // Keeps only titles, vibes and dates for holidays that are on, so the saved config stays tidy.
        void Commit(IEnumerable<string> nextEnabled, Dictionary<string, string> nextTitles, Dictionary<string, string> nextVibes, Dictionary<string, (string Start, string End)> nextDates)
        {
            var on = nextEnabled.ToHashSet(StringComparer.Ordinal);
            config["enabled_themes"] = new JsonArray(on.Select(key => (JsonNode)JsonValue.Create(key)!).ToArray());
            void Set(string key, JsonObject? node) { if (node == null) config.Remove(key); else config[key] = node; }
            Set("theme_titles", MapNode(nextTitles.Where(pair => on.Contains(pair.Key) && pair.Value.Trim() != "").Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value.Trim()))));
            Set("vibes", MapNode(nextVibes.Where(pair => on.Contains(pair.Key))));
            var kept = nextDates.Where(pair => on.Contains(pair.Key)).ToList();
            Set("theme_dates", kept.Count == 0 ? null : new JsonObject(kept.Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, new JsonObject { ["start"] = pair.Value.Start, ["end"] = pair.Value.End }))));
            // Legacy single-theme keys would shadow the list on the server.
            config["theme"] = ""; config["mode"] = ""; Dirty();
        }
        host.Children.Add(U.Text(T("web.home.addRow.seasonal.holidays"), 15));
        foreach (var theme in themes)
        {
            var on = enabled.Contains(theme.Key); var row = new StackPanel { Spacing = 8 };
            var own = dates.TryGetValue(theme.Key, out var window) ? window : default((string Start, string End)?);
            var when = own is { } w && AdminHomeSectionsRules.HolidayDatesProblem(w.Start, w.End) == null ? w.Start + " – " + w.End : theme.When;
            var box = new CheckBox { Content = theme.Name + " · " + when, IsChecked = on, MinWidth = 0 };
            AutomationProperties.SetAutomationId(box, "HomeSectionHoliday_" + theme.Key);
            // Attached after the initial value is set, so only the admin's change commits.
            void SetHoliday(bool value)
            {
                var next = enabled.Where(key => key != theme.Key).ToList(); if (value) next.Add(theme.Key);
                Commit(next, titles, vibes, dates); RenderSeasonal(host, catalog);
            }
            box.Checked += (_, _) => SetHoliday(true); box.Unchecked += (_, _) => SetHoliday(false);
            row.Children.Add(box);
            if (on)
            {
                var vibe = ChosenVibe(theme, vibes);
                if (theme.Vibes.Count > 1) VibeSelect(row, theme, vibe, key => { vibes[theme.Key] = key; Commit(enabled, titles, vibes, dates); }, T("web.home.addRow.seasonal.vibe"));
                var name = Input(T("web.home.addRow.seasonal.rowNameDuring", new { holiday = theme.Name }), "HomeSectionThemeName_" + theme.Key, titles.GetValueOrDefault(theme.Key, ""),
                    text => { titles[theme.Key] = text; Commit(enabled, titles, vibes, dates); });
                name.PlaceholderText = T("web.home.addRow.seasonal.nameInSeason", new { title = vibe.Title });
                Field(row, T("web.home.addRow.seasonal.rowNameDuring", new { holiday = theme.Name }), name);
                if (theme.DatesEditable)
                {
                    var current = own ?? ("", ""); var problem = U.Text("", 12); var hint = U.Text("", 12, true);
                    void Show() { var text = own == null && current is ("", "") ? null : AdminHomeSectionsRules.HolidayDatesProblem(current.Start, current.End); problem.Text = text ?? ""; hint.Text = text != null ? "" : current is ("", "") ? T("web.home.addRow.seasonal.usualDates", new { when = theme.When }) : T("web.home.addRow.seasonal.everyYear"); }
                    void Update()
                    {
                        if (current is ("", "")) dates.Remove(theme.Key); else dates[theme.Key] = current;
                        Commit(enabled, titles, vibes, dates); Show();
                    }
                    var start = Input(T("web.home.addRow.seasonal.starts", new { holiday = theme.Name }), "HomeSectionThemeStart_" + theme.Key, current.Start, text => { current = (text, current.End); Update(); }, 5);
                    var end = Input(T("web.home.addRow.seasonal.ends", new { holiday = theme.Name }), "HomeSectionThemeEnd_" + theme.Key, current.End, text => { current = (current.Start, text); Update(); }, 5);
                    start.PlaceholderText = end.PlaceholderText = T("web.home.addRow.seasonal.datePlaceholder"); start.Width = end.Width = 110;
                    var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; line.Children.Add(start); line.Children.Add(end);
                    var datesBox = new StackPanel { Spacing = 4 }; datesBox.Children.Add(line); datesBox.Children.Add(problem); datesBox.Children.Add(hint); Show();
                    Field(row, "In season", datesBox);
                }
            }
            host.Children.Add(row);
        }
        host.Children.Add(themes.Any(theme => enabled.Contains(theme.Key)) || custom is { Count: > 0 } ? U.Text(T("web.home.addRow.seasonal.inSeasonNote"), 13, true)
            : U.Text(T("web.home.addRow.seasonal.pickOne"), 13));
        CustomHolidays(host, catalog);
        SourceField(host);
    }

    private void CustomHolidays(StackPanel host, AdminHomeSeasonalCatalog catalog)
    {
        var config = _draft!.Config; var holidays = config["custom_holidays"] as JsonArray ?? new JsonArray();
        host.Children.Add(U.Text(T("web.home.addRow.seasonal.yourHolidays"), 15)); host.Children.Add(U.Text(T("web.home.addRow.seasonal.yourHolidaysIntro"), 13, true));
        void Commit() { if (holidays.Count == 0) config.Remove("custom_holidays"); else config["custom_holidays"] = holidays; Dirty(); }
        for (var index = 0; index < holidays.Count; index++)
        {
            var at = index; if (holidays[index] is not JsonObject holiday) continue;
            var label = Value(holiday, "name").Trim() is { Length: > 0 } named ? named : T("web.home.addRow.seasonal.holidayNumber", new { number = (long)at + 1 });
            var problem = U.Text(AdminHomeSectionsRules.CustomHolidayProblem(holiday) ?? "", 12);
            void Changed(string key, string value) { holiday[key] = value; problem.Text = AdminHomeSectionsRules.CustomHolidayProblem(holiday) ?? ""; Commit(); }
            var row = new StackPanel { Spacing = 8 };
            var name = Input(T("web.home.addRow.seasonal.holidayName", new { number = (long)at + 1 }), "HomeSectionHoliday_" + at + "_name", Value(holiday, "name"), value => Changed("name", value), 200);
            name.PlaceholderText = T("web.home.addRow.seasonal.holidayNamePlaceholder"); row.Children.Add(name);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var start = Input(T("web.home.addRow.seasonal.starts", new { holiday = label }), "HomeSectionHoliday_" + at + "_start", Value(holiday, "start"), value => Changed("start", value), 5);
            var end = Input(T("web.home.addRow.seasonal.ends", new { holiday = label }), "HomeSectionHoliday_" + at + "_end", Value(holiday, "end"), value => Changed("end", value), 5);
            start.PlaceholderText = end.PlaceholderText = T("web.home.addRow.seasonal.datePlaceholder"); start.Width = end.Width = 110; line.Children.Add(start); line.Children.Add(end); row.Children.Add(line);
            var list = Input(T("web.home.addRow.seasonal.listLabel", new { holiday = label }), "HomeSectionHoliday_" + at + "_list_url", Value(holiday, "list_url"), value => Changed("list_url", value), 2000);
            list.PlaceholderText = "https://mdblist.com/lists/…"; list.FlowDirection = FlowDirection.LeftToRight; row.Children.Add(list); row.Children.Add(problem);
            var remove = Action(T("web.home.addRow.seasonal.removeHoliday", new { holiday = label }), "HomeSectionHolidayRemove_" + at);
            remove.Click += (_, _) => { holidays.RemoveAt(at); Commit(); RenderSeasonal(host, catalog); }; row.Children.Add(remove);
            host.Children.Add(new Border { Child = row, Padding = new(12), BorderThickness = new(1), BorderBrush = U.Brush("MutedBrush"), CornerRadius = new(12) });
        }
        var add = Action(T("web.home.addRow.seasonal.addHoliday"), "HomeSectionHolidayAdd"); add.IsEnabled = holidays.Count < catalog.MaxCustomHolidays;
        add.Click += (_, _) => { holidays.Add(new JsonObject { ["name"] = "", ["start"] = "", ["end"] = "", ["list_url"] = "" }); Commit(); RenderSeasonal(host, catalog); };
        host.Children.Add(add);
    }
}
