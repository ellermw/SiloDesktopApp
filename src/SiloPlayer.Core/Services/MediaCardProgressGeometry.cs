namespace SiloPlayer.Core.Services;

public readonly record struct MediaCardProgressLayout(
    double HorizontalInset,
    double BottomInset,
    double TrackWidth,
    double FillWidth);

/// <summary>Calculates the current WebUI's inset wide-card progress track.</summary>
public static class MediaCardProgressGeometry
{
    public static MediaCardProgressLayout Calculate(
        double cardWidth,
        double ratio,
        bool episodeCard)
    {
        var horizontalInset = episodeCard ? 8d : 10d;
        var bottomInset = episodeCard ? 6d : 8d;
        var trackWidth = Math.Max(0, cardWidth - (horizontalInset * 2));
        var fillWidth = trackWidth * Math.Clamp(ratio, 0, 1);
        return new MediaCardProgressLayout(
            horizontalInset,
            bottomInset,
            trackWidth,
            fillWidth);
    }
}
