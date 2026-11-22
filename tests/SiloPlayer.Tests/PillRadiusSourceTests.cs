using System.Text.RegularExpressions;

namespace SiloPlayer.Tests;

/// <summary>
/// WinUI limits an oversized corner radius on each axis by itself, so a "rounded-full" element written with a radius such as 999 is drawn as an
/// oval with pointed ends. Such an element must follow its size through <c>RoundedBadge</c> (or the Dashboard's <c>Stadium</c>). The native
/// checks look at laid-out elements (<c>CornerShapeChecks</c>); this source check catches a new page before it has a fixture.
/// </summary>
public sealed partial class PillRadiusSourceTests
{
    // A uniform radius of 50 or more written in code: CornerRadius = new(999), new CornerRadius(100), CornerRadius(9999).
    [GeneratedRegex(@"CornerRadius(\s*=\s*new(\s*CornerRadius)?)?\(\s*([5-9][0-9]|[1-9][0-9]{2,})(\.[0-9]+)?\s*\)|new CornerRadius\(\s*([5-9][0-9]|[1-9][0-9]{2,})(\.[0-9]+)?\s*\)")]
    private static partial Regex LargeRadius();

    [GeneratedRegex(@"RoundedBadge\.(SetRadius|Apply)\(|Stadium\(")]
    private static partial Regex Helper();

    // A fixed 100x100 circle: a radius of exactly half its size.
    private static readonly string[] FixedCircles = ["ProfileEditorDialog.cs"];

    [Fact]
    public void EveryRoundedFullElementInCodeFollowsItsSize()
    {
        var source = Path.Combine(TestRepository.Root, "src", "SiloPlayer");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            if (FixedCircles.Contains(Path.GetFileName(file))) continue;
            var text = File.ReadAllText(file);
            var large = LargeRadius().Matches(text).Count;
            if (large == 0) continue;
            // The helper itself declares none; every element with a large radius needs one helper call in the same file.
            var helpers = Helper().Matches(text).Count;
            if (helpers < large) offenders.Add($"{Path.GetRelativePath(TestRepository.Root, file)}: {large} large radii, {helpers} size-following helper calls");
        }
        Assert.True(offenders.Count == 0, "A radius of 50 or more is drawn as an oval unless it follows the element's size (RoundedBadge.SetRadius or DashboardUi.Stadium):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryRoundedFullElementInXamlFollowsItsSize()
    {
        var source = Path.Combine(TestRepository.Root, "src", "SiloPlayer");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            var text = File.ReadAllText(file);
            var large = Regex.Matches(text, "CornerRadius=\"([5-9][0-9]|[1-9][0-9]{2,})\"").Count;
            var helpers = Regex.Matches(text, "RoundedBadge\\.Radius=").Count;
            if (helpers < large) offenders.Add($"{Path.GetRelativePath(TestRepository.Root, file)}: {large} large radii, {helpers} RoundedBadge.Radius");
        }
        Assert.True(offenders.Count == 0, "A XAML CornerRadius of 50 or more needs helpers:RoundedBadge.Radius on the same element:\n" + string.Join("\n", offenders));
    }
}
