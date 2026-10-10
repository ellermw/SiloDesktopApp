namespace SiloPlayer.Core.Services;

/// <summary>Current Silo DefaultArtwork type marks and container-relative geometry.</summary>
public static class DefaultArtworkStyle
{
    public static string Icon(string? mediaType) => mediaType switch
    {
        "series" or "season" or "episode" => "tv",
        "audiobook" or "book" or "books" or "podcast" or "podcasts" => "headphones",
        "ebook" or "ebooks" or "manga" or "comic" or "comics" => "book",
        _ => "film"
    };

    public static double MarkSize(double width) => Math.Clamp(width * .24, 14, 40);
    public static double GlowRadius(double width, double height, double fraction) => Math.Max(width, height) * fraction;
}
