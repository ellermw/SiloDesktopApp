using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public static class AdminAutoscanRegistration
{
    public static IServiceCollection AddAdminAutoscanSources(this IServiceCollection services)
    {
        services.TryAddSingleton<AdminAutoscanApi>();
        services.TryAddSingleton<AdminLibrariesApi>();
        services.AddTransient<AdminAutoscanController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.AutoscanSources, typeof(AdminAutoscanSourcesPage)));
        return services;
    }
}
