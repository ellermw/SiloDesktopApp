namespace SiloPlayer.Core.Services.Updates;

/// <summary>A verified, structurally valid package requires a separate full migration.</summary>
public sealed class UpdateMigrationRequiredException() : IOException("This update requires the full installer.");
