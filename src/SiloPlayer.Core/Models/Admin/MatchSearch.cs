namespace SiloPlayer.Core.Models.Admin;

public class ItemMatchSearchRequest
{
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }
    public string? TvdbId { get; set; }
}

public class MatchCandidate
{
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public string ContentType { get; set; } = "";
    public Dictionary<string, string> ProviderIds { get; set; } = new();
    public string ImageUrl { get; set; } = "";
    public string Overview { get; set; } = "";
    public List<string> Sources { get; set; } = [];
    public List<string> AgreementHints { get; set; } = [];
}

public class ItemMatchSearchResponse
{
    public List<MatchCandidate> Candidates { get; set; } = [];
}

public class ItemMatchApplyRequest
{
    public Dictionary<string, string> ProviderIds { get; set; } = new();
}

public class UnmatchedLibraryItem
{
    public string ContentId { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public string ContentType { get; set; } = "";
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = "";
    public string Status { get; set; } = "";
}

public class UnmatchedLibraryItemsResponse
{
    public List<UnmatchedLibraryItem> Items { get; set; } = [];
    public int Total { get; set; }
}
