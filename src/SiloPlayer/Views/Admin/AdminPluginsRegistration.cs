using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>Registers Configure › Integrations › Plugins (/admin/plugins and /admin/plugins/:pluginId). Its update check reads and runs the shared check_plugin_updates task through the Tasks API.</summary>
public static class AdminPluginsRegistration
{
    public static IServiceCollection AddAdminPlugins(this IServiceCollection services)
    {
        services.AddSingleton<AdminPluginsApi>();
        services.TryAddSingleton<AdminTasksApi>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.Plugins, typeof(AdminPluginsPage)));
        return services;
    }
}
