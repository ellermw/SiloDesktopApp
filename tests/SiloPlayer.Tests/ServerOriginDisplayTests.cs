using SiloPlayer.Helpers;

namespace SiloPlayer.Tests;

/// <summary>
/// A silo://invite link can name any server, and that server styles the
/// invitation and login pages (name, artwork). The pages must show where the
/// password actually goes.
/// </summary>
public sealed class ServerOriginDisplayTests
{
    [Theory]
    [InlineData("https://media.example.com", "media.example.com")]
    [InlineData("https://media.example.com/", "media.example.com")]
    [InlineData("https://media.example.com:8443", "media.example.com:8443")]
    [InlineData("http://192.168.1.20:8096", "192.168.1.20:8096 · Not secure (HTTP)")]
    [InlineData("http://media.example.com", "media.example.com · Not secure (HTTP)")]
    [InlineData("https://[::1]:8443", "[::1]:8443")]
    // Look-alike Cyrillic "а" is shown as punycode rather than as "apple".
    [InlineData("https://\u0430pple.com", "xn--pple-43d.com")]
    public void ShowsTheHostPortAndPlainHttpWarning(string url, string expected)
        => Assert.Equal(expected, ServerOriginDisplay.Format(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("file:///C:/silo")]
    [InlineData("javascript:alert(1)")]
    public void UnusableAddressesShowNothing(string? url)
        => Assert.Equal("", ServerOriginDisplay.Format(url));

    [Theory]
    [InlineData("LoginPage.xaml.cs", "ServerOriginDisplay.Format(ViewModel.ServerUrl)")]
    [InlineData("InviteClaimPage.xaml.cs", "ServerOriginDisplay.Format(navigation.ServerUrl)")]
    public void PasswordPagesShowTheServerOrigin(string page, string call)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "SiloPlayer", "Views", page))) dir = dir.Parent;
        Assert.NotNull(dir);
        var views = Path.Combine(dir!.FullName, "src", "SiloPlayer", "Views");
        Assert.Contains(call, File.ReadAllText(Path.Combine(views, page)), StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ServerOriginText\"", File.ReadAllText(Path.Combine(views, page.Replace(".xaml.cs", ".xaml"))), StringComparison.Ordinal);
    }
}
