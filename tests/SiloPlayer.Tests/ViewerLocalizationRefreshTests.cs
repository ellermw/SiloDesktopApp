using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SiloPlayer.Core.Localization;
using Xunit.Abstractions;

namespace SiloPlayer.Tests;

public sealed class ViewerLocalizationRefreshTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> CardinalCases()
    {
        decimal[] values = [0, 1, 2, 5, 11, 21, 22, 25, 101, 1.5m, 1.2m, 0.1m, 2.1m, 1_000_000];
        var rules = new Dictionary<string, string>
        {
            ["en,de,nl,it,tr,sv,nb,fi"] = "other one other other other other other other other other other other other other",
            ["es"] = "other one other other other other other other other other other other other many",
            ["fr,pt-BR"] = "one one other other other other other other other one one one other many",
            ["pl"] = "many one few many many many few many many other other other other many",
            ["ru,uk"] = "many one few many many one few many one other other other other many",
            ["cs"] = "other one few other other other other other other many many many many other",
            ["hr"] = "other one few other other one few other one other few one one other",
            ["da"] = "other one other other other other other other other one one one other other",
            ["ja,zh-Hans,zh-Hant,ko"] = "other other other other other other other other other other other other other other",
            ["ar"] = "zero one two few many many many many other other other other other other",
        };
        foreach (var (languages, categories) in rules)
        foreach (var language in languages.Split(','))
        foreach (var (value, category) in values.Zip(categories.Split(' ')))
        {
            var expected = language == "it" && value == 1_000_000 ? "many" : category;
            yield return [language, value, expected];
            if (value != 0) yield return [language, -value, expected];
        }
    }

    [Theory]
    [MemberData(nameof(CardinalCases))]
    public void CardinalsMatchCldrForAllShippedLanguages(string language, decimal value, string expected)
    {
        Assert.Equal(expected, PluralRules.Category(language, value));
        using var document = JsonDocument.Parse("""[{"p":"count","o":{"zero":["zero"],"one":["one"],"two":["two"],"few":["few"],"many":["many"],"other":["other"]}}]""");
        var message = MessagePattern.Parse(document.RootElement);
        var context = new MessageFormatContext(language, CultureInfo.InvariantCulture.NumberFormat, CultureInfo.InvariantCulture, false);
        Assert.True(message.TryFormat(context, new Dictionary<string, object?> { ["count"] = value }, out var formatted));
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData("en", "1.00", "one")]
    [InlineData("pl", "2.000", "few")]
    [InlineData("ru", "21.00", "one")]
    [InlineData("cs", "2.00", "few")]
    [InlineData("hr", "1.20", "few")]
    [InlineData("hr", "1.0100", "one")]
    [InlineData("hr", "1.1100", "other")]
    [InlineData("hr", "2.120", "other")]
    [InlineData("da", "0.000", "other")]
    [InlineData("da", "0.0100", "one")]
    [InlineData("it", "1000000.000", "many")]
    [InlineData("it", "1000000.1", "other")]
    [InlineData("pl", "12", "many")]
    [InlineData("pl", "14", "many")]
    [InlineData("pl", "112", "many")]
    [InlineData("pl", "122", "few")]
    [InlineData("ru", "111", "many")]
    [InlineData("ru", "112", "many")]
    [InlineData("uk", "114", "many")]
    [InlineData("uk", "122", "few")]
    [InlineData("hr", "1.14", "other")]
    [InlineData("cs", "4", "few")]
    public void DecimalScaleDoesNotInventVisibleZerosUnlikeJavascriptNumbers(string language, string value, string expected)
        => Assert.Equal(expected, PluralRules.Category(language, decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("pl", "1", "visible odcinek")]
    [InlineData("pl", "2", "visible odcinki")]
    [InlineData("pl", "5", "visible odcinków")]
    [InlineData("pl", "1.5", "visible odcinka")]
    [InlineData("ru", "21", "visible серия")]
    [InlineData("ru", "22", "visible серии")]
    [InlineData("ru", "25", "visible серий")]
    [InlineData("cs", "1.5", "visible dílu")]
    [InlineData("hr", "1.2", "visible epizode")]
    [InlineData("it", "1000000", "visible di episodi")]
    [InlineData("ko", "2", "에피소드 visible개")]
    [InlineData("zh-Hant", "2", "visible 集")]
    public void CatalogCountLabelsUseCountForGrammarAndKeepCallerFormattedShownText(string language, string count, string expected)
    {
        var locale = new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US"));
        Assert.Equal(expected, locale.Get("web.library.browse.countEpisodes", new Dictionary<string, object?>
        {
            ["count"] = decimal.Parse(count, CultureInfo.InvariantCulture),
            ["shown"] = "visible",
        }));
    }

    [Theory]
    [InlineData("en", "Sign in with OpenID Connect")]
    [InlineData("pl", "Zaloguj się przez usługę OpenID Connect")]
    [InlineData("ru", "Войти через OpenID Connect")]
    [InlineData("ko", "OpenID Connect 계정으로 로그인")]
    [InlineData("zh-Hant", "使用 OpenID Connect 登入")]
    public void ProviderPatternPlacesTheUnchangedProviderNameInTheLocalizedSentence(string language, string expected)
    {
        var locale = new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US"));
        Assert.Equal(expected, locale.Get("signIn.withProvider", new Dictionary<string, object?> { ["provider"] = "OpenID Connect" }));
        Assert.Equal("Sign in", locale.Get("signIn.withProvider", fallback: "Sign in"));
    }

    [Fact]
    public void OAuthProviderTemplateRefreshesTextAndAccessibilityWithoutChangingTheNetworkTemplate()
    {
        var xaml = File.ReadAllText(Path.Combine(TestRepository.Root, "src/SiloPlayer/Views/LoginPage.xaml"));
        Assert.Contains("Loaded=\"AuthProviderButton_Loaded\"", xaml);
        Assert.Contains("Click=\"NetworkProviderButton_Click\"", xaml);
        Assert.Contains("Text=\"{Binding Label}\"", xaml);
        Assert.Contains("Text=\"{Binding Via}\"", xaml);
        Assert.Contains("Source=\"{Binding Provider.IconUrl, Converter={StaticResource UrlToImage}}\"", xaml);
        var code = File.ReadAllText(Path.Combine(TestRepository.Root, "src/SiloPlayer/Views/LoginPage.xaml.cs"));
        Assert.Contains("button.Tag is not AuthProvider provider", code);
        Assert.Contains("AppLocalization.Register(button, () => RefreshAuthProviderText(button))", code);
        Assert.Contains("AppStrings.Get(\"signIn.withProvider\"", code);
        Assert.Contains("fallback: \"Sign in with \" + provider.DisplayName", code);
        Assert.Contains("AutomationProperties.SetName(button, text)", code);
    }

    [Theory]
    [InlineData("no-NO", "nb")]
    [InlineData("zh_Hant_TW", "zh-Hant")]
    [InlineData("nn-NO", "en")]
    public void ReadingPreferencesKeepsStoredBytesAndSeparateFormatting(string stored, string expected)
    {
        var resolution = AppLocaleResolution.Resolve(stored, ["en-US"], "en-US");
        Assert.Equal(stored, resolution.StoredTag);
        Assert.Equal(expected, resolution.Language);
        Assert.Equal(expected == "en", resolution.StoredTagUnsupported);
        Assert.False(resolution.IsRightToLeft);
    }

    [Fact]
    public void NewViewerKeysShipAndRemovedAudiobookKeysStayRemoved()
    {
        string[] added = ["signIn.withProvider", "web.library.browse.countBooks", "web.library.browse.countEpisodes", "web.library.browse.countMovies", "web.library.browse.countSeries", "web.audiobooks.detail.finished", "web.audiobooks.chapters.showAll", "web.audiobooks.hero.alsoListening", "web.audiobooks.player.comingUp"];
        string[] removed = ["web.audiobooks.chapters.count", "web.audiobooks.chapters.listening", "web.audiobooks.chapters.sortPosition", "web.audiobooks.chapters.sortLongest", "web.audiobooks.hero.eyebrow", "web.audiobooks.player.chapterNumber"];
        foreach (var language in MessageCatalog.ShippedLanguages)
        {
            var catalog = MessageCatalog.Load(language.Tag);
            foreach (var key in added) Assert.True(catalog.Contains(key), language.Tag + ":" + key);
            foreach (var key in removed) Assert.False(catalog.Contains(key), language.Tag + ":" + key);
        }
    }

    [Fact]
    public void CurrentNotificationGroupingAndBulkMuteKeysShipInEveryLanguage()
    {
        string[] added =
        [
            "web.notifications.inbox.dayOlder",
            "web.notifications.inbox.dayToday",
            "web.notifications.inbox.dayWeek",
            "web.notifications.inbox.dayYesterday",
            "web.notifications.inbox.episodeCount",
            "web.notifications.inbox.episodeRange",
            "web.notifications.inbox.episodeRangeLatest",
            "web.notifications.inbox.newCount",
            "web.notifications.inbox.requestTag",
            "web.notifications.inbox.unreadBadge",
            "web.notifications.inbox.viewInbox",
            "web.notifications.inbox.views",
            "web.notifications.queries.muteManyFailed",
            "web.notifications.queries.mutedMany",
            "web.notifications.queries.unmuteManyFailed",
            "web.notifications.queries.unmutedMany",
            "web.notifications.selection.clear",
            "web.notifications.selection.count",
            "web.notifications.selection.inboxLabel",
            "web.notifications.selection.markRead",
            "web.notifications.selection.muteSeries",
            "web.notifications.selection.notificationCount",
            "web.notifications.selection.select",
            "web.notifications.selection.selectAllMatching",
            "web.notifications.selection.seriesLabel",
            "web.notifications.series.columnNotifiesFor",
            "web.notifications.series.columnNotify",
            "web.notifications.series.columnReasons",
            "web.notifications.series.columnSeries",
            "web.notifications.series.notifySwitch",
            "web.notifications.series.selectSeries",
            "web.notifications.series.selectShown",
        ];
        foreach (var language in MessageCatalog.ShippedLanguages)
        {
            var catalog = MessageCatalog.Load(language.Tag);
            foreach (var key in added) Assert.True(catalog.Contains(key), language.Tag + ":" + key);
        }
    }

    [Fact]
    public void RemovedKeyCallsitesCarryExplicitFallbacks()
    {
        // Every remaining call to a key the 22-language catalog retired must carry an explicit
        // readable fallback, wherever it lives (the #182 audiobook redesign moved chapter rows).
        string[] removed = ["web.audiobooks.chapters.count", "web.audiobooks.chapters.listening", "web.audiobooks.chapters.sortPosition", "web.audiobooks.chapters.sortLongest", "web.audiobooks.hero.eyebrow", "web.audiobooks.player.chapterNumber"];
        foreach (var path in Directory.EnumerateFiles(Path.Combine(TestRepository.Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            foreach (var key in removed)
                for (var at = source.IndexOf("\"" + key + "\"", StringComparison.Ordinal); at >= 0; at = source.IndexOf("\"" + key + "\"", at + 1, StringComparison.Ordinal))
                {
                    var call = source[at..source.IndexOf(';', at)];
                    Assert.True(call.Contains("fallback:", StringComparison.Ordinal), $"{Path.GetFileName(path)} calls retired {key} without a fallback");
                }
        }
        var rows = File.ReadAllText(Path.Combine(TestRepository.Root, "src/SiloPlayer.Core/Helpers/AudiobookDetailPresentation.cs"));
        Assert.Contains("\"web.audiobooks.chapter.fallbackTitle\"", rows);
        var hero = File.ReadAllText(Path.Combine(TestRepository.Root, "src/SiloPlayer/Controls/NowListeningHero.xaml.cs"));
        Assert.Contains("web.audiobooks.hero.ariaLabel", hero);
        var transport = File.ReadAllText(Path.Combine(TestRepository.Root, "src/SiloPlayer/Controls/AudiobookTransportAppearance.cs"));
        Assert.Contains("fallback: $\"Chapter {number}\"", transport);
    }

    [Fact]
    public void SourceWalkingResolvesThisCheckoutNotPhysicalBuildRoot()
    {
        var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", ".."));
        output.WriteLine("AppContext.BaseDirectory=" + AppContext.BaseDirectory);
        output.WriteLine("CallerFilePath=" + SourceFile());
        output.WriteLine("ResolvedSourceRoot=" + TestRepository.Root);
        output.WriteLine("ExpectedSourceRoot=" + expectedRoot);
        Assert.Equal(expectedRoot, TestRepository.Root);
        output.WriteLine("SourceRootProof=PASS");
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;
}
