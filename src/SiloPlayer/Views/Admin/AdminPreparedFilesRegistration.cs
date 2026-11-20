using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public static class AdminPreparedFilesRegistration
{
    /// <summary>Registers only Manage > Downloads > Prepared Files. Viewer Downloads and Download Settings are separate.</summary>
    public static IServiceCollection AddAdminPreparedFiles(this IServiceCollection services)
    {
        services.AddSingleton<AdminPreparedFilesApi>();
        services.AddSingleton(new AdminPageRegistration(AdminRoute.PreparedFiles, typeof(AdminPreparedFilesPage)));
        return services;
    }
}
