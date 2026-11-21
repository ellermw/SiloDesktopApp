using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public static class AdminHomeSectionsRegistration
{
    public static IServiceCollection AddAdminHomeSections(this IServiceCollection services)
    {
        services.TryAddSingleton<AdminHomeSectionsApi>();
        services.TryAddSingleton<AdminServerSettingsApi>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.HomeSections, typeof(AdminHomeSectionsPage)));
        return services;
    }
}
