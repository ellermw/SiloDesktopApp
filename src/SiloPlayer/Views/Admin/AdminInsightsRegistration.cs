using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Registers the Insights tabs and panels, and the record of departing layout owners the Insights page and the Dashboard share. Every tab but
/// the live one and both panels are registered; a tab without a registration would show its placeholder.
/// Call <c>services.AddAdminInsights();</c> in <c>App.ConfigureServices</c>.
/// </summary>
public static class AdminInsightsRegistration
{
    public static IServiceCollection AddAdminInsights(this IServiceCollection services)
    {
        services.AddSingleton<DashboardLayoutWrites>();
        services.AddSingleton(new AdminInsightsTabRegistration(AdminInsightsTab.Playback, page => new AdminInsightsPlaybackTab(page)));
        services.AddSingleton(new AdminInsightsTabRegistration(AdminInsightsTab.Titles, page => new AdminInsightsTitlesTab(page)));
        services.AddSingleton(new AdminInsightsTabRegistration(AdminInsightsTab.Library, page => new AdminInsightsLibraryTab(page)));
        services.AddSingleton(new AdminInsightsTabRegistration(AdminInsightsTab.People, page => new AdminInsightsPeopleTab(page)));
        services.AddSingleton(new AdminInsightsTabRegistration(AdminInsightsTab.Devices, page => new AdminInsightsDevicesTab(page)));
        services.AddSingleton(new AdminInsightsTabRegistration(AdminInsightsTab.Bandwidth, page => new AdminInsightsBandwidthTab(page)));
        services.AddSingleton(new AdminInsightsPanelRegistration(AdminInsightsPanelKind.Title, page => new AdminInsightsTitlePanel(page)));
        services.AddSingleton(new AdminInsightsPanelRegistration(AdminInsightsPanelKind.Person, page => new AdminInsightsPersonPanel(page)));
        return services;
    }
}
