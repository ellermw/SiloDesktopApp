using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminAppearanceAssetPolicyTests
{
    [Theory]
    [InlineData("https://silo.invalid/api/v2/branding/assets/favicon?v=stored-favicon.svg", true)]
    [InlineData("https://silo.invalid/api/v2/branding/assets/favicon?v=stored-favicon.png", false)]
    [InlineData("https://silo.invalid/api/v2/branding/assets/favicon?v=first.svg&v=second.png", false)]
    [InlineData("https://silo.invalid/legacy-favicon.SVG", true)]
    public void SvgDecoderUsesTheV2VersionReference(string url, bool expected)
        => Assert.Equal(expected, AdminAppearanceAssetPolicy.IsSvgUrl(new Uri(url)));

    // Resolve the future policy by name so this checkpoint compiles on the baseline
    // and reports a failing assertion before the policy is added.
    private static bool Allowed(string kind, string mediaType, long length)
    {
        var policy = typeof(AdminPageRegistry).Assembly.GetType("SiloPlayer.Core.Services.Admin.AdminAppearanceAssetPolicy");
        Assert.NotNull(policy);
        var method = policy!.GetMethod("IsAllowed", [typeof(string), typeof(string), typeof(long)]);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [kind, mediaType, length])!;
    }

    [Theory]
    [InlineData("wordmark", "image/png", 8388608, true)]
    [InlineData("wordmark", "image/jpeg", 8388609, false)]
    [InlineData("mark", "image/webp", 8388608, true)]
    [InlineData("login_bg", "image/jpeg", 12582912, true)]
    [InlineData("login_bg", "image/png", 12582913, false)]
    [InlineData("favicon", "image/x-icon", 1048576, true)]
    [InlineData("favicon", "image/vnd.microsoft.icon", 1, true)]
    [InlineData("favicon", "image/svg+xml", 1, true)]
    [InlineData("favicon", "image/jpeg", 1, false)]
    [InlineData("favicon", "image/png", 1048577, false)]
    [InlineData("mark", "image/svg+xml", 1, false)]
    [InlineData("mark", "image/png", 0, false)]
    [InlineData("wordmark_light", "image/png", 1, false)]
    [InlineData("mark_light", "image/png", 1, false)]
    [InlineData("../favicon", "image/png", 1, false)]
    public void RejectsFilesThePinnedV2AssetEndpointCannotAccept(string kind, string mediaType, long length, bool expected)
        => Assert.Equal(expected, Allowed(kind, mediaType, length));
}
