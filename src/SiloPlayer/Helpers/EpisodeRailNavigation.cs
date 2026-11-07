namespace SiloPlayer.Helpers;

/// <summary>Separates deliberate horizontal wheel intent from ordinary page scrolling.</summary>
internal static class EpisodeRailNavigation
{
    internal static double HorizontalWheelDelta(int delta, bool horizontalWheel, bool shift, double cardStride)
        => horizontalWheel ? delta / 120d * cardStride : shift ? -delta / 120d * cardStride : 0;
}
