using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Registers Admin › Playback › History and Skip Markers (read-only audit
/// pages) with the foundation shell. Lead integration: call
/// <c>services.AddAdminPlaybackAudit();</c> in <c>App.ConfigureServices</c>
/// after <c>AdminMonitorAuthority</c>. The domain APIs are separate from
/// <see cref="AdminMonitorApi"/> and share only <see cref="SiloApiClient"/>.
/// </summary>
public static class AdminPlaybackAuditRegistration
{
    public static IServiceCollection AddAdminPlaybackAudit(this IServiceCollection services)
    {
        services.AddSingleton(sp => new AdminPlaybackHistoryApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton(sp => new AdminMarkerHistoryApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.PlaybackHistory, typeof(AdminPlaybackHistoryPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.SkipMarkers, typeof(AdminMarkerHistoryPage)));
        return services;
    }
}
