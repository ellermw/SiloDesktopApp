using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

namespace SiloPlayer.Services;

/// <summary>Production registrations shared by App startup and the native synthetic host.</summary>
internal static class AdminConfigureServices
{
    internal static void Add(IServiceCollection services)
    {
        services.AddSingleton<AdminServerSettingsApi>();
        services.AddSingleton(sp => new AdminServerSettingsStore(sp.GetRequiredService<AdminServerSettingsApi>(),
            sp.GetRequiredService<AdminMonitorAuthority>(), AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets));
        services.AddTransient<AdminGeneralSettingsController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.GeneralSettings, typeof(AdminGeneralSettingsPage)));
    }
}
