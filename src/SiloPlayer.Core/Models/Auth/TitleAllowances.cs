namespace SiloPlayer.Core.Models.Auth;

public sealed class TitleAllowanceCapability
{
    public string State { get; set; } = "unsupported";
}

public sealed class TitleAllowanceProfile
{
    public string ProfileId { get; set; } = "";
    public string Name { get; set; } = "";
    public string MaxContentRating { get; set; } = "";
    public int? MaxAdvisoryAge { get; set; }
    public string State { get; set; } = "";
}

public sealed class AllowedCollection
{
    public string CollectionId { get; set; } = "";
    public string Name { get; set; } = "";
    public int ItemCount { get; set; }
    public bool Live { get; set; }
    public string AllowedAt { get; set; } = "";
}

public sealed class AllowCollectionTitlesResult
{
    public int Added { get; set; }
}
