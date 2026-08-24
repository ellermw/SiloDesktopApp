namespace SiloPlayer.Core.Models.Admin;

public class ItemMatchSearchRequest
{
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }
    public string? TvdbId { get; set; }
    public Dictionary<string, string>? ProviderIds { get; set; }
    public int? LibraryId { get; set; }
}

public class MatchCandidate
{
    public string Title { get; set; } = "";
    public string? OriginalTitle { get; set; }
    public List<MatchTitleAlias> Aliases { get; set; } = [];
    public string? TitleLanguage { get; set; }
    public bool TitleIsFallback { get; set; }
    public string? MatchedTitle { get; set; }
    public double? MatchScore { get; set; }
    public List<string> MatchReasons { get; set; } = [];
    public int Year { get; set; }
    public string ContentType { get; set; } = "";
    public Dictionary<string, string> ProviderIds { get; set; } = new();
    public string ImageUrl { get; set; } = "";
    public string Overview { get; set; } = "";
    public List<string> Sources { get; set; } = [];
    public List<string> AgreementHints { get; set; } = [];
}

public class MatchTitleAlias
{
    public string Title { get; set; } = "";
    public string? Language { get; set; }
    public string Kind { get; set; } = "";
    public string? Provider { get; set; }
}

public class ItemMatchSearchResponse
{
    public List<MatchCandidate> Candidates { get; set; } = [];
}

public class ItemMatchApplyRequest
{
    public Dictionary<string, string> ProviderIds { get; set; } = new();
    public int? LibraryId { get; set; }
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
