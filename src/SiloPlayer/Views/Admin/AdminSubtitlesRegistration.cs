using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Integration hook for Libraries › Subtitle Files. The integration owner
/// adds one line to App.xaml.cs service registration:
/// <c>services.AddAdminSubtitleFiles();</c>
/// (after <c>SiloApiClient</c> and <c>AdminMonitorAuthority</c> are registered).
/// It registers the domain API and the finished page for
/// <see cref="AdminRoute.SubtitleFiles"/>, which makes the Manage › Libraries
/// topic, its "Subtitle Files" leaf and its search entry appear.
/// </summary>
public static class AdminSubtitlesRegistration
{
    public static IServiceCollection AddAdminSubtitleFiles(this IServiceCollection services)
    {
        services.AddSingleton<AdminSubtitlesApi>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.SubtitleFiles, typeof(AdminSubtitlesPage)));
        return services;
    }
}
