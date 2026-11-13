using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

public sealed class InterfaceTextCasingTests
{
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("az-Latn-AZ")]
    public void MatchDeviceEnglishLabelsIgnoreRegionalCasing(string regionalCulture)
    {
        var locale = LocaleService.Shared.Create(AppLocaleResolution.Resolve(null, ["en-US"], regionalCulture));
        using var scope = AppStrings.UseForCurrentFlow(locale);
        Assert.Equal("en", AppStrings.Language);
        Assert.Equal(regionalCulture, AppStrings.FormatCulture.Name);
        Assert.Equal("LIBRARIES", AppStrings.Uppercase(AppStrings.Get("web.shell.sidebar.libraries")));
        Assert.Equal("DISCOVER", AppStrings.Uppercase(AppStrings.Get("web.shell.sidebar.discover")));
        Assert.Equal("KIDS", AppStrings.Uppercase(AppStrings.Get("web.onboarding.householdSetup.kidsBadge")));
    }
}
