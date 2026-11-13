using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

/// <summary>
/// Collections view-model messages are worded in the interface language at the moment they are produced, from explicit
/// catalog keys; requests keep their wire values and a server problem is shown through its Problem Details type.
/// </summary>
public sealed class CollectionsLocalizationTests
{
    private static AppLocale Locale(string language) => new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US"));

    [Fact]
    public async Task SmartRuleRequirementIsWordedInTheInterfaceLanguageWithoutARequest()
    {
        foreach (var language in new[] { "de", "en" })
        {
            using var wire = new Wire(); using var http = new HttpClient(wire);
            var client = new SiloApiClient(http); client.SetBaseUrl("https://collections-localization.invalid");
            using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine", profile: new() { Id = "mine", IsPrimary = true });
            var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth)
            { CollectionType = "smart", Name = "Weekend picks" };
            using (AppStrings.UseForCurrentFlow(Locale(language)))
                await vm.SaveCommand.ExecuteAsync(null);
            var expected = Locale(language).Get("web.collections.copy.addAtLeastOneRule");
            Assert.Equal(expected, vm.ErrorMessage);
            if (language == "en") Assert.Equal(CollectionEditorViewModel.SmartRuleNeededMessage, vm.ErrorMessage);
            else Assert.NotEqual(CollectionEditorViewModel.SmartRuleNeededMessage, vm.ErrorMessage);
            Assert.Empty(wire.Requests);
            Assert.Equal("Weekend picks", vm.Name);
        }
    }

    [Fact]
    public async Task SharingFailureUsesTheCatalogPrefixAndProblemTypeWhileTheRequestStaysOnTheWire()
    {
        var messages = new Dictionary<string, string>();
        foreach (var language in new[] { "de", "en" })
        {
            using var wire = new Wire { RejectPatch = true }; using var http = new HttpClient(wire);
            var client = new SiloApiClient(http); client.SetBaseUrl("https://collections-localization.invalid");
            using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine", profile: new() { Id = "mine", IsPrimary = true });
            var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client), new AuthApi(client), client);
            var collection = new Collection { Id = "c1", Name = "Noches de cine — ليالي السينما", CreatorProfileId = "mine", ProfileId = "mine", CollectionType = "manual" };
            bool changed;
            using (AppStrings.UseForCurrentFlow(Locale(language)))
                changed = await vm.SetSharedAsync(collection, true);
            Assert.False(changed);
            Assert.False(collection.IsShared);
            Assert.Equal("Noches de cine — ليالي السينما", collection.Name);
            var patch = Assert.Single(wire.Requests, request => request.StartsWith("PATCH ", StringComparison.Ordinal));
            using (var body = JsonDocument.Parse(patch[(patch.IndexOf(' ', 6) + 1)..])) Assert.True(body.RootElement.GetProperty("is_shared").GetBoolean());
            messages[language] = vm.ErrorMessage!;
        }
        var german = Locale("de");
        Assert.Equal(german.Get("web.collections.toast.sharingFailed") + ": " + german.Get("error.conflict"), messages["de"]);
        // English shows the server's own detail exactly as sent.
        Assert.Equal(Locale("en").Get("web.collections.toast.sharingFailed") + ": Sharing changed on another device.", messages["en"]);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public bool RejectPatch;
        public readonly List<string> Requests = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (Requests) Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath} {body}");
            if (request.Method == HttpMethod.Patch && RejectPatch)
                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { type = "https://collections-localization.invalid/docs/api/v2/problems/conflict", title = "Conflict", status = 409, detail = "Sharing changed on another device." }),
                        System.Text.Encoding.UTF8, "application/problem+json"),
                };
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { id = "c1", name = "Noches de cine — ليالي السينما", creator_profile_id = "mine", collection_type = "manual", is_shared = false, item_count = 0 }),
                    System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"rev-1\"");
            return response;
        }
    }
}
