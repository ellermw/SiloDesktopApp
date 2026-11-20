using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The shared realtime channel this page leases <c>download_preparations</c> and <c>downloads</c> on (the one
/// <see cref="EventChannelClient"/>; nothing here opens another socket).
/// </summary>
public sealed class AdminPreparedFilesRealtimeSource(IAdminRealtimeChannel channel)
{
    public IAdminRealtimeChannel Channel { get; } = channel;
}

public static class AdminPreparedFilesRegistration
{
    /// <summary>Registers only Manage > Downloads > Prepared Files. Viewer Downloads and Download Settings are separate.</summary>
    public static IServiceCollection AddAdminPreparedFiles(this IServiceCollection services)
    {
        services.TryAddSingleton<AdminPreparedFilesApi>();
        services.TryAddSingleton(provider => new AdminPreparedFilesRealtimeSource(
            new EventChannelAdminRealtime(provider.GetRequiredService<EventChannelClient>())));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.PreparedFiles, typeof(AdminPreparedFilesPage)));
        return services;
    }
}
