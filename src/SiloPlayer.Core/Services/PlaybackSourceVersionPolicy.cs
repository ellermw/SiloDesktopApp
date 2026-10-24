namespace SiloPlayer.Core.Services;

public static class PlaybackSourceVersionPolicy
{
    public static bool IsPinned(int? fileId, bool? explicitPin, bool audiobook = false, bool watchParty = false, bool receiver = false)
        => audiobook || watchParty || receiver || (explicitPin ?? fileId.HasValue);
}
