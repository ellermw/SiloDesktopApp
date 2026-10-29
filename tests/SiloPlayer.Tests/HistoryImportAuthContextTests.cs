namespace SiloPlayer.Tests;

public sealed class HistoryImportAuthContextTests
{
    private static string Method(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "src", "SiloPlayer", "App.xaml.cs"))) root = root.Parent;
        var source = File.ReadAllText(Path.Combine(root!.FullName, "src", "SiloPlayer", "ViewModels", "SettingsViewModel.cs"));
        var start = source.IndexOf($"private async Task {name}()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("[RelayCommand]", start, StringComparison.Ordinal);
        return source[start..(end < 0 ? source.Length : end)];
    }

    [Fact]
    public void PlexApprovalPollingStopsWhenTheAccountChanges()
    {
        // Settings stays cached across profile/server changes, and LoadImportSources
        // clears PlexSessionId/PlexOAuthServers for the new account. A poll for the
        // previous account must not refill them or keep sending checks.
        var plex = Method("PlexAuthStartAsync");
        var capture = plex.IndexOf("var context = _historyImportApi.CaptureContext();", StringComparison.Ordinal);
        var pin = plex.IndexOf("await _historyImportApi.PlexAuthPinAsync()", StringComparison.Ordinal);
        Assert.InRange(capture, 0, pin);
        Assert.InRange(plex.IndexOf("IsCurrentContext(context)", pin, StringComparison.Ordinal), pin, plex.IndexOf("PlexSessionId = pinResponse.SessionId;", StringComparison.Ordinal));
        var delay = plex.IndexOf("await Task.Delay(2000);", StringComparison.Ordinal);
        var check = plex.IndexOf("await _historyImportApi.PlexAuthCheckAsync(", StringComparison.Ordinal);
        var servers = plex.IndexOf("PlexOAuthServers.Clear();", StringComparison.Ordinal);
        Assert.InRange(plex.IndexOf("IsCurrentContext(context)", delay, StringComparison.Ordinal), delay, check);
        Assert.InRange(plex.IndexOf("IsCurrentContext(context)", check, StringComparison.Ordinal), check, servers);
    }

    [Fact]
    public void EmbyConnectLoginResultIsDroppedAfterAnAccountChange()
    {
        var emby = Method("EmbyConnectLoginAsync");
        var login = emby.IndexOf("await _historyImportApi.EmbyConnectLoginAsync(", StringComparison.Ordinal);
        Assert.InRange(emby.IndexOf("var context = _historyImportApi.CaptureContext();", StringComparison.Ordinal), 0, login);
        Assert.InRange(emby.IndexOf("IsCurrentContext(context)", login, StringComparison.Ordinal), login, emby.IndexOf("EmbyConnectSessionId = response.ConnectSessionId;", StringComparison.Ordinal));
    }
}
