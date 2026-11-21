using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public static class AdminCollectionsRegistration
{
    public static IServiceCollection AddAdminCollections(this IServiceCollection services)
    {
        services.TryAddSingleton<AdminCollectionsApi>();
        services.TryAddSingleton<AdminLibrariesApi>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.Collections, typeof(AdminCollectionsPage)));
        return services;
    }
}
