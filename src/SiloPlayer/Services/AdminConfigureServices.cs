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
        .Concat(AdminDownloadSettingsController.Keys).Concat(AdminDownloadSettingsController.ReadOnlyKeys).Concat(AdminStorageSettingsController.Keys).Concat(AdminSearchSettingsController.Keys).Concat(AdminBackupsController.Keys).Concat(AdminNotificationSettingsController.Keys)
        .Concat(AdminIntegrationsSettings.AllKeys).Concat(AdminLibrarySettingsController.Keys).Concat(AdminProvidersController.Keys).Concat(AdminPlaybackSettingsRules.Keys).Concat(AdminPlaybackSettingsRules.RoutingKeys).Distinct(StringComparer.Ordinal).ToArray());
    internal static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly(AdminGeneralSettingsController.Secrets.Concat(AdminEmailSettingsController.Secrets).Concat(AdminSecuritySettingsController.Secrets)
        .Concat(AdminDownloadSettingsController.Secrets).Concat(AdminStorageSettingsController.Secrets).Concat(AdminSearchSettingsController.Secrets).Concat(AdminBackupsController.Secrets).Concat(AdminNotificationSettingsController.Secrets)
        .Concat(AdminProvidersController.Secrets).Distinct(StringComparer.Ordinal).ToArray());

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
        services.AddSingleton<AdminIntegrationsApi>();
        services.AddSingleton<IAdminChatGptBrowser, AdminChatGptBrowser>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.GeneralSettings, typeof(AdminGeneralSettingsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.EmailSettings, typeof(AdminEmailSettingsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.SecuritySettings, typeof(AdminSecuritySettingsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.NetworkAccessSettings, typeof(AdminNetworkAccessSettingsPage)));
        services.AddTransient<AdminDownloadSettingsController>();
        services.AddSingleton<AdminSearchApi>();
        // Playback start keeps the network: the status poll defers while the player is loading.
        services.AddTransient(sp => new AdminSearchSettingsController(sp.GetRequiredService<AdminServerSettingsApi>(), sp.GetRequiredService<AdminSearchApi>(),
            sp.GetRequiredService<AdminServerSettingsStore>(), sp.GetRequiredService<AdminMonitorAuthority>(),
            deferBackground: () => sp.GetService<PlayerService>()?.IsLoading == true));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.SearchSettings, typeof(AdminSearchSettingsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.DownloadSettings, typeof(AdminDownloadSettingsPage)));
        services.AddSingleton<AdminLibrarySettingsApi>();
        services.AddTransient<AdminLibrarySettingsController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.LibrarySettings, typeof(AdminLibrarySettingsPage)));
        services.AddSingleton<AdminProvidersApi>();
        services.AddTransient<AdminProvidersController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.ProviderSettings, typeof(AdminProvidersPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.AiSettings, typeof(AdminIntegrationsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.Recommendations, typeof(AdminIntegrationsPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.CompatibilitySettings, typeof(AdminIntegrationsPage)));
        // Maintenance is a tool page (catalog seed export/import and read-only job history) with its own domain API.
        services.AddSingleton<AdminMaintenanceApi>();
        services.AddTransient<AdminMaintenanceController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.Maintenance, typeof(AdminMaintenancePage)));
        // Storage reuses the shared settings snapshot; status, transitions and jobs use their own domain API. The page owns its controller and polling.
        services.AddSingleton<AdminStorageApi>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.StorageSettings, typeof(AdminStorageSettingsPage)));
        services.AddSingleton<AdminTasksApi>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.ScheduledTasks, typeof(AdminScheduledTasksPage)));
        // Configure > Server > Backups: the list page and both restore routes share one page type.
        services.AddSingleton<AdminBackupsApi>();
        services.AddTransient(sp => new AdminBackupsController(sp.GetRequiredService<AdminBackupsApi>(), sp.GetRequiredService<AdminServerSettingsApi>(),
            sp.GetRequiredService<AdminServerSettingsStore>(), sp.GetRequiredService<AdminMonitorAuthority>(), download: sp.GetService<AdminBackupSignedDownload>()));
        services.AddTransient(sp => new AdminBackupRestoreController(sp.GetRequiredService<AdminBackupsApi>(), sp.GetRequiredService<AdminMonitorAuthority>()));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.Backups, typeof(AdminBackupsPage)));
        services.AddSingleton<AdminNotificationSettingsApi>();
        services.AddTransient<AdminNotificationSettingsController>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.NotificationSettings, typeof(AdminNotificationSettingsPage)));
        services.AddSingleton<AdminPlaybackAdministrationApi>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.Nodes, typeof(AdminNodesPage)));
        services.AddSingleton(new AdminPageRegistration(AdminRoute.PlaybackSettings, typeof(AdminPlaybackSettingsPage)));
    }
}
