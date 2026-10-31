namespace SiloPlayer.Core.Services.Updates;

/// <summary>A hash/size-verified manifest declares an unsupported contract requiring a separate full migration.</summary>
public sealed class UpdateMigrationRequiredException() : IOException("This update requires the full installer.");
