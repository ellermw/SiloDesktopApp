namespace SiloPlayer.Core.Models.Admin;

public class AdminSection
{
    public string Id { get; set; } = "";
    public string Scope { get; set; } = "";
    public int? LibraryId { get; set; }
    public int Position { get; set; }
    public string SectionType { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Featured { get; set; }
    public int ItemLimit { get; set; } = 20;
    public Dictionary<string, object>? Config { get; set; }
    public bool Enabled { get; set; } = true;
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class AdminSectionsListResponse
{
    public List<AdminSection> Sections { get; set; } = [];
}

public sealed class AdminSectionPreviewRequest
{
    public string SectionType { get; set; } = "";
    public Dictionary<string, object?> Config { get; set; } = [];
    public int? ItemLimit { get; set; }
    public int? LibraryId { get; set; }
    public List<int>? LibraryIds { get; set; }
}

public sealed class AdminSectionPreviewResponse
{
    public List<AdminSectionPreviewItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
}

public sealed class AdminSectionPreviewItem
{
    public string ContentId { get; set; } = "";
    public string? Title { get; set; }
    public string? PosterPath { get; set; }
}
