using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class ShellNotificationLanguageTests
{
    [Theory]
    [InlineData("read", "web.notifications.queries.markReadFailed", false)]
    [InlineData("read-all", "web.notifications.queries.markAllReadFailed", false)]
    [InlineData("preferences", "web.notifications.queries.savePreferencesFailed", false)]
    [InlineData("read", "web.notifications.queries.markReadFailed", true)]
    [InlineData("read-all", "web.notifications.queries.markAllReadFailed", true)]
    [InlineData("preferences", "web.notifications.queries.savePreferencesFailed", true)]
    public async Task MutationFailuresRetainSummaryAndRenderCurrentLocaleDetail(string action, string key, bool typedProblem)
    {
        using var http = new HttpClient(new Wire(typedProblem));
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://shell-notification.invalid");
        client.SetAccessToken("synthetic-access"); client.SetProfile("owner");
        var owner = client.CaptureContext();
        var model = new NotificationsViewModel(new NotificationsApi(client));
        using (AppStrings.UseForCurrentFlow(LocaleService.Shared.Create(AppLocaleResolution.Resolve("de", ["en-US"], "en-US"))))
        {
            await model.LoadCommand.ExecuteAsync(null);
            Assert.Single(model.Notifications);
            if (action == "read") await model.MarkReadCommand.ExecuteAsync(model.Notifications[0]);
            else if (action == "read-all") await model.MarkAllReadCommand.ExecuteAsync(null);
            else await model.SavePreferencesCommand.ExecuteAsync(null);
            var detail = typedProblem ? AppStrings.Get("error.not_found") : "Raw server detail 日本語";
            Assert.Equal(AppStrings.Get(key) + ": " + detail, action == "preferences" ? model.PreferencesErrorMessage : model.ErrorMessage);
        }
        using (AppStrings.UseForCurrentFlow(LocaleService.Shared.Create(AppLocaleResolution.Resolve("ar", ["en-US"], "en-US"))))
        {
            model.RefreshLocalizedText();
            var detail = typedProblem ? AppStrings.Get("error.not_found") : "Raw server detail 日本語";
            Assert.Equal(AppStrings.Get(key) + ": " + detail, action == "preferences" ? model.PreferencesErrorMessage : model.ErrorMessage);
        }
        Assert.Equal("Raw Series العربية", Assert.Single(model.Notifications).SeriesTitle);
        Assert.Equal(owner, client.CaptureContext());
    }

    private sealed class Wire(bool typedProblem) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.StartsWith("/api/v2/notifications", request.RequestUri!.AbsolutePath);
            if (request.Method != HttpMethod.Get)
            {
                if (!typedProblem) throw new HttpRequestException("Raw server detail 日本語");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"type":"https://silo.invalid/problems/not_found","title":"Not found","status":404,"detail":"Raw server detail 日本語"}""", System.Text.Encoding.UTF8, "application/problem+json")
                });
            }
            var json = request.RequestUri.AbsolutePath == "/api/v2/notifications"
                ? """{"items":[{"id":"row","type":"episode.available","series_title":"Raw Series العربية","created_at":"2026-11-12T12:00:00Z"}],"read_cutoff":"fixture-cutoff"}"""
                : """{"count":1}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
