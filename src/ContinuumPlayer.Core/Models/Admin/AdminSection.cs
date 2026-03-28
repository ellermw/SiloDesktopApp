using System.Text.Json.Nodes;

namespace ContinuumPlayer.Core.Models.Admin;

public class AdminSection
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string SectionType { get; set; } = "";
    public int ItemLimit { get; set; } = 20;
    public bool Featured { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Scope { get; set; }
    public int? LibraryId { get; set; }
    public JsonObject? Config { get; set; }
    public int SortOrder { get; set; }
}
