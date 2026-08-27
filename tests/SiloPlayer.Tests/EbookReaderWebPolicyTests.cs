using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class EbookReaderWebPolicyTests
{
    [Theory]
    [InlineData("https://silo-reader.local/chapter-1.xhtml")]
    [InlineData("https://SILO-READER.LOCAL/images/cover.jpg")]
    public void IsTrustedReaderUri_AllowsOnlyMappedReaderOrigin(string input)
    {
        Assert.True(EbookReaderWebPolicy.IsTrustedReaderUri(input));
    }

    [Theory]
    [InlineData("https://example.com/tracker.js")]
    [InlineData("http://silo-reader.local/chapter.xhtml")]
    [InlineData("file:///C:/Users/test/secret.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    [InlineData("")]
    public void IsTrustedReaderUri_RejectsExternalAndPrivilegedOrigins(string input)
    {
        Assert.False(EbookReaderWebPolicy.IsTrustedReaderUri(input));
    }

    [Theory]
    [InlineData("data:image/png;base64,AA==")]
    [InlineData("blob:https://silo-reader.local/00000000-0000-0000-0000-000000000000")]
    public void IsAllowedSubresource_AllowsReaderGeneratedInlineContent(string input)
    {
        Assert.True(EbookReaderWebPolicy.IsAllowedSubresource(input));
    }

    [Fact]
    public void IsAllowedSubresource_RejectsRemoteNetworkContent()
    {
        Assert.False(EbookReaderWebPolicy.IsAllowedSubresource("https://tracker.example/pixel"));
    }
}
