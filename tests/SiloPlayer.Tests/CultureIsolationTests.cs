using System.Globalization;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

/// <summary>The helper every culture-changing test uses, and the process locale baseline it protects (see <see cref="TestCulture"/>).</summary>
public sealed class CultureIsolationTests
{
    private static string MachineCulture()
    {
        // A new thread starts with the process default, whatever other threads have set for themselves.
        string? name = null;
        var thread = new Thread(() => name = CultureInfo.CurrentCulture.Name);
        thread.Start();
        thread.Join();
        return name!;
    }

    [Fact]
    public void ScopeRestoresBothCulturesEvenWhenTheBodyThrows()
    {
        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        Action body = () =>
        {
            using var scope = TestCulture.Use("ar-SA", ui: true);
            Assert.Equal("ar-SA", CultureInfo.CurrentCulture.Name);
            Assert.Equal("ar-SA", CultureInfo.CurrentUICulture.Name);
            throw new InvalidOperationException();
        };
        Assert.Throws<InvalidOperationException>(body);
        Assert.Same(culture, CultureInfo.CurrentCulture);
        Assert.Same(ui, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void ScopesNestAndDisposingTwiceIsHarmless()
    {
        var before = CultureInfo.CurrentCulture;
        var outer = TestCulture.Use("de-DE");
        var inner = TestCulture.Use("th-TH");
        Assert.Equal("th-TH", CultureInfo.CurrentCulture.Name);
        inner.Dispose();
        Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
        outer.Dispose();
        outer.Dispose();
        Assert.Same(before, CultureInfo.CurrentCulture);
    }

    [Fact]
    public async Task ScopeSurvivesAnAwaitThatResumesOnAnotherThread()
    {
        var before = CultureInfo.CurrentCulture;
        using (TestCulture.Use("fi-FI"))
        {
            await Task.Run(() => Thread.Sleep(1)).ConfigureAwait(false);
            Assert.Equal("fi-FI", CultureInfo.CurrentCulture.Name);
        }
        Assert.Same(before, CultureInfo.CurrentCulture);
    }

    [Fact]
    public void ProcessFormatCultureIsTheMachinesNotATestThreads()
    {
        var machine = MachineCulture();
        Assert.Equal(machine, LocaleService.Shared.Current.Resolution.FormatCultureName);
        using var scope = TestCulture.Use("ar-SA", ui: true);
        // A test that needs another format culture pins it with AppStrings.UseForCurrentFlow; an unpinned one reads the process's.
        Assert.Equal(machine, AppStrings.FormatCulture.Name);
        Assert.Equal(machine, LocaleService.Shared.Current.Resolution.FormatCultureName);
    }
}
