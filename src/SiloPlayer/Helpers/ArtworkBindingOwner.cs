using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Helpers;

/// <summary>A DTO bind owns artwork across native unload/reload and deferred presentation.</summary>
internal sealed record ArtworkBindingOwner(SiloApiClient Client, ApiIdentityContext Identity)
{
    internal bool IsCurrent => Client.IsCurrentIdentityContext(Identity);
    internal static ArtworkBindingOwner? Capture()
    {
        var client = App.Services.GetService<SiloApiClient>();
        return client == null ? null : new(client, client.CaptureIdentityContext());
    }
}
