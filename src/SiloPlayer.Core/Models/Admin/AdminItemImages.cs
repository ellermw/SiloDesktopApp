namespace SiloPlayer.Core.Models.Admin;

public sealed class RemoteItemImage
{
    public string ProviderId { get; set; } = "";
    public string Url { get; set; } = "";
    public string OriginalUrl { get; set; } = "";
    public string Type { get; set; } = "";
    public string Language { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public double Rating { get; set; }
}

public sealed class CurrentItemImages
{
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public string? LogoUrl { get; set; }
}

public sealed class ItemImagesResponse
{
    public List<RemoteItemImage> Images { get; set; } = [];
    public CurrentItemImages Current { get; set; } = new();
    public Dictionary<string, string>? ProviderErrors { get; set; }
}

public sealed class ApplyItemImageRequest
{
    public string OriginalUrl { get; set; } = "";
    public string Type { get; set; } = "";
    public string ProviderId { get; set; } = "";
}

public sealed class ApplyItemImageResponse
{
    public string ContentId { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public string Thumbhash { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string? Revision { get; set; }
}
