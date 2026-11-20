using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public static class AdminRequestsRegistration
{
    public static IServiceCollection AddAdminRequests(this IServiceCollection services)
    {
        services.AddSingleton<AdminRequestsApi>();
        services.AddSingleton<AdminRequestCountStore>();
        services.AddSingleton<IAdminNavBadgeSource>(provider => provider.GetRequiredService<AdminRequestCountStore>());
        services.AddSingleton(new AdminPageRegistration(AdminRoute.RequestQueue, typeof(AdminRequestsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.RequestDiscover, typeof(AdminRequestsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.RequestSettings, typeof(AdminRequestsPage)));
        return services;
    }
}
