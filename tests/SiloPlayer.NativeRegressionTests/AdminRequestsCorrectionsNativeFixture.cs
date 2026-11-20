using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

internal static partial class AdminRequestsManagementNativeFixture
{
    private const string CorrectionPlugin = """
        {"items":[{"id":"7","plugin_id":"synthetic-arr","capabilities":[{"id":"arr","type":"request_router.v1","display_name":"Arr","config_schema":[{"admin_form":{"fields":[
          {"key":"service_kind","label":"Service","control":"SELECT","options":[{"value":"radarr","label":"Radarr"},{"value":"sonarr","label":"Sonarr"}]},
          {"key":"quality_profile_id","label":"Quality profile","control":"SELECT","dynamic_options":true},
          {"key":"series_type","label":"Series type","control":"SELECT","options":[{"value":"standard","label":"Standard"},{"value":"anime","label":"Anime"}]},
          {"key":"timeout","label":"Timeout","control":"NUMBER","validation":{"has_min":false,"min":100,"has_max":false,"max":0}},
          {"key":"ratio","label":"Ratio","control":"NUMBER","validation":{"has_min":true,"min":0.25,"has_max":true,"max":1.75}},
          {"key":"attempts","label":"Attempts","control":"NUMBER"},
          {"key":"root_folder","label":"Root folder","control":"SELECT","dynamic_options":true},
          {"key":"tags","label":"Tags","control":"MULTI_SELECT","options":[{"value":"alpha","label":"Alpha"}]},
          {"key":"credential","label":"Credential","control":"PASSWORD"},
          {"key":"folder","label":"Required folder","control":"TEXT","required":true}
        ]},"json_schema":"{\"type\":\"object\",\"properties\":{\"timeout\":{\"type\":\"number\"},\"ratio\":{\"type\":\"number\"},\"attempts\":{\"type\":\"integer\"}}}"}]}]}],"page":{"has_more":false}}
        """;

    internal static async Task RunCorrectionsAsync(Window window)
    {
        var failed = 0;
        async Task Case(string name, Func<Server, AdminShellPage, Frame, Task> exercise, Action<Server>? configure = null)
        {
            var server = new Server { Corrections = true };
            configure?.Invoke(server);
            using var http = new HttpClient(server);
            var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
            using var auth = new AuthService(client, new AuthApi(client));
            auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "admin" });
            auth.SelectProfile("p1", null, new Profile { Id = "p1", IsPrimary = true, Name = "Primary" });
            using var authority = new AdminMonitorAuthority(client, auth);
            var navigation = new NavigationService();
            var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
                .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
            typeof(AdminShellPage).Assembly.GetType("SiloPlayer.Views.Admin.AdminRequestsRegistration")!.GetMethod("AddAdminRequests")!.Invoke(null, [collection]);
            await using var services = collection.BuildServiceProvider();
            var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
            var previousServices = serviceField.GetValue(null); var previousContent = window.Content;
            var frame = new Frame(); serviceField.SetValue(null, services);
            try
            {
                window.Content = frame; navigation.Frame = frame;
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
                Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.RequestSettings)), name + " opens the production shell");
                await Until(() => Find(frame, "AdminRequestServerEdit-integration-2") != null && Find(frame, "AdminRequestRoutingMode") != null);
                await exercise(server, (AdminShellPage)frame.Content, frame);
                Program.Log("PASS: Requests correction " + name);
            }
            catch (Exception error)
            {
                failed++; Program.Log("FAIL: Requests correction " + name + " · " + error.GetType().Name + ": " + error.Message);
            }
            finally
            {
                server.ReleasePreviewRead.TrySetResult(true);
                var shell = frame.Content as AdminShellPage;
                var page = shell == null ? null : CurrentPage(shell);
                var dialog = shell == null ? null : Dialog(shell);
                if (shell != null && dialog is { IsLoaded: true }) { dialog.Hide(); await ClosedAsync(shell, dialog); }
                var closed = page == null ? null : Field<Task>(page, "_dialogClosed");
                (page as IAdminPageSuspension)?.SuspendAdminPage();
                frame.Navigate(typeof(Page));
                if (closed != null) await closed;
                if (page != null) await Until(() => !page.IsLoaded);
                window.Content = previousContent;
                await Until(() => !frame.IsLoaded);
                serviceField.SetValue(null, previousServices);
            }
        }
        await Case("probe preserves live plugin draft and controls", async (server, shell, frame) =>
        {
            var dialog = await OpenServer(shell, frame);
            var number = (TextBox)Find(dialog, "AdminRequestPlugin-timeout")!; number.Text = "7";
            ((TextBox)Find(dialog, "AdminRequestPlugin-tagsId")!).Text = "alpha";
            Click(Find(dialog, "AdminRequestPlugin-tagsAdd")!);
            var secret = Walk(dialog).OfType<PasswordBox>().Single(input => AutomationProperties.GetAutomationId(input) != "AdminRequestServerApiKey");
            secret.Password = "synthetic-plugin-value";
            var profile = Find(dialog, "AdminRequestPlugin-quality_profile_id");
            Click(Find(dialog, "AdminRequestServerTest")!);
            await Until(() => Walk(dialog).OfType<TextBlock>().Any(text => text.Text.StartsWith("Connected")));
            var config = JsonNode.Parse(server.LastProbe!)!["plugin_config"]!;
            Check(config["timeout"]?.ToString() == "7" && config["tags"]?.ToJsonString() == "[\"alpha\"]" && config["credential"]?.ToString() == "synthetic-plugin-value", "probe sends staged number, tags and secret without requiring unrelated fields");
            Check(ReferenceEquals(number, Find(dialog, "AdminRequestPlugin-timeout")) && number.Text == "7" && secret.IsLoaded && secret.Password == "synthetic-plugin-value" && ReferenceEquals(profile, Find(dialog, "AdminRequestPlugin-quality_profile_id")), "successful probe retains mounted dirty controls and the credential editor");
            var choices = ((ComboBox)profile!).Items.OfType<ComboBoxItem>();
            Check(choices.Any(item => item.Tag?.ToString() == "1" && item.Content?.ToString() == "Synthetic HD"), "probe updates dynamic choices in the retained selector");
            secret.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.8 });
            await Until(() => Within(secret, dialog));
            await CaptureAsync(dialog, "admin-requests-live-plugin-draft.png");
            dialog.Hide(); await ClosedAsync(shell, dialog);
            Check(secret.Password.Length == 0, "retained plugin credential is scrubbed on close");
        });
        await Case("disabled numeric bounds permit saving", async (server, shell, frame) =>
        {
            var dialog = await OpenServer(shell, frame);
            ((TextBox)Find(dialog, "AdminRequestPlugin-timeout")!).Text = "7";
            ((TextBox)Find(dialog, "AdminRequestPlugin-folder")!).Text = "synthetic";
            ClickPrimary(dialog);
            await Settled(() => server.LastWrite != null || Walk(dialog).OfType<TextBlock>().Any(text => text.Text.Contains("enter a whole number")));
            Check(server.LastWrite != null && JsonNode.Parse(server.LastWrite)!["plugin_config"]!["timeout"]?.ToString() == "7", "has_min=false and has_max=false do not enforce the serialized bounds");
            await ClosedAsync(shell, dialog);
        });
        await Case("blank plugin password preserves the saved credential", async (server, shell, frame) =>
        {
            var dialog = await OpenServer(shell, frame);
            var secret = (PasswordBox)Find(dialog, "AdminRequestPlugin-credential")!;
            Check(secret.Password.Length == 0 && !Walk(dialog).OfType<TextBlock>().Any(text => text.Text.Contains("synthetic-saved-plugin-credential")), "saved plugin credentials never populate visible controls");
            ((TextBox)Find(dialog, "AdminRequestServerName")!).Text = "Renamed synthetic server";
            ((TextBox)Find(dialog, "AdminRequestPlugin-folder")!).Text = "synthetic";
            Click(Find(dialog, "AdminRequestServerTest")!);
            await Until(() => server.LastProbe != null);
            Check(JsonNode.Parse(server.LastProbe!)!["plugin_config"]!["credential"]?.ToString() == "synthetic-saved-plugin-credential", "a blank password preserves the unchanged connection credential during Test");
            await Until(() => Walk(dialog).OfType<TextBlock>().Any(text => text.Text.StartsWith("Connected")));
            ClickPrimary(dialog);
            await Until(() => server.LastWrite != null);
            Check(JsonNode.Parse(server.LastWrite!)!["plugin_config"]!["credential"]?.ToString() == "synthetic-saved-plugin-credential", "an unrelated Save retains the saved plugin credential");
            await ClosedAsync(shell, dialog);
            dialog = await OpenServer(shell, frame);
            secret = (PasswordBox)Find(dialog, "AdminRequestPlugin-credential")!;
            ((TextBox)Find(dialog, "AdminRequestServerUrl")!).Text = "http://replacement.test:7878";
            server.LastProbe = null; Click(Find(dialog, "AdminRequestServerTest")!);
            await Until(() => server.LastProbe != null && Find(dialog, "AdminRequestServerTest") is Button { IsEnabled: true });
            Check(JsonNode.Parse(server.LastProbe!)!["plugin_config"]!["credential"] == null, "a changed connection never inherits an untouched saved plugin credential");
            secret.Password = "synthetic-replacement-credential";
            server.LastProbe = null; Click(Find(dialog, "AdminRequestServerTest")!);
            await Until(() => server.LastProbe != null && Find(dialog, "AdminRequestServerTest") is Button { IsEnabled: true });
            Check(JsonNode.Parse(server.LastProbe!)!["plugin_config"]!["credential"]?.ToString() == "synthetic-replacement-credential", "an explicitly entered replacement credential belongs to the edited connection");
            dialog.Hide(); await ClosedAsync(shell, dialog);
            Check(secret.Password.Length == 0, "connection-editor teardown scrubs the newly entered plugin credential");
        }, server => server.StoredPluginSecret = true);
        await Case("fractional plugin values survive Test and Save", async (server, shell, frame) =>
        {
            var dialog = await OpenServer(shell, frame);
            ((TextBox)Find(dialog, "AdminRequestPlugin-folder")!).Text = "synthetic";
            Click(Find(dialog, "AdminRequestServerTest")!);
            await Settled(() => server.LastProbe != null || Walk(dialog).OfType<TextBlock>().Any(text => text.Text.Contains("whole number")));
            Check(server.LastProbe != null && JsonNode.Parse(server.LastProbe)!["plugin_config"]!["timeout"]?.GetValue<double>() == 0.5, "schema number accepts a stored fractional value during Test");
            await Until(() => Walk(dialog).OfType<TextBlock>().Any(text => text.Text.StartsWith("Connected")));
            ClickPrimary(dialog);
            await Settled(() => server.LastWrite != null || Walk(dialog).OfType<TextBlock>().Any(text => text.Text.Contains("whole number")));
            Check(server.LastWrite != null && JsonNode.Parse(server.LastWrite)!["plugin_config"]!["ratio"]?.GetValue<double>() == 0.5, "fractional plugin values remain numbers during Save");
            await ClosedAsync(shell, dialog);
        }, server => server.FractionalNumbers = true);
        await Case("fractional plugin bounds and integer schema remain distinct", async (server, shell, frame) =>
        {
            var dialog = await OpenServer(shell, frame);
            ((TextBox)Find(dialog, "AdminRequestPlugin-folder")!).Text = "synthetic";
            var ratio = (TextBox)Find(dialog, "AdminRequestPlugin-ratio")!;
            ratio.Text = "2"; ClickPrimary(dialog);
            await Settled(() => server.LastWrite != null || Walk(dialog).OfType<TextBlock>().Any(text => text.Text.Contains("Ratio:")));
            Check(server.LastWrite == null, "a floating-point maximum of 1.75 rejects 2");
            ratio.Text = "0"; ClickPrimary(dialog);
            await Settled(() => server.LastWrite != null || Walk(dialog).OfType<TextBlock>().Any(text => text.Text.Contains("Ratio:")));
            Check(server.LastWrite == null, "a floating-point minimum of 0.25 rejects 0");
            ratio.Text = "0.25";
            var attempts = (TextBox)Find(dialog, "AdminRequestPlugin-attempts")!; attempts.Text = "1.5";
            ClickPrimary(dialog);
            await Settled(() => server.LastWrite != null || Walk(dialog).OfType<TextBlock>().Any(text => text.Text.Contains("Attempts:")));
            Check(server.LastWrite == null, "a declared integer field rejects fractions");
            attempts.Text = "2"; ClickPrimary(dialog);
            await Settled(() => server.LastWrite != null);
            Check(server.LastWrite != null && JsonNode.Parse(server.LastWrite)!["plugin_config"]!["ratio"]?.GetValue<double>() == 0.25 && JsonNode.Parse(server.LastWrite)!["plugin_config"]!["attempts"]?.GetValue<int>() == 2, "inclusive fractional bounds and integer serialization survive Save");
            await ClosedAsync(shell, dialog);
        });
        await Case("hidden plugin passwords do not cross connection identity", async (server, shell, frame) =>
        {
            var dialog = await OpenServer(shell, frame);
            var secret = (PasswordBox)Find(dialog, "AdminRequestPlugin-credential")!;
            Click(Find(dialog, "AdminRequestServerTest")!);
            await Until(() => server.LastProbe != null && Find(dialog, "AdminRequestServerTest") is Button { IsEnabled: true });
            Check(JsonNode.Parse(server.LastProbe!)!["plugin_config"]!["credential"]?.ToString() == "synthetic-saved-plugin-credential", "unchanged visible conditional credential is preserved during Test");
            ((ToggleSwitch)Find(dialog, "AdminRequestPlugin-use_credential")!).IsOn = false;
            Check(secret.Parent is FrameworkElement { Visibility: Visibility.Collapsed }, "the conditional credential field is genuinely hidden");
            ((TextBox)Find(dialog, "AdminRequestServerUrl")!).Text = "http://replacement.test:7878";
            server.LastProbe = null;
            Click(Find(dialog, "AdminRequestServerTest")!);
            await Until(() => server.LastProbe != null && Find(dialog, "AdminRequestServerTest") is Button { IsEnabled: true });
            Check(JsonNode.Parse(server.LastProbe!)!["plugin_config"]!["credential"] == null, "a hidden saved credential is absent from the replacement connection Test");
            ((TextBox)Find(dialog, "AdminRequestPlugin-folder")!).Text = "synthetic";
            ClickPrimary(dialog); await Until(() => server.LastWrite != null);
            Check(JsonNode.Parse(server.LastWrite!)!["plugin_config"]!["credential"] == null, "a hidden saved credential is absent from the replacement connection Save");
            await ClosedAsync(shell, dialog);
            Check(secret.Password.Length == 0, "conditional credential controls scrub on close");
        }, server => { server.StoredPluginSecret = true; server.ConditionalPluginSecret = true; });
        foreach (var holdOptions in new[] { false, true })
        {
            await Case(holdOptions ? "latest title owns delayed metadata preview" : "latest title owns delayed route preview", async (server, shell, frame) =>
            {
                ((TextBox)Find(frame, "AdminRequestPreviewSearch")!).Text = "Synthetic";
                Click(Find(frame, "AdminRequestPreviewSearchGo")!);
                await Until(() => Find(frame, "AdminRequestPreviewTitle-456") != null);
                var earlier = (Button)Find(frame, "AdminRequestPreviewTitle-123")!;
                Click(earlier); await Until(() => server.PreviewReadStarted.Task.IsCompleted);
                Click(Find(frame, "AdminRequestPreviewTitle-456")!);
                await Until(() => Walk(frame).OfType<TextBlock>().Any(text => text.Text.Contains("Latest selection")));
                server.ReleasePreviewRead.TrySetResult(true);
                await Until(() => earlier.IsEnabled);
                Check(Walk(frame).OfType<TextBlock>().Any(text => text.Text.Contains("Latest selection")) && !Walk(frame).OfType<TextBlock>().Any(text => text.Text.Contains("Earlier selection")), "the earlier selection cannot overwrite the latest title after its held read settles");
                Check(server.Calls.Count(call => call == "POST /api/v2/admin/request-routes/preview") == 2, "each title selection sends exactly one preview");
            }, server => { server.OverlapPreviews = true; server.HoldPreviewOptions = holdOptions; });
        }
        await Case("closed unknown create retains canonical review barrier", async (server, shell, frame) =>
        {
            Click(Find(frame, "AdminRequestServerAdd")!);
            await Until(() => Dialog(shell) is { IsLoaded: true }); var dialog = Dialog(shell)!;
            ((TextBox)Find(dialog, "AdminRequestServerName")!).Text = "Synthetic added server";
            ((TextBox)Find(dialog, "AdminRequestServerUrl")!).Text = "http://added.test:7878";
            ((PasswordBox)Find(dialog, "AdminRequestServerApiKey")!).Password = "synthetic-added-key";
            Choose((ComboBox)Find(dialog, "AdminRequestPlugin-service_kind")!, "radarr");
            ((TextBox)Find(dialog, "AdminRequestPlugin-folder")!).Text = "synthetic";
            ClickPrimary(dialog);
            await Until(() => server.Calls.Contains("POST /api/v2/admin/request-integrations") && Find(dialog, "AdminRequestEditorReload") is Button { Visibility: Visibility.Visible });
            dialog.Hide(); await ClosedAsync(shell, dialog);
            await Until(() => server.FailedConfigurationReads > 0);
            await Settled(() => Field<Task>(CurrentPage(shell), "_dialogClosed")?.IsCompleted == true);
            Click(Find(frame, "AdminRequestServerAdd")!); await Task.Delay(100);
            Check(Dialog(shell) == null && Find(frame, "AdminRequestsWriteReview") is Button { Visibility: Visibility.Visible }, "closing an unknown create with failed canonical reads keeps every new editor gated and exposes recovery");
            Check(server.Calls.Count(call => call == "POST /api/v2/admin/request-integrations") == 1, "closing and retrying never replays the uncertain create");
            server.FailConfigurationReads = false;
            Click(Find(frame, "AdminRequestsWriteReview")!);
            await Until(() => Find(frame, "AdminRequestsWriteReview") is Button { Visibility: Visibility.Collapsed });
            Click(Find(frame, "AdminRequestServerAdd")!);
            await Until(() => Dialog(shell) is { IsLoaded: true });
            Check(server.Calls.Count(call => call == "POST /api/v2/admin/request-integrations") == 1, "canonical recovery allows a new editor without an automatic mutation");
        }, server => server.LoseCreate = true);
        await Case("superseded failed preview stays silent", async (server, shell, frame) =>
        {
            ((TextBox)Find(frame, "AdminRequestPreviewSearch")!).Text = "Synthetic";
            Click(Find(frame, "AdminRequestPreviewSearchGo")!);
            await Until(() => Find(frame, "AdminRequestPreviewTitle-456") != null);
            var earlier = (Button)Find(frame, "AdminRequestPreviewTitle-123")!;
            Click(earlier); await Until(() => server.PreviewReadStarted.Task.IsCompleted);
            Click(Find(frame, "AdminRequestPreviewTitle-456")!);
            await Until(() => Walk(frame).OfType<TextBlock>().Any(text => text.Text.Contains("Latest selection")));
            server.ReleasePreviewRead.TrySetResult(true);
            await Until(() => earlier.IsEnabled);
            await Task.Delay(100);
            Check(Find(frame, "AdminRequestsNotice") is TextBlock { Visibility: Visibility.Collapsed } && Walk(frame).OfType<TextBlock>().Any(text => text.Text.Contains("Latest selection")), "a superseded title's late failure neither raises a notice nor removes the latest preview");
        }, server => { server.OverlapPreviews = true; server.FailEarlierPreview = true; });
        await Case("accepted create without a usable identity stays unconfirmed", async (server, shell, frame) =>
        {
            Click(Find(frame, "AdminRequestServerAdd")!);
            await Until(() => Dialog(shell) is { IsLoaded: true }); var dialog = Dialog(shell)!;
            ((TextBox)Find(dialog, "AdminRequestServerName")!).Text = "Synthetic added server";
            ((TextBox)Find(dialog, "AdminRequestServerUrl")!).Text = "http://added.test:7878";
            ((PasswordBox)Find(dialog, "AdminRequestServerApiKey")!).Password = "synthetic-added-key";
            Choose((ComboBox)Find(dialog, "AdminRequestPlugin-service_kind")!, "radarr");
            ((TextBox)Find(dialog, "AdminRequestPlugin-folder")!).Text = "synthetic";
            ClickPrimary(dialog);
            await Until(() => Walk(dialog).OfType<TextBlock>().Any(text => text.Text.Contains("was not confirmed") || text.Text.Contains("Invalid record identity")));
            Check(!dialog.IsPrimaryButtonEnabled && Field<Delegate>(CurrentPage(shell), "_pendingWriteReview") != null, "an accepted create without a usable identity cannot be saved again before canonical review");
            Check(server.Calls.Count(call => call == "POST /api/v2/admin/request-integrations") == 1, "the accepted create was sent exactly once");
            dialog.Hide(); await ClosedAsync(shell, dialog);
        }, server => server.CreateWithoutIdentity = true);
        await Case("routing previews name effective overrides on both surfaces", async (server, shell, frame) =>
        {
            ((TextBox)Find(frame, "AdminRequestPreviewSearch")!).Text = "Synthetic";
            Click(Find(frame, "AdminRequestPreviewSearchGo")!);
            await Until(() => Find(frame, "AdminRequestPreviewTitle-123") != null);
            Click(Find(frame, "AdminRequestPreviewTitle-123")!);
            await Until(() => server.Calls.Contains("POST /api/v2/admin/request-routes/preview"));
            await Settled(() => Walk(frame).OfType<TextBlock>().Any(text => text.Text.Contains("Synthetic HD")));
            Check(PreviewHasOverrides(frame), "Settings preview resolves root folder, quality profile and series-type labels");
            var effectiveQuality = Walk(frame).OfType<TextBlock>().Single(text => text.Text == "Quality: Synthetic HD");
            effectiveQuality.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.6 });
            await Until(() => Within(effectiveQuality, frame));
            await CaptureAsync(frame, "admin-requests-routing-effective-overrides.png");
            shell.Navigate(new AdminRouteRequest(AdminRoute.RequestQueue));
            await Until(() => Find(frame, "AdminRequestDetails-7") != null);
            Click(Find(frame, "AdminRequestDetails-7")!);
            await Until(() => Dialog(shell) is { IsLoaded: true }); var dialog = Dialog(shell)!;
            Click(Find(dialog, "AdminRequestDetailsPreview")!);
            await Settled(() => PreviewHasOverrides(dialog));
            Check(PreviewHasOverrides(dialog), "Queue Details preview names the same effective destination settings");
            await CaptureAsync(dialog, "admin-requests-details-effective-overrides.png");
        }, server => server.PreviewOverrides = true);
        await Case("failed reorder reconciliation blocks another write until review", async (server, shell, frame) =>
        {
            Choose((ComboBox)Find(frame, "AdminRequestRoutingMode")!, "advanced");
            await Until(() => Find(frame, "AdminRequestRuleDown-rule-a") != null);
            server.LoseReorder = true;
            Click(Find(frame, "AdminRequestRuleDown-rule-a")!);
            await Until(() => server.FailedConfigurationReads > 0);
            await Task.Delay(100);
            Click(Find(frame, "AdminRequestRuleUp-rule-b")!);
            await Task.Delay(100);
            Check(server.Calls.Count(call => call == "POST /api/v2/admin/request-routes/order") == 1, "failed canonical reconciliation does not authorize another reorder POST");
            Check(Find(frame, "AdminRequestsWriteReview") is Button { Visibility: Visibility.Visible }, "unresolved writes expose a reachable read-only recovery action");
            server.FailConfigurationReads = false;
            Click(Find(frame, "AdminRequestsWriteReview")!);
            await Until(() => Find(frame, "AdminRequestsWriteReview") is Button { Visibility: Visibility.Collapsed });
            Click(Find(frame, "AdminRequestRuleUp-rule-b")!);
            await Until(() => server.Calls.Count(call => call == "POST /api/v2/admin/request-routes/order") == 2);
            Check(server.Calls.Count(call => call == "POST /api/v2/admin/request-routes/order") == 2, "successful canonical review permits one fresh explicit reorder");
        }, server => server.RulesForReorder = true);
        await Case("anime destination starts with the explicit override", async (server, shell, frame) =>
        {
            Choose((ComboBox)Find(frame, "AdminRequestRoutingMode")!, "advanced");
            await Until(() => Find(frame, "AdminRequestRoutingMedia") != null);
            Choose((ComboBox)Find(frame, "AdminRequestRoutingMedia")!, "series");
            Click(Find(frame, "AdminRequestRuleAdd")!);
            await Until(() => Dialog(shell) is { IsLoaded: true }); var preset = Dialog(shell)!;
            ClickPrimary(preset);
            await Until(() => Dialog(shell) is { IsLoaded: true } next && !ReferenceEquals(next, preset) && Find(next, "AdminRequestDestination-hd") != null);
            var dialog = Dialog(shell)!;
            Choose((ComboBox)Find(dialog, "AdminRequestDestination-hd")!, "integration-2");
            var series = Walk(dialog).OfType<ComboBox>().Single(input => AutomationProperties.GetAutomationId(input) == "AdminRequestPlugin-series_type" && input.IsLoaded);
            Check((series.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "anime", "Anime preset is seeded before the Sonarr override selector is constructed");
            Choose(series, "standard");
            Click(Find(dialog, "AdminRequestDestinationOptions-hd")!);
            await Until(() => server.Calls.Any(call => call == "POST /api/v2/admin/request-integrations/integration-2/options"));
            await Until(() => Find(dialog, "AdminRequestDestinationOptions-hd") is Button { IsEnabled: true });
            var refreshed = Walk(dialog).OfType<ComboBox>().Single(input => AutomationProperties.GetAutomationId(input) == "AdminRequestPlugin-series_type" && input.IsLoaded);
            Check((refreshed.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "standard", "option loading does not reapply a preset over an explicit override");
        });
        await Case("media switch replaces both genre pickers", async (server, shell, frame) =>
        {
            shell.Navigate(new AdminRouteRequest(AdminRoute.RequestDiscover));
            await Until(() => Find(frame, "AdminDiscoverAdd") != null);
            Click(Find(frame, "AdminDiscoverAdd")!);
            await Until(() => Dialog(shell) is { IsLoaded: true }); var kind = Dialog(shell)!;
            Choose((ComboBox)Find(kind, "AdminDiscoverKind")!, "tmdb_discover"); ClickPrimary(kind);
            await Until(() => Dialog(shell) is { IsLoaded: true } next && !ReferenceEquals(next, kind) && Find(next, "AdminDiscoverFilterMedia") != null);
            var dialog = Dialog(shell)!;
            Choose((ComboBox)Find(dialog, "AdminDiscoverGenresPick")!, "28");
            Choose((ComboBox)Find(dialog, "AdminDiscoverExcludeGenresPick")!, "12");
            Choose((ComboBox)Find(dialog, "AdminDiscoverFilterMedia")!, "series");
            Check(Find(dialog, "AdminDiscoverGenresRemove-28") == null && Find(dialog, "AdminDiscoverExcludeGenresRemove-12") == null, "media switch clears incompatible include and exclude genres");
            Check(((ComboBox)Find(dialog, "AdminDiscoverGenresPick")!).Items.OfType<ComboBoxItem>().Any(item => item.Tag?.ToString() == "10759") && !((ComboBox)Find(dialog, "AdminDiscoverExcludeGenresPick")!).Items.OfType<ComboBoxItem>().Any(item => item.Tag?.ToString() == "12"), "both genre choice lists use the new media taxonomy");
            ((TextBox)Find(dialog, "AdminDiscoverName")!).Text = "Synthetic series filter";
            ClickPrimary(dialog); await ClosedAsync(shell, dialog);
            var filter = JsonNode.Parse(server.LastWrite!)!["rows"]!.AsArray().Last()!["discover"]!;
            Check(filter["genre_ids"] == null && filter["exclude_genre_ids"] == null, "saved series query contains no stale movie genre IDs");
        });
        await Case("missing fallback gates Add rule", async (server, shell, frame) =>
        {
            server.MissingSeriesFallback = true;
            Choose((ComboBox)Find(frame, "AdminRequestRoutingMode")!, "advanced");
            await Until(() => Find(frame, "AdminRequestRoutingMedia") != null);
            Choose((ComboBox)Find(frame, "AdminRequestRoutingMedia")!, "series");
            Check(Find(frame, "AdminRequestRuleAdd") is Button { IsEnabled: false }, "Add rule is disabled until Everything else has an HD server");
            Check(Walk(frame).OfType<TextBlock>().Any(text => text.Text.Contains("Everything else") && text.Text.Contains("HD")), "missing fallback presents the actionable prerequisite");
        });
        await Case("active downloading rows poll and retire", async (server, shell, frame) =>
        {
            server.Downloading = true;
            shell.Navigate(new AdminRouteRequest(AdminRoute.RequestQueue));
            await Until(() => Find(frame, "AdminRequestView-in_progress") != null);
            Click(Find(frame, "AdminRequestView-in_progress")!);
            await Until(() => Walk(frame).OfType<TextBlock>().Any(text => text.Text == "Synthetic Download"));
            var reads = server.QueueReads;
            await Settled(() => server.QueueReads > reads, 1400);
            Check(server.QueueReads > reads, "downloading queue refreshes within the official thirty-second cadence");
            shell.Navigate(new AdminRouteRequest(AdminRoute.RequestDiscover));
            await Until(() => Find(frame, "AdminDiscoverAdd") != null);
            reads = server.QueueReads; await Task.Delay(1200);
            Check(server.QueueReads == reads, "queue polling stops after actual Frame departure");
        });
        foreach (var progressOnTarget in new[] { false, true })
            await Case(progressOnTarget ? "queued target progress polls and retires" : "queued request progress polls and retires", async (server, shell, frame) =>
            {
                shell.Navigate(new AdminRouteRequest(AdminRoute.RequestQueue));
                await Until(() => Find(frame, "AdminRequestView-in_progress") != null);
                Click(Find(frame, "AdminRequestView-in_progress")!);
                await Until(() => Walk(frame).OfType<TextBlock>().Any(text => text.Text == "Synthetic Download"));
                Check(Field<Microsoft.UI.Dispatching.DispatcherQueueTimer>(CurrentPage(shell), "_queueTimer") is { IsRunning: true }, "queued download presence schedules polling without aggregate downloading status");
                var reads = server.QueueReads;
                await Settled(() => server.QueueReads > reads, 1400);
                Check(server.QueueReads > reads, "queued progress performs a real owner-scoped thirty-second refresh");
                shell.Navigate(new AdminRouteRequest(AdminRoute.RequestDiscover));
                await Until(() => Find(frame, "AdminDiscoverAdd") != null);
                reads = server.QueueReads; await Task.Delay(300);
                Check(server.QueueReads == reads, "queued-progress polling retires on Frame departure");
            }, server => { server.QueuedProgress = true; server.ProgressOnTarget = progressOnTarget; });
        await Case("retired action capture is a silent abort", async (server, shell, frame) =>
        {
            var page = CurrentPage(shell);
            var owner = (MonitorAuthorityLease)page.GetType().GetProperty("Owner", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            owner.Dispose(); var invoked = false;
            Func<MonitorAuthorityLease, Task> operation = _ => { invoked = true; return Task.CompletedTask; };
            var method = page.GetType().GetMethod("WriteAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var task = (Task)method.Invoke(page, [operation, null])!;
            await task;
            Check(!invoked, "retired ownership between event admission and capture aborts without a fault or write");
        });
        if (failed > 0) throw new InvalidOperationException(failed + " Requests correction scenarios failed.");
    }

    private static async Task<ContentDialog> OpenServer(AdminShellPage shell, Frame frame)
    {
        Click(Find(frame, "AdminRequestServerEdit-integration-1")!);
        await Until(() => Dialog(shell) is { IsLoaded: true }); return Dialog(shell)!;
    }
    private static bool PreviewHasOverrides(DependencyObject root)
    {
        var text = string.Join("\n", Walk(root).OfType<TextBlock>().Select(block => block.Text));
        return text.Contains("/media/movies") && text.Contains("Synthetic HD") && text.Contains("Anime");
    }
    private static bool Within(FrameworkElement element, FrameworkElement viewport)
    {
        var point = element.TransformToVisual(viewport).TransformPoint(new Windows.Foundation.Point());
        return point.Y >= 0 && point.Y + element.ActualHeight <= viewport.ActualHeight;
    }
    private static void Choose(ComboBox select, string value) => select.SelectedItem = select.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == value);
    private static async Task Settled(Func<bool> ready, int attempts = 80)
    {
        for (var attempt = 0; attempt < attempts && !ready(); attempt++) await Task.Delay(25);
    }
}

