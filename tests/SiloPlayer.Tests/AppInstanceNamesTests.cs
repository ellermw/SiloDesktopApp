using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AppInstanceNamesTests
{
    [Fact]
    public void Create_UsesProductionIdentityWhenQaIdIsMissing()
    {
        var names = AppInstanceNames.Create(null);

        Assert.Equal(@"Local\SiloDesktopPlayer-6F4EE0EA-4DA3-49D0-940D-461F977BA343", names.MutexName);
        Assert.Equal("SiloDesktopPlayer-Activation-6F4EE0EA", names.PipeName);
    }

    [Fact]
    public void Create_IsolatesQaInstancesWithoutEmbeddingRawInput()
    {
        var first = AppInstanceNames.Create("codex-parity-pass");
        var same = AppInstanceNames.Create("codex-parity-pass");
        var other = AppInstanceNames.Create("other-pass");

        Assert.Equal(first, same);
        Assert.NotEqual(first, other);
        Assert.DoesNotContain("codex-parity-pass", first.MutexName, StringComparison.Ordinal);
        Assert.DoesNotContain("codex-parity-pass", first.PipeName, StringComparison.Ordinal);
    }
}
