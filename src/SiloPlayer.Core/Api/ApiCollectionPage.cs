namespace SiloPlayer.Core.Api;

public sealed class ApiCollectionPage<T>
{
    public List<T> Items { get; set; } = [];
    public ApiPageInfo? Page { get; set; }
}

public sealed class ApiPageInfo
{
    public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
}
