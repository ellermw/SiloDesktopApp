using System.Globalization;
using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class CalendarCultureNativeFixture
{
    internal static async Task RunAsync()
    {
        var cultureBefore = CultureInfo.CurrentCulture;
        var localeBefore = LocaleService.Shared.Current;
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var servicesBefore = (IServiceProvider)field.GetValue(null)!;
        try
        {
            foreach (var name in new[] { "th-TH", "ar-SA", "fa-IR", "en-US" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                LocaleService.Shared.Apply(LocaleService.Shared.Create(AppLocaleResolution.Resolve(null, [name], name)));
                using var wire = new Wire();
                using var http = new HttpClient(wire);
                var client = new SiloApiClient(http); client.SetBaseUrl("https://calendar-culture.invalid");
                var model = new CalendarViewModel(new CatalogApi(client),
                    new SettingsService(Path.Combine(Program.ResultDirectory, name)));
                try
                {
                    model.WeekStart = CalendarViewModel.GetWeekStart(new DateTime(2026, 10, 28));
                    await model.LoadCommand.ExecuteAsync(null);
                    Program.Log($"Calendar culture {name}: requests={string.Join(";", wire.Ranges)}, loaded={model.HasLoaded}, error={model.ErrorMessage ?? "none"}");
                    Require(model.HasLoaded && model.ErrorMessage == null && model.Days.Count == 1,
                        name + ": real week load failed.");
                    Require(wire.Ranges.Single() == "2026-10-26/2026-11-01", name + ": non-Gregorian HTTP week.");
                    await model.NextWeekCommand.ExecuteAsync(null);
                    Require(wire.Ranges.Last() == "2026-11-02/2026-11-08", name + ": next week HTTP range.");
                    await model.PrevWeekCommand.ExecuteAsync(null);
                    Require(wire.Ranges.Last() == "2026-10-26/2026-11-01", name + ": previous week HTTP range.");
                    field.SetValue(null, new Services(servicesBefore, model));
                    foreach (var width in new[] { 460, 1280 })
                    {
                        var frame = new Frame();
                        var window = new Window { Content = frame };
                        try
                        {
                            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
                            window.AppWindow.Show(false);
                            frame.Navigate(typeof(CalendarPage));
                            await Task.Delay(250); frame.UpdateLayout();
                            var texts = Descendants(frame).OfType<TextBlock>().ToArray();
                            Require(texts.Any(t => t.Text == "Gregorian calendar fixture"), name + ": event missing from real CalendarPage.");
                            Require(texts.Any(t => t.Text == model.WeekRangeLabel && t.ActualHeight > 0), name + ": range label missing.");
                            Require(model.WeekRangeLabel.Contains("2026", StringComparison.Ordinal), name + ": range year changed.");
                            // The compact preset is never opened here: its items and closed selection follow the page.
                            var preset = (ComboBox)((CalendarPage)frame.Content).FindName("CompactPresetComboBox");
                            RequirePresetLanguage(preset, name);
                            if (name == "en-US")
                            {
                                LocaleService.Shared.Apply(LocaleService.Shared.Create(AppLocaleResolution.Resolve("de", [name], name)));
                                await Task.Delay(100); frame.UpdateLayout();
                                RequirePresetLanguage(preset, name + " switched to German");
                                LocaleService.Shared.Apply(LocaleService.Shared.Create(AppLocaleResolution.Resolve(null, [name], name)));
                                await Task.Delay(100); frame.UpdateLayout();
                                RequirePresetLanguage(preset, name + " switched back");
                            }
                            Program.Log($"PASS: {name} actual HTTP week/navigation and CalendarPage at {width}px; range={model.WeekRangeLabel}.");
                        }
                        finally { frame.Content = null; window.Close(); }
                    }
                }
                finally { model.CancelLoad(); field.SetValue(null, servicesBefore); }
            }
        }
        finally
        {
            field.SetValue(null, servicesBefore); CultureInfo.CurrentCulture = cultureBefore;
            LocaleService.Shared.Apply(LocaleService.Shared.Create(localeBefore.Resolution));
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void RequirePresetLanguage(ComboBox preset, string name)
    {
        var expected = new[]
        {
            AppStrings.Get("web.calendar.presetFollowing", fallback: "Following"),
            AppStrings.Get("web.calendar.presetTrending", fallback: "Trending"),
            AppStrings.Get("web.calendar.presetAll", fallback: "All"),
        };
        var shown = preset.Items.OfType<ComboBoxItem>().Select(item => item.Content as string ?? "").ToArray();
        Require(shown.SequenceEqual(expected), name + ": never-opened calendar preset items show " + string.Join(",", shown));
        if (preset.Visibility == Visibility.Visible && preset.IsLoaded && preset.ActualWidth > 0)
            Require(Equals(preset.SelectionBoxItem, (preset.SelectedItem as ComboBoxItem)?.Content),
                name + ": closed calendar preset selection shows " + preset.SelectionBoxItem);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class Services(IServiceProvider fallback, CalendarViewModel model) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(CalendarViewModel) ? model : fallback.GetService(type);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal List<string> Ranges { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "calendar-culture.invalid") throw new InvalidOperationException("External fixture request refused.");
            if (uri.AbsolutePath.EndsWith("/libraries", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") });
            if (!uri.AbsolutePath.EndsWith("/calendar", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected fixture route.");
            var query = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
                .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
            Ranges.Add(query["start"] + "/" + query["end"]);
            if (!query["start"].StartsWith("2026-", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"events\":[{\"date\":\"2026-10-28\",\"items\":[{\"content_id\":\"calendar-fixture\",\"type\":\"movie\",\"title\":\"Gregorian calendar fixture\"}]}]}") });
        }
    }
}
