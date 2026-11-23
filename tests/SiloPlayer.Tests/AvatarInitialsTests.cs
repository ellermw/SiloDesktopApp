using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

/// <summary>Official silo-server #4998 and #5027 (web/src/lib/text.ts and its tests).</summary>
public sealed class AvatarInitialsTests
{
    [Fact]
    public void ACharacterOutsideTheBasicPlaneIsNeverCutInHalf()
    {
        Assert.Equal("\U0001F43B", AvatarInitials.One("\U0001F43B Bear", languageTag: "en"));
        Assert.Equal("\U0001F43BB", AvatarInitials.OfWords(["\U0001F43B", "Bear"], languageTag: "en"));
        // A flag is two code points and a letter with a combining mark is two chars; each is one character to a reader.
        Assert.Equal("\U0001F1EF\U0001F1F5", AvatarInitials.One("\U0001F1EF\U0001F1F5 Tokyo", languageTag: "en"));
        Assert.Equal("E\u0301", AvatarInitials.One("e\u0301lodie", languageTag: "en"));
    }

    [Fact]
    public void InitialsAreUpperCasedByTheInterfaceLanguage()
    {
        Assert.Equal("I", AvatarInitials.One("irem", languageTag: "en"));
        Assert.Equal("\u0130", AvatarInitials.One("irem", languageTag: "tr"));
        Assert.Equal("\u0130R", AvatarInitials.Leading("irem", 2, "tr"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ANameWithNoCharactersShowsTheFallback(string? name)
    {
        Assert.Equal("?", AvatarInitials.One(name, languageTag: "en"));
        Assert.Equal("", AvatarInitials.One(name, fallback: "", languageTag: "en"));
        Assert.Equal("?", AvatarInitials.OfWords([name], languageTag: "en"));
    }

    [Fact]
    public void LeadingSpacesAreNotTheInitial()
        => Assert.Equal("AK", AvatarInitials.OfWords([" ana", "kim "], languageTag: "en"));
}
