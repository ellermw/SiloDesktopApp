using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public sealed class AdminAutoscanActivityRealtime(IAdminRealtimeChannel channel)
{
    public IAdminRealtimeChannel Channel { get; } = channel;
}

public static class AdminAutoscanActivityRegistration
{
    public static IServiceCollection AddAdminAutoscanActivity(this IServiceCollection services)
    {
        services.TryAddSingleton<AdminAutoscanActivityApi>();
        services.TryAddSingleton(provider => new AdminAutoscanActivityRealtime(
            new EventChannelAdminRealtime(provider.GetRequiredService<EventChannelClient>())));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.AutoscanActivity, typeof(AdminAutoscanActivityPage)));
        return services;
    }
}
