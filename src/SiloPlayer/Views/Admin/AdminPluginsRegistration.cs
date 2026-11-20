using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public static class AdminPluginsRegistration
{
    public static IServiceCollection AddAdminPlugins(this IServiceCollection services)
    {
        services.AddSingleton<AdminPluginsApi>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.Plugins, typeof(AdminPluginsPage)));
        return services;
    }
}
