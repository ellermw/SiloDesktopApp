using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

internal static partial class AdminMonitorLogsNativeFixture
{
    private const string ReferencePin = "1a99f7beffcea3850f067df000c319f2704fd23b";
    private const string AppPath = "/api/v2/admin/logs/app";
    private const string ReportsPath = "/api/v2/admin/diagnostics/reports";
    private const string DetailPath = ReportsPath + "/report-1";
    private const string SettingsPath = "/api/v2/admin/settings";
    private sealed record CoverageCase(string Families, string Image, string Exercise, string Gap = "Named variant only; other family subvariants, theme/DPI and complete keyboard cycles remain unchecked.");
    private sealed record CoverageCapture(string Families, string Reference, string[] Native, string[] Assertions, string Gap, bool Passed);
    private static readonly CoverageCase[] CoverageCases =
    [
        new("L01 L05", "aligned-logs-live-app-1440x900.png", "list"),
        new("L02", "logs-connecting-1440x900.png", "pending"),
        new("L02", "logs-live-pending-1440x900.png", "pending-snapshot"),
        new("L03", "logs-live-empty-1440x900.png", "list"),
        new("L04", "logs-live-audit-empty-1440x900.png", "list"),
        new("L06 L07 L08", "logs-rich-audit-1440x900.png", "list", "Exact reference audit data; right-scrolled identity columns and hover remain unchecked."),
        new("L09", "logs-disconnected-empty-1440x900.png", "disconnected"),
        new("L10 L11", "logs-disconnected-retained-1440x900.png", "reconnect", "Disconnected reference data; reconnect transition asserted, not a matched reconnect image."),
        new("L12", "logs-filter-whitespace-cleared-1440x900.png", "whitespace"),
        new("L13", "logs-level-popup-1440x900.png", "level"),
        new("L14", "logs-component-suggestions-1440x900.png", "component", "Native AutoSuggestBox is not WebUI datalist; popup geometry unverified."),
        new("L15", "logs-audit-filter-fields-1440x900.png", "audit-filters"),
        new("L16 L17 L18 L19", "aligned-logs-playback-summary-1440x900.png", "summary"),
        new("L16 L17", "logs-rich-rows-summary-1440x900.png", "summary"),
        new("L16 L17 L19", "logs-summary-zero-1440x900.png", "summary"),
        new("L16 L17 L18 L19", "logs-summary-audit-768x900.png", "summary"),
        new("L16 L17 L18 L19", "logs-summary-audit-767x900.png", "summary"),
        new("L16 L17 L18 L19", "logs-summary-audit-560x900.png", "summary"),
        new("L20 L22", "logs-history-first-more-1440x900.png", "history"),
        new("L21", "logs-history-pending-1440x900.png", "history-pending"),
        new("L21", "logs-history-empty-1440x900.png", "history"),
        new("L21 L23", "logs-history-error-1440x900.png", "history-error"),
        new("L22 L23", "logs-history-second-1440x900.png", "history-next", "Synthetic reference cursor bodies do not prove real server paging semantics."),
        new("L24", "logs-history-filter-reset-1440x900.png", "history-filter"),
        new("L25 L27 L28", "aligned-logs-app-detail-1440x900.png", "log-detail", "X-close/focus asserted; Escape/backdrop/full Tab cycle and related-link click remain unchecked."),
        new("L26", "logs-detail-missing-1440x900.png", "log-detail"),
        new("L26", "logs-detail-long-1440x900.png", "log-detail"),
        new("L26", "logs-detail-huge-json-1440x900.png", "log-detail"),
        new("L29", "aligned-logs-live-app-1440x900.png", "authority", "Native-only authority replacement; no official L29 reference exists."),
        new("C01", "diagnostics-capabilities-available-1440x900.png", "list"),
        new("C02 C06 C15", "aligned-diagnostics-default-1440x900.png", "list"),
        new("C03", "diagnostics-capabilities-disabled-1440x900.png", "list"),
        new("C04", "diagnostics-status-pending-1440x900.png", "pending"),
        new("C04 C14", "diagnostics-status-error-1440x900.png", "error"),
        new("C05", "diagnostics-upload-toggle-pending-1440x900.png", "toggle"),
        new("C07 C11", "diagnostics-filter-draft-1440x900.png", "diagnostics-filters", "Draft/Apply/Clear/invalid User asserted; Enter/reversed dates/short-ID casing unchecked."),
        new("C08", "diagnostics-platform-popup-1440x900.png", "platform"),
        new("C09", "diagnostics-type-popup-1440x900.png", "report-type"),
        new("C10", "diagnostics-calendar-empty-1440x900.png", "calendar", "Native calendar/time controls differ; To/time-first/selected/cleared variants unchecked."),
        new("C10", "diagnostics-calendar-selected-1440x900.png", "calendar-selected", "Selected From and Clear covered using reference day/time; To and time-first remain unchecked native control differences."),
        new("C12", "diagnostics-list-pending-1440x900.png", "pending"),
        new("C13", "diagnostics-empty-1440x900.png", "list"),
        new("C13", "diagnostics-filtered-empty-1440x900.png", "list"),
        new("C14", "diagnostics-list-error-1440x900.png", "error"),
        new("C14", "diagnostics-combined-error-1440x900.png", "error"),
        new("C16", "diagnostics-row-receiving-1440x900.png", "list"),
        new("C17", "aligned-diagnostics-default-1440x900.png", "diagnostics-pages", "Cursor controls asserted with reference rows, not matched second-page reference geometry."),
        new("C18 C20 C23", "aligned-diagnostics-ready-detail-1440x900.png", "report-detail", "Programmatic selection only; hover/Enter/Space and long-manifest scroll unchecked."),
        new("C19", "diagnostics-detail-pending-1440x900.png", "report-pending"),
        new("C19", "diagnostics-detail-error-1440x900.png", "report-error"),
        new("C21", "diagnostics-detail-receiving-1440x900.png", "report-detail"),
        new("C21", "diagnostics-detail-failed-1440x900.png", "report-detail"),
        new("C22", "diagnostics-detail-missing-fields-1440x900.png", "report-detail"),
        new("C24", "diagnostics-download-pending-1440x900.png", "download", "Memory transport only; OS picker, disk completion and valid gzip success unchecked."),
        new("C25", "diagnostics-download-error-409-1440x900.png", "download-error"),
        new("C25", "diagnostics-download-error-404-1440x900.png", "download-error"),
        new("C25", "diagnostics-download-error-503-1440x900.png", "download-error"),
        new("C25", "diagnostics-download-error-200-1440x900.png", "download-error"),
        new("C26", "aligned-diagnostics-delete-confirm-1440x900.png", "delete", "Confirmation auto-closes before held DELETE; static-open D18 was disproven."),
        new("C27 C28", "aligned-diagnostics-ready-detail-1440x900.png", "playback", "X-close/playback link covered; Escape/backdrop/Tab trap and retirement during picker unchecked."),
        new("S01", "logging-initial-pending-1440x900.png", "pending"),
        new("S02", "aligned-logging-clean-1440x900.png", "logging-load-error", "Native recovery extension; no equivalent WebUI inline load-error state."),
        new("S03 S23", "aligned-logging-clean-1440x900.png", "logging-clean"),
        new("S04", "logging-level-popup-1440x900.png", "logging-level"),
        new("S05 S06 S07", "logging-both-advanced-1440x900.png", "logging-expanded", "Both disclosures open; bottom-scroll/long values/remount persistence unchecked."),
        new("S08 S09 S10 S17", "logging-dirty-1440x900.png", "logging-dirty", "Dirty dots/count/manual collapse/Discard asserted; newly hidden dirty/remount unchecked."),
        new("S11 S12", "logging-save-pending-1440x900.png", "logging-save"),
        new("S13", "aligned-logging-clean-1440x900.png", "logging-invalid", "Native validation extension; no matched invalid-number reference."),
        new("S14", "logging-save-error-500-1440x900.png", "logging-save-error"),
        new("S15 S16", "logging-save-error-409-1440x900.png", "logging-conflict", "409 toast reference; native reconciliation Keep/Cancel covered, Discard unchecked."),
        new("S18", "logging-restart-chip-1440x900.png", "logging-restart"),
        new("S21 S22", "logging-leave-confirm-1440x900.png", "logging-leave", "Leave Cancel/retirement asserted; no official S22 reference; retirement during held save unchecked."),
        new("S24", "logging-dirty-640x900.png", "logging-phone"),
        new("S24", "logging-dirty-639x900.png", "logging-phone"),
        new("S24", "logging-dirty-560x900.png", "logging-phone")
    ];

    private static async Task RunStateCoverageAsync(Window window, Frame frame, Server server, SiloApiClient client, AuthService auth, AdminMonitorAuthority authority)
    {
        var directory = Environment.GetEnvironmentVariable("SILO_LOGS_REFERENCE_DIRECTORY") ?? "D:/SiloPlayer-build/admin-visual-parity/reference-logs-complete";
        using var reference = new ReferenceData(directory);
        server.Reference = reference;
        var captures = new List<CoverageCapture>();
        var start = int.TryParse(Environment.GetEnvironmentVariable("SILO_LOGS_COVERAGE_START"), out var first) ? Math.Clamp(first, 0, CoverageCases.Length) : 0;
        var limit = int.TryParse(Environment.GetEnvironmentVariable("SILO_LOGS_COVERAGE_LIMIT"), out var count) ? Math.Max(1, count) : CoverageCases.Length;
        var serial = start;
        try
        {
            foreach (var spec in CoverageCases.Skip(start).Take(limit))
            {
                var assertions = new List<string>(); var native = new List<string>(); serial++;
                void Require(bool condition, string message) { Check(condition, spec.Families + ": " + message); if (!condition) throw new InvalidOperationException(message); assertions.Add(message); }
                async Task Wait(Func<bool> condition, string message) { for (var attempt = 0; attempt < 1600 && !condition(); attempt++) await Task.Delay(25); Require(condition(), message); await Task.Delay(50); frame.UpdateLayout(); }
                async Task Snapshot(string suffix = "")
                {
                    await Task.Delay(100); frame.UpdateLayout(); var name = $"logs-state-{serial:000}-{spec.Families.Split(' ')[0]}{suffix}.png";
                    await Capture(frame, name); native.Add(name);
                    foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot))
                        if (popup.Child is FrameworkElement child && child.ActualWidth > 0 && child.ActualHeight > 0) { var popupName = Path.GetFileNameWithoutExtension(name) + $"-popup-{native.Count}.png"; if (await Capture(child, popupName, allowUnavailableCalendar: true)) native.Add(popupName); }
                    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(Program.ResultDirectory, name))));
                    await File.WriteAllTextAsync(Path.Combine(Program.ResultDirectory, Path.ChangeExtension(name, ".state.json")), JsonSerializer.Serialize(new { pin = ReferencePin, reference = spec.Image, suffix, sha256 = hash, width = window.AppWindow.ClientSize.Width, height = window.AppWindow.ClientSize.Height, initialApiPreset = reference.Sidecar["initialApiPreset"], activeApiPreset = reference.Preset(), assertions = assertions.ToArray(), gap = spec.Gap }, new JsonSerializerOptions { WriteIndented = true }));
                }
                bool SheetVisible() => All(frame).OfType<FrameworkElement>().Any(element => element.GetType().Name == "LogsDetailSheet" && element.Visibility == Visibility.Visible);
                ContentDialog? Dialog(object page) => page.GetType().GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(page) as ContentDialog;
                async Task ClickDialog(object page, string button)
                {
                    await Wait(() => Dialog(page) is { } dialog && All(dialog).OfType<Button>().Any(control => control.Name == button), "confirmation button mounted");
                    Invoke(All(Dialog(page)!).OfType<Button>().Single(control => control.Name == button));
                }
                async Task Feedback(string text) => await Wait(() => All(frame).OfType<FrameworkElement>().Any(element => AutomationProperties.GetAutomationId(element) == "LogsActionFeedback" && element.Visibility == Visibility.Visible && All(element).OfType<TextBlock>().Any(label => label.Text == text)), "feedback visible above sheet");
                try
                {
                    foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot)) popup.IsOpen = false;
                    foreach (var previous in All(frame).OfType<Page>()) Dialog(previous)?.Hide();
                    foreach (var logging in All(frame).OfType<AdminLoggingSettingsPage>()) logging.Controller?.Discard();
                    frame.Content = null;
                    reference.Use(spec.Image); var viewport = reference.Sidecar["viewport"]!;
                    reference.HoldSocket = spec.Exercise == "pending-snapshot";
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(viewport["width"]!.GetValue<int>(), viewport["height"]!.GetValue<int>()));
                    var route = spec.Families.StartsWith('L') ? AdminRoute.ServerLog : spec.Families.StartsWith('C') ? AdminRoute.ClientDiagnostics : AdminRoute.LoggingSettings;
                    if (spec.Exercise == "logging-load-error") reference.Fail(SettingsPath + "/effective", 400);
                    frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(route, reference.RouteArguments()));
                    await Wait(() => All(frame).OfType<Page>().Any(page => page.GetType() == CoveragePageType(route)), "requested page mounted");
                    var page = All(frame).OfType<Page>().Single(page => page.GetType() == CoveragePageType(route));
                    if (!spec.Exercise.StartsWith("pending", StringComparison.Ordinal)) await Wait(() => page switch { AdminLogsPage logs => logs.Controller?.State.Loading == false, AdminDiagnosticsPage diagnostics => diagnostics.Controller?.State is { Loading: false, StatusLoading: false }, AdminLoggingSettingsPage logging => logging.Controller?.State.Loading == false, _ => false }, "initial request settled");
                    if (page is AdminLogsPage logs) await ExerciseLogs(logs);
                    else if (page is AdminDiagnosticsPage diagnostics) await ExerciseDiagnostics(diagnostics);
                    else await ExerciseLogging((AdminLoggingSettingsPage)page);
                    await Snapshot(); captures.Add(new(spec.Families, spec.Image, native.ToArray(), assertions.ToArray(), spec.Gap + " Popup images are separate crops, not full-window parity evidence.", true));
                }
                catch (Exception error) { Program.Log(error.ToString()); Check(false, spec.Families + ": " + error.Message); captures.Add(new(spec.Families, spec.Image, native.ToArray(), assertions.ToArray(), error.GetType().Name + ": " + error.Message, false)); }
                finally { reference.Release(); if (auth.CurrentUser?.Role != "admin") auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" }); }

                async Task ExerciseDiagnostics(AdminDiagnosticsPage page)
                {
                    var controller = page.Controller!;
                    if (spec.Exercise == "pending") { await Wait(() => controller.State.Loading || controller.State.StatusLoading, "Diagnostics endpoint pending"); return; }
                    if (spec.Exercise == "error") { Require(controller.State.Error != null || controller.State.StatusError != null, "Diagnostics error and Retry visible"); await Snapshot("-error"); reference.Release(); await controller.RefreshAsync(); Require(controller.State.Error == null && controller.State.StatusError == null, "Retry recovers failed endpoint"); return; }
                    Require(controller.State.Reports.Count == reference.Items(ReportsPath).Count, "list uses exact reference rows");
                    Require(Find<Button>(page, "DiagnosticsClear").IsEnabled == controller.State.Query.HasFilters, "Clear follows applied filters");
                    if (spec.Exercise == "toggle")
                    {
                        reference.Hold(SettingsPath + "/diagnostics.uploads_enabled", "PUT"); var toggle = Find<ToggleSwitch>(page, "DiagnosticsUploads"); toggle.IsOn = !toggle.IsOn;
                        await Wait(() => controller.State.ToggleBusy && !toggle.IsEnabled, "pending upload toggle disabled"); await Snapshot("-pending"); reference.ApplyCapture("diagnostics-upload-toggle-off-1440x900.png");
                        await Wait(() => !controller.State.ToggleBusy && controller.State.Notice != null, "upload write acknowledged"); Require(controller.State.Capabilities is { Status: "available", State: "disabled" } && controller.State.UploadsEnabled, "status-based toggle mirrors pinned WebUI despite disabled state token"); await Feedback(controller.State.Notice!); await Snapshot("-success");
                        reference.Fail(SettingsPath + "/diagnostics.uploads_enabled", 409, "PUT"); toggle.IsOn = !toggle.IsOn;
                        await Wait(() => !controller.State.ToggleBusy && controller.State.ActionError != null && toggle.IsEnabled, "generic 409 failure permits explicit retry with retained revision"); await Feedback(controller.State.ActionError!); await Snapshot("-error-409");
                        reference.Fail(SettingsPath + "/diagnostics.uploads_enabled", 412, "PUT"); toggle.IsOn = !toggle.IsOn;
                        await Wait(() => !controller.State.ToggleBusy && controller.State.ActionError != null && controller.State.SettingsRevision == null && !toggle.IsEnabled, "412 disables toggle without fresh revision"); await Feedback(controller.State.ActionError!);
                    }
                    if (spec.Exercise == "diagnostics-filters")
                    {
                        var previous = controller.State.Query; var user = Find<TextBox>(page, "DiagnosticsFilter_user"); user.Text = "12"; Require(controller.State.Query == previous, "draft leaves applied query unchanged"); await Snapshot("-draft");
                        user.Text = "-1"; Invoke(Find<Button>(page, "DiagnosticsApply")); await Wait(() => All(page).OfType<TextBlock>().Any(text => text.Text == "Enter a valid user ID." && text.Visibility == Visibility.Visible), "invalid User displays validation"); Require(controller.State.Query == previous, "invalid User cannot replace query"); await Snapshot("-invalid");
                        user.Text = "12"; Invoke(Find<Button>(page, "DiagnosticsApply")); await Wait(() => controller.State.Query.UserId == "12", "Apply commits draft");
                        Invoke(Find<Button>(page, "DiagnosticsClear")); await Wait(() => !controller.State.Query.HasFilters && user.Text == "", "Clear resets applied and draft filters");
                    }
                    if (spec.Exercise is "platform" or "report-type") { var select = Find<ComboBox>(page, spec.Exercise == "platform" ? "web.admin.diagnostics.filters.platform" : "web.admin.diagnostics.filters.reportType"); Require(select.Items.Count == (spec.Exercise == "platform" ? 6 : 7), "complete reference option count"); select.IsDropDownOpen = true; }
                    if (spec.Exercise.StartsWith("calendar", StringComparison.Ordinal))
                    {
                        var from = Find<Button>(page, "DiagnosticsFrom"); from.Flyout.ShowAt(from); await Task.Delay(100);
                        var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot).Single(open => All(open.Child).OfType<CalendarView>().Any());
                        Require(!Find<Button>(popup.Child, "DiagnosticsFromClear").IsEnabled, "empty From calendar has disabled Clear");
                        if (spec.Exercise == "calendar-selected")
                        {
                            var day = new DateTime(2026, 11, 13); All(popup.Child).OfType<CalendarView>().Single().SelectedDates.Add(new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day))); All(popup.Child).OfType<TimePicker>().Single().Time = new TimeSpan(13, 45, 0);
                            var expected = reference.Sidecar["elements"]!.AsArray().First(element => element!["tag"]?.GetValue<string>() == "BUTTON" && element["text"]?.GetValue<string>().StartsWith("11/13/2026", StringComparison.Ordinal) == true)!["text"]!.GetValue<string>();
                            Require(All(from).OfType<TextBlock>().Any(text => text.Text == expected), "selected date button matches reference minute precision"); await Snapshot("-selected");
                            Invoke(Find<Button>(popup.Child, "DiagnosticsFromClear")); Require(!Find<Button>(popup.Child, "DiagnosticsFromClear").IsEnabled, "Clear removes selected day and time");
                        }
                    }
                    if (spec.Exercise == "diagnostics-pages")
                    {
                        var body = reference.Body(ReportsPath).DeepClone().AsObject(); body["page"] = new JsonObject { ["has_more"] = true, ["next_cursor"] = "native-next" }; reference.BodyOverride(ReportsPath, body); await controller.RefreshAsync(); Require(controller.State.NextCursor != null && !controller.State.CanPrevious, "first page enables Next only");
                        body["page"] = new JsonObject { ["has_more"] = false }; reference.BodyOverride(ReportsPath, body); await controller.NextAsync(); Require(controller.State.Page == 2 && controller.State.CanPrevious && controller.State.NextCursor == null, "last page enables Previous only");
                    }
                    if (spec.Exercise is "report-pending" or "report-error")
                    {
                        reference.ApplyCapture(spec.Image); var pending = controller.SelectAsync("report-1");
                        await Wait(() => spec.Exercise == "report-pending" ? controller.State.DetailLoading : controller.State.DetailError != null, "detail pending/error fallback rendered");
                        if (spec.Exercise == "report-error") await pending; return;
                    }
                    if (spec.Exercise is "report-detail" or "download" or "download-error" or "delete" or "playback")
                    {
                        await controller.SelectAsync("report-1"); await Wait(() => All(frame).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "DiagnosticsDelete"), "report detail actions mounted");
                        Require(Find<Button>(frame, "DiagnosticsDownload").IsEnabled == (controller.State.Detail!.State == "ready"), "only ready report downloadable");
                        if (spec.Exercise == "report-detail")
                        {
                            await Snapshot("-detail"); Invoke(Find<Button>(frame, "DiagnosticsManifest"));
                            Require((bool)typeof(AdminDiagnosticsPage).GetField("_manifestExpanded", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!, "manifest disclosure expanded");
                            await Wait(() => All(frame).OfType<TextBlock>().Any(text => text.IsTextSelectionEnabled && text.Text.Contains("{", StringComparison.Ordinal)), "manifest JSON selectable");
                        }
                        if (spec.Exercise == "download")
                        {
                            reference.Hold(DetailPath + "/download"); using var action = controller.CaptureAction(); using var output = new MemoryStream(); using var cancellation = new CancellationTokenSource();
                            var pending = controller.DownloadAsync(action, controller.State.Detail!, output, cancellation.Token);
                            await Wait(() => controller.State.Downloading && !Find<Button>(frame, "DiagnosticsDownload").IsEnabled, "held download Preparing and disabled"); await Snapshot("-preparing"); cancellation.Cancel(); await pending; Require(!controller.State.Downloading, "cancel clears download busy state");
                        }
                        if (spec.Exercise == "download-error")
                        {
                            reference.ApplyCapture(spec.Image); using var action = controller.CaptureAction(); using var output = new MemoryStream(); Require(!await controller.DownloadAsync(action, controller.State.Detail!, output), "error/malformed bundle rejected");
                            Require(!controller.State.Downloading && controller.State.ActionError != null, "download failure clears busy and exposes feedback"); await Feedback(controller.State.ActionError!);
                        }
                        if (spec.Exercise == "delete")
                        {
                            Invoke(Find<Button>(frame, "DiagnosticsDelete")); await Wait(() => Dialog(page) != null, "delete confirmation shown"); await Snapshot("-confirm"); Dialog(page)!.Hide(); await Wait(() => Dialog(page) == null, "Cancel closes delete confirmation");
                            reference.Hold(DetailPath, "DELETE"); Invoke(Find<Button>(frame, "DiagnosticsDelete")); await ClickDialog(page, "PrimaryButton");
                            await Wait(() => controller.State.Deleting && Dialog(page) == null, "confirmation auto-closes before held DELETE"); Require(!Find<Button>(frame, "DiagnosticsDelete").IsEnabled, "sheet Delete disabled while pending"); await Snapshot("-pending");
                            reference.Fail(DetailPath, 400, "DELETE"); await Wait(() => !controller.State.Deleting && controller.State.ActionError != null, "delete failure retains selected report"); await Feedback(controller.State.ActionError!); await Snapshot("-error");
                            reference.Release(); Invoke(Find<Button>(frame, "DiagnosticsDelete")); await ClickDialog(page, "PrimaryButton"); await Wait(() => controller.State.SelectedId == null && controller.State.Reports.Count == 0, "successful delete closes sheet and removes report");
                        }
                        if (spec.Exercise == "playback")
                        {
                            Invoke(Find<Button>(frame, "LogsDetailClose")); await Wait(() => !SheetVisible(), "Diagnostics X closes sheet"); await controller.SelectAsync("report-1"); await Wait(SheetVisible, "report reopened");
                            Invoke(Find<Button>(frame, "DiagnosticsPlayback_synthetic-playback")); await Wait(() => All(frame).OfType<AdminLogsPage>().Any(logs => logs.Controller?.State.Query.PlaybackSessionId == "synthetic-playback"), "playback link navigates to matching focused Logs");
                        }
                    }
                }

                async Task ExerciseLogging(AdminLoggingSettingsPage page)
                {
                    var controller = page.Controller!;
                    void BothAdvanced()
                    {
                        if (!controller.State.ServerAdvancedOpen) Invoke(Find<Button>(page, "LoggingAdvanced_server"));
                        if (!controller.State.RetentionAdvancedOpen) Invoke(Find<Button>(page, "LoggingAdvanced_retention"));
                    }
                    if (spec.Exercise == "pending") { await Wait(() => controller.State.Loading, "Logging initial read held"); Require(All(page).OfType<Border>().Count(border => Math.Abs(border.Height - 40) < 0.1) == 5, "five initial skeleton rows"); return; }
                    if (spec.Exercise == "logging-load-error") { Require(controller.State.Error != null && !controller.State.Loaded, "failed initial read has native Retry"); await Snapshot("-error"); reference.Release(); await controller.OpenAsync(); Require(controller.State.Loaded, "Retry reloads Logging"); return; }
                    Require(controller.State.Loaded && controller.State.DirtyKeys.Count == 0, "reference values load without false draft");
                    if (spec.Exercise == "logging-clean") { if (controller.State.ServerAdvancedOpen) Invoke(Find<Button>(page, "LoggingAdvanced_server")); if (controller.State.RetentionAdvancedOpen) Invoke(Find<Button>(page, "LoggingAdvanced_retention")); }
                    if (spec.Exercise == "logging-level") { var select = Find<ComboBox>(page, "LoggingField_server.log_level"); Require(select.Items.Count == 4, "four Logging level choices"); select.IsDropDownOpen = true; }
                    if (spec.Exercise is "logging-expanded" or "logging-restart") BothAdvanced();
                    if (spec.Exercise == "logging-restart") Require(controller.State.RestartKeys.Has("server.log_level"), "reference restart-key field annotation available");
                    if (spec.Exercise is "logging-dirty" or "logging-phone")
                    {
                        BothAdvanced(); controller.Set("opslog.retention_days", "14"); controller.Set("server.log_quiet", "metadata, scanner");
                        await Wait(() => controller.State.DirtyKeys.Count == 2, "essential and advanced edits counted"); await Snapshot("-dirty");
                        if (spec.Exercise == "logging-dirty")
                        {
                            Invoke(Find<Button>(page, "LoggingAdvanced_server")); Require(!controller.State.ServerAdvancedOpen, "manual dirty disclosure collapse respected"); await Snapshot("-collapsed");
                            Invoke(Find<Button>(page, "LoggingDiscard")); await Wait(() => controller.State.DirtyKeys.Count == 0, "Discard clears staged changes"); Require(controller.State.Values["opslog.retention_days"] == "7", "Discard restores server values");
                        }
                        else Require(Find<Button>(page, "LoggingSave").IsEnabled, "phone Save remains enabled for valid dirty draft");
                    }
                    if (spec.Exercise == "logging-save")
                    {
                        BothAdvanced(); Find<TextBox>(page, "LoggingField_opslog.retention_days").Text = "14"; reference.Hold(SettingsPath, "PUT"); Invoke(Find<Button>(page, "LoggingSave"));
                        await Wait(() => controller.State.Saving && !Find<Button>(page, "LoggingSave").IsEnabled, "held save disables Save"); Require(Find<Button>(page, "LoggingDiscard").IsEnabled && Find<TextBox>(page, "LoggingField_opslog.retention_days").IsEnabled, "Discard and newer editing remain available"); await Snapshot("-saving");
                        Find<TextBox>(page, "LoggingField_opslog.retention_days").Text = "21"; reference.Release(); await Wait(() => !controller.State.Saving, "held save acknowledged");
                        Require(controller.State.Values["opslog.retention_days"] == "21" && controller.State.DirtyKeys.Contains("opslog.retention_days"), "acknowledgement preserves newer dirty edit"); await Snapshot("-newer-edit");
                        Invoke(Find<Button>(page, "LoggingSave")); await Wait(() => !controller.State.Saving && controller.State.DirtyKeys.Count == 0, "second save clears canonical acknowledged draft");
                    }
                    if (spec.Exercise is "logging-invalid" or "logging-save-error" or "logging-conflict")
                    {
                        controller.Set("opslog.retention_days", spec.Exercise == "logging-invalid" ? "-1" : "14");
                        if (spec.Exercise != "logging-invalid") reference.ApplyCapture(spec.Image);
                        Invoke(Find<Button>(page, "LoggingSave")); await Wait(() => controller.State.Error != null && !controller.State.Saving, "failed save preserves draft and exposes error"); Require(controller.State.DirtyKeys.Count > 0, "failed save remains dirty"); await Feedback(controller.State.Error!);
                        if (spec.Exercise == "logging-conflict")
                        {
                            Require(!controller.State.Conflict, "generic 409 retains draft without a stale-validator classification"); await Snapshot("-error-409");
                            reference.Fail(SettingsPath, 412, "PUT"); Invoke(Find<Button>(page, "LoggingSave"));
                            await Wait(() => controller.State.Conflict && !Find<Button>(page, "LoggingSave").IsEnabled, "412 cannot save without reconciliation"); await Snapshot("-conflict"); reference.Release(); Invoke(Find<Button>(page, "LoggingReload"));
                            await Wait(() => Dialog(page) != null, "native reconciliation dialog shown"); await Snapshot("-reconcile"); Dialog(page)!.Hide(); await Wait(() => Dialog(page) == null, "reconciliation Cancel closed"); Require(controller.State.Conflict, "Cancel retains conflict guard");
                            Invoke(Find<Button>(page, "LoggingReload")); await ClickDialog(page, "PrimaryButton"); await Wait(() => !controller.State.Conflict && controller.State.DirtyKeys.Count > 0, "Keep edits accepts latest revision without losing draft");
                        }
                    }
                    if (spec.Exercise == "logging-leave")
                    {
                        controller.Set("opslog.retention_days", "14"); var leave = page.CanLeaveAsync(); await Wait(() => Dialog(page) != null, "dirty leave confirmation shown"); await Snapshot("-confirm"); Dialog(page)!.Hide(); Require(!await leave && controller.State.DirtyKeys.Count > 0, "Cancel keeps dirty page");
                        leave = page.CanLeaveAsync(); await Wait(() => Dialog(page) != null, "second dirty leave confirmation shown"); auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" }); Require(await leave, "retired authority releases navigation while sensitive draft is retired");
                        await Wait(() => controller.State.Owner == null && controller.State.Values.Count == 0, "authority retirement clears sensitive draft");
                        Require(!All(frame).OfType<FrameworkElement>().Any(element => AutomationProperties.GetAutomationId(element) == "LogsActionFeedback" && element.Visibility == Visibility.Visible), "retirement clears feedback overlay"); await Snapshot("-retired");
                        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
                    }
                }

                async Task ExerciseLogs(AdminLogsPage page)
                {
                    var controller = page.Controller!;
                    if (spec.Exercise.StartsWith("pending", StringComparison.Ordinal)) { await Wait(() => controller.State is { Connection: "connecting", Loading: true }, "held ticket or snapshot shows Connecting and loading"); return; }
                    if (spec.Exercise == "disconnected") { Require(controller.State.Connection == "disconnected" && controller.State.Error != null, "failed ticket exposes reconnect"); return; }
                    if (spec.Exercise == "authority") { var owner = controller.State.Owner!; client.InvalidateAccessContext(); await Wait(() => !owner.IsCurrent && controller.State.Owner is { IsCurrent: true } next && !ReferenceEquals(owner, next), "old authority retired and replaced"); return; }
                    Require((controller.State.Stream == "audit" ? controller.State.AuditRows.Count : controller.State.AppRows.Count) == reference.SocketItems(controller.State.Stream == "audit" ? "/api/v2/admin/logs/audit" : AppPath).Count, "exact reference row count");
                    if (spec.Exercise == "reconnect") { reference.Fail("/api/v2/admin/logs/ws-ticket", 400, "POST"); server.LatestSocket!.Disconnect(); await Wait(() => controller.State.Connection == "disconnected", "renewal ticket failure disconnects stream"); Require(controller.State.AppRows.Count > 0, "rows survive disconnect"); await Snapshot("-disconnected"); reference.Release(); Invoke(Find<Button>(page, "LogsReconnect")); await Wait(() => controller.State.Connection == "live", "Reconnect returns live"); }
                    if (spec.Exercise == "whitespace") { Find<TextBox>(page, "LogsFilter_message").Text = "   "; await Wait(() => string.IsNullOrEmpty(controller.State.Query.Message), "whitespace normalizes empty"); }
                    if (spec.Exercise == "audit-filters") Require(controller.State.Stream == "audit" && controller.State.Query.TargetId == "synthetic" && controller.State.Query.ClientIp == "2001:db8::1", "audit route fields reach applied query");
                    if (spec.Exercise == "level") { var level = All(page).OfType<ComboBox>().Single(); Require(level.Items.Count == 5, "all/debug/info/warn/error available"); level.IsDropDownOpen = true; }
                    if (spec.Exercise == "component") { var component = All(page).OfType<AutoSuggestBox>().Single(); component.Text = "ffmpeg"; await Wait(() => controller.State.Query.Component == "ffmpeg", "free-text component applies"); component.Focus(FocusState.Programmatic); component.IsSuggestionListOpen = true; }
                    if (spec.Exercise == "summary")
                    {
                        var metrics = All(page).OfType<StackPanel>().Where(panel => AutomationProperties.GetAutomationId(panel).StartsWith("LogsSummary_", StringComparison.Ordinal)).ToArray();
                        Require(metrics.Length == (controller.State.AppRows.Count + controller.State.AuditRows.Count > 0 ? 7 : 6), "conditional Last Seen and six required metrics");
                        var actionGeometry = reference.Sidecar["elements"]!.AsArray().First(element => element!["tag"]?.GetValue<string>() == "BUTTON" && element["text"]?.GetValue<string>().Contains("ffmpeg", StringComparison.Ordinal) == true)!["rect"]!;
                        Require(Math.Abs(Find<Button>(page, "LogsFilterFFmpeg").ActualHeight - actionGeometry["height"]!.GetValue<double>()) < 1, "summary action height matches reference geometry");
                        if (window.AppWindow.ClientSize.Width < 768) { var session = Find<StackPanel>(page, "LogsSummary_playbackSession"); var count = Find<StackPanel>(page, "LogsSummary_applicationLogs"); var first = session.TransformToVisual(page).TransformPoint(new(0, 0)); var second = count.TransformToVisual(page).TransformPoint(new(0, 0)); Require(Math.Abs(first.Y - second.Y) < 1 && second.X > first.X && Math.Abs(session.ActualWidth - count.ActualWidth) > 1, "narrow summary packs unequal intrinsic metrics on one row"); }
                        await Snapshot("-summary"); Invoke(Find<Button>(page, "LogsFilterFFmpeg")); await Wait(() => controller.State.Query.Component == "ffmpeg", "FFmpeg action applies without requiring focus flag");
                    }
                    if (spec.Exercise.StartsWith("history", StringComparison.Ordinal))
                    {
                        reference.ApplyCapture(spec.Image); var pending = controller.BrowseHistoryAsync();
                        if (spec.Exercise == "history-pending") { await Wait(() => controller.State.History && controller.State.Loading, "held history displays loading"); return; }
                        await pending; Require(controller.State.History, "REST history active");
                        if (spec.Exercise == "history-error") { Require(controller.State.Error != null, "history error exposes recovery"); await Snapshot("-error"); reference.Release(); await controller.RetryHistoryAsync(); Require(controller.State.Error == null, "history Retry recovers"); }
                        if (spec.Exercise == "history-next") { var body = reference.Body(AppPath).DeepClone().AsObject(); body["page"] = new JsonObject { ["has_more"] = true, ["next_cursor"] = "native-next" }; reference.BodyOverride(AppPath, body); await controller.RetryHistoryAsync(); body["page"] = new JsonObject { ["has_more"] = false }; reference.BodyOverride(AppPath, body); await controller.OlderAsync(); Require(controller.State.Page == 2 && controller.State.CanNewer && controller.State.NextCursor == null, "Older advances; last page enables Newer and disables Older"); }
                        if (spec.Exercise == "history-filter") { Find<TextBox>(page, "LogsFilter_message").Text = "Synthetic"; await Wait(() => !controller.State.History && controller.State.Query.Message == "Synthetic", "filter replacement leaves history and resets cursor"); }
                    }
                    if (spec.Exercise == "log-detail")
                    {
                        var selected = spec.Image.Contains("missing", StringComparison.Ordinal) ? controller.State.AppRows.Single(entry => entry.Component.Length == 0) : spec.Image.Contains("long", StringComparison.Ordinal) ? controller.State.AppRows.Single(entry => entry.Id == "43") : controller.State.AppRows.First();
                        var row = All(page).OfType<Button>().First(button => AutomationProperties.GetName(button) == selected.Message); row.Focus(FocusState.Programmatic); Invoke(row);
                        await Wait(SheetVisible, "Application sheet opened"); Require(All(frame).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "LogsRelatedPlayback") == !string.IsNullOrWhiteSpace(selected.PlaybackSessionId), "related action requires playback identity");
                        await Snapshot("-detail"); Invoke(Find<Button>(frame, "LogsDetailClose")); await Wait(() => !SheetVisible(), "X closes sheet"); Require(row.FocusState != FocusState.Unfocused, "X restores row focus");
                    }
                }
            }
        }
        finally
        {
            reference.Release(); server.Reference = null;
            var inventory = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "inventory.json")))!.AsArray();
            var matrix = inventory.Select(family => new { id = family!["id"]!.GetValue<string>(), condition = family["condition"]!.GetValue<string>(), referenceGaps = family["gaps"]!.GetValue<string>(), states = captures.Where(capture => capture.Families.Split(' ').Contains(family["id"]!.GetValue<string>())).Select(capture => new { officialReference = family["id"]!.GetValue<string>() is "L29" or "S02" or "S13" or "S16" or "S22" ? null : capture.Reference, fixturePreset = capture.Reference, capture.Native, capture.Assertions, capture.Gap, capture.Passed }).ToArray(), remaining = family["id"]!.GetValue<string>() is "S19" or "S20" ? "Shared restart banner/dialog intentionally lead-owned; not exercised here." : "Only named variants above exercised; unlisted states and pixel equivalence remain unverified." }).ToArray();
            await File.WriteAllTextAsync(Path.Combine(Program.ResultDirectory, "logs-state-coverage.json"), JsonSerializer.Serialize(new { pin = ReferencePin, dependency = "4794c265261140171292c9e54d5b00e17e23c65f", familyCount = matrix.Length, matrix }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private static Type CoveragePageType(AdminRoute route) => route switch { AdminRoute.ServerLog => typeof(AdminLogsPage), AdminRoute.ClientDiagnostics => typeof(AdminDiagnosticsPage), _ => typeof(AdminLoggingSettingsPage) };

    private sealed class ReferenceData : IDisposable
    {
        private readonly string _directory;
        private readonly JsonObject _defaults;
        private readonly ConcurrentDictionary<string, JsonObject> _overrides = new();
        private readonly object _gate = new();
        private TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private JsonObject _settings;
        public JsonObject Sidecar { get; private set; } = new();
        public bool HoldSocket { get; set; }
        public JsonObject Preset() => new(_overrides.OrderBy(pair => pair.Key).Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value.DeepClone())));
        public ReferenceData(string directory)
        {
            _directory = directory;
            _defaults = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "fixture-defaults.json")))!["responses"]!.AsObject();
            _settings = Body(SettingsPath + "/effective").DeepClone().AsObject();
            var provenance = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "source-provenance.json")))!;
            if (provenance["pinnedUpstream"]!.GetValue<string>() != ReferencePin) throw new InvalidDataException("Logs reference pin differs from required official commit.");
        }
        public void Use(string image)
        {
            Release(); HoldSocket = false; Sidecar = ReadSidecar(image); Apply(Sidecar["initialApiPreset"]?.AsObject());
            _settings = Body(SettingsPath + "/effective").DeepClone().AsObject();
        }
        private JsonObject ReadSidecar(string image) => JsonNode.Parse(File.ReadAllText(Path.Combine(_directory, Path.ChangeExtension(image, ".json"))))!.AsObject();
        private void Apply(JsonObject? preset) { if (preset != null) foreach (var pair in preset) if (pair.Value is JsonObject value) _overrides[pair.Key] = value.DeepClone().AsObject(); }
        public void ApplyCapture(string image) { var preset = ReadSidecar(image)["captureApiPreset"]?.AsObject(); lock (_gate) { _overrides.Clear(); Apply(preset); Pulse(); } }
        public Dictionary<string, string> RouteArguments()
        {
            var uri = new Uri(Sidecar["url"]!.GetValue<string>()); var arguments = new Dictionary<string, string>();
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) { var parts = pair.Split('=', 2); arguments[Uri.UnescapeDataString(parts[0])] = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : ""; }
            return arguments;
        }
        public JsonNode Body(string path) => _overrides.TryGetValue(path, out var response) && response["body"] is { } body ? body : _defaults[path] ?? new JsonObject();
        public JsonArray Items(string path) => Body(path)["items"]!.AsArray();
        public JsonArray SocketItems(string path) => Body(path)["items"] as JsonArray ?? _defaults[path]?["items"] as JsonArray ?? new JsonArray();
        public string Snapshot(string stream) => new JsonObject { ["type"] = "snapshot", ["stream"] = stream, ["entries"] = SocketItems(stream == "audit" ? "/api/v2/admin/logs/audit" : AppPath).DeepClone() }.ToJsonString();
        public void BodyOverride(string path, JsonNode body) => _overrides[path] = new JsonObject { ["body"] = body.DeepClone() };
        public void Hold(string path, string method = "GET") => _overrides[method + " " + path] = new JsonObject { ["hold"] = true };
        public void Fail(string path, int status, string method = "GET") { _overrides[method + " " + path] = new JsonObject { ["status"] = status, ["body"] = new JsonObject() }; Pulse(); }
        private void Pulse() { lock (_gate) { _release.TrySetResult(true); _release = new(TaskCreationOptions.RunContinuationsAsynchronously); } }
        public void Release() { _overrides.Clear(); Pulse(); }
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var path = request.RequestUri!.AbsolutePath; var key = request.Method.Method + " " + path;
            JsonObject? response;
            while (true)
            {
                Task release;
                lock (_gate)
                {
                    if (!_overrides.TryGetValue(key, out response)) _overrides.TryGetValue(path, out response);
                    if (response?["hold"]?.GetValue<bool>() != true) break;
                    release = _release.Task;
                }
                await release.WaitAsync(cancellation);
            }
            var status = response?["status"]?.GetValue<int>() ?? 200;
            if (request.Method == HttpMethod.Delete && status < 300 && path.StartsWith(ReportsPath + "/", StringComparison.Ordinal))
            {
                var reports = Body(ReportsPath).DeepClone().AsObject();
                reports["items"] = new JsonArray(reports["items"]!.AsArray().Where(item => item?["id"]?.GetValue<string>() != path.Split('/').Last()).Select(item => item?.DeepClone()).ToArray());
                BodyOverride(ReportsPath, reports);
            }
            JsonNode? body = response?["body"]?.DeepClone();
            if (body == null && request.Method == HttpMethod.Put)
            {
                var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!;
                if (path == SettingsPath)
                {
                    var values = payload["values"]!.DeepClone(); foreach (var pair in values.AsObject()) _settings[pair.Key] = pair.Value?.DeepClone();
                    body = new JsonObject { ["values"] = values, ["restart_required"] = false };
                }
                else body = new JsonObject { ["value"] = payload["value"]?.DeepClone() };
            }
            body ??= path == SettingsPath + "/effective" ? _settings.DeepClone() : Body(path).DeepClone();
            var result = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            result.Headers.ETag = new("\"fixture-current\""); return result;
        }
        public void Dispose() => Release();
    }
}
