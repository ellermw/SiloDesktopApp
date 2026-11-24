using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class PlaybackApi
{
    /// <summary>
    /// <c>POST /api/v2/watch-together/rooms/{room_id}/source-fallback</c> (fallbackWatchTogetherSource):
    /// asks the room to replace its shared source after this member's player was refused it. Sent under the
    /// captured request context and the room proof, never with an authentication retry: a replay could
    /// run under another account or profile. The reply is rejected when the context changed while it was
    /// pending or when it describes another room.
    /// </summary>
    public async Task<WatchTogetherRoomResponse> FallbackWatchTogetherSourceAsync(ApiRequestContext context,
        string roomId, string roomToken, int selectionRevision, int failedFileId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(roomToken) || selectionRevision <= 0 || failedFileId <= 0 ||
            !WatchPartySourceFallback.IsFallbackReason(reason))
            throw new InvalidDataException(AppStrings.Get("web.common.errors.sourceFallbackIdentityInvalid", fallback: "Invalid source fallback identity."));
        EnsureAuthority(context);
        var response = await client.SendRequestWithoutRefreshAsync<WatchTogetherRoomResponse>(context, HttpMethod.Post,
            RoomPath(roomId) + "/source-fallback", new Dictionary<string, object?>
            {
                ["selection_revision"] = selectionRevision,
                ["failed_file_id"] = failedFileId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["reason"] = reason,
            }, ct, headers: RoomProof(roomToken)).ConfigureAwait(false);
        EnsureAuthority(context);
        if (response.Room.RoomId != roomId || string.IsNullOrEmpty(response.RoomAccessToken))
            throw new InvalidDataException(AppStrings.Get("web.common.errors.roomSnapshotInvalid", fallback: "Invalid room snapshot."));
        return response;
    }
}
