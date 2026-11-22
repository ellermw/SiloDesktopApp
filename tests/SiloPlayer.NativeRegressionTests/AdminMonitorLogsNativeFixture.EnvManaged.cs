using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

/// <summary>
/// Settings > Logging with the server's environment holding the log level (official <c>managedByEnv={form.envVar("server.log_level")}</c>): the row is read-only and says which
/// variable sets it, an editable row beside it still saves, the save never carries the managed key, and the note and the read-only row hold at every window width.
/// The variable name is synthetic and as long as a name can be, so the note has to wrap.
/// </summary>
internal static partial class AdminMonitorLogsNativeFixture
{
    private static async Task CheckManagedLoggingAsync(Window window, Frame frame, Server server)
    {
        server.ManagedLogLevel = true; server.LastPut = null; var writes = server.BatchWrites;
        frame.Navigate(typeof(AdminLoggingSettingsPage), new AdminRouteRequest(AdminRoute.LoggingSettings));
        var page = (AdminLoggingSettingsPage)frame.Content;
        await Until(() => page.Controller?.State is { Loaded: true, StatusKnown: true });
        var controller = page.Controller!;
        var level = Find<ComboBox>(page, "LoggingField_server.log_level");
        var note = Find<TextBlock>(page, "LoggingEnvironmentNote_server.log_level");
        // The page paints the controller state from a queued render: wait for the painted note and the painted editable row.
        await Until(() => note.Text.Length > 0 && Find<TextBox>(page, "LoggingField_opslog.retention_days").IsEnabled);
        var expected = AdminText.T("web.admin.settings.fields.managedByEnv", new { env = ManagedVariable });
        Check(!level.IsEnabled, "a log level the environment sets is read-only");
        Check(note.Visibility == Visibility.Visible && note.Text == expected, "the log level row names the variable that sets it as plain text (" + note.Text.Length + " characters)");
        Check(Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(level).Contains(ManagedVariable), "the note is part of the read-only control's help text");
        Check(All(page).OfType<TextBlock>().Count(text => (Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(text) ?? "").StartsWith("LoggingEnvironmentNote_", StringComparison.Ordinal) && text.Visibility == Visibility.Visible) == 1,
            "only the managed row carries a note");
        var days = Find<TextBox>(page, "LoggingField_opslog.retention_days");
        Check(days.IsEnabled, "an editable row beside the managed one stays editable");

        // A typed value on the editable row saves, and the managed key is not part of the write.
        days.Text = "14";
        await Until(() => controller.State.DirtyKeys.Contains("opslog.retention_days"));
        Check(controller.State.DirtyKeys.Count == 1, "only the editable row is staged");
        Invoke(Find<Button>(page, "LoggingSave"));
        await Until(() => server.BatchWrites == writes + 1 && !controller.State.Saving && controller.State.DirtyKeys.Count == 0);
        Check(server.LastPut != null && server.LastPut.Contains("opslog.retention_days") && !server.LastPut.Contains("server.log_level"), "the save sends the editable key and never the managed one");
        await Until(() => note.Text.Length > 0 && !controller.State.Saving);
        Check(!level.IsEnabled && note.Visibility == Visibility.Visible, "the row is still read-only after the save re-read the list");

        await CheckLoggingFitsAsync(window, page, "Logging page with a managed row", expandAdvanced: false, [1440, 1024, 800, 560]);
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(560, 900));
        await LoggingLayoutSettledAsync(window, page, 560);
        Check(note.ActualHeight > note.FontSize * 2, "the long variable name wraps onto more than one line at 560px (" + Math.Round(note.ActualHeight) + "px high)");
        await CaptureFour(window, frame, "admin-logging-managed");
        server.ManagedLogLevel = false; server.BatchWrites = writes; server.LastPut = null;
    }
}
