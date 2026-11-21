using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

public static class AdminUsersRegistration
{
    public static IServiceCollection AddAdminUsers(this IServiceCollection services)
    {
        services.AddSingleton<AdminUsersApi>();
        services.AddSingleton(_ => new AdminUserAvatarImages());
        services.AddSingleton(new AdminPageRegistration(AdminRoute.Users, typeof(AdminUsersPage)));
        return services;
    }
}
