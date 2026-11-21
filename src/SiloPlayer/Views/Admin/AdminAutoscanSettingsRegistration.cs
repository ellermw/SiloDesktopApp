using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public static class AdminAutoscanSettingsRegistration
{
    public static IServiceCollection AddAdminAutoscanSettings(this IServiceCollection services)
    {
        services.TryAddSingleton<AdminAutoscanSettingsApi>();
        services.AddTransient<AdminAutoscanSettingsController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.AutoscanSettings, typeof(AdminAutoscanSettingsPage)));
        return services;
    }
}

