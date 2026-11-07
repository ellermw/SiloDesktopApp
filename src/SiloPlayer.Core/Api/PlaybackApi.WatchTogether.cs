using SiloPlayer.Core.Models;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Api;

public partial class PlaybackApi
{
    public Task<WatchTogetherRoomResponse> CreateWatchTogetherRoomAsync(string selectionMode = "host_pick", CancellationToken ct = default)
        => RoomRequestAsync(HttpMethod.Post, "/api/v2/watch-together/rooms", new Dictionary<string, object?>
        {
            ["room_id"] = Guid.NewGuid().ToString(),
            ["selection_mode"] = selectionMode,
        }, null, ct);

    public Task<WatchTogetherRoomResponse> JoinWatchTogetherRoomAsync(string? code, string? joinToken, CancellationToken ct = default)
        => RoomRequestAsync(HttpMethod.Post, "/api/v2/watch-together/join",
            !string.IsNullOrEmpty(joinToken)
                ? new Dictionary<string, object?> { ["join_token"] = joinToken }
                : new Dictionary<string, object?> { ["code"] = code ?? "" }, null, ct);

    public Task<WatchTogetherRoomResponse> GetWatchTogetherRoomAsync(string roomId, string roomToken, CancellationToken ct = default)
        => RoomRequestAsync(HttpMethod.Get, RoomPath(roomId), null, RoomProof(roomToken), ct);

    public Task<WatchTogetherRoomResponse> UpdateWatchTogetherRoomPolicyAsync(string roomId, string guestControlPolicy, CancellationToken ct = default)
        => RoomRequestAsync(HttpMethod.Patch, RoomPath(roomId) + "/policy",
            new Dictionary<string, object?> { ["guest_control_policy"] = guestControlPolicy }, null, ct);

    public Task<WatchTogetherRoomResponse> SelectWatchTogetherRoomItemAsync(string roomId, string contentId, int? fileId = null, int? libraryId = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["content_id"] = contentId };
        if (fileId.HasValue) body["file_id"] = fileId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (libraryId.HasValue) body["library_id"] = libraryId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return RoomRequestAsync(HttpMethod.Put, RoomPath(roomId) + "/selection", body, null, ct);
    }

    public Task<WatchTogetherCapabilities> GetWatchTogetherCapabilitiesAsync(CancellationToken ct = default)
        => client.GetAsync<WatchTogetherCapabilities>("/api/v2/watch-together/capabilities", ct);

    public Task<WatchTogetherRoomResponse> StageWatchTogetherRoomItemAsync(string roomId, string contentId, int? fileId = null, int? libraryId = null, CancellationToken ct = default,
        ApiIdentityContext? expectedIdentityContext = null)
    {
        var body = new Dictionary<string, object?> { ["content_id"] = contentId };
        if (fileId.HasValue) body["file_id"] = fileId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (libraryId.HasValue) body["library_id"] = libraryId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return RoomRequestAsync(HttpMethod.Put, RoomPath(roomId) + "/staged-selection", body, null, ct, expectedIdentityContext);
    }
    public Task<WatchTogetherRoomResponse> StartWatchTogetherRoomPlaybackAsync(string roomId, CancellationToken ct = default)
        => RoomRequestAsync(HttpMethod.Post, RoomPath(roomId) + "/playback/start", null, null, ct);
    public Task<WatchTogetherRoomResponse> UpdateWatchTogetherSelectionModeAsync(string roomId, string selectionMode, CancellationToken ct = default)
        => RoomRequestAsync(HttpMethod.Patch, RoomPath(roomId) + "/selection-mode", new { selection_mode = selectionMode }, null, ct);
    public Task<WatchTogetherPickerResponse> GetWatchTogetherPickerAsync(string roomId, string roomToken, CancellationToken ct = default)
        => client.SendRequestAsync<WatchTogetherPickerResponse>(HttpMethod.Get, RoomPath(roomId) + "/picker", null, RoomProof(roomToken), ct);
    public Task<WatchTogetherMemberStateResponse> GetWatchTogetherMemberStateAsync(string roomId, string roomToken, IEnumerable<string> contentIds, CancellationToken ct = default)
    {
        var ids = contentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct().ToArray();
        if (ids.Length > 200) throw new ArgumentOutOfRangeException(nameof(contentIds));
        return client.SendRequestAsync<WatchTogetherMemberStateResponse>(HttpMethod.Post, RoomPath(roomId) + "/member-state", new { content_ids = ids }, RoomProof(roomToken), ct);
    }

    public Task CloseWatchTogetherRoomAsync(string roomId, CancellationToken ct = default)
        => client.DeleteAsync(RoomPath(roomId), ct);

    public Task<WatchTogetherSuggestionsResponse> ListWatchTogetherSuggestionsAsync(string roomId, string roomToken, CancellationToken ct = default)
        => ListWatchTogetherSuggestionsAsync(roomId, roomToken, ct, null);

    private async Task<WatchTogetherSuggestionsResponse> ListWatchTogetherSuggestionsAsync(string roomId, string roomToken, CancellationToken ct,
        ApiIdentityContext? expectedIdentityContext)
    {
        var authority = client.CaptureContext();
        var result = new WatchTogetherSuggestionsResponse();
        var path = RoomPath(roomId) + "/suggestions?limit=100";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            EnsureRoomAuthority(authority, expectedIdentityContext);
            var page = expectedIdentityContext is { } identity
                ? await client.SendIdentityBoundRequestAsync<ApiCollectionPage<WatchTogetherSuggestion>>(
                    identity, HttpMethod.Get, path, null, ct, RoomProof(roomToken)).ConfigureAwait(false)
                : await client.SendRequestAsync<ApiCollectionPage<WatchTogetherSuggestion>>(
                    HttpMethod.Get, path, null, RoomProof(roomToken), ct).ConfigureAwait(false);
            EnsureRoomAuthority(authority, expectedIdentityContext);
            result.Suggestions.AddRange(page.Items);
            if (page.Page?.HasMore != true) return result;
            var cursor = page.Page.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seen.Add(cursor)) throw new InvalidDataException("Invalid room suggestions cursor.");
            path = RoomPath(roomId) + "/suggestions?limit=100&cursor=" + Uri.EscapeDataString(cursor);
        }
    }

    public Task<WatchTogetherSuggestionsResponse> CreateWatchTogetherSuggestionAsync(string roomId, string roomToken,
        string contentId, string contentType, string title, string? subtitle = null, string? posterUrl = null, string? note = null, CancellationToken ct = default,
        ApiIdentityContext? expectedIdentityContext = null)
        => MutateSuggestionsAsync(HttpMethod.Post, roomId, roomToken, "",
            SuggestionBody(contentId, contentType, title, subtitle, posterUrl, note), ct, expectedIdentityContext);

    // Movie entry only needs the accepted write before navigating. A separate
    // suggestions read must not turn that acceptance into a retryable failure.
    public Task SubmitWatchTogetherSuggestionAsync(string roomId, string roomToken,
        string contentId, string contentType, string title, ApiIdentityContext expectedIdentityContext,
        string? subtitle = null, string? posterUrl = null, string? note = null, CancellationToken ct = default)
        => WriteSuggestionsAsync(HttpMethod.Post, roomId, roomToken, "",
            SuggestionBody(contentId, contentType, title, subtitle, posterUrl, note), ct, expectedIdentityContext);

    private static Dictionary<string, object?> SuggestionBody(string contentId, string contentType, string title,
        string? subtitle, string? posterUrl, string? note)
        => new()
        {
            ["suggestion_id"] = Guid.NewGuid().ToString(), ["content_id"] = contentId,
            ["content_type"] = contentType, ["title"] = title, ["subtitle"] = subtitle ?? "",
            ["poster_url"] = posterUrl ?? "", ["note"] = note ?? "",
        };

    public Task<WatchTogetherSuggestionsResponse> DeleteWatchTogetherSuggestionAsync(string roomId, string roomToken, string suggestionId, CancellationToken ct = default)
        => MutateSuggestionsAsync(HttpMethod.Delete, roomId, roomToken, "/" + Uri.EscapeDataString(suggestionId), null, ct);

    public Task<WatchTogetherSuggestionsResponse> VoteWatchTogetherSuggestionAsync(string roomId, string roomToken, string suggestionId, CancellationToken ct = default)
        => MutateSuggestionsAsync(HttpMethod.Post, roomId, roomToken, "/" + Uri.EscapeDataString(suggestionId) + "/vote", null, ct);

    public Task<WatchTogetherSuggestionsResponse> UnvoteWatchTogetherSuggestionAsync(string roomId, string roomToken, string suggestionId, CancellationToken ct = default)
        => MutateSuggestionsAsync(HttpMethod.Delete, roomId, roomToken, "/" + Uri.EscapeDataString(suggestionId) + "/vote", null, ct);

    private async Task<WatchTogetherSuggestionsResponse> MutateSuggestionsAsync(HttpMethod method, string roomId, string roomToken, string suffix, object? body, CancellationToken ct,
        ApiIdentityContext? expectedIdentityContext = null)
    {
        await WriteSuggestionsAsync(method, roomId, roomToken, suffix, body, ct, expectedIdentityContext).ConfigureAwait(false);
        return await ListWatchTogetherSuggestionsAsync(roomId, roomToken, ct, expectedIdentityContext).ConfigureAwait(false);
    }

    private async Task WriteSuggestionsAsync(HttpMethod method, string roomId, string roomToken, string suffix, object? body, CancellationToken ct,
        ApiIdentityContext? expectedIdentityContext)
    {
        var authority = client.CaptureContext();
        EnsureRoomIntentOwner(authority, expectedIdentityContext, ct);
        var path = RoomPath(roomId) + "/suggestions" + suffix;
        if (expectedIdentityContext is { } owner)
            await client.SendIdentityBoundNoContentAsync(owner, method, path, body, ct, RoomProof(roomToken)).ConfigureAwait(false);
        else
            await client.SendNoContentRequestAsync(method, path, body, RoomProof(roomToken), ct).ConfigureAwait(false);
        EnsureRoomIntentOwner(authority, expectedIdentityContext, ct);
    }

    public Task<WatchTogetherRoomResponse> PromoteWatchTogetherSuggestionAsync(string roomId, string roomToken, string suggestionId, CancellationToken ct = default)
        => RoomRequestAsync(HttpMethod.Post, RoomPath(roomId) + "/suggestions/promote",
            new Dictionary<string, object?> { ["suggestion_id"] = suggestionId }, RoomProof(roomToken), ct);

    public async Task<PlaybackControlTicket> CreateRoomControlTicketAsync(string roomId, string roomToken, CancellationToken ct = default)
    {
        var authority = client.CaptureContext();
        var ticket = await client.SendRequestAsync<PlaybackControlTicket>(HttpMethod.Post, RoomPath(roomId) + "/ws-ticket", null, RoomProof(roomToken), ct).ConfigureAwait(false);
        EnsureAuthority(authority);
        if (ticket.Protocol != "silo.room.v2" || string.IsNullOrWhiteSpace(ticket.Ticket))
            throw new InvalidOperationException("Invalid Watch Together socket ticket.");
        return ticket;
    }

    private static string RoomPath(string roomId) => "/api/v2/watch-together/rooms/" + Uri.EscapeDataString(roomId);
    private static Dictionary<string, string> RoomProof(string roomToken) => new() { ["X-Room-Token"] = roomToken };

    private async Task<WatchTogetherRoomResponse> RoomRequestAsync(HttpMethod method, string path, object? body,
        IReadOnlyDictionary<string, string>? headers, CancellationToken ct, ApiIdentityContext? expectedIdentityContext = null)
    {
        var authority = client.CaptureContext();
        EnsureRoomIntentOwner(authority, expectedIdentityContext, ct);
        var response = expectedIdentityContext is { } owner
            ? await client.SendIdentityBoundRequestAsync<WatchTogetherRoomResponse>(owner, method, path, body, ct, headers).ConfigureAwait(false)
            : await client.SendRequestAsync<WatchTogetherRoomResponse>(method, path, body, headers, ct).ConfigureAwait(false);
        EnsureRoomIntentOwner(authority, expectedIdentityContext, ct);
        return response;
    }

    private void EnsureRoomAuthority(ApiRequestContext readOwner, ApiIdentityContext? identity)
    {
        // Only accepted playback actions opt in; ordinary room reads still retire
        // on access revisions. The server authorizes every identity-bound action.
        EnsureRoomIntentOwner(readOwner, identity, CancellationToken.None);
    }

    private void EnsureRoomIntentOwner(ApiRequestContext authority, ApiIdentityContext? identity, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (identity is not { } owner) EnsureAuthority(authority);
        else if (!client.IsCurrentIdentityContext(owner)) throw new OperationCanceledException("Room intent identity changed.", ct);
    }
}
