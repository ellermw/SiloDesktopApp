using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

internal static class AllowanceDialogOwner
{
    internal static TitleAllowanceSession Capture(Func<bool> navigationCurrent, CancellationToken ct = default)
    {
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var auth = App.Services.GetRequiredService<AuthService>();
        var generation = auth.SessionGeneration;
        var actor = auth.SelectedProfile;
        return new(client, actor, () => auth.SessionGeneration == generation
            && auth.SelectedProfile is { IsPrimary: true } current && current.Id == actor?.Id && navigationCurrent(), ct);
    }
}
