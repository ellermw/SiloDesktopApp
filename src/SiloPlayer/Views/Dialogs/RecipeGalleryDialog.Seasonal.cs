using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Views.Dialogs;

public static partial class RecipeGalleryDialog
{
    private sealed class SeasonalEditorState(HomeApi api, ApiRequestContext context, Dictionary<string, object> config)
    {
        public bool Ready { get; set; }
        public int Maximum { get; set; } = 12;
        public string? Problem() => !api.IsCurrentContext(context)
            ? "The selected profile changed. Close this row and try again."
            : !Ready ? "Load the holidays before saving this row." : SeasonalRowConfiguration.Validate(config, Maximum);
    }

    private static string? SeasonalEditorProblem(DependencyObject root)
    {
        if (root is FrameworkElement { Tag: SeasonalEditorState state } && state.Problem() is { } error) return error;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (SeasonalEditorProblem(VisualTreeHelper.GetChild(root, i)) is { } childError) return childError;
        return null;
    }

    private static async Task<bool> AddSeasonalFieldsAsync(Dictionary<string, object> config, StackPanel host)
    {
        var api = App.Services.GetRequiredService<HomeApi>();
        var context = api.CaptureContext();
        var state = new SeasonalEditorState(api, context, config);
        var panel = new StackPanel { Spacing = 16, Tag = state };
        host.Children.Add(panel);
        async Task LoadAsync()
        {
            state.Ready = false;
            panel.Children.Clear();
            panel.Children.Add(Hint("Loading holidays…"));
            try
            {
                var catalog = await api.GetSeasonalThemesAsync();
                if (!api.IsCurrentContext(context) || !host.Children.Contains(panel)) return;
                if (catalog.Themes.Count == 0 || catalog.MaxCustomHolidays < 0 || catalog.Themes.Any(theme => theme.Vibes.Count == 0))
                    throw new InvalidDataException("The server returned no holiday choices.");
                panel.Children.Clear();
                state.Maximum = catalog.MaxCustomHolidays;
                BuildSeasonalCatalogFields(config, panel, catalog);
                state.Ready = true;
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                if (!api.IsCurrentContext(context) || !host.Children.Contains(panel)) return;
                panel.Children.Clear();
                panel.Children.Add(Hint("This server uses the earlier seasonal holiday choices. Existing custom settings are preserved."));
                AddLegacySeasonalFields(config, panel);
                state.Ready = true;
            }
            catch
            {
                if (!host.Children.Contains(panel)) return;
                panel.Children.Clear();
                panel.Children.Add(Hint("Couldn't load the holidays. Try again."));
                var retry = new Button { Content = "Try again", Style = ResourceStyle("GhostButtonStyle") };
                retry.Click += async (_, _) => await LoadAsync();
                panel.Children.Add(retry);
            }
        }
        await LoadAsync();
        return api.IsCurrentContext(context) && host.Children.Contains(panel);
    }

    private static void BuildSeasonalCatalogFields(Dictionary<string, object> config, StackPanel host, SeasonalThemeCatalog catalog)
    {
        var enabled = SeasonalRowConfiguration.Read(config, "enabled_themes", Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        if (!config.ContainsKey("enabled_themes") && StringValue(config, "theme") is { Length: > 0 } legacy) enabled.Add(legacy);
        var family = HomeRowVariants.Selected("seasonal_themed", config) == "se_family_movie_night";
        var titles = SeasonalRowConfiguration.Read(config, "theme_titles", new Dictionary<string, string>());
        var vibes = SeasonalRowConfiguration.Read(config, "vibes", new Dictionary<string, string>());
        var dates = SeasonalRowConfiguration.Read(config, "theme_dates", new Dictionary<string, SeasonalDates>());
        void Commit()
        {
            config["enabled_themes"] = enabled.ToArray();
            SeasonalRowConfiguration.Set(config, "theme_titles", titles.Where(pair => enabled.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value.Trim()));
            SeasonalRowConfiguration.Set(config, "vibes", vibes.Where(pair => enabled.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
            SeasonalRowConfiguration.Set(config, "theme_dates", dates.Where(pair => enabled.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
            config.Remove("theme"); config.Remove("mode");
        }
        if (!family) host.Children.Add(Label("Holidays to celebrate"));
        foreach (var theme in catalog.Themes.Where(theme => family ? theme.Key == "family_movie_night" : theme.Key != "family_movie_night"))
        {
            var fields = new StackPanel { Spacing = 10, Margin = new Thickness(family ? 0 : 28, 0, 0, 0) };
            var chosen = theme.Vibes.FirstOrDefault(vibe => vibes.GetValueOrDefault(theme.Key) == vibe.Key) ?? theme.Vibes[0];
            var hint = Hint(chosen.Description);
            TextBox? title = null;
            if (theme.Vibes.Count > 1)
            {
                var combo = CreateCombo("Vibe", theme.Vibes.Select(vibe => new Option(vibe.Key, vibe.Name)).ToArray(), chosen.Key);
                AutomationProperties.SetName(combo, $"Vibe for {theme.Name}");
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not Option option) return;
                    vibes[theme.Key] = option.Value;
                    var vibe = theme.Vibes.First(vibe => vibe.Key == option.Value);
                    hint.Text = vibe.Description;
                    if (title != null) title.PlaceholderText = $"Defaults to “{vibe.Title}”";
                    Commit();
                };
                fields.Children.Add(combo); fields.Children.Add(hint);
            }
            if (!family)
            {
                title = new TextBox { Header = "Name in season", PlaceholderText = $"Defaults to “{chosen.Title}”", Text = titles.GetValueOrDefault(theme.Key) ?? "" };
                title.TextChanged += (_, _) => { titles[theme.Key] = title.Text; Commit(); };
                fields.Children.Add(title);
                if (theme.DatesEditable)
                {
                    var own = dates.GetValueOrDefault(theme.Key);
                    var start = new TextBox { Header = "Start (MM-DD)", PlaceholderText = theme.Start ?? "MM-DD", Text = own?.Start ?? "", MaxLength = 5 };
                    var end = new TextBox { Header = "End (MM-DD)", PlaceholderText = theme.End ?? "MM-DD", Text = own?.End ?? "", MaxLength = 5 };
                    void DatesChanged()
                    {
                        if (start.Text == "" && end.Text == "") dates.Remove(theme.Key);
                        else dates[theme.Key] = new() { Start = start.Text.Trim(), End = end.Text.Trim() };
                        Commit();
                    }
                    start.TextChanged += (_, _) => DatesChanged(); end.TextChanged += (_, _) => DatesChanged();
                    fields.Children.Add(new Expander { Header = "Use my own dates", HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = new StackPanel { Spacing = 8, Children = { start, end, Hint("Leave both blank for the usual dates. An end before the start wraps across New Year.") } } });
                }
                fields.Visibility = enabled.Contains(theme.Key) ? Visibility.Visible : Visibility.Collapsed;
                var check = new CheckBox { Content = $"{theme.Name} · {theme.When}", IsChecked = enabled.Contains(theme.Key) };
                check.Checked += (_, _) => { enabled.Add(theme.Key); fields.Visibility = Visibility.Visible; Commit(); };
                check.Unchecked += (_, _) => { enabled.Remove(theme.Key); fields.Visibility = Visibility.Collapsed; Commit(); };
                host.Children.Add(check);
            }
            host.Children.Add(fields);
        }
        if (!family)
        {
            host.Children.Add(Hint("The row shows whichever holiday is in season and hides itself when none is."));
            AddCustomHolidayFields(config, host, catalog.MaxCustomHolidays);
        }
        AddCombo(host, "Picks from", [new("recommendations", "Silo recommendations"), new("lists", "The lists only")],
            StringValue(config, "source") == "lists" ? "lists" : "recommendations", value =>
            { if (value == "lists") config["source"] = value; else config.Remove("source"); });
        host.Children.Add(Hint("Silo recommendations starts with the list's titles you have, most popular first, then similar titles from your library. The lists only skips similar titles."));
    }

    private static void AddCustomHolidayFields(Dictionary<string, object> config, StackPanel host, int maximum)
    {
        var holidays = SeasonalRowConfiguration.Read(config, "custom_holidays", new List<SeasonalCustomHoliday>());
        var rows = new StackPanel { Spacing = 12 };
        var add = new Button { Content = "Add my own holiday", Style = ResourceStyle("GhostButtonStyle") };
        void Commit()
        {
            if (holidays.Count == 0) config.Remove("custom_holidays");
            else SeasonalRowConfiguration.Set(config, "custom_holidays", holidays);
            add.IsEnabled = holidays.Count < maximum;
        }
        void Render()
        {
            rows.Children.Clear();
            for (var index = 0; index < holidays.Count; index++)
            {
                var holiday = holidays[index];
                var fields = new StackPanel { Spacing = 8 };
                TextBox Field(string header, string value, string placeholder, Action<string> change)
                {
                    var box = new TextBox { Header = header, Text = value, PlaceholderText = placeholder };
                    box.TextChanged += (_, _) => { change(box.Text.Trim()); Commit(); };
                    fields.Children.Add(box); return box;
                }
                Field("Holiday name", holiday.Name, "My holiday", value => holiday.Name = value);
                Field("Start (MM-DD)", holiday.Start, "07-04", value => holiday.Start = value).MaxLength = 5;
                Field("End (MM-DD)", holiday.End, "07-04", value => holiday.End = value).MaxLength = 5;
                Field("Public MDBList list", holiday.ListUrl, "https://mdblist.com/lists/…", value => holiday.ListUrl = value);
                Field("Name in season (optional)", holiday.Title ?? "", "Defaults to the holiday name", value => holiday.Title = value.Length == 0 ? null : value);
                var remove = new Button { Content = "Remove holiday", Style = ResourceStyle("GhostButtonStyle") };
                remove.Click += (_, _) => { holidays.Remove(holiday); Commit(); Render(); };
                fields.Children.Add(remove);
                rows.Children.Add(new Expander { Header = $"Custom holiday {index + 1}", IsExpanded = true, Content = fields,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
            add.IsEnabled = holidays.Count < maximum;
        }
        add.Click += (_, _) => { if (holidays.Count >= maximum) return; holidays.Add(new()); Commit(); Render(); };
        Render(); host.Children.Add(rows); host.Children.Add(add);
    }
}
