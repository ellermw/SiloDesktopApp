namespace ContinuumPlayer.Core.Models.Admin;

public class StaleMediaId
{
    public string ContentId { get; set; } = "";
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public string ContentType { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string FirstSeenAt { get; set; } = "";
    public string LastSeenAt { get; set; } = "";
}

public class StaleMediaIdsResponse
{
    public List<StaleMediaId> Items { get; set; } = [];
}
