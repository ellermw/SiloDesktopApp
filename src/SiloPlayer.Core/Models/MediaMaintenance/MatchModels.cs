namespace SiloPlayer.Core.Models.MediaMaintenance;

public sealed class ItemMatchSearchRequest
{
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }
    public string? TvdbId { get; set; }
    public Dictionary<string, string>? ProviderIds { get; set; }
    public int? LibraryId { get; set; }
}

public sealed class MatchCandidate
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

public sealed class MatchTitleAlias
{
    public string Title { get; set; } = "";
    public string? Language { get; set; }
    public string Kind { get; set; } = "";
    public string? Provider { get; set; }
}

public sealed class ItemMatchSearchResponse
{
    public List<MatchCandidate> Candidates { get; set; } = [];
}

public sealed class ItemMatchApplyRequest
{
    public Dictionary<string, string> ProviderIds { get; set; } = new();
    public int? LibraryId { get; set; }
}
