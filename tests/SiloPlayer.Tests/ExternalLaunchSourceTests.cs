using System.Text.RegularExpressions;

namespace SiloPlayer.Tests;

public sealed class ExternalLaunchSourceTests
{
    // Launch arguments built from the server base URL or a fixed https origin.
    private static readonly string[] SafeByConstruction =
    [
        "ServerWebUiUri.FromApiBase(",      // ServerSetupRequiredPage
        "auth.BuildAuthenticationUri(pin)", // fixed https://app.plex.tv origin
        "authorizeUri",                     // NativeOAuthHandshake: server base + native start path
        "uri => await",                     // SettingsPage.SignIn launcher; its StartUri is the server-base handshake
        "new Uri(new Uri(Client.BaseUrl",   // RedetectMarkersDialog admin settings link
    ];

    [Fact]
    public void ServerOrStoredUrlsAreLaunchedOnlyAsWebLinks()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "src", "SiloPlayer", "App.xaml.cs"))) root = root.Parent;
        var app = Path.Combine(root!.FullName, "src", "SiloPlayer");
        var unchecked_ = new List<string>();
        foreach (var file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("LaunchUriAsync(", StringComparison.Ordinal)) continue;
                var window = string.Join("\n", lines[Math.Max(0, i - 3)..Math.Min(lines.Length, i + 2)]);
                if (window.Contains("ExternalBrowserUrlPolicy.TryGetSafeUri(", StringComparison.Ordinal)) continue;
                if (SafeByConstruction.Any(marker => window.Contains(marker, StringComparison.Ordinal))) continue;
                // Fixed https prefixes (RequestDetailPage IMDb/TMDB buttons) are checked below.
                if (file.EndsWith("RequestDetailPage.xaml.cs", StringComparison.Ordinal)) continue;
                if (file.EndsWith("NotificationSettingsControl.xaml.cs", StringComparison.Ordinal) && window.Contains("/settings/notifications", StringComparison.Ordinal)) continue;
                unchecked_.Add($"{Path.GetRelativePath(app, file)}:{i + 1}");
            }
        }
        Assert.Empty(unchecked_);

        var request = File.ReadAllText(Path.Combine(app, "Views", "RequestDetailPage.xaml.cs"));
        foreach (Match link in Regex.Matches(request, @"ExternalLinkButton\(""[^""]+"", \$?""([^""]*)"""))
            Assert.StartsWith("https://", link.Groups[1].Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Views/CollectionEditorPage.xaml.cs", "ViewModel.SourceUrl")]
    [InlineData("Views/Dialogs/FeatureTourDialog.cs", "link.Url")]
    [InlineData("Controls/NotificationSettingsControl.xaml.cs", "url")]
    public void ServerSuppliedLinksUseTheWebLinkPolicy(string relative, string value)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "src", "SiloPlayer", "App.xaml.cs"))) root = root.Parent;
        var source = File.ReadAllText(Path.Combine(root!.FullName, "src", "SiloPlayer", relative.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains($"ExternalBrowserUrlPolicy.TryGetSafeUri({value}, out var", source, StringComparison.Ordinal);
        Assert.DoesNotContain($"Uri.TryCreate({value}, UriKind.Absolute, out var", source, StringComparison.Ordinal);
    }
}
