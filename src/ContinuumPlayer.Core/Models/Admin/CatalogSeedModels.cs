namespace ContinuumPlayer.Core.Models.Admin;

public class CatalogPathRewrite
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public class CatalogSeedExportRequest
{
    public List<int>? LibraryIds { get; set; }
}

public class CatalogSeedExportResult
{
    public int FormatVersion { get; set; }
    public int SchemaVersion { get; set; }
    public int LibrariesExported { get; set; }
    public int ItemsExported { get; set; }
    public int CastExported { get; set; }
    public int CrewExported { get; set; }
    public int SeasonsExported { get; set; }
    public int EpisodesExported { get; set; }
    public int FilesExported { get; set; }
    public int LibraryLinksExported { get; set; }
}

public class CatalogSeedImportRequest
{
    public string Source { get; set; } = "";
    public string? LocalPath { get; set; }
    public string? ExportJobId { get; set; }
    public string? ArtifactKey { get; set; }
    public string? RemoteUrl { get; set; }
    public string ConflictMode { get; set; } = "skip_existing";
    public List<CatalogPathRewrite> PathRewrites { get; set; } = [];
}

public class CatalogSeedImportSource
{
    public string Key { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? LastModified { get; set; }
}

public class CatalogSeedImportSourcesResponse
{
    public List<CatalogSeedImportSource> Sources { get; set; } = [];
}

public class CatalogSeedImportResponse
{
    public int LibrariesCreated { get; set; }
    public int LibrariesMatched { get; set; }
    public int ItemsCreated { get; set; }
    public int ItemsUpdated { get; set; }
    public int SeasonsCreated { get; set; }
    public int SeasonsUpdated { get; set; }
    public int EpisodesCreated { get; set; }
    public int EpisodesUpdated { get; set; }
    public int FilesCreated { get; set; }
    public int FilesUpdated { get; set; }
    public int LinksCreated { get; set; }
    public int CreditsReplaced { get; set; }
    public int Skipped { get; set; }
    public List<string>? UnmatchedRoots { get; set; }
}
