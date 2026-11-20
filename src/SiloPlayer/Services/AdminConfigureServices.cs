using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

namespace SiloPlayer.Services;

/// <summary>Production registrations shared by App startup and the native synthetic host.</summary>
internal static class AdminConfigureServices
{
    /// <summary>Keys and protected keys read by the one shared settings snapshot store.</summary>
    internal static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(AdminGeneralSettingsController.Keys.Concat(AdminEmailSettingsController.Keys).Concat(AdminSecuritySettingsController.Keys)
        .Concat(AdminDownloadSettingsController.Keys).Concat(AdminDownloadSettingsController.ReadOnlyKeys)
        .Concat(AdminLibrarySettingsController.Keys).ToArray());
    internal static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly(AdminGeneralSettingsController.Secrets.Concat(AdminEmailSettingsController.Secrets).Concat(AdminSecuritySettingsController.Secrets)
        .Concat(AdminDownloadSettingsController.Secrets).ToArray());

    internal static void Add(IServiceCollection services)
    {
        services.AddSingleton<AdminServerSettingsApi>();
        services.AddSingleton(sp => new AdminServerSettingsStore(sp.GetRequiredService<AdminServerSettingsApi>(),
            sp.GetRequiredService<AdminMonitorAuthority>(), Keys, Secrets));
        services.AddTransient<AdminGeneralSettingsController>();
        services.AddTransient<AdminEmailSettingsController>();
        services.AddSingleton<AdminRateLimitApi>();
        services.AddTransient<AdminSecuritySettingsController>();
        // Network access is a tool page with its own domain API; the page owns its controller and polling.
        services.AddSingleton<AdminNetworkAccessApi>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.GeneralSettings, typeof(AdminGeneralSettingsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.EmailSettings, typeof(AdminEmailSettingsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.SecuritySettings, typeof(AdminSecuritySettingsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.NetworkAccessSettings, typeof(AdminNetworkAccessSettingsPage)));
        services.AddTransient<AdminDownloadSettingsController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.DownloadSettings, typeof(AdminDownloadSettingsPage)));
        services.AddSingleton<AdminLibrarySettingsApi>();
        services.AddTransient<AdminLibrarySettingsController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.LibrarySettings, typeof(AdminLibrarySettingsPage)));
    }
}
