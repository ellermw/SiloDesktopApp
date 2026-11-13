using System.Collections.ObjectModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class LocalizationCatalogTests
{
    private static readonly string[] Shipped = ["en", "es", "de", "fr", "pt-BR", "ja", "zh-Hans", "ar"];

    [Fact]
    public void ManifestPinsTheAuthoritativeReleaseAndShipsOnlyImportedLanguages()
    {
        var manifest = MessageCatalog.Manifest;
        Assert.Equal("2026.11.12-ad2447a", manifest.Release);
        Assert.Equal("0b579aa6192d49d7e8e0921dde5e955ac29ce302", manifest.SourceCommit);
        Assert.Equal(Shipped, MessageCatalog.ShippedLanguages.Select(language => language.Tag));
        Assert.Equal(["ar"], MessageCatalog.ShippedLanguages.Where(language => language.IsRightToLeft).Select(language => language.Tag));
        Assert.Equal("العربية", MessageCatalog.ShippedLanguages.Single(language => language.Tag == "ar").Endonym);
        foreach (var entry in manifest.Entries)
        {
            var catalog = MessageCatalog.Load(entry.Tag);
            Assert.Equal(entry.Messages, catalog.Count);
            Assert.True(catalog.Count > 6000);
        }
    }

    [Fact]
    public void GeneratedCatalogFilesMatchTheirRecordedChecksumsAndNothingElseIsVendored()
    {
        var directory = Path.Combine(TestRepository.Root, "src", "SiloPlayer.Core", "Localization", "Generated");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "catalog-manifest.json")));
        var listed = new HashSet<string>(StringComparer.Ordinal) { "catalog-manifest.json" };
        Assert.Equal("web.admin.", manifest.RootElement.GetProperty("excluded_key_prefixes")[0].GetString());
        foreach (var language in manifest.RootElement.GetProperty("languages").EnumerateArray())
        {
            var output = language.GetProperty("output").GetString()!;
            listed.Add(output);
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, output)))).ToLowerInvariant();
            Assert.Equal(language.GetProperty("output_sha256").GetString(), actual);
        }
        Assert.Equal(listed.Order(StringComparer.Ordinal), Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Keys("en"), key => key.StartsWith("web.admin.", StringComparison.Ordinal));
    }

    private static IEnumerable<string> Keys(string language)
    {
        using var document = JsonDocument.Parse(MessageCatalog.ReadResource(language + ".json"));
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }
}

public sealed class AppLanguageMatchingTests
{
    [Theory]
    [InlineData("es-MX", "es")]
    [InlineData("es", "es")]
    [InlineData("pt", "pt-BR")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("pt_br", "pt-BR")]
    [InlineData("zh", "zh-Hans")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-SG", "zh-Hans")]
    [InlineData("zh-Hans-HK", "zh-Hans")]
    [InlineData("en-GB", "en")]
    [InlineData("en-XA", "en")]
    [InlineData("ar-EG", "ar")]
    [InlineData("ar-XB", "ar")]
    [InlineData("ja-JP", "ja")]
    [InlineData("de-CH", "de")]
    [InlineData("fr-CA", "fr")]
    [InlineData("English", "en")]
    [InlineData("zh-TW", null)]
    [InlineData("zh-HK", null)]
    [InlineData("zh-Hant", null)]
    [InlineData("he", null)]
    [InlineData("tlh", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("x", null)]
    [InlineData("12", null)]
    [InlineData(null, null)]
    public void TagsMatchShippedLanguagesByLanguageAndChineseScript(string? tag, string? expected)
        => Assert.Equal(expected, AppLanguageMatcher.MatchShipped(tag));

    [Theory]
    [InlineData("pt_br", "pt-BR")]
    [InlineData("ZH-hans-cn", "zh-Hans-CN")]
    [InlineData(" es-419 ", "es-419")]
    [InlineData("iw", "he")]
    [InlineData("", null)]
    public void CanonicalizationIsForComparisonOnly(string value, string? expected)
        => Assert.Equal(expected, AppLanguageMatcher.Canonicalize(value));

    [Fact]
    public void NullStoredValueFollowsTheFirstDeviceLanguageWithACatalog()
    {
        var resolution = AppLocaleResolution.Resolve(null, ["tlh", "fr-CA", "de-DE"], "en-US");
        Assert.Equal("fr", resolution.Language);
        Assert.Null(resolution.StoredTag);
        Assert.False(resolution.StoredTagUnsupported);
        Assert.Equal("en-US", resolution.FormatCultureName);
        Assert.False(resolution.LocalizedFormats);
    }

    [Fact]
    public void StoredLanguageWinsAndFormatsInItsOwnLocaleWhenTheDeviceSpeaksAnother()
    {
        var resolution = AppLocaleResolution.Resolve("de", ["en-US"], "en-US");
        Assert.Equal("de", resolution.Language);
        Assert.Equal("de", resolution.FormatCultureName);
        Assert.True(resolution.LocalizedFormats);
    }

    [Fact]
    public void ChoosingEnglishOnABritishDeviceKeepsBritishFormats()
    {
        var resolution = AppLocaleResolution.Resolve("en", ["en-GB"], "en-GB");
        Assert.Equal("en", resolution.Language);
        Assert.Equal("en-GB", resolution.FormatCultureName);
        Assert.False(resolution.LocalizedFormats);
    }

    [Fact]
    public void UnsupportedStoredTagIsPreservedButResolvesAsIfNull()
    {
        var resolution = AppLocaleResolution.Resolve("he", ["ja-JP"], "ja-JP");
        Assert.Equal("ja", resolution.Language);
        Assert.Equal("he", resolution.StoredTag);
        Assert.True(resolution.StoredTagUnsupported);
        Assert.Equal("ja-JP", resolution.FormatCultureName);
        Assert.True(resolution.LocalizedFormats);

        var traditional = AppLocaleResolution.Resolve("zh-TW", ["zh-TW", "es-ES"], "zh-TW");
        Assert.Equal("es", traditional.Language);
        Assert.True(traditional.StoredTagUnsupported);
        Assert.Equal("en", AppLocaleResolution.Resolve(null, [], "").Language);
    }

    [Fact]
    public void ArabicIsRightToLeftAndAppliedTogetherWithItsCulture()
    {
        var service = new LocaleService();
        var locale = service.Create(AppLocaleResolution.Resolve("ar", ["en-US"], "en-US"));
        Assert.True(locale.IsRightToLeft);
        Assert.Equal("ar", locale.Language);
        Assert.Equal("ar", locale.FormatCulture.Name);
        Assert.False(service.Create(AppLocaleResolution.Resolve("fr", [], "en-US")).IsRightToLeft);
    }
}

public sealed class AppStringsFormattingTests
{
    private static AppLocale Locale(string language) => new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US"));
    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] values)
        => values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    [Theory]
    [InlineData(0, "0 ثانية")]
    [InlineData(1, "ثانية واحدة")]
    [InlineData(2, "ثانيتان")]
    [InlineData(3, "3 ثوانٍ")]
    [InlineData(11, "11 ثانية")]
    [InlineData(100, "100 ثانية")]
    [InlineData(103, "103 ثوانٍ")]
    public void ArabicPluralsSelectEveryCldrCategory(int seconds, string expected)
        => Assert.Equal(expected, Locale("ar").Get("player.seekInterval.choice", Args(("seconds", seconds))));

    [Fact]
    public void EnglishPluralsUseEnglishRulesAndGroupThePoundNumber()
    {
        var english = Locale("en");
        Assert.Equal("1 second", english.Get("player.seekInterval.choice", Args(("seconds", 1))));
        Assert.Equal("2 seconds", english.Get("player.seekInterval.choice", Args(("seconds", 2))));
        Assert.Equal("1,000 seconds", english.Get("player.seekInterval.choice", Args(("seconds", 1000L))));
        Assert.Equal("1.5 seconds", english.Get("player.seekInterval.choice", Args(("seconds", 1.5))));
        Assert.Contains("1.000", Locale("de").Get("player.seekInterval.choice", Args(("seconds", 1000))));
    }

    [Fact]
    public void ExactPluralOptionsWinOverCategories()
    {
        var english = Locale("en");
        Assert.Equal("Not in a collection yet", english.Get("web.collections.copy.inCollections", Args(("count", 0), ("count_text", "0"))));
        Assert.Equal("In 1 collection", english.Get("web.collections.copy.inCollections", Args(("count", 1), ("count_text", "1"))));
        Assert.Equal("In 1,204 collections", english.Get("web.collections.copy.inCollections", Args(("count", 1204), ("count_text", "1,204"))));
    }

    [Fact]
    public void SelectsAndRichTextTagsFormatAsPlainText()
    {
        var english = Locale("en");
        Assert.Equal("No narrators found in this library.", english.Get("web.audiobooks.groups.empty", Args(("group_by", "narrator"))));
        Assert.Equal("No series found in this library.", english.Get("web.audiobooks.groups.empty", Args(("group_by", "unknown-value"))));
        Assert.Equal("Narrated by Jim Dale", english.Get("web.audiobooks.player.narratedBy", Args(("narrator", "Jim Dale"))));
    }

    [Fact]
    public void MissingArgumentsNeverRenderARawOrPartlyFilledPattern()
    {
        var spanish = Locale("es");
        Assert.Equal("Recent rows", spanish.Get("home.section.recentlyAddedIn", null, "Recent rows"));
        Assert.Equal("home.section.recentlyAddedIn", spanish.Get("home.section.recentlyAddedIn"));
        Assert.Equal("home.section.recentlyAddedIn", spanish.Get("home.section.recentlyAddedIn", Args(("other", "x"))));
        Assert.Equal("plural fallback", spanish.Get("player.seekInterval.choice", Args(("seconds", "many")), "plural fallback"));
        Assert.Equal("missing", spanish.Get("player.seekInterval.choice", Args(("seconds", null)), "missing"));
        Assert.Equal("Desktop only fallback", spanish.Get("desktop.notInCatalog", null, "Desktop only fallback"));
        Assert.Equal("desktop.notInCatalog", spanish.Get("desktop.notInCatalog"));
    }

    [Fact]
    public void TranslatedTextUsesTheCatalogAndEnglishDataTextStaysEnglish()
    {
        var arabic = Locale("ar");
        Assert.Equal("إلغاء", arabic.Get("common.cancel"));
        Assert.Equal("Cancel", arabic.English("common.cancel"));
        Assert.Equal("Cancel", Locale("en").Get("common.cancel"));
        Assert.Equal("Cancelar", Locale("es").Get("common.cancel"));
        Assert.Equal("Abbrechen", Locale("de").Get("common.cancel"));
    }

    [Fact]
    public void RightToLeftIsolatesPrintedValuesButNotBareNumbersAndLeftToRightKeepsItsBytes()
    {
        Assert.Equal("أحدث الإضافات في ⁨Movies (2024)⁩",
            Locale("ar").Get("home.section.recentlyAddedIn", Args(("library", "Movies (2024)"))));
        Assert.Equal("Recently Added in Movies (2024)",
            Locale("en").Get("home.section.recentlyAddedIn", Args(("library", "Movies (2024)"))));
        Assert.Equal("3 ثوانٍ", Locale("ar").Get("player.seekInterval.choice", Args(("seconds", 3))));
    }

    [Fact]
    public void StaticFacadeFormatsInTheAppliedLocaleAndOnlyChangesOnApply()
    {
        var service = new LocaleService();
        var changes = new List<AppLocaleChangedEventArgs>();
        service.Changed += (_, change) => changes.Add(change);
        var spanish = service.Create(AppLocaleResolution.Resolve("es", [], "en-US"));
        var arabic = service.Create(AppLocaleResolution.Resolve("ar", [], "en-US"));
        Assert.True(service.Apply(arabic));
        Assert.False(service.Apply(spanish)); // created before arabic: a superseded result
        Assert.Equal("ar", service.Current.Language);
        Assert.False(service.Apply(service.Create(AppLocaleResolution.Resolve("ar", [], "en-US"))));
        var change = Assert.Single(changes);
        Assert.True(change.LanguageChanged && change.DirectionChanged);

        using (AppStrings.UseForCurrentFlow(service.Current))
        {
            Assert.Equal("إلغاء", AppStrings.Get("common.cancel"));
            Assert.True(AppStrings.IsRightToLeft);
        }
    }

    [Fact]
    public void AFailingSubscriberDoesNotStopLaterSubscribersOrTheApply()
    {
        var service = new LocaleService();
        var reached = new List<string>();
        service.Changed += (_, _) => throw new InvalidOperationException("view refresh failed");
        service.Changed += (_, change) => reached.Add(change.Current.Language);
        Assert.True(service.Apply(service.Create(AppLocaleResolution.Resolve("de", [], "en-US"))));
        Assert.Equal("de", Assert.Single(reached));
        Assert.Equal("de", service.Current.Language);
    }
}

public sealed class ServerTitleLocalizationTests
{
    private static AppLocale Locale(string language) => new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US"));
    private static readonly Dictionary<string, string> Movies = new() { ["library"] = "Movies" };

    [Fact]
    public void EnglishShowsTheServerTextExactlyAsSent()
        => Assert.Equal("Recently Added in Films", Locale("en").ServerText("home.section.recentlyAddedIn", Movies, "Recently Added in Films"));

    [Fact]
    public void TranslatedInterfaceUsesTheKeyOnlyWhenItsArgumentsAreComplete()
    {
        var spanish = Locale("es");
        var translated = spanish.ServerText("home.section.recentlyAddedIn", Movies, "Recently Added in Movies");
        Assert.NotEqual("Recently Added in Movies", translated);
        Assert.EndsWith("Movies", translated);
        Assert.Equal("Recently Added in Movies", spanish.ServerText("home.section.recentlyAddedIn", null, "Recently Added in Movies"));
        Assert.Equal("Recently Added in Movies", spanish.ServerText("home.section.recentlyAddedIn", new Dictionary<string, string> { ["genre"] = "x" }, "Recently Added in Movies"));
        Assert.Equal("Brand new row", spanish.ServerText("home.section.notARealKey", null, "Brand new row"));
        Assert.Equal("Typed", spanish.ServerText(null, null, "Typed"));
        Assert.Equal("Typed", spanish.ServerText("", null, "Typed"));
    }

    [Fact]
    public async Task SectionKeysSurviveDecodingCopiesAndReconciliationWhileRawTitlesStayStored()
    {
        var client = Client(request => request.RequestUri!.AbsolutePath switch
        {
            _ => Json("""{"sections":[{"id":"recent-1","section_type":"recently_added","title":"Recently Added in Movies","title_key":"home.section.recentlyAddedIn","title_args":{"library":"Movies"},"items":[]},{"id":"mine","section_type":"rules","title":"Friday picks","title_key":"","items":[]}]}"""),
        });
        var response = await client.GetAsync<HomeSectionsResponse>("/api/v2/home/sections");
        var keyed = response.Sections[0];
        Assert.Equal("home.section.recentlyAddedIn", keyed.TitleKey);
        Assert.Equal("Movies", keyed.TitleArgs!["library"]);
        Assert.Equal("Recently Added in Movies", keyed.KeyedTitle);
        Assert.Null(response.Sections[1].TitleKey);

        var spanish = Locale("es");
        using (AppStrings.UseForCurrentFlow(spanish))
        {
            var expected = spanish.ServerText("home.section.recentlyAddedIn", Movies, keyed.Title);
            Assert.Equal(expected, keyed.DisplayTitle);
            Assert.Equal("Friday picks", response.Sections[1].DisplayTitle);

            var snapshot = HomeSectionDedupe.Snapshot(keyed);
            Assert.Equal(expected, snapshot.DisplayTitle);
            snapshot.TitleArgs!["library"] = "Changed";
            Assert.Equal("Movies", keyed.TitleArgs["library"]);

            var mounted = new HomeSectionWithItems { Id = "recent-1", SectionType = "recently_added", Title = keyed.Title, Items = [] };
            Assert.NotEqual(HomeSectionChange.None, HomeSectionReconciler.Apply(mounted, keyed));
            Assert.Equal(expected, mounted.DisplayTitle);
            Assert.Equal("Recently Added in Movies", mounted.Title);

            // A rename shows as typed; the stored title, not the translation, is what an editor saves.
            mounted.Title = "Our films";
            Assert.Equal("Our films", mounted.DisplayTitle);
        }
    }

    [Fact]
    public void ProfileSectionSettingsCarryDefaultTitleKeysThroughWriteSnapshots()
    {
        var row = JsonSerializer.Deserialize<SettingsSectionsResponse>(
            """{"items":[{"id":"cw","section_type":"continue_watching","title":"Keep going","default_title":"Continue Watching","default_title_key":"home.section.continueWatching","position":0}]}""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!.Sections.Single();
        var copy = HomeSectionWritePolicy.Snapshot(row);
        Assert.Equal("home.section.continueWatching", copy.DefaultTitleKey);
        Assert.Null(copy.TitleKey);
        using (AppStrings.UseForCurrentFlow(Locale("de")))
        {
            Assert.Equal("Keep going", copy.DisplayTitle);
            Assert.Equal(Locale("de").Get("home.section.continueWatching"), copy.DisplayDefaultTitle);
        }
        Assert.Equal("Continue Watching", copy.DefaultTitle);
    }

    [Fact]
    public void RecipePresetLabelsTranslateForDisplayWhileTheSavedNameStaysEnglish()
    {
        var preset = JsonSerializer.Deserialize<GalleryPreset>(
            """{"key":"continue","display_name":"Continue Watching","display_name_key":"home.section.continueWatching","description_short":"Pick up where you left off.","description_short_key":"home.recipe.notShipped.description"}""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        using (AppStrings.UseForCurrentFlow(Locale("ar")))
        {
            Assert.Equal(Locale("ar").Get("home.section.continueWatching"), preset.DisplayNameText);
            Assert.Equal("Pick up where you left off.", preset.DescriptionShortText);
        }
        Assert.Equal("Continue Watching", preset.DisplayName);
    }

    internal static StringContent Json(string text) => new(text, Encoding.UTF8, "application/json");

    internal static SiloApiClient Client(Func<HttpRequestMessage, HttpContent> respond, HttpStatusCode status = HttpStatusCode.OK)
    {
        var client = new SiloApiClient(new HttpClient(new Handler(request => new HttpResponseMessage(status) { Content = respond(request) })));
        client.SetBaseUrl("https://fixture.invalid");
        return client;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(handle(request));
    }
}

public sealed class ApiProblemLocalizationTests
{
    private const string Validation = """{"type":"https://siloserver.org/docs/api/v2/problems/validation_failed","title":"Validation failed","status":422,"detail":"The request did not pass validation; see errors.","instance":"urn:silo:request:1","errors":[{"location":"body.name","code":"required","detail":"expected required property name to be present"},{"location":"body.limit","code":"out_of_range","detail":"limit must be at most 100"},{"location":"body.bad"}]}""";

    [Fact]
    public async Task StructuredFieldErrorsAreRetainedWithoutChangingMachineContracts()
    {
        var client = ServerTitleLocalizationTests.Client(_ => ServerTitleLocalizationTests.Json(Validation), (HttpStatusCode)422);
        var error = await Assert.ThrowsAsync<ApiException>(() => client.PostAsync<JsonElement>("/api/v2/fixture", new { name = "" }));
        Assert.Equal("validation_failed", error.ErrorCode);
        Assert.Equal(422, error.StatusCode);
        Assert.Equal("body.name", error.ErrorLocation);
        Assert.Equal("The request did not pass validation; see errors.", error.Message);
        Assert.Equal("validation_failed", error.ProblemType);
        Assert.Equal("Validation failed", error.ProblemTitle);
        Assert.Equal(
            new[] { new ApiFieldError("body.name", "required", "expected required property name to be present"),
             new ApiFieldError("body.limit", "out_of_range", "limit must be at most 100") },
            error.FieldErrors);
    }

    [Fact]
    public async Task ProblemTextTranslatesOnlyAtPresentationAndKeepsEnglishDetail()
    {
        var client = ServerTitleLocalizationTests.Client(_ => ServerTitleLocalizationTests.Json(Validation), (HttpStatusCode)422);
        var error = await Assert.ThrowsAsync<ApiException>(() => client.PostAsync<JsonElement>("/api/v2/fixture", new { }));
        var english = new LocaleService().Create(AppLocaleResolution.Resolve("en", [], "en-US"));
        var spanish = new LocaleService().Create(AppLocaleResolution.Resolve("es", [], "en-US"));
        using (AppStrings.UseForCurrentFlow(english))
        {
            Assert.Equal(error.Message, ApiProblemText.Display(error));
            Assert.Equal("limit must be at most 100", ApiProblemText.FieldDetail(error.FieldErrors[1]));
        }
        using (AppStrings.UseForCurrentFlow(spanish))
        {
            Assert.Equal(spanish.Get("error.field.required"), ApiProblemText.Display(error));
            Assert.Equal(spanish.Get("error.validation_failed"), ApiProblemText.Display("validation_failed", [], "detail"));
            Assert.Equal(spanish.Get("error.not_found"), ApiProblemText.Display("not_found", [new ApiFieldError(null, "brand_new_code", "x")], "Gone"));
            Assert.Equal("Server words this", ApiProblemText.Display("brand_new_problem", null, "Server words this"));
            Assert.Equal("Server words this", ApiProblemText.Display(null, null, "Server words this"));
        }
        Assert.Equal("The request did not pass validation; see errors.", error.Message);
    }

    [Fact]
    public async Task NonProblemBodiesKeepTheirExistingShape()
    {
        var client = ServerTitleLocalizationTests.Client(_ => new StringContent("<html>bad gateway</html>"), HttpStatusCode.BadGateway);
        var error = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync<JsonElement>("/api/v2/fixture"));
        Assert.Equal("unknown", error.ErrorCode);
        Assert.Equal("HTTP 502", error.Message);
        Assert.Null(error.ProblemType);
        Assert.Empty(error.FieldErrors);
    }
}

public sealed class AppLanguagePreferenceTests
{
    private const string ProfileA = "profile-a";

    [Fact]
    public async Task OlderServersReportUnsupportedWithoutReadingTheSetting()
    {
        var wire = new SettingsWire { Revision = 27 };
        var (preference, _) = Create(wire);
        var read = await preference.ReadEffectiveAsync(preference.CaptureContext());
        Assert.False(read.Supported);
        Assert.Null(read.Tag);
        Assert.DoesNotContain(wire.Requests, request => request.Contains("/values/effective", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"pt-BR\"", "pt-BR")]
    [InlineData("null", null)]
    [InlineData("\"tlh\"", "tlh")]
    public async Task EffectiveReadReturnsTheStoredTagUntouched(string value, string? expected)
    {
        var wire = new SettingsWire { EffectiveValue = value };
        var (preference, _) = Create(wire);
        var read = await preference.ReadEffectiveAsync(preference.CaptureContext());
        Assert.True(read.Supported);
        Assert.Equal(expected, read.Tag);
        Assert.Contains(wire.Requests, request => request.StartsWith("GET /api/v2/settings/values/effective?keys=ui.app_language", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PickerWritesTheProfileValueThenClearsThisDevicesOwnValue()
    {
        var wire = new SettingsWire { DeviceDeleteStatus = HttpStatusCode.NotFound };
        var (preference, _) = Create(wire);
        var result = await preference.SaveAsync(preference.CaptureContext(), "ar");
        Assert.True(result.DeviceValueCleared);
        Assert.Equal(["PUT /api/v2/settings/values/ui.app_language?scope=profile&profile_id=profile-a {\"value\":\"ar\"}",
                      "DELETE /api/v2/settings/values/ui.app_language?scope=profile_device&profile_id=profile-a"],
            wire.Requests.Where(request => !request.StartsWith("GET", StringComparison.Ordinal)));

        wire.Requests.Clear();
        await preference.SaveAsync(preference.CaptureContext(), null);
        Assert.Contains("PUT /api/v2/settings/values/ui.app_language?scope=profile&profile_id=profile-a {\"value\":null}", wire.Requests);
        await Assert.ThrowsAsync<ArgumentException>(() => preference.SaveAsync(preference.CaptureContext(), ""));
    }

    [Fact]
    public async Task DeviceClearRetryDoesNotRepeatTheProfileWriteAndRejectsARetiredOwner()
    {
        var wire = new SettingsWire { DeviceDeleteStatus = HttpStatusCode.ServiceUnavailable };
        var (preference, client) = Create(wire);
        var owner = preference.CaptureSettingsOwner();
        Assert.False((await preference.SaveAsync(preference.CaptureContext(), "ar")).DeviceValueCleared);
        wire.Requests.Clear();
        wire.DeviceDeleteStatus = HttpStatusCode.NoContent;
        Assert.True((await preference.ClearDeviceValueAsync(owner)).DeviceValueCleared);
        Assert.Single(wire.Requests);
        Assert.StartsWith("DELETE ", wire.Requests.Single());
        wire.Requests.Clear();
        client.SetProfile("profile-b");
        Assert.False((await preference.ClearDeviceValueAsync(owner)).DeviceValueCleared);
        Assert.Empty(wire.Requests);
    }

    [Fact]
    public async Task OwnerChangeAfterTheWriteSkipsTheDeviceDelete()
    {
        var wire = new SettingsWire();
        var (preference, client) = Create(wire);
        wire.OnPut = () => client.SetProfile("profile-b");
        var result = await preference.SaveAsync(preference.CaptureContext(), "es");
        Assert.False(result.DeviceValueCleared);
        Assert.DoesNotContain(wire.Requests, request => request.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public void RealtimeChangesAreFilteredByKeyAndProfile()
    {
        JsonElement Data(string json) => JsonDocument.Parse(json).RootElement;
        Assert.True(AppLanguagePreference.IsRealtimeChange("user_settings", "user_settings.changed", Data("""{"key":"ui.app_language","profile_id":"profile-a"}"""), ProfileA));
        Assert.True(AppLanguagePreference.IsRealtimeChange("user_settings", "user_settings.changed", Data("""{"key":"ui.app_language"}"""), ProfileA));
        Assert.False(AppLanguagePreference.IsRealtimeChange("user_settings", "user_settings.changed", Data("""{"key":"ui.app_language","profile_id":"profile-b"}"""), ProfileA));
        Assert.False(AppLanguagePreference.IsRealtimeChange("user_settings", "user_settings.changed", Data("""{"key":"catalog.metadata_language"}"""), ProfileA));
        Assert.False(AppLanguagePreference.IsRealtimeChange("notifications", "user_settings.changed", Data("""{"key":"ui.app_language"}"""), ProfileA));
    }

    [Fact]
    public async Task CoordinatorAppliesTheProfileValueAndCachesItForTheNextLaunch()
    {
        var wire = new SettingsWire { EffectiveValue = "\"es\"" };
        var (preference, _) = Create(wire);
        var locales = new LocaleService();
        using var directory = new TemporaryDirectory();
        var local = new SettingsService(directory.Path);
        var coordinator = new AppLocaleCoordinator(preference, locales, local, () => ["de-DE"], "de-DE", action => { action(); return true; });

        Assert.Equal("de", coordinator.ApplyBeforeSettings().Language);
        Assert.True(await coordinator.RefreshAsync());
        Assert.Equal("es", locales.Current.Language);
        Assert.Equal("es", local.Load().LastAppLanguageOverride);
        Assert.Equal("es", new SettingsService(directory.Path).Load().LastAppLanguageOverride);

        // "Match device" clears the cache and follows the device again.
        wire.EffectiveValue = "null";
        Assert.True(await coordinator.RefreshAsync());
        Assert.Equal("de", locales.Current.Language);
        Assert.Null(local.Load().LastAppLanguageOverride);

        // An unshipped tag stays stored and resolves as if null.
        wire.EffectiveValue = "\"he\"";
        Assert.True(await coordinator.RefreshAsync());
        Assert.Equal("de", locales.Current.Language);
        Assert.Equal("he", locales.Current.Resolution.StoredTag);
        Assert.True(locales.Current.Resolution.StoredTagUnsupported);
    }

    [Fact]
    public async Task LateResultForARetiredProfileIsNeverApplied()
    {
        var gate = new TaskCompletionSource();
        var wire = new SettingsWire { EffectiveValue = "\"ar\"", HoldEffective = gate.Task };
        var (preference, client) = Create(wire);
        var locales = new LocaleService();
        using var directory = new TemporaryDirectory();
        var local = new SettingsService(directory.Path);
        var coordinator = new AppLocaleCoordinator(preference, locales, local, () => ["en-US"], "en-US", action => { action(); return true; });

        var pending = coordinator.RefreshAsync();
        client.SetProfile("profile-b");
        coordinator.Retire();
        gate.SetResult();
        Assert.False(await pending);
        Assert.Equal("en", locales.Current.Language);
        Assert.Null(local.Load().LastAppLanguageOverride);
        Assert.Null(coordinator.LoadedOwner);
    }

    [Fact]
    public async Task OlderRefreshThatFinishesLastCannotOverwriteTheNewerOne()
    {
        var first = new TaskCompletionSource();
        var wire = new SettingsWire { EffectiveValue = "\"fr\"", HoldEffective = first.Task };
        var (preference, _) = Create(wire);
        var locales = new LocaleService();
        using var directory = new TemporaryDirectory();
        var coordinator = new AppLocaleCoordinator(preference, locales, new SettingsService(directory.Path), () => ["en-US"], "en-US", action => { action(); return true; });

        var stale = coordinator.RefreshAsync();
        wire.HoldEffective = null;
        wire.EffectiveValue = "\"ja\"";
        Assert.True(await coordinator.RefreshAsync());
        first.SetResult();
        Assert.False(await stale);
        Assert.Equal("ja", locales.Current.Language);
    }

    [Fact]
    public async Task SupersedingAQueuedApplyCompletesTheOldRefreshWithoutFaulting()
    {
        var posts = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var queued = new SemaphoreSlim(0);
        var wire = new SettingsWire { EffectiveValue = "\"fr\"" };
        var (preference, _) = Create(wire);
        var locales = new LocaleService();
        using var directory = new TemporaryDirectory();
        var coordinator = new AppLocaleCoordinator(preference, locales, new SettingsService(directory.Path),
            () => ["en-US"], "en-US", action => { posts.Enqueue(action); queued.Release(); return true; });

        var first = coordinator.RefreshAsync();
        Assert.True(await queued.WaitAsync(TimeSpan.FromSeconds(5)));
        wire.EffectiveValue = "\"ja\"";
        var second = coordinator.RefreshAsync();
        Assert.True(await queued.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(posts.TryDequeue(out var stale));
        stale();
        Assert.False(await first);
        Assert.Equal("en", locales.Current.Language);
        Assert.True(posts.TryDequeue(out var current));
        current();
        Assert.True(await second);
        Assert.Equal("ja", locales.Current.Language);
    }

    [Fact]
    public async Task UnsupportedServerKeepsTheCachedLanguage()
    {
        var wire = new SettingsWire { Revision = 27 };
        var (preference, _) = Create(wire);
        var locales = new LocaleService();
        using var directory = new TemporaryDirectory();
        var local = new SettingsService(directory.Path);
        local.Save(new SiloPlayer.Core.Models.AppSettings { LastAppLanguageOverride = "fr" });
        var coordinator = new AppLocaleCoordinator(preference, locales, local, () => ["en-US"], "en-US", action => { action(); return true; });
        coordinator.ApplyBeforeSettings();
        Assert.True(await coordinator.RefreshAsync());
        Assert.Equal("fr", locales.Current.Language);
        Assert.False(coordinator.ServerSupportsSetting);
        Assert.Equal("fr", local.Load().LastAppLanguageOverride);
    }

    private static (AppLanguagePreference Preference, SiloApiClient Client) Create(SettingsWire wire)
    {
        var client = new SiloApiClient(new HttpClient(wire));
        client.SetBaseUrl("https://settings.invalid");
        client.SetProfile(ProfileA);
        return (new AppLanguagePreference(new SettingsApi(client)), client);
    }

    private sealed class SettingsWire : HttpMessageHandler
    {
        public int Revision { get; set; } = 28;
        public string EffectiveValue { get; set; } = "null";
        public Task? HoldEffective { get; set; }
        public HttpStatusCode DeviceDeleteStatus { get; set; } = HttpStatusCode.NoContent;
        public Action? OnPut { get; set; }
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? "" : " " + await request.Content.ReadAsStringAsync(ct);
            lock (Requests) Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}{body}");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/settings/contract/capabilities")
                return Ok($$"""{"api_version":1,"manifest_revision":{{Revision}},"supports_batched_effective":true}""");
            if (path == "/api/v2/settings/values/effective")
            {
                var value = EffectiveValue;
                if (HoldEffective is { } hold) await hold.WaitAsync(ct);
                return Ok($$"""{"items":[{"key":"ui.app_language","value":{{value}},"source":"profile"}],"revision":1}""");
            }
            if (request.Method == HttpMethod.Put)
            {
                OnPut?.Invoke();
                return Ok("""{"key":"ui.app_language","value":null,"source":"profile"}""");
            }
            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(request.RequestUri.Query.Contains("profile_device") ? DeviceDeleteStatus : HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "silo-locale-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }
}
