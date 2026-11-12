using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Models.MediaMaintenance;

/// <summary>The selected semantics and authority used by both confirmation and submission.</summary>
public sealed record MetadataResetCapability(ApiRequestContext Owner, bool AtomicReset);
