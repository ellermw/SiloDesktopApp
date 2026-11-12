using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Messaging;

public sealed record HomeDismissalApplied(string Surface, MediaItem Item, ApiRequestContext Context);

/// <summary>The captured owner's dismissal was reverted on the server by a surface other than Home.</summary>
public sealed record HomeDismissalUndone(string Surface, string ItemId, ApiRequestContext Context);
