using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminCollectionEntry
{
    public string Id { get; set; } = "";
    public string LibraryId { get; set; } = "";
    public List<string> LibraryIds { get; set; } = [];
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CollectionType { get; set; } = "manual";
    public string Visibility { get; set; } = "visible";
    public string? GroupId { get; set; }
    public int SortOrder { get; set; }
    public bool Featured { get; set; }
    public string PosterUrl { get; set; } = "";
    public string BackdropUrl { get; set; } = "";
    public string LastSyncStatus { get; set; } = "";
    public string LastSyncMessage { get; set; } = "";
    public string ManagementMode { get; set; } = "";
    public string ManagementSource { get; set; } = "";
    public int ItemCount { get; set; }
    public int RowCount { get; set; }
    public int HomeRowCount { get; set; }
    public string UpdatedAt { get; set; } = "";
    public string Kind => CollectionType == "manual" ? "manual" : CollectionType == "smart" ? "smart" : "synced";
    public bool InLibrary(string? id) => string.IsNullOrEmpty(id) || LibraryId == id || LibraryIds.Contains(id);
}

public sealed class AdminCollectionShelf
{
    public string Id { get; set; } = "";
    public string LibraryId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "regular";
    public string DefaultSortMode { get; set; } = "manual";
    public int SortOrder { get; set; }
}

public sealed class AdminCollectionsList
{
    public List<AdminCollectionEntry> Items { get; set; } = [];
    public List<AdminCollectionShelf> Groups { get; set; } = [];
    public ApiPageInfo? Page { get; set; }
}

public sealed class AdminCollectionsCapabilities
{
    public string State { get; set; } = "";
    public bool Groups { get; set; }
    public bool Imports { get; set; }
    public List<string> ImportSources { get; set; } = [];
    public bool Artwork { get; set; }
    public bool ItemReorder { get; set; }
    public bool SectionReferences { get; set; }
    public bool TemplateSummaries { get; set; }
    public bool MdblistSearch { get; set; }
}

public sealed record AdminCollectionSnapshot(JsonObject Value, string ETag, MonitorAuthorityLease Owner, string Path);
public sealed record AdminCollectionsBoard(string LibraryId, AdminCollectionsList List,
    AdminCollectionSnapshot GroupOrder, IReadOnlyDictionary<string, AdminCollectionSnapshot> Orders);
public sealed record AdminCollectionPreviewItem(string ContentId, string Title, string Type);
public sealed record AdminCollectionPreview(IReadOnlyList<AdminCollectionPreviewItem> Items, int Total);
public sealed record AdminMdbListEntry(string Id, string Name, string UserName, string Description, string MediaType, int Items, int Likes, string Url);
public sealed record AdminMdbListSearch(bool Configured, IReadOnlyList<AdminMdbListEntry> Lists);
public sealed record AdminCollectionDeletePlan(AdminCollectionSnapshot Collection, IReadOnlyList<JsonObject> Rows);

public sealed class AdminCollectionChangedException() : InvalidOperationException("It changed since you opened this. Reload and review before trying again.");
