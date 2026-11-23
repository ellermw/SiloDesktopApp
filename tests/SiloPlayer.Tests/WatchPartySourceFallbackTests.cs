using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

// Official reference: silo-server 3b2b32168, watchTogetherSourceFallback.ts, WatchPage.tsx
// (fallbackKey/fallingBack) and watchTogetherSocket.test.tsx (the source-fallback cases).
public sealed class WatchPartySourceFallbackTests
{
    private static WatchTogetherRoomSnapshot Room(int revision = 1, int? file = 7, int generation = 5, bool connectedSelf = true) => new()
    {
        RoomId = "room", Phase = "playing", SelectionRevision = revision, SelectedFileId = file, Generation = generation,
        SelectedContentId = "movie",
        Members = [new() { UserId = 1, ProfileId = "p", IsSelf = connectedSelf, Connected = connectedSelf }],
    };

    private static WatchPartySourceFallbackInput Input(WatchTogetherRoomSnapshot? room = null, string? roomId = "room",
        string? token = "proof", string? connection = "connected", int? file = 7, string? reason = "no_alternate_version",
        string? attempt = "attempt-1")
        => new(roomId, token, room ?? Room(), connection, file, reason, attempt);

    [Theory]
    [InlineData("no_alternate_version")]
    [InlineData("hdr_transcode_unsupported")]
    [InlineData("subtitle_conversion_unsupported")]
    [InlineData("transcoding_disabled")]
    public void TheFourOfficialReasonsEarnAFallback(string reason)
    {
        var plan = WatchPartySourceFallback.Plan(Input(reason: reason));
        Assert.NotNull(plan);
        Assert.Equal(reason, plan.Reason);
        Assert.Equal(1, plan.SelectionRevision);
        Assert.Equal(7, plan.FailedFileId);
        Assert.Equal("room", plan.RoomId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("source_unavailable")]
    [InlineData("dv_conversion_unsupported")]
    [InlineData("audio_transcoding_disabled")]
    [InlineData("capacity_unavailable")]
    [InlineData("recovery_exhausted")]
    [InlineData("No_Alternate_Version")]
    [InlineData(" transcoding_disabled")]
    public void AnyOtherReasonNeverFallsBack(string? reason)
    {
        Assert.False(WatchPartySourceFallback.IsFallbackReason(reason));
        Assert.Null(WatchPartySourceFallback.Plan(Input(reason: reason)));
    }

    [Fact]
    public void NeverForAFileThatIsNotTheRoomsSelection()
    {
        Assert.Null(WatchPartySourceFallback.Plan(Input(file: 8)));
        Assert.Null(WatchPartySourceFallback.Plan(Input(room: Room(file: null))));
        Assert.Null(WatchPartySourceFallback.Plan(Input(file: null)));
        Assert.Null(WatchPartySourceFallback.Plan(Input(file: 0, room: Room(file: 0))));
    }

    [Theory]
    [InlineData(false, "connected")]
    [InlineData(true, "connecting")]
    [InlineData(true, "reconnecting")]
    [InlineData(true, "disconnected")]
    [InlineData(true, null)]
    public void OnlyAConnectedMemberOfAConnectedRoomSends(bool selfConnected, string? connection)
    {
        Assert.Null(WatchPartySourceFallback.Plan(Input(room: Room(connectedSelf: selfConnected), connection: connection)));
    }

    [Fact]
    public void ASelfMemberWhoIsNotConnectedDoesNotSend()
    {
        var room = Room();
        room.Members[0].Connected = false;
        Assert.Null(WatchPartySourceFallback.Plan(Input(room: room)));
        var other = Room(connectedSelf: false);
        other.Members.Add(new() { UserId = 2, ProfileId = "q", IsSelf = false, Connected = true });
        Assert.Null(WatchPartySourceFallback.Plan(Input(room: other)));
    }

    [Fact]
    public void NeedsTheRoomItsProofAndAPlaybackAttempt()
    {
        Assert.Null(WatchPartySourceFallback.Plan(Input(roomId: null)));
        Assert.Null(WatchPartySourceFallback.Plan(Input(roomId: "")));
        Assert.Null(WatchPartySourceFallback.Plan(Input(token: null)));
        Assert.Null(WatchPartySourceFallback.Plan(Input(token: "")));
        Assert.Null(WatchPartySourceFallback.Plan(Input(roomId: "other")));
        Assert.Null(WatchPartySourceFallback.Plan(new("room", "proof", null, "connected", 7, "no_alternate_version", "a")));
        Assert.Null(WatchPartySourceFallback.Plan(Input(attempt: null)));
        Assert.Null(WatchPartySourceFallback.Plan(Input(attempt: " ")));
    }

    [Fact]
    public void ARevisionThatIsNotPositiveIsAnInvalidIdentity()
    {
        Assert.Null(WatchPartySourceFallback.Plan(Input(room: Room(revision: 0))));
        Assert.Null(WatchPartySourceFallback.Plan(Input(room: Room(revision: -1))));
    }

    [Fact]
    public void TheKeyIsUniquePerRoomProofRevisionFileAttemptAndReason()
    {
        var baseKey = WatchPartySourceFallback.Plan(Input())!.Key;
        Assert.Equal(baseKey, WatchPartySourceFallback.Plan(Input())!.Key);
        var others = new[]
        {
            Input(token: "renewed-proof"),
            Input(room: Room(revision: 2)),
            Input(room: Room(file: 8), file: 8),
            Input(attempt: "attempt-2"),
            Input(reason: "transcoding_disabled"),
        };
        foreach (var input in others)
            Assert.NotEqual(baseKey, WatchPartySourceFallback.Plan(input)!.Key);
        var roomB = Room(); roomB.RoomId = "room-b";
        Assert.NotEqual(baseKey, WatchPartySourceFallback.Plan(Input(room: roomB, roomId: "room-b"))!.Key);
    }

    [Fact]
    public void TheKeyNeverHoldsTheRoomProof()
    {
        Assert.DoesNotContain("proof", WatchPartySourceFallback.Plan(Input())!.Key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARoomPlaybackAttemptSendsOnce()
    {
        var gate = new WatchPartySourceFallbackGate();
        var key = WatchPartySourceFallback.Plan(Input())!.Key;
        Assert.True(gate.TryClaim(key));
        Assert.False(gate.TryClaim(key));
        // A fresh playback attempt may try again; the earlier key stays used.
        var next = WatchPartySourceFallback.Plan(Input(attempt: "attempt-2"))!.Key;
        Assert.True(gate.TryClaim(next));
        Assert.False(gate.TryClaim(key));
        Assert.False(gate.TryClaim(next));
    }

    [Fact]
    public void AMissingCapabilitySendsNothing()
    {
        Assert.True(WatchPartySourceFallback.Supports(["fixed_media_file_v1", "watch_party_source_fallback_v1"]));
        Assert.False(WatchPartySourceFallback.Supports(["fixed_media_file_v1", "watch_party_coordinator_v1"]));
        Assert.False(WatchPartySourceFallback.Supports([]));
        Assert.False(WatchPartySourceFallback.Supports(null));
        Assert.False(WatchPartySourceFallback.Supports(["Watch_Party_Source_Fallback_V1"]));
    }

    [Fact]
    public void AReplyThatAdvancesTheSelectionIsAReplacement()
    {
        var plan = WatchPartySourceFallback.Plan(Input())!;
        var incoming = Room(revision: 2, file: 8, generation: 10);
        Assert.Equal(SourceFallbackOutcome.Replaced, WatchPartySourceFallback.ClassifyReply(Room(), incoming, plan));
        Assert.True(WatchPartySourceFallback.ShouldAssignReply(Room(), incoming, socketSnapshotSinceRequest: false));
    }

    [Fact]
    public void AStaleFileOrRevisionReturnsTheRoomUnchanged()
    {
        var plan = WatchPartySourceFallback.Plan(Input())!;
        var unchanged = Room();
        Assert.Equal(SourceFallbackOutcome.Unchanged, WatchPartySourceFallback.ClassifyReply(Room(), unchanged, plan));
        // The same generation is applied harmlessly; the original refusal stays on screen.
        Assert.True(WatchPartySourceFallback.ShouldAssignReply(Room(), unchanged, false));
    }

    [Fact]
    public void AReplyAfterTheRoomMovedToAnotherSelectionIsDropped()
    {
        var plan = WatchPartySourceFallback.Plan(Input())!;
        var moved = Room(revision: 3, file: 9, generation: 12);
        var incoming = Room(revision: 2, file: 8, generation: 10);
        Assert.Equal(SourceFallbackOutcome.Moved, WatchPartySourceFallback.ClassifyReply(moved, incoming, plan));
        // The socket already delivered the replacement before the HTTP reply did.
        var replacementFirst = Room(revision: 2, file: 8, generation: 10);
        Assert.Equal(SourceFallbackOutcome.Moved, WatchPartySourceFallback.ClassifyReply(replacementFirst, incoming, plan));
    }

    [Fact]
    public void AReplyAfterTheMemberLeftTheRoomIsDropped()
    {
        var plan = WatchPartySourceFallback.Plan(Input())!;
        var incoming = Room(revision: 2, file: 8, generation: 10);
        Assert.Equal(SourceFallbackOutcome.Abandoned, WatchPartySourceFallback.ClassifyReply(null, incoming, plan));
        var otherRoom = Room(); otherRoom.RoomId = "other";
        Assert.Equal(SourceFallbackOutcome.Abandoned, WatchPartySourceFallback.ClassifyReply(otherRoom, incoming, plan));
        Assert.False(WatchPartySourceFallback.ShouldAssignReply(null, incoming, false));
        Assert.False(WatchPartySourceFallback.ShouldAssignReply(otherRoom, incoming, false));
    }

    [Fact]
    public void AnOlderHttpGenerationNeverOverwritesALaterSocketSnapshot()
    {
        // Official: the socket's generation-10 snapshot wins over a late generation-9 answer.
        Assert.False(WatchPartySourceFallback.ShouldAssignReply(Room(generation: 10), Room(generation: 9), false));
        Assert.True(WatchPartySourceFallback.ShouldAssignReply(Room(generation: 9), Room(generation: 10), false));
    }

    [Fact]
    public void AnAttachmentReceivedWhileTheReceiptWasPendingIsKept()
    {
        // Official "preserves an attachment received while a source fallback receipt is pending".
        Assert.False(WatchPartySourceFallback.ShouldAssignReply(Room(generation: 2), Room(generation: 2), socketSnapshotSinceRequest: true));
        Assert.True(WatchPartySourceFallback.ShouldAssignReply(Room(generation: 2), Room(generation: 2), socketSnapshotSinceRequest: false));
        Assert.True(WatchPartySourceFallback.ShouldAssignReply(Room(generation: 2), Room(generation: 3), socketSnapshotSinceRequest: true));
    }

    // ===== The request =====

    private static (PlaybackApi Api, SiloApiClient Client, Wire Wire) Api(Wire? wire = null)
    {
        wire ??= new Wire();
        var client = new SiloApiClient(new HttpClient(wire));
        client.SetBaseUrl("https://party.invalid");
        client.SetProfile("host");
        client.SetAccessToken("access");
        return (new PlaybackApi(client), client, wire);
    }

    [Fact]
    public async Task PostsTheOfficialRequestWithTheRoomProofAndAStringFileId()
    {
        var (api, client, wire) = Api();
        var response = await api.FallbackWatchTogetherSourceAsync(client.CaptureContext(), "room/one", "proof", 4, 7, "hdr_transcode_unsupported");
        var request = Assert.Single(wire.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/api/v2/watch-together/rooms/room%2Fone/source-fallback", request.Path);
        Assert.Equal("proof", request.RoomToken);
        Assert.Equal("host", request.Profile);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(["failed_file_id", "reason", "selection_revision"],
            body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("failed_file_id").ValueKind);
        Assert.Equal("7", body.RootElement.GetProperty("failed_file_id").GetString());
        Assert.Equal(4, body.RootElement.GetProperty("selection_revision").GetInt32());
        Assert.Equal("hdr_transcode_unsupported", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal("room/one", response.Room.RoomId);
        Assert.Equal(8, response.Room.SelectedFileId);
    }

    [Theory]
    [InlineData("", "proof", 1, 7, "no_alternate_version")]
    [InlineData("room", "", 1, 7, "no_alternate_version")]
    [InlineData("room", "proof", 0, 7, "no_alternate_version")]
    [InlineData("room", "proof", -2, 7, "no_alternate_version")]
    [InlineData("room", "proof", 1, 0, "no_alternate_version")]
    [InlineData("room", "proof", 1, -7, "no_alternate_version")]
    [InlineData("room", "proof", 1, 7, "source_unavailable")]
    public async Task AnInvalidIdentityIsRefusedBeforeAnythingIsSent(string room, string token, int revision, int file, string reason)
    {
        var (api, client, wire) = Api();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            api.FallbackWatchTogetherSourceAsync(client.CaptureContext(), room, token, revision, file, reason));
        Assert.Equal("Invalid source fallback identity.", error.Message);
        Assert.Empty(wire.Requests);
    }

    [Fact]
    public async Task ACapturedRequestCannotBeSentForAReplacementProfile()
    {
        var (api, client, wire) = Api();
        var context = client.CaptureContext();
        client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            api.FallbackWatchTogetherSourceAsync(context, "room", "proof", 1, 7, "no_alternate_version"));
        Assert.Empty(wire.Requests);
    }

    [Fact]
    public async Task AReplyIsDroppedWhenTheAccountOrProfileChangedWhilePending()
    {
        var wire = new Wire { Hold = true };
        var (api, client, _) = Api(wire);
        var sending = api.FallbackWatchTogetherSourceAsync(client.CaptureContext(), "room/one", "proof", 4, 7, "no_alternate_version");
        await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        client.SetProfile("replacement");
        wire.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        Assert.Single(wire.Requests);
    }

    [Fact]
    public async Task NeverRetriesAuthenticationAndNeverResendsAfterAFailure()
    {
        var wire = new Wire { Status = HttpStatusCode.Unauthorized };
        var (api, client, _) = Api(wire);
        var refreshes = 0;
        client.SetTokenRefresher(_ => { refreshes++; client.SetAccessToken("fresh"); return Task.FromResult(true); });
        await Assert.ThrowsAsync<ApiException>(() =>
            api.FallbackWatchTogetherSourceAsync(client.CaptureContext(), "room/one", "proof", 4, 7, "no_alternate_version"));
        Assert.Single(wire.Requests);
        Assert.Equal(0, refreshes);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AFailedRequestThrowsOnceAndIsNotRepeated(HttpStatusCode status)
    {
        var wire = new Wire { Status = status };
        var (api, client, _) = Api(wire);
        await Assert.ThrowsAsync<ApiException>(() =>
            api.FallbackWatchTogetherSourceAsync(client.CaptureContext(), "room/one", "proof", 4, 7, "no_alternate_version"));
        Assert.Single(wire.Requests);
    }

    [Fact]
    public async Task ARoomSnapshotForAnotherRoomOrWithoutProofIsInvalid()
    {
        var (api, client, _) = Api(new Wire { Json = "{\"room\":{\"room_id\":\"someone-else\",\"selection_revision\":2},\"room_access_token\":\"p\"}" });
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            api.FallbackWatchTogetherSourceAsync(client.CaptureContext(), "room/one", "proof", 4, 7, "no_alternate_version"));
        Assert.Equal("Invalid room snapshot.", error.Message);
        (api, client, _) = Api(new Wire { Json = "{\"room\":{\"room_id\":\"room/one\",\"selection_revision\":2}}" });
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            api.FallbackWatchTogetherSourceAsync(client.CaptureContext(), "room/one", "proof", 4, 7, "no_alternate_version"));
    }

    [Fact]
    public async Task TheUnchangedRoomAnswerParsesAsTheCurrentRoom()
    {
        var (api, client, _) = Api(new Wire
        {
            Json = "{\"room\":{\"room_id\":\"room/one\",\"selection_revision\":4,\"selected_file_id\":\"7\",\"generation\":9},\"room_access_token\":\"p\"}",
        });
        var response = await api.FallbackWatchTogetherSourceAsync(client.CaptureContext(), "room/one", "proof", 4, 7, "no_alternate_version");
        var plan = new WatchPartySourceFallbackPlan("k", "room/one", 4, 7, "no_alternate_version");
        var current = new WatchTogetherRoomSnapshot { RoomId = "room/one", SelectionRevision = 4, SelectedFileId = 7, Generation = 9 };
        Assert.Equal(SourceFallbackOutcome.Unchanged, WatchPartySourceFallback.ClassifyReply(current, response.Room, plan));
    }

    [Fact]
    public void ARefusedStartCarriesItsPlaybackAttempt()
    {
        var terminal = new PlaybackPlanTerminalException("hdr_transcode_unsupported", null, false) { PlaybackAttemptId = "attempt-9" };
        Assert.Equal("attempt-9", terminal.PlaybackAttemptId);
        Assert.True(WatchPartySourceFallback.IsFallbackReason(terminal.Reason));
    }

    private sealed class Wire : HttpMessageHandler
    {
        public sealed record Seen(string Method, string Path, string? RoomToken, string? Profile, string? Body);
        public List<Seen> Requests { get; } = [];
        public bool Hold;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Json = "{\"room\":{\"room_id\":\"room/one\",\"phase\":\"playing\",\"selection_revision\":5,\"selected_file_id\":\"8\",\"selected_content_id\":\"movie\",\"generation\":10},\"room_access_token\":\"renewed\"}";
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(request.Method.Method, request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("X-Room-Token", out var proof) ? proof.Single() : null,
                request.Headers.TryGetValues("X-Profile-Id", out var profile) ? profile.Single() : null,
                request.Content == null ? null : await request.Content.ReadAsStringAsync(ct)));
            if (Hold) { Entered.TrySetResult(); await Release.Task; }
            return new(Status) { Content = new StringContent(Status == HttpStatusCode.OK ? Json : "{}", System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
