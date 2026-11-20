using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using J = SiloPlayer.Core.Services.Admin.AdminRequestJson;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminRequestsPage
{
    private sealed class RequestForm(JsonObject data)
    {
        internal JsonObject Data { get; } = data;
        internal StackPanel Panel { get; } = Stack();
        internal List<Action> Readers { get; } = [];
        internal List<Action> DraftReaders { get; } = [];
        internal bool PreserveSavedSecrets { get; set; } = true;
        private readonly Dictionary<string, Action<IEnumerable<(string Value, string Label)>, bool>> _choiceUpdates = new(StringComparer.Ordinal);
        internal event Action? Changed;
        internal void NotifyChange() => Changed?.Invoke();
        internal JsonObject Snapshot() { foreach (var read in DraftReaders) read(); return (JsonObject)Data.DeepClone(); }
        internal JsonObject Read() { Snapshot(); foreach (var read in Readers) read(); return (JsonObject)Data.DeepClone(); }
        internal void UpdateChoices(string key, IEnumerable<(string Value, string Label)> choices, bool clear = false)
        { if (_choiceUpdates.TryGetValue(key, out var update)) update(choices, clear); }
        internal TextBox Text(string key, string label, string id, string? help = null, int maximum = 2048, bool required = false)
        {
            var input = Input(J.Text(Data, key), id, label); input.MaxLength = maximum;
            input.Tag = key;
            input.TextChanged += (_, _) => { if (input.Text.Trim().Length == 0 && !required) Data.Remove(key); else Data[key] = input.Text.Trim(); NotifyChange(); };
            Panel.Children.Add(Field(label, input, help));
            Readers.Add(() => { var text = input.Text.Trim(); if (required && text.Length == 0) throw new ArgumentException(label + " is required."); if (text.Length == 0 && !required) Data.Remove(key); else Data[key] = text; });
            return input;
        }
        internal ToggleSwitch Flag(string key, string label, string id, string? help = null)
        {
            var toggle = Toggle(label, id, J.Flag(Data, key)); Panel.Children.Add(toggle); if (help != null) Panel.Children.Add(U.Text(help, 12, true));
            toggle.Tag = key;
            toggle.Toggled += (_, _) => { Data[key] = toggle.IsOn; NotifyChange(); };
            Readers.Add(() => Data[key] = toggle.IsOn); return toggle;
        }
        internal ComboBox Choice(string key, string label, string id, IEnumerable<(string Value, string Label)> options, string? help = null)
        {
            var saved = J.Text(Data, key); var offered = options.ToList();
            // A saved value outside the offered list stays a choice, so opening and saving the editor never drops it unseen.
            if (saved.Length > 0 && !offered.Any(option => option.Value == saved)) offered.Add((saved, saved));
            var select = Select(id, offered, saved); Panel.Children.Add(Field(label, select, help));
            select.Tag = key;
            var placeholder = select.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, ""))?.Content?.ToString();
            _choiceUpdates[key] = (choices, clear) =>
            {
                var value = clear ? "" : J.Text(Data, key); var available = choices.ToList();
                if (placeholder != null && !available.Any(option => option.Value.Length == 0)) available.Insert(0, ("", placeholder));
                if (value.Length > 0 && !available.Any(option => option.Value == value)) available.Add((value, value));
                select.Items.Clear();
                foreach (var option in available) select.Items.Add(new ComboBoxItem { Tag = option.Value, Content = option.Label });
                select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, value));
            };
            select.SelectionChanged += (_, _) => { var value = Selected(select); if (value.Length == 0) Data.Remove(key); else Data[key] = value; NotifyChange(); };
            DraftReaders.Add(() => { var value = Selected(select); if (value.Length == 0) Data.Remove(key); else Data[key] = value; }); return select;
        }
        internal TextBox Number(string key, string label, string id, double minimum = 0, double maximum = int.MaxValue, bool optional = true, bool integer = true)
        {
            var input = Input(Data[key]?.ToString() ?? "", id, label); input.MaxLength = 16;
            input.Tag = key;
            input.TextChanged += (_, _) => NotifyChange();
            Panel.Children.Add(Field(label, input));
            void Synchronize(bool validate)
            {
                if ((!validate || optional) && input.Text.Trim().Length == 0) { Data.Remove(key); return; }
                if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || integer && (value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue) || validate && (value < minimum || value > maximum))
                    throw new ArgumentException(label + $": enter {(integer ? "a whole number" : "a number")} from {minimum} to {maximum}.");
                Data[key] = integer ? JsonValue.Create((int)value) : JsonValue.Create(value);
            }
            DraftReaders.Add(() => Synchronize(false)); Readers.Add(() => Synchronize(true)); return input;
        }
        internal FrameworkElement Values(string key, string label, string id, IEnumerable<(string Value, string Label)> choices, bool numeric = false, int maximum = 20)
        {
            var values = J.Items(Data, key).Select(value => value!.ToString()).ToList();
            var options = choices.ToArray(); var selected = Stack();
            var select = Select(id + "Pick", new[] { ("", "Choose…") }.Concat(options), "");
            select.Tag = key;
            var add = Input("", id + "Id", numeric ? "TMDB ID" : "Code"); add.MaxLength = 30;
            var addButton = U.Action("Add", id + "Add");
            var updatingChoices = false;
            void Store()
            {
                if (values.Count == 0) Data.Remove(key);
                else Data[key] = new JsonArray(values.Select(value => numeric ? (JsonNode?)JsonValue.Create(int.Parse(value, CultureInfo.InvariantCulture)) : JsonValue.Create(value)).ToArray());
            }
            void Render()
            {
                selected.Children.Clear();
                foreach (var value in values.ToArray())
                {
                    var remove = U.Action("×", id + "Remove-" + value); AutomationProperties.SetName(remove, "Remove " + value);
                    remove.Click += (_, _) => { values.Remove(value); Store(); Render(); NotifyChange(); };
                    selected.Children.Add(Row(U.Text(options.FirstOrDefault(option => option.Value == value).Label ?? value, 13), remove));
                }
                select.IsEnabled = add.IsEnabled = addButton.IsEnabled = values.Count < maximum;
            }
            void Add(string value)
            {
                value = value.Trim(); if (value.Length == 0 || values.Contains(value) || values.Count >= maximum) return;
                if (numeric && (!int.TryParse(value, out var number) || number < 1)) return;
                values.Add(value); add.Text = ""; Store(); Render(); NotifyChange();
            }
            select.SelectionChanged += (_, _) => { if (updatingChoices) return; var value = Selected(select); if (value.Length > 0) { Add(value); select.SelectedIndex = 0; } };
            addButton.Click += (_, _) => Add(add.Text);
            add.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; Add(add.Text); } };
            var host = Stack(U.Text(label, 13), selected, select, Row(add, addButton)); Panel.Children.Add(host); Render();
            _choiceUpdates[key] = (choices, clear) =>
            {
                options = choices.ToArray(); updatingChoices = true;
                try
                {
                    if (clear) { values.Clear(); add.Text = ""; Store(); }
                    select.Items.Clear(); select.Items.Add(new ComboBoxItem { Tag = "", Content = "Choose…" });
                    foreach (var option in options) select.Items.Add(new ComboBoxItem { Tag = option.Value, Content = option.Label });
                    select.SelectedIndex = 0; Render();
                }
                finally { updatingChoices = false; }
                if (clear) NotifyChange();
            };
            Readers.Add(() =>
            {
                if (add.Text.Trim().Length > 0) throw new ArgumentException("Press Add to include the typed " + label + " value.");
                Store();
            }); return host;
        }
        internal void Attach(RequestForm child) { Panel.Children.Add(child.Panel); Readers.Add(() => child.Read()); }
    }

    private static IReadOnlyList<(string Value, string Label)> Genres(string media) => (media == "series"
        ? new[] { (10759, "Action & Adventure"), (16, "Animation"), (35, "Comedy"), (80, "Crime"), (99, "Documentary"), (18, "Drama"), (10751, "Family"), (10762, "Kids"), (9648, "Mystery"), (10763, "News"), (10764, "Reality"), (10765, "Sci-Fi & Fantasy"), (10766, "Soap"), (10767, "Talk"), (10768, "War & Politics"), (37, "Western") }
        : new[] { (28, "Action"), (12, "Adventure"), (16, "Animation"), (35, "Comedy"), (80, "Crime"), (99, "Documentary"), (18, "Drama"), (10751, "Family"), (14, "Fantasy"), (36, "History"), (27, "Horror"), (10402, "Music"), (9648, "Mystery"), (10749, "Romance"), (878, "Science Fiction"), (10770, "TV Movie"), (53, "Thriller"), (10752, "War"), (37, "Western") })
        .Select(pair => (pair.Item1.ToString(CultureInfo.InvariantCulture), pair.Item2)).ToArray();
    private static IEnumerable<(string Value, string Label)> Languages() => CultureInfo.GetCultures(CultureTypes.NeutralCultures)
        .Where(c => c.TwoLetterISOLanguageName.Length == 2).DistinctBy(c => c.TwoLetterISOLanguageName).OrderBy(c => c.EnglishName).Select(c => (c.TwoLetterISOLanguageName, c.EnglishName));
    private static IEnumerable<(string Value, string Label)> Countries() => CultureInfo.GetCultures(CultureTypes.SpecificCultures).Select(c => new RegionInfo(c.Name))
        .DistinctBy(r => r.TwoLetterISORegionName).OrderBy(r => r.EnglishName).Select(r => (r.TwoLetterISORegionName, r.EnglishName));
}
