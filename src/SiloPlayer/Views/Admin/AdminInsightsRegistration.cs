using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Registers the Insights tabs and panels that exist, and the record of departing layout owners the Insights page and the Dashboard share.
/// Add the People, Devices and Bandwidth tabs and the person panel here as they land; a tab without a registration shows its placeholder.
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
        services.AddSingleton(new AdminInsightsPanelRegistration(AdminInsightsPanelKind.Title, page => new AdminInsightsTitlePanel(page)));
        return services;
    }
}
