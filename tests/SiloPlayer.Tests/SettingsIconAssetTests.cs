using System.Xml.Linq;

namespace SiloPlayer.Tests;

public sealed class SettingsIconAssetTests
{
    [Theory]
    [InlineData("play")]
    [InlineData("subtitles")]
    [InlineData("monitor-smartphone")]
    [InlineData("panel-top")]
    [InlineData("layers")]
    [InlineData("layout-dashboard")]
    [InlineData("cast")]
    [InlineData("cloud")]
    [InlineData("server")]
    [InlineData("key-round")]
    public void DirectoryIconsContainValidDrawableSvgNodes(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "SiloPlayer"))) root = root.Parent;
        Assert.NotNull(root);
        var document = XDocument.Load(Path.Combine(root.FullName, "src", "SiloPlayer", "Assets", "Icons", name + ".svg"));
        Assert.Equal("http://www.w3.org/2000/svg", document.Root!.Name.NamespaceName);
        Assert.NotEmpty(document.Root.Elements());
        foreach (var node in document.Root.Elements())
        {
            Assert.Contains(node.Name.LocalName, new[] { "path", "circle", "rect", "line", "polyline", "polygon", "ellipse" });
            if (node.Name.LocalName == "path") Assert.False(string.IsNullOrWhiteSpace((string?)node.Attribute("d")));
        }
    }
}
