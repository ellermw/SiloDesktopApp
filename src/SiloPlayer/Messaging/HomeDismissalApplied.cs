using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Messaging;

public sealed record HomeDismissalApplied(string Surface, MediaItem Item, ApiRequestContext Context);
