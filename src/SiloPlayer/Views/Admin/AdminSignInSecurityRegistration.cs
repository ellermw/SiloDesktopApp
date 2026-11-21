using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public static class AdminSignInSecurityRegistration
{
    /// <summary>Production entry point also exercised by the synthetic native host. Security #175 stays registered separately.</summary>
    public static IServiceCollection AddAdminSignInSecurity(this IServiceCollection services)
    {
        services.AddSingleton<AdminApiKeysApi>(); services.AddTransient<AdminApiKeysController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.ApiKeys, typeof(AdminApiKeysPage)));
        services.AddSingleton<AdminSignInApi>(); services.AddTransient<AdminSignInController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.SignInSettings, typeof(AdminSignInSettingsPage)));
        services.AddSingleton<AdminPolicyApi>(); services.AddTransient<AdminPolicyController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.Policy, typeof(AdminPolicyPage)));
        return services;
    }
}
