namespace SiloPlayer.Tests;

/// <summary>A base-compilable RED checkpoint. Run alone before adding controller/native tests and production.</summary>
public sealed class AdminNotificationRegistrationTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
    [Fact]
    public void NotificationsMustBeReachableThroughProductionConfigureRegistration()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src/SiloPlayer/Services/AdminConfigureServices.cs"));
        Assert.Contains("new AdminPageRegistration(AdminRoute.NotificationSettings, typeof(AdminNotificationSettingsPage))", source);
        Assert.Contains("AddTransient<AdminNotificationSettingsController>()", source);
        Assert.Contains("AdminNotificationSettingsController.Keys", source);
        Assert.Contains("AdminNotificationSettingsController.Secrets", source);
    }
    [Fact]
    public void PublishedXbfAndSurfacePolicyMustIncludeTheActualPage()
    {
        var build = File.ReadAllText(Path.Combine(Root(), "installer/build.ps1"));
        Assert.Contains("'Views/Admin/AdminNotificationSettingsPage.xbf'", build);
        var policy = File.ReadAllText(Path.Combine(Root(), "docs/parity/admin-monitor-surfaces.json"));
        Assert.Contains("src/SiloPlayer/Views/Admin/AdminNotificationSettingsPage.cs", policy);
    }
}
