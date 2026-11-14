using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class ShellProfileLanguageTests
{
    [Fact]
    public async Task LockoutAndIncorrectPinRefreshWithoutTranslatingTheOwnerOrKeepingStaleClassification()
    {
        using var http = new HttpClient(new PinWire());
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://shell-pin.invalid"); client.BeginAuthenticationSession("synthetic-access");
        var api = new AuthApi(client);
        using var auth = new AuthService(client, api);
        auth.SelectProfile("viewer");
        var local = new SettingsService(Path.Combine(Path.GetTempPath(), "ShellProfileLanguageTests", Guid.NewGuid().ToString("N")));
        var model = new ProfileSelectViewModel(api, new(client), new(client), auth, local);
        var profile = new Profile { Id = "protected", Name = "My Profile العربية", HasPin = true };
        model.Profiles.Add(profile);
        await model.SelectProfileCommand.ExecuteAsync(profile);
        using (AppStrings.UseForCurrentFlow(LocaleService.Shared.Create(AppLocaleResolution.Resolve("de", ["en-US"], "en-US"))))
        {
            model.Pin = "1234";
            await model.VerifyPinCommand.ExecuteAsync(null);
            Assert.True(model.IsPinLockedOut);
            Assert.Equal(AppStrings.Get("web.profiles.pin.lockedMinutes", new Dictionary<string, object?> { ["count"] = 2, ["count_text"] = "2" }), model.PinErrorMessage);
        }
        using (AppStrings.UseForCurrentFlow(LocaleService.Shared.Create(AppLocaleResolution.Resolve("ar", ["en-US"], "en-US"))))
        {
            model.RefreshLocalizedText();
            Assert.Equal(AppStrings.Get("web.profiles.pin.lockedMinutes", new Dictionary<string, object?> { ["count"] = 2, ["count_text"] = "2" }), model.PinErrorMessage);
            Assert.Equal(AppStrings.Get("web.profiles.picker.pinProtected", new Dictionary<string, object?> { ["name"] = profile.Name }), model.PickerCards[0].AccessibleName);
            model.Pin = "1234";
            await model.VerifyPinCommand.ExecuteAsync(null);
            Assert.False(model.IsPinLockedOut);
            Assert.Equal(AppStrings.Get("web.profiles.pin.incorrect"), model.PinErrorMessage);
        }
        Assert.Equal("My Profile العربية", profile.Name);
        Assert.Equal("viewer", auth.SelectedProfileId);
        Assert.Equal("viewer", client.ProfileId);
    }

    private sealed class PinWire : HttpMessageHandler
    {
        private int _attempts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/v2/profiles/protected/verify-pin", request.RequestUri!.AbsolutePath);
            if (++_attempts > 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"valid":false}""") });
            var refusal = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("""{"type":"https://silo.invalid/docs/api/v2/problems/rate_limited","detail":"Raw English retry detail"}""") };
            refusal.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return Task.FromResult(refusal);
        }
    }
}
