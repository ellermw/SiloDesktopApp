namespace SiloPlayer.Services;

/// <summary>Moves one live toast host; presentation changes never recreate its contents.</summary>
internal sealed class ToastHostPlacement<T>(T content, Action<T, bool> move) where T : class
{
    internal T Content { get; } = content;
    internal bool IsPlaybackOverlay { get; private set; }

    internal void Update(bool playbackCoversShell, bool surfaceVisible, bool hasToasts)
    {
        var useOverlay = playbackCoversShell && surfaceVisible && hasToasts;
        if (useOverlay == IsPlaybackOverlay) return;
        move(Content, useOverlay);
        IsPlaybackOverlay = useOverlay;
    }
}
