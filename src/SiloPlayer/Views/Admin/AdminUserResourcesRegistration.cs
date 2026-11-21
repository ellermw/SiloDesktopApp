using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public static class AdminUserResourcesRegistration
{
    public static IServiceCollection AddAdminUserResources(this IServiceCollection services)
    {
        services.AddSingleton<AdminUserResourcesApi>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.AccessGroups, typeof(AdminAccessGroupsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.Devices, typeof(AdminDevicesPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.HistoryImport, typeof(AdminHistoryImportPage)));
        return services;
    }
}
