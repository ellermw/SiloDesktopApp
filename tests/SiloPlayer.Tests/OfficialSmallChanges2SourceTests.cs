namespace SiloPlayer.Tests;

/// <summary>
/// Official small changes, batch 2. Source guards for the shared styles; the behaviour is proven by the
/// official-small-changes-2 native fixture (focus rings, Match Item Enter, short-window confirm dialogs).
/// </summary>
public sealed class OfficialSmallChanges2SourceTests
{
    private static string Theme => File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Themes", "DarkTheme.xaml"));

    // #5081: the red button and badge variants no longer replace the shared focus ring with a 20% red.
    [Fact]
    public void NoDestructiveStyleOrTemplateOverridesTheSharedFocusVisual()
    {
        var theme = Theme;
        foreach (var key in new[] { "DestructiveButtonStyle", "WebUiDestructiveButtonTemplate" })
        {
            var start = theme.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
            Assert.True(start >= 0, key);
            var end = theme.IndexOf(key.EndsWith("Template", StringComparison.Ordinal) ? "</ControlTemplate>" : "</Style>", start, StringComparison.Ordinal);
            var block = theme[start..end];
            Assert.DoesNotContain("FocusVisualPrimaryBrush", block);
            Assert.DoesNotContain("FocusVisualSecondaryBrush", block);
            Assert.DoesNotContain("FocusVisualMargin", block);
            Assert.DoesNotContain("FocusVisualPrimaryThickness", block);
        }
        var style = theme[theme.IndexOf("x:Key=\"DestructiveButtonStyle\"", StringComparison.Ordinal)..];
        Assert.Contains("UseSystemFocusVisuals", style[..style.IndexOf("</Style>", StringComparison.Ordinal)]);
    }

    // #5070: the shared dialog template turns its body scroller on for a plain body.
    [Fact]
    public void TheSharedDialogStyleLetsATallBodyScrollUnderItsActions()
    {
        var theme = Theme;
        var style = theme[theme.IndexOf("x:Key=\"WebUiContentDialogStyle\"", StringComparison.Ordinal)..];
        Assert.Contains("helpers:DialogBodyScroll.Enabled", style[..style.IndexOf("<Setter Property=\"Template\">", StringComparison.Ordinal)]);
        // The command space stays outside the scrolled region.
        Assert.True(style.IndexOf("x:Name=\"ContentScrollViewer\"", StringComparison.Ordinal) < style.IndexOf("x:Name=\"CommandSpace\"", StringComparison.Ordinal));
        var helper = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Helpers", "DialogBodyScroll.cs"));
        Assert.Contains("ScrollBarVisibility.Auto", helper);
        Assert.Contains("ownScroll", helper);
    }
}
