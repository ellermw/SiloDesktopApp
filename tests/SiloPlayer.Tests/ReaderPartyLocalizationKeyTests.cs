using System.Text.RegularExpressions;
using SiloPlayer.Core.Localization;
using Xunit;

namespace SiloPlayer.Tests;

/// <summary>
/// Task5 Reader/Watch Party key contract. A mistyped key silently shows its
/// English fallback in every language, so each explicit key in the owned
/// surfaces must exist in the generated English, Japanese and Arabic
/// catalogs. The authored reader renderer and the native page must agree on
/// the exact payload ids, and desktop-only ids must not collide with catalog keys.
/// </summary>
public sealed class ReaderPartyLocalizationKeyTests
{
    private static readonly string[][] OwnedFiles =
    [
        ["src", "SiloPlayer", "Views", "EbookReaderPage.xaml"],
        ["src", "SiloPlayer", "Views", "EbookReaderPage.xaml.cs"],
        ["src", "SiloPlayer", "Views", "EbookReaderPage.Localization.cs"],
        ["src", "SiloPlayer", "Views", "WatchTogetherJoinPage.xaml"],
        ["src", "SiloPlayer", "Views", "WatchTogetherJoinPage.xaml.cs"],
        ["src", "SiloPlayer", "Views", "WatchTogetherJoinPage.Localization.cs"],
        ["src", "SiloPlayer", "Views", "WatchTogetherRoomPage.xaml"],
        ["src", "SiloPlayer", "Views", "WatchTogetherRoomPage.xaml.cs"],
        ["src", "SiloPlayer", "Views", "WatchTogetherRoomPage.Picker.cs"],
        ["src", "SiloPlayer", "Views", "WatchTogetherRoomPage.Localization.cs"],
        ["src", "SiloPlayer", "ViewModels", "WatchTogetherViewModels.cs"],
        ["src", "SiloPlayer", "ViewModels", "WatchTogetherViewModels.PlaybackPanel.cs"],
        ["src", "SiloPlayer", "ViewModels", "WatchTogetherViewModels.Localization.cs"],
        ["src", "SiloPlayer", "Controls", "MangaFilesDialog.cs"],
        ["src", "SiloPlayer", "Assets", "Reader", "reader.mjs"],
    ];

    // Every quoted key literal: helper calls, XAML attached keys, choice tables and renderer text().
    private static readonly Regex KeyUse = new(
        "[\"']((?:web|watchParty|common|settingsManifest|desktop)\\.[A-Za-z0-9_]+(?:\\.[A-Za-z0-9_]+)+)[\"']",
        RegexOptions.Compiled);

    [Fact]
    public void EveryExplicitKeyInTheOwnedSurfacesExistsInShippedCatalogs()
    {
        var keys = OwnedFiles.SelectMany(parts => KeyUse.Matches(ReadRepoFile(parts)).Select(match => match.Groups[1].Value))
            .Where(key => !key.StartsWith("desktop.", StringComparison.Ordinal))
            .Distinct().Order().ToList();
        Assert.True(keys.Count > 150, $"Expected the full owned key set, found {keys.Count}.");
        foreach (var language in new[] { "en", "ja", "ar" })
        {
            var catalog = MessageCatalog.Load(language);
            var missing = keys.Where(key => !catalog.Contains(key)).ToList();
            Assert.True(missing.Count == 0, $"{language} catalog lacks: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void ReaderRendererUsesTheNativePayloadIdsAndDesktopIdsAreNotCatalogKeys()
    {
        var renderer = ReadRepoFile("src", "SiloPlayer", "Assets", "Reader", "reader.mjs");
        var native = ReadRepoFile("src", "SiloPlayer", "Views", "EbookReaderPage.Localization.cs");
        var rendererIds = Regex.Matches(renderer, "\\btext\\('([^']+)'").Select(match => match.Groups[1].Value).Distinct().Order().ToList();
        var nativeIds = Regex.Matches(native, "internal const string Renderer\\w+ = \"([^\"]+)\"").Select(match => match.Groups[1].Value).Distinct().Order().ToList();
        Assert.Equal(nativeIds, rendererIds);
        Assert.Contains("web.reader.error.openFailed", nativeIds);
        var english = MessageCatalog.English;
        Assert.All(nativeIds.Where(id => id.StartsWith("desktop.", StringComparison.Ordinal)), id => Assert.False(english.Contains(id)));
        // The interface language may set the shell's lang, never the book's direction.
        Assert.DoesNotContain("documentElement.dir", renderer);
        Assert.DoesNotContain(".dir =", renderer);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
