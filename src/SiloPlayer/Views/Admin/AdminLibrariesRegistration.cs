using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The shared realtime channel this page leases <c>scans</c> and <c>jobs</c> on (the one
/// <see cref="EventChannelClient"/>; nothing here opens another socket).
/// </summary>
public sealed class AdminLibrariesRealtimeSource(IAdminRealtimeChannel channel)
{
    public IAdminRealtimeChannel Channel { get; } = channel;
}

/// <summary>
/// Integration hook for Libraries › Libraries. The integration owner adds one line to
/// App.xaml.cs service registration: <c>services.AddAdminLibraries();</c> (after
/// <c>SiloApiClient</c>, <c>AdminMonitorAuthority</c> and <c>EventChannelClient</c> are
/// registered). It registers the domain API, the realtime source and the finished page for
/// <see cref="AdminRoute.Libraries"/>, which makes the Manage › Libraries leaf and its
/// search entry appear.
/// </summary>
public static class AdminLibrariesRegistration
{
    public static IServiceCollection AddAdminLibraries(this IServiceCollection services)
    {
        services.TryAddSingleton<AdminLibrariesApi>();
        services.TryAddSingleton(provider => new AdminLibrariesRealtimeSource(
            new EventChannelAdminRealtime(provider.GetRequiredService<EventChannelClient>())));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.Libraries, typeof(AdminLibrariesPage)));
        return services;
    }
}
