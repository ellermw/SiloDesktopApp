using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

internal static class CalendarDateOct20NativeFixture
{
    internal static Task RunAsync()
    {
        var settings = SiloPlayer.App.Services.GetRequiredService<SettingsService>();
        var values = settings.Load();
        var original = values.UiDateFormat;
        var formatter = typeof(ItemDetailPage).GetMethod("FormatDetailDate", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            values.UiDateFormat = "YYYY-MM-DD"; settings.Save(values);
            foreach (var input in new[] { "2026-10-18", "2028-02-29" })
                if ((string)formatter.Invoke(null, [input])! != input)
                    throw new InvalidOperationException("Stored air date moved off its calendar day: " + input);
            if ((string)formatter.Invoke(null, ["2026-02-30"])! != "2026-02-30")
                throw new InvalidOperationException("Invalid stored day was normalized into a different date.");
            var timestamp = "2026-10-18T00:00:00Z";
            var expected = DateTimeDisplay.FormatDate(DateTimeOffset.Parse(timestamp), medium: true);
            if ((string)formatter.Invoke(null, [timestamp])! != expected)
                throw new InvalidOperationException("True timestamps stopped honoring the display timezone.");
            Program.Log("PASS: actual detail date formatter preserves stored calendar dates and leap days while timestamps still localize.");
        }
        finally { values.UiDateFormat = original; settings.Save(values); }
        return Task.CompletedTask;
    }
}
