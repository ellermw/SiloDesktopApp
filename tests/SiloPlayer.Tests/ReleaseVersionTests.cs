using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.518-beta.2", "1.2.518-beta.12", -1)]
    [InlineData("1.2.518-beta.12", "1.2.518", -1)]
    [InlineData("1.2.518", "1.2.518-beta.12", 1)]
    [InlineData("1.2.517", "1.2.518-beta.1", -1)]
    [InlineData("1.2.518-beta.12+abc", "1.2.518-beta.12+xyz", 0)]
    [InlineData("1.2.518-alpha", "1.2.518-alpha.1", -1)]
    [InlineData("1.2.518-alpha.999999999999999999999", "1.2.518-alpha.1000000000000000000000", -1)]
    [InlineData("1.2.518-alpha.1", "1.2.518-alpha.beta", -1)]
    [InlineData("1.2.518-beta", "1.2.518-rc.1", -1)]
    public void SemanticPrecedenceDoesNotUseLexicalOrWindowsRevisionOrdering(string left, string right, int expected)
        => Assert.Equal(expected, Math.Sign(ReleaseVersion.Parse(left).CompareTo(ReleaseVersion.Parse(right))));

    [Theory]
    [InlineData("1.2.518", "1.2.518.0")]
    [InlineData("1.2.518-beta.1", "1.2.517.1")]
    [InlineData("1.2.518-beta.2", "1.2.517.2")]
    [InlineData("1.2.518-beta.12", "1.2.517.12")]
    [InlineData("1.2.518-beta.65534", "1.2.517.65534")]
    public void WindowsMappingKeepsStableStampsAndOrdersSkippedBetasBeforePromotion(string text, string numeric)
    {
        var version = ReleaseVersion.Parse(text);
        Assert.Equal(text, version.Text);
        Assert.Equal(Version.Parse(numeric), version.GetWindowsVersion());
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1.2.518")]
    [InlineData("01.2.518")]
    [InlineData("1.2.518.0")]
    [InlineData("1.2.518-beta.02")]
    [InlineData("1.2.518-beta.")]
    [InlineData("1.2.518-βeta.1")]
    [InlineData("1.2.518+")]
    [InlineData("1.2.518\n")]
    public void MalformedVersionsAreNeverAccepted(string text)
        => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Theory]
    [InlineData("1.2.0-beta.1")]
    [InlineData("1.2.518-beta.0")]
    [InlineData("1.2.518-beta.65535")]
    [InlineData("1.2.518-rc.1")]
    [InlineData("1.2.65535")]
    [InlineData("1.2.518+repacked")]
    public void UnrepresentableOrNoncanonicalPackagesFailRatherThanWrapping(string text)
        => Assert.Throws<InvalidDataException>(() => ReleaseVersion.Parse(text).GetWindowsVersion());

    [Fact]
    public void InstalledInformationalIdentityMustMatchTheNumericBinary()
    {
        Assert.Equal("1.2.518-beta.12", ReleaseVersion.FromBuild("1.2.518-beta.12+sourcehash", new Version(1, 2, 517, 12)).Text);
        Assert.Equal("1.2.516", ReleaseVersion.FromBuild("1.2.516+sourcehash", new Version(1, 2, 516, 0)).Text);
        Assert.Throws<InvalidDataException>(() => ReleaseVersion.FromBuild("1.2.518-beta.12", new Version(1, 2, 518, 0)));
    }

    [Theory]
    [InlineData("1.2.517.12", true)]
    [InlineData("1.2.518.0", false)]
    [InlineData("", false)]
    public void InstalledAssemblyAlsoRequiresTheMatchingNumericFileVersion(string fileVersion, bool valid)
    {
        var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName("SyntheticUpdateIdentity" + Guid.NewGuid().ToString("N")) { Version = new(1, 2, 517, 12) },
            System.Reflection.Emit.AssemblyBuilderAccess.Run);
        assembly.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(
            typeof(System.Reflection.AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, ["1.2.518-beta.12+source"]));
        if (fileVersion.Length != 0) assembly.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(
            typeof(System.Reflection.AssemblyFileVersionAttribute).GetConstructor([typeof(string)])!, [fileVersion]));
        if (valid) Assert.Equal("1.2.518-beta.12", ReleaseVersion.FromAssembly(assembly).Text);
        else Assert.Throws<InvalidDataException>(() => ReleaseVersion.FromAssembly(assembly));
    }
}
