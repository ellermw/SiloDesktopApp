using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// A scan sends one catalog event per file, with the library as a v2 string id. The open
/// Library grid must refresh for its own library only; the page used to read only numbers,
/// so every library's scan refreshed it.
/// </summary>
public sealed class CatalogEventScopeTests
{
    private const int OpenLibrary = 7;

    [Theory]
    [InlineData("{\"library_id\":\"7\"}", 7)] // The server's form.
    [InlineData("{\"library_id\":7}", 7)] // An older node's form.
    [InlineData("{\"library_id\":\"12\"}", 12)]
    [InlineData("{\"library_id\":12}", 12)]
    [InlineData("{\"library_id\":\"2147483647\"}", int.MaxValue)]
    public void ReadsTheLibraryAsAStringOrANumber(string json, int expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, CatalogEventScope.LibraryId(document.RootElement));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"library_id\":null}")]
    [InlineData("{\"library_id\":\"\"}")]
    [InlineData("{\"library_id\":\"malformed\"}")]
    [InlineData("{\"library_id\":\"8\\u0000\"}")]
    [InlineData("{\"library_id\":\"\\uD800\"}")]
    [InlineData("{\"library_id\":\"07\"}")]
    [InlineData("{\"library_id\":\" 7\"}")]
    [InlineData("{\"library_id\":\"-7\"}")]
    [InlineData("{\"library_id\":\"0\"}")]
    [InlineData("{\"library_id\":0}")]
    [InlineData("{\"library_id\":-3}")]
    [InlineData("{\"library_id\":7.5}")]
    [InlineData("{\"library_id\":\"2147483648\"}")]
    [InlineData("{\"library_id\":true}")]
    [InlineData("[7]")]
    public void AnythingElseIsUnscoped(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Null(CatalogEventScope.LibraryId(document.RootElement));
    }

    [Theory]
    [InlineData("{\"library_id\":\"7\"}", true)]
    [InlineData("{\"library_id\":7}", true)]
    [InlineData("{\"library_id\":\"8\"}", false)] // A scan of another library.
    [InlineData("{\"library_id\":8}", false)]
    [InlineData("{}", true)] // Unscoped events refresh every library, as on the WebUI.
    [InlineData("{\"library_id\":\"malformed\"}", true)]
    [InlineData("{\"library_id\":\"8\\u0000\"}", true)]
    [InlineData("{\"library_id\":\"\\uD800\"}", true)]
    public void OnlyTheOpenLibraryOrAnUnscopedEventRefreshesTheGrid(string json, bool refreshes)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(refreshes, CatalogEventScope.Refreshes(CatalogEventScope.LibraryId(document.RootElement), OpenLibrary));
    }
}
