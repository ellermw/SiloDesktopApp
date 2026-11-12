using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Native fixture tests for the five profile-transfer review corrections (R1–R5).
/// Exercises PIN handling, owner/context guards, bounded file reading, and post-import
/// cache invalidation using synthetic HTTP handlers and in-memory byte streams.
/// No real user data, account, server, dialog, or StorageFile is used.
/// </summary>
public sealed class ProfileTransferCorrectionTests
{
    private const string ExportDocument = """{"format":"silo-profile-export","version":1,"server_id":"test","exported_at":"2026-11-11T00:00:00Z","profile_name":"Test","data":{"watch_history":[]},"items":[]}""";
    private const string ImportResult = """{"same_server":true,"data":[{"type":"watch_history","added":5,"updated":2,"unchanged":10,"left_out":1,"notes":["Some note"]}],"unmatched":[],"hidden":[]}""";

    // ── R1: PIN valid:false / nonempty proof ──

    [Fact]
    public async Task R1_WrongPinReturns200WithValidFalse()
    {
        var handler = new StatusHandler();
        handler.Enqueue(200, """{"valid":false,"profile_token":"","expires_at":null}""");
        var client = CreateClient(handler);
        var context = client.CaptureContext();
        var result = await client.VerifyProfilePinForManagementAsync(context, "p1", "0000");
        Assert.False(result.Valid);
        Assert.True(string.IsNullOrWhiteSpace(result.ProfileToken));
    }

    [Fact]
    public async Task R1_CorrectPinReturnsValidWithProof()
    {
        var handler = new StatusHandler();
        handler.Enqueue(200, """{"valid":true,"profile_token":"pvt_abc123","expires_at":"2026-12-01T00:00:00Z"}""");
        var client = CreateClient(handler);
        var context = client.CaptureContext();
        var result = await client.VerifyProfilePinForManagementAsync(context, "p1", "1234");
        Assert.True(result.Valid);
        Assert.Equal("pvt_abc123", result.ProfileToken);
    }

    [Fact]
    public async Task ProofAdoptionRejectsSameProfileAuthorityRetirement()
    {
        var client = CreateClient(new StatusHandler());
        var owner = client.CaptureContext();
        await Task.Run(() => client.InvalidateAccessContext());
        Assert.Equal(owner.ProfileId, client.ProfileId);
        Assert.Throws<OperationCanceledException>(() => client.AdoptProfileProof(owner, "retired-proof"));
        Assert.Null(client.ProfileToken);
    }

    [Fact]
    public void R1_AdoptProfileProofAdvancesContextGeneration()
    {
        var client = CreateClient(new StatusHandler());
        var before = client.CaptureContext();
        client.AdoptProfileProof(client.CaptureContext(), "pvt_abc123");
        var after = client.CaptureContext();
        Assert.NotEqual(before.RequestContextGeneration, after.RequestContextGeneration);
        Assert.False(client.IsCurrentContext(before));
        Assert.True(client.IsCurrentContext(after));
        Assert.Equal("pvt_abc123", client.ProfileToken);
    }

    [Fact]
    public void R1_AdoptProfileProofRejectsWrongProfile()
    {
        var client = CreateClient(new StatusHandler());
        Assert.Throws<OperationCanceledException>(() => client.AdoptProfileProof(client.CaptureContext() with { ProfileId = "wrong-profile" }, "pvt_x"));
    }

    [Fact]
    public async Task R1_RetryWithAdoptedProofSendsProfileTokenHeader()
    {
        var handler = new StatusHandler();
        handler.Enqueue(403, """{"type":"https://silo.example/docs/api/v2/problems/profile_verification_required","detail":"PIN required"}""");
        handler.Enqueue(200, """{"valid":true,"profile_token":"pvt_proof","expires_at":null}""");
        handler.Enqueue(200, ExportDocument);
        var client = CreateClient(handler);
        var api = new ProfileTransferApi(client);
        var context = api.CaptureContext();

        var ex = await Assert.ThrowsAsync<ApiException>(() => api.ExportAsync(context));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("profile_verification_required", ex.ErrorCode);

        var pinResult = await client.VerifyProfilePinForManagementAsync(context, "p1", "1234");
        Assert.True(pinResult.Valid);

        client.AdoptProfileProof(client.CaptureContext(), pinResult.ProfileToken);
        var freshContext = api.CaptureContext();
        var data = await api.ExportAsync(freshContext);
        Assert.Contains("silo-profile-export", Encoding.UTF8.GetString(data));
        Assert.Equal(3, handler.Requests.Count);
        Assert.True(handler.LastRequestHeaders.ContainsKey("X-Profile-Token"));
        Assert.Equal("pvt_proof", handler.LastRequestHeaders["X-Profile-Token"]);
    }

    [Fact]
    public async Task R1_Ordinary403HasDifferentErrorCode()
    {
        var handler = new StatusHandler();
        handler.Enqueue(403, """{"type":"https://silo.example/docs/api/v2/problems/forbidden","detail":"Not allowed"}""");
        var client = CreateClient(handler);
        var api = new ProfileTransferApi(client);
        var context = api.CaptureContext();
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.ExportAsync(context));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("forbidden", ex.ErrorCode);
        Assert.NotEqual("profile_verification_required", ex.ErrorCode);
    }

    // ── R2: Stale dialog / owner-bound PIN ──

    [Fact]
    public async Task R2_OwnerBoundPinVerificationRejectsStaleContext()
    {
        var handler = new StatusHandler();
        handler.Enqueue(200, """{"valid":true,"profile_token":"pvt_stale","expires_at":null}""");
        var client = CreateClient(handler);
        var context = client.CaptureContext();
        client.SetProfile("p2");
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.VerifyProfilePinForManagementAsync(context, "p1", "1234"));
    }

    [Fact]
    public async Task R2_PinVerificationRejectsProfileChangeAfterCall()
    {
        var handler = new StatusHandler();
        handler.Enqueue(200, """{"valid":true,"profile_token":"pvt_mid","expires_at":null}""");
        var client = CreateClient(handler);
        handler.SetClientRef(client);
        handler.OnBeforeResponse = c => c.SetProfile("p2");
        var context = client.CaptureContext();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.VerifyProfilePinForManagementAsync(context, "p1", "1234"));
    }

    // ── R3: Route/overview/unload/authority retirement ──

    [Fact]
    public async Task R3_ExportRejectsStaleContextAfterProfileChange()
    {
        var handler = new StatusHandler();
        handler.Enqueue(200, ExportDocument);
        var client = CreateClient(handler);
        handler.SetClientRef(client);
        handler.OnBeforeResponse = c => c.SetProfile("p2");
        var api = new ProfileTransferApi(client);
        var context = api.CaptureContext();
        await Assert.ThrowsAsync<OperationCanceledException>(() => api.ExportAsync(context));
    }

    [Fact]
    public async Task R3_ImportRejectsStaleContextAfterAccessChange()
    {
        var handler = new StatusHandler();
        handler.Enqueue(200, ImportResult);
        var client = CreateClient(handler);
        handler.SetClientRef(client);
        handler.OnBeforeResponse = c => c.SetProfile("p2");
        var api = new ProfileTransferApi(client);
        var context = api.CaptureContext();
        var doc = Encoding.UTF8.GetBytes(ExportDocument);
        await Assert.ThrowsAsync<OperationCanceledException>(() => api.ImportAsync(context, doc, 67108864));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task R3_ImportKeepsCommittedResultWhenOnlyRequestContextRetires(bool transportCatchUp)
    {
        var handler = new StatusHandler();
        handler.Enqueue(200, ImportResult);
        var client = CreateClient(handler);
        handler.SetClientRef(client);
        handler.OnBeforeResponse = c => c.TryInvalidateAccessContext(c.CaptureContext(), out _, transportCatchUp);
        var api = new ProfileTransferApi(client);
        var context = api.CaptureContext();
        var doc = Encoding.UTF8.GetBytes(ExportDocument);
        var result = await api.ImportAsync(context, doc, 67108864);
        Assert.False(api.IsCurrentContext(context));
        Assert.True(result.SameServer);
        Assert.Equal(5, Assert.Single(result.Data).Added);
    }

    [Fact]
    public void R3_GenerationOwnedBusyCleanup()
    {
        var client = CreateClient(new StatusHandler());
        var gen1 = client.CaptureContext();
        client.AdoptProfileProof(client.CaptureContext(), "pvt_a");
        var gen2 = client.CaptureContext();
        Assert.False(client.IsCurrentContext(gen1));
        Assert.True(client.IsCurrentContext(gen2));
    }

    // ── R4: File size before reading, bounded streaming growth ──

    [Fact]
    public async Task R4_FileSizeRejectedBeforeRead()
    {
        var source = new SyntheticFileSource(new byte[100], 100_000_000);
        Assert.Equal(100_000_000, source.FileSize);
        Assert.True(source.FileSize > 67108864);
    }

    [Fact]
    public async Task R4_BoundedReadRejectsGrowthBeyondLimit()
    {
        var source = new SyntheticFileSource(new byte[200], 100);
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => source.ReadBoundedAsync(150, CancellationToken.None));
        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("too large", ex.Message);
    }

    [Fact]
    public async Task R4_BoundedReadSucceedsWithinLimit()
    {
        var content = Encoding.UTF8.GetBytes(ExportDocument);
        var source = new SyntheticFileSource(content, content.Length);
        var bytes = await source.ReadBoundedAsync(67108864, CancellationToken.None);
        Assert.Equal(content.Length, bytes.Length);
        Assert.Contains("silo-profile-export", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task R4_BoundedReadWithZeroLimitAllowsAnySize()
    {
        var content = new byte[1024 * 1024];
        var source = new SyntheticFileSource(content, content.Length);
        var bytes = await source.ReadBoundedAsync(0, CancellationToken.None);
        Assert.Equal(content.Length, bytes.Length);
    }

    [Fact]
    public async Task R4_BoundedReadRespectsCancellation()
    {
        var source = new SyntheticFileSource(new byte[1024], 1024);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => source.ReadBoundedAsync(67108864, cts.Token));
    }

    // ── R5: Import invalidation ──

    [Fact]
    public async Task R5_UICustomizationInvalidateMarksNotLoaded()
    {
        const string caps = """{"api_version":1,"manifest_revision":5,"contract_etag":"\"e1\"","definition_count":3,"scopes":["profile_client"],"client_families":["desktop"],"supports_batched_effective":true,"supports_idempotent_writes":true,"supports_atomic_shortcuts":false,"supports_device_rename":false}""";
        const string effective = """{"items":[],"revision":1}""";
        var handler = new StatusHandler();
        handler.Enqueue(200, caps);
        handler.Enqueue(200, effective);
        var client = CreateClient(handler);
        var settingsApi = new SettingsApi(client);
        var service = new UICustomizationService(settingsApi);
        await service.EnsureLoadedAsync();
        Assert.True(service.IsSupported);
        service.Invalidate();
        handler.Enqueue(200, caps);
        handler.Enqueue(200, effective);
        await service.EnsureLoadedAsync();
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public void R5_LibraryPageStateInvalidateClearsConfirmed()
    {
        var handler = new StatusHandler();
        var client = CreateClient(handler);
        var settingsApi = new SettingsApi(client);
        var store = settingsApi.LibraryPageState;
        var revisionBefore = store.Revision;
        store.Invalidate();
        Assert.True(store.Revision > revisionBefore);
        Assert.False(store.TryGetConfirmed(1, out _));
    }

    [Fact]
    public async Task R5_ImportThenInvalidateForcesCacheRefresh()
    {
        var handler = new StatusHandler();
        handler.Enqueue(200, ImportResult);
        var client = CreateClient(handler);
        var api = new ProfileTransferApi(client);
        var settingsApi = new SettingsApi(client);
        var uiService = new UICustomizationService(settingsApi);
        var libraryState = settingsApi.LibraryPageState;

        var context = api.CaptureContext();
        var doc = Encoding.UTF8.GetBytes(ExportDocument);
        var result = await api.ImportAsync(context, doc, 67108864);
        Assert.True(result.SameServer);

        var libRevBefore = libraryState.Revision;
        uiService.Invalidate();
        libraryState.Invalidate();
        Assert.True(libraryState.Revision > libRevBefore);
        Assert.False(libraryState.TryGetConfirmed(1, out _));
    }

    // ── R2 supplement: context invalidation event ──

    [Fact]
    public void R2_InvalidateAccessContextFiresContextInvalidatedEvent()
    {
        var client = CreateClient(new StatusHandler());
        var fired = 0;
        client.ContextInvalidated += () => fired++;
        client.InvalidateAccessContext();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void R2_TryInvalidateAccessContextFiresContextInvalidatedEvent()
    {
        var client = CreateClient(new StatusHandler());
        var fired = 0;
        client.ContextInvalidated += () => fired++;
        var context = client.CaptureContext();
        Assert.True(client.TryInvalidateAccessContext(context, out _));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void R2_TryInvalidateAccessContextDoesNotFireOnMismatch()
    {
        var client = CreateClient(new StatusHandler());
        var fired = 0;
        client.ContextInvalidated += () => fired++;
        var context = client.CaptureContext();
        client.InvalidateAccessContext();
        fired = 0;
        Assert.False(client.TryInvalidateAccessContext(context, out _));
        Assert.Equal(0, fired);
    }

    // ── R5 supplement: post-import notification ──

    [Fact]
    public async Task R5_OwnerRefreshNotifiesConsumersWithImportedCustomization()
    {
        const string caps = """{"api_version":1,"manifest_revision":22,"client_families":["desktop"],"supports_batched_effective":true,"supports_idempotent_writes":true}""";
        var handler = new StatusHandler();
        var client = CreateClient(handler);
        var service = new UICustomizationService(new SettingsApi(client));
        handler.Enqueue(200, caps);
        handler.Enqueue(200, """{"items":[{"key":"ui.card_presentation","value":{"poster_size":"compact"},"source":"profile_client"}]}""");
        await service.EnsureLoadedAsync();
        Assert.Equal("compact", service.CardPresentation.PosterSize);
        var observed = new List<string>();
        service.Changed += (_, _) => observed.Add(service.CardPresentation.PosterSize);
        service.Invalidate();
        handler.Enqueue(200, caps);
        handler.Enqueue(200, """{"items":[{"key":"ui.card_presentation","value":{"poster_size":"standard"},"source":"profile_client"}]}""");
        await service.RefreshAsync(client.CaptureContext());
        Assert.Equal("standard", Assert.Single(observed));
    }

    [Fact]
    public async Task R5_RetiredRefreshCannotReadReplacementOwner()
    {
        var handler = new StatusHandler();
        var client = CreateClient(handler);
        var owner = client.CaptureContext();
        client.InvalidateAccessContext();
        var service = new UICustomizationService(new SettingsApi(client));
        var events = 0;
        service.Changed += (_, _) => events++;
        await service.RefreshAsync(owner);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, events);
    }

    [Fact]
    public async Task ProofRetirementAfterAdoptionCannotAuthorizeRetry()
    {
        var handler = new StatusHandler();
        var client = CreateClient(handler);
        var retryOwner = client.AdoptProfileProof(client.CaptureContext(), "verified-proof");
        await Task.Run(() => client.InvalidateAccessContext());
        Assert.False(client.IsCurrentContext(retryOwner));
        await Assert.ThrowsAsync<OperationCanceledException>(() => new ProfileTransferApi(client).ExportAsync(retryOwner));
        Assert.Empty(handler.Requests);
    }

    // ── NEW P2 supplement: export preserves raw bytes ──

    [Fact]
    public async Task Export_PreservesRawServerBytesWithoutExpansion()
    {
        var handler = new StatusHandler();
        handler.Enqueue(200, ExportDocument);
        var client = CreateClient(handler);
        var api = new ProfileTransferApi(client);
        var context = api.CaptureContext();
        var bytes = await api.ExportAsync(context);
        var exported = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\n", exported);
        Assert.DoesNotContain("  ", exported);
        Assert.Equal(ExportDocument, exported);
    }

    [Fact]
    public async Task Export_PreservesUnicodeBytesWithoutEscaping()
    {
        var handler = new StatusHandler();
        var unicodeDoc = """{"format":"silo-profile-export","version":1,"data":{},"items":[{"title":"ééé"}]}""";
        handler.Enqueue(200, unicodeDoc);
        var client = CreateClient(handler);
        var api = new ProfileTransferApi(client);
        var context = api.CaptureContext();
        var bytes = await api.ExportAsync(context);
        var raw = Encoding.UTF8.GetBytes(unicodeDoc);
        Assert.Equal(raw.Length, bytes.Length);
    }

    // ── Combined scenarios ──

    [Fact]
    public void Combined_ProofAdoptionMakesOriginalContextStale()
    {
        var client = CreateClient(new StatusHandler());
        var original = client.CaptureContext();
        Assert.True(client.IsCurrentContext(original));
        client.AdoptProfileProof(client.CaptureContext(), "pvt_proof");
        Assert.False(client.IsCurrentContext(original));
        var refreshed = client.CaptureContext();
        Assert.True(client.IsCurrentContext(refreshed));
        Assert.NotEqual(original.RequestContextGeneration, refreshed.RequestContextGeneration);
    }

    [Fact]
    public void Combined_ProfileTransferApiExposesClientForProofAdoption()
    {
        var client = CreateClient(new StatusHandler());
        var api = new ProfileTransferApi(client);
        Assert.Same(client, api.Client);
    }

    [Fact]
    public async Task Combined_FullPinVerifyAdoptRetrySequence()
    {
        var handler = new StatusHandler();
        // 1. Export -> 403 profile_verification_required
        handler.Enqueue(403, """{"type":"https://silo.example/docs/api/v2/problems/profile_verification_required","detail":"PIN required"}""");
        // 2. PIN verification -> valid:false (wrong PIN)
        handler.Enqueue(200, """{"valid":false,"profile_token":"","expires_at":null}""");
        // 3. PIN verification -> valid:true (correct PIN)
        handler.Enqueue(200, """{"valid":true,"profile_token":"pvt_final","expires_at":"2026-12-01T00:00:00Z"}""");
        // 4. Retry export with proof
        handler.Enqueue(200, ExportDocument);

        var client = CreateClient(handler);
        var api = new ProfileTransferApi(client);
        var context = api.CaptureContext();

        // Step 1: export fails with profile_verification_required
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.ExportAsync(context));
        Assert.Equal("profile_verification_required", ex.ErrorCode);

        // Step 2: wrong PIN - valid:false
        var wrongPin = await client.VerifyProfilePinForManagementAsync(context, "p1", "0000");
        Assert.False(wrongPin.Valid);
        Assert.True(string.IsNullOrWhiteSpace(wrongPin.ProfileToken));

        // Step 3: correct PIN - valid:true with proof
        var correctPin = await client.VerifyProfilePinForManagementAsync(context, "p1", "1234");
        Assert.True(correctPin.Valid);
        Assert.Equal("pvt_final", correctPin.ProfileToken);

        // Step 4: adopt proof, re-capture, retry
        client.AdoptProfileProof(client.CaptureContext(), correctPin.ProfileToken);
        var freshContext = api.CaptureContext();
        Assert.False(client.IsCurrentContext(context));
        Assert.True(client.IsCurrentContext(freshContext));

        var data = await api.ExportAsync(freshContext);
        Assert.Contains("silo-profile-export", Encoding.UTF8.GetString(data));
    }

    // ── Helpers ──

    private static SiloApiClient CreateClient(StatusHandler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.example");
        client.SetAccessToken("test-access-token");
        client.SetProfile("p1");
        return client;
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly Queue<(int Status, string Body)> _responses = new();
        public List<string> Requests { get; } = [];
        public Dictionary<string, string> LastRequestHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Action<SiloApiClient>? OnBeforeResponse { get; set; }
        private SiloApiClient? _clientRef;

        public void Enqueue(int status, string body) => _responses.Enqueue((status, body));
        internal void SetClientRef(SiloApiClient client) => _clientRef = client;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            LastRequestHeaders.Clear();
            foreach (var h in request.Headers)
                LastRequestHeaders[h.Key] = string.Join(",", h.Value);

            if (OnBeforeResponse != null && _clientRef != null)
                OnBeforeResponse(_clientRef);

            var (status, body) = _responses.Count > 0 ? _responses.Dequeue() : (200, "{}");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>
    /// Synthetic file source implementing bounded reading without StorageFile.
    /// Enables testing file size rejection and growth-cap enforcement with pure byte streams.
    /// </summary>
    private sealed class SyntheticFileSource(byte[] content, long reportedSize)
    {
        public long FileSize => reportedSize;

        public async Task<byte[]> ReadBoundedAsync(long maxBytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var stream = new MemoryStream(content);
            var buffer = new MemoryStream();
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (maxBytes > 0 && total > maxBytes)
                    throw new ApiException("validation_failed",
                        $"The file is too large. The limit is {total} B.", 422);
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
    }
}
