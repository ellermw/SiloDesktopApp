using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Services;

public interface IAdminChatGptBrowser
{
    Task<bool> OpenAsync(Uri authorizationUrl, MonitorAuthorityLease lease);
}

internal sealed class AdminChatGptBrowser : IAdminChatGptBrowser
{
    public async Task<bool> OpenAsync(Uri authorizationUrl, MonitorAuthorityLease lease)
    {
        lease.ThrowIfRetired();
        if (!ExternalBrowserUrlPolicy.TryGetSafeUri(authorizationUrl.AbsoluteUri, out var safeUri) || !AdminIntegrationPolicy.IsAuthorizationUrl(safeUri)) return false;
        var opened = await Windows.System.Launcher.LaunchUriAsync(safeUri, new Windows.System.LauncherOptions { TreatAsUntrusted = true });
        lease.ThrowIfRetired(); return opened;
    }
}
