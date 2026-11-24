using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using Xunit;

namespace SiloPlayer.Tests;

/// <summary>
/// The Activate device page's rules, ported from the official page's tests
/// (web/src/pages/ActivateDevice.test.tsx, silo-server 3b2b32168, #4652). Texts are checked through
/// <see cref="ActivationText.English"/>, the official English the page passes as the fallback, so the
/// expectations do not depend on which catalog generation is bundled.
/// </summary>
public sealed class DeviceActivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static DeviceLoginLookupResponse Pending(Action<DeviceLoginLookupResponse>? change = null)
    {
        var details = new DeviceLoginLookupResponse
        {
            Status = "pending",
            UserCode = "4821-7730",
            MatchCode = "warm pony",
            DeviceName = "Living room TV",
            DevicePlatform = "tvos",
            IpAddressHint = "192.168.*.*",
            RequestedAt = Now.AddSeconds(-10).ToString("O"),
            ClientPurpose = "device_login",
            ServerName = "Media Room",
        };
        change?.Invoke(details);
        return details;
    }

    private static DeviceLoginLookupResponse QuickConnect(string status = "pending") => Pending(d =>
    {
        d.Status = status;
        d.UserCode = "482177";
        d.DeviceName = "Firefox";
        d.DevicePlatform = "Jellyfin Web";
        d.ClientPurpose = "jellyfin_quick_connect";
    });

    private static DeviceLoginLookupResponse RemotePlayback(string status = "pending") => Pending(d =>
    {
        d.Status = status;
        d.ClientPurpose = "remote_playback";
        d.Temporary = true;
    });

    // ----- Code entry -----

    [Theory]
    [InlineData("48217730", "4821 7730")]
    [InlineData("4821-7730", "4821 7730")]
    [InlineData("abcd12345678", "ABCD 1234")]
    [InlineData("482", "482")]
    [InlineData("482177", "4821 77")]
    public void TheCodeIsGroupedFourAndFourAsItIsTyped(string typed, string shown)
        => Assert.Equal(shown, DeviceActivation.FormatCode(typed));

    [Fact]
    public void TheApiTakesTheCodeWithoutItsGrouping()
        => Assert.Equal("48217730", DeviceActivation.CompactCode("4821 7730"));

    [Theory]
    [InlineData("48217730", true)]
    [InlineData("482177", true)]
    [InlineData("4821773", false)]
    [InlineData("48217", false)]
    [InlineData("", false)]
    public void OnlyAWholeDeviceOrQuickConnectCodeCanBeSubmitted(string compact, bool complete)
        => Assert.Equal(complete, DeviceActivation.IsCompleteCode(compact));

    [Theory]
    [InlineData("4821-7730", "4821 7730")]
    [InlineData("482177", "482177")]
    public void AQuickConnectCodeIsShownUngroupedAndADeviceCodeFourAndFour(string code, string shown)
        => Assert.Equal(shown, DeviceActivation.DisplayCode(code));

    [Fact]
    public void TheCodeIsReadOneCharacterAtATime()
        => Assert.Equal("4 8 2 1 7 7 3 0", DeviceCodeText.Spoken(DeviceActivation.DisplayCode("4821-7730")));

    [Fact]
    public void OnlyASixDigitCodeWithoutATokenNeedsAnAccountToLookUp()
    {
        Assert.True(DeviceActivation.NeedsAccount(null, "482177"));
        Assert.False(DeviceActivation.NeedsAccount(null, "48217730"));
        Assert.False(DeviceActivation.NeedsAccount("link-token", "482177"));
    }

    // ----- What the card says -----

    [Fact]
    public void AnApprovalCardNamesTheDeviceItsPlatformAndWhenItAsked()
    {
        var details = Pending();
        Assert.Equal("Allow Living room TV to sign in?", DeviceActivation.Heading(details)!.English);
        Assert.Equal("web.auth.activate.signInDevice", DeviceActivation.Heading(details)!.Key);
        Assert.Equal("Apple TV · requested just now", DeviceActivation.DeviceLine(details, Now));
        Assert.Equal("Requested from 192.168.*.*", ActivationText.Of("web.auth.activate.requestedFrom", "Requested from {ip}", ("ip", details.IpAddressHint!)).English);
    }

    [Fact]
    public void ADeviceWithoutANameIsAskedAboutAsThisDevice()
    {
        var heading = DeviceActivation.Heading(Pending(d => d.DeviceName = ""))!;
        Assert.Equal("web.auth.activate.signInDeviceUnknown", heading.Key);
        Assert.Equal("Allow this device to sign in?", heading.English);
    }

    [Fact]
    public void ARemotePlaybackRequestAsksToPlayOnTheDevice()
    {
        Assert.Equal("Play on Living room TV?", DeviceActivation.Heading(RemotePlayback())!.English);
        Assert.Equal("Play on this device?", DeviceActivation.Heading(Pending(d => { d.ClientPurpose = "remote_playback"; d.DeviceName = null; }))!.English);
    }

    [Fact]
    public void AnExpiredRequestDropsTheQuestion()
        => Assert.Null(DeviceActivation.Heading(Pending(d => d.Status = "expired")));

    [Fact]
    public void APlatformThatRepeatsTheDeviceNameIsLeftOut()
    {
        var details = Pending(d => { d.DeviceName = "Tracearr"; d.DevicePlatform = "Tracearr"; });
        Assert.Equal("Requested just now", DeviceActivation.DeviceLine(details, Now));
        Assert.DoesNotContain("Tracearr ·", DeviceActivation.DeviceLine(details, Now));
    }

    [Theory]
    [InlineData("tvos", "Apple TV")]
    [InlineData("android-tv", "Android TV")]
    [InlineData("androidtv", "Android TV")]
    [InlineData("roku", "Roku")]
    [InlineData("Jellyfin Web", "Jellyfin Web")]
    public void PlatformsGetTheirProductNames(string platform, string label)
        => Assert.Equal(label, DeviceActivation.PlatformLabel(platform));

    [Fact]
    public void WhenTheRequestWasMadeIsRelative()
    {
        Assert.Equal("just now", DeviceActivation.RelativeTime(Now.AddSeconds(-20).ToString("O"), Now));
        Assert.Equal("5m ago", DeviceActivation.RelativeTime(Now.AddMinutes(-5).ToString("O"), Now));
        Assert.Equal("3h ago", DeviceActivation.RelativeTime(Now.AddHours(-3).ToString("O"), Now));
        Assert.Equal("2d ago", DeviceActivation.RelativeTime(Now.AddDays(-2).ToString("O"), Now));
        Assert.Null(DeviceActivation.RelativeTime("not a time", Now));
        Assert.Null(DeviceActivation.RelativeTime(null, Now));
        Assert.Equal("", DeviceActivation.DeviceLine(Pending(d => { d.DevicePlatform = null; d.RequestedAt = null; }), Now));
    }

    // ----- Which kind of request -----

    [Fact]
    public void RequestKindsComeFromTheirPurpose()
    {
        Assert.True(DeviceActivation.IsQuickConnect(QuickConnect()));
        Assert.False(DeviceActivation.IsRemotePlayback(QuickConnect()));
        Assert.True(DeviceActivation.IsRemotePlayback(RemotePlayback()));
        Assert.True(DeviceActivation.IsRemotePlayback(Pending(d => d.Temporary = true)));
        Assert.False(DeviceActivation.IsRemotePlayback(Pending()));
        Assert.False(DeviceActivation.IsQuickConnect(null));
    }

    // ----- Results -----

    [Fact]
    public void ApprovingHereThenTheDeviceSigningInReadsAsDoneThenSignedIn()
    {
        Assert.Equal("Done. Living room TV is signing in.", DeviceActivation.Result(Pending(d => d.Status = "approved"), approvedHere: true, null)!.English);
        Assert.Equal("Living room TV is signed in.", DeviceActivation.Result(Pending(d => d.Status = "consumed"), approvedHere: true, null)!.English);
        Assert.Equal("Done. Your device is signing in.", DeviceActivation.Result(Pending(d => { d.Status = "approved"; d.DeviceName = ""; }), true, null)!.English);
        Assert.Equal("Your device is signed in.", DeviceActivation.Result(Pending(d => { d.Status = "consumed"; d.DeviceName = ""; }), true, null)!.English);
    }

    [Fact]
    public void ARequestApprovedOrFinishedElsewhereIsReportedAsSuch()
    {
        Assert.Equal("This device was approved and is finishing sign-in.", DeviceActivation.Result(Pending(d => d.Status = "approved"), false, null)!.English);
        Assert.Equal("This device is already signed in.", DeviceActivation.Result(Pending(d => d.Status = "consumed"), false, null)!.English);
    }

    [Theory]
    [InlineData("denied", "This sign-in was declined.", "web.auth.activate.result.declined")]
    [InlineData("canceled", "This device stopped waiting. Start sign-in again on it.", "web.auth.activate.result.canceled")]
    [InlineData("expired", "This code expired. Get a new code on the device and try again.", "web.auth.activate.result.expired")]
    public void DeclinedCanceledAndExpiredDeviceSignIns(string status, string english, string key)
    {
        var text = DeviceActivation.Result(Pending(d => d.Status = status), false, null)!;
        Assert.Equal(english, text.English);
        Assert.Equal(key, text.Key);
    }

    [Fact]
    public void APendingRequestHasNoResult()
        => Assert.Null(DeviceActivation.Result(Pending(), false, null));

    [Theory]
    [InlineData("approved", "This request was approved in the Silo app.")]
    [InlineData("consumed", "This request was approved in the Silo app.")]
    [InlineData("denied", "This request was declined.")]
    [InlineData("canceled", "The TV stopped waiting. Send playback from the Silo app again.")]
    [InlineData("expired", "This request expired. Send playback from the Silo app again.")]
    public void ARemotePlaybackRequestIsNeverReportedAsATvSigningIn(string status, string english)
    {
        var text = DeviceActivation.Result(RemotePlayback(status), approvedHere: false, null)!;
        Assert.Equal(english, text.English);
        Assert.StartsWith("web.auth.activate.result.remote", text.Key);
    }

    [Fact]
    public void QuickConnectResultsNameTheProfileTheDeviceSignsInAs()
    {
        Assert.Equal("Done. Firefox is signing in as Kids.", DeviceActivation.Result(QuickConnect("approved"), true, "Kids")!.English);
        Assert.Equal("Done. This device is signing in as Kids.", DeviceActivation.Result(Pending(d => { d.ClientPurpose = "jellyfin_quick_connect"; d.Status = "approved"; d.DeviceName = null; }), true, "Kids")!.English);
        Assert.Equal("This device was approved and is finishing sign-in.", DeviceActivation.Result(QuickConnect("approved"), false, null)!.English);
        Assert.Equal("That device is signed in.", DeviceActivation.Result(QuickConnect("consumed"), false, null)!.English);
        Assert.Equal("This code expired. Start Quick Connect again in the app.", DeviceActivation.Result(QuickConnect("expired"), false, null)!.English);
        Assert.Equal("This sign-in was declined.", DeviceActivation.Result(QuickConnect("denied"), false, null)!.English);
    }

    // ----- Lookup errors -----

    [Theory]
    [InlineData(404, ActivationLoadError.NotFound)]
    [InlineData(429, ActivationLoadError.RateLimited)]
    [InlineData(403, ActivationLoadError.Impersonating)]
    [InlineData(500, ActivationLoadError.Failed)]
    [InlineData(0, ActivationLoadError.Failed)]
    public void ALookupFailureIsSortedByItsStatus(int status, ActivationLoadError error)
        => Assert.Equal(error, DeviceActivation.LoadErrorFor(status));

    [Fact]
    public void TheLookupErrorsAreTheOfficialWording()
    {
        Assert.Equal("We couldn't find that code. Check the code and try again.", DeviceActivation.NotFound.English);
        Assert.Equal("web.auth.activate.notFound", DeviceActivation.NotFound.Key);
        Assert.Equal("Couldn't look up this code. Try again.", DeviceActivation.LookupFailed.English);
    }

    [Fact]
    public void TheImpersonationBlockNamesTheAccountItIsViewingAs()
    {
        Assert.Equal("Stop viewing as laura to approve this sign-in. Only the account itself can approve devices.", DeviceActivation.Impersonation("laura").English);
        Assert.Equal("Stop viewing as this user to approve this sign-in. Only the account itself can approve devices.", DeviceActivation.Impersonation(null).English);
        Assert.Equal("web.auth.activate.impersonatingUnknown", DeviceActivation.Impersonation("").Key);
    }

    [Fact]
    public void TooManyCodesSaysHowLongToWaitInWholeMinutes()
    {
        Assert.Equal("You've tried too many codes. Try again in 15 minutes.", DeviceActivation.TooManyCodes(900).English);
        Assert.Equal("You've tried too many codes. Try again in 1 minute.", DeviceActivation.TooManyCodes(20).English);
        Assert.Equal("You've tried too many codes. Try again later.", DeviceActivation.TooManyCodes(null).English);
        Assert.Equal("web.auth.quickConnect.tooManyCodes", DeviceActivation.TooManyCodes(0).Key);
    }

    // ----- Decisions -----

    [Theory]
    [InlineData(429, null, DecisionFailureKind.TooManyCodes)]
    [InlineData(403, "permission_denied", DecisionFailureKind.Impersonation)]
    [InlineData(403, "forbidden", DecisionFailureKind.Failed)]
    [InlineData(404, null, DecisionFailureKind.Moved)]
    [InlineData(409, "conflict", DecisionFailureKind.Moved)]
    [InlineData(410, null, DecisionFailureKind.Moved)]
    [InlineData(500, null, DecisionFailureKind.Failed)]
    public void AFailedDecisionIsSortedByStatusAndProblem(int status, string? problem, DecisionFailureKind kind)
        => Assert.Equal(kind, DeviceActivation.ClassifyDecisionFailure(status, problem));

    [Fact]
    public void ARefusedApprovalFailsWithTheApproveWordingAndDeclineWithItsOwn()
    {
        Assert.Equal("Couldn't approve sign-in. Try again.", DeviceActivation.PlanDecisionFailure(false, true, 500, null, null, "laura").Error!.English);
        Assert.Equal("Couldn't decline. Try again.", DeviceActivation.PlanDecisionFailure(false, false, 500, null, null, "laura").Error!.English);
    }

    [Fact]
    public void AnImpersonationSessionIsToldToStopViewingAndNothingOffersARetry()
    {
        var plan = DeviceActivation.PlanDecisionFailure(false, true, 403, "permission_denied", null, "laura");
        Assert.Equal("Stop viewing as laura to approve this sign-in. Only the account itself can approve devices.", plan.Error!.English);
        Assert.DoesNotContain("Try again", plan.Error.English);
    }

    [Fact]
    public void ARequestThatMovedOnShowsNoErrorUnlessItIsStillPendingAfterTheReload()
    {
        var plan = DeviceActivation.PlanDecisionFailure(false, true, 409, "conflict", null, "laura");
        Assert.True(plan.Reload);
        Assert.Null(plan.Error);
        Assert.Equal("Couldn't approve sign-in. Try again.", plan.ErrorIfUnchanged!.English);
    }

    [Fact]
    public void AQuickConnectDecisionReloadsOnlyWhenTheRequestMovedOn()
    {
        Assert.False(DeviceActivation.PlanDecisionFailure(true, false, 500, null, null, "laura").Reload);
        Assert.False(DeviceActivation.PlanDecisionFailure(true, false, 429, null, 60, "laura").Reload);
        Assert.True(DeviceActivation.PlanDecisionFailure(true, false, 410, null, null, "laura").Reload);
        Assert.True(DeviceActivation.PlanDecisionFailure(false, false, 500, null, null, "laura").Reload);
    }

    [Fact]
    public void QuickConnectApprovalFailuresHaveTheirOwnWording()
    {
        Assert.Equal("Only the account itself can approve a device, not an admin viewing as it.", DeviceActivation.ClassifyQuickConnectFailure(403, "permission_denied", null).Error!.English);
        Assert.Equal("Couldn't approve sign-in. Try again.", DeviceActivation.ClassifyQuickConnectFailure(500, null, null).Error!.English);
        Assert.Equal("You've tried too many codes. Try again in 15 minutes.", DeviceActivation.ClassifyQuickConnectFailure(429, null, 900).Error!.English);
        Assert.Equal((DecisionFailureKind.Moved, (ActivationText?)null), DeviceActivation.ClassifyQuickConnectFailure(410, null, null));
        Assert.Equal("Too many incorrect PINs. Try again in 2 minutes.", DeviceActivation.PinLockout(90).English);
        Assert.Equal("Too many incorrect PINs. Try again in 1 minute.", DeviceActivation.PinLockout(null).English);
        Assert.Equal("That PIN isn't right.", DeviceActivation.WrongPin.English);
        Assert.Equal("Your session ended. Sign in again to approve.", DeviceActivation.SessionEnded.English);
    }

    // ----- Watching -----

    [Fact]
    public void ARequestApprovedHereIsFollowedUntilItSettles()
    {
        Assert.True(DeviceActivation.ShouldWatch(Pending(d => d.Status = "approved"), approvedHere: true));
        Assert.False(DeviceActivation.ShouldWatch(Pending(d => d.Status = "approved"), approvedHere: false));
        Assert.False(DeviceActivation.ShouldWatch(Pending(d => d.Status = "consumed"), approvedHere: true));
        Assert.False(DeviceActivation.ShouldWatch(Pending(), approvedHere: false));
    }

    [Fact]
    public void APendingRemotePlaybackRequestIsFollowedUntilTheAppApprovesIt()
    {
        Assert.True(DeviceActivation.ShouldWatch(RemotePlayback(), approvedHere: false));
        Assert.False(DeviceActivation.ShouldWatch(RemotePlayback("approved"), approvedHere: false));
    }

    [Fact]
    public void AQuickConnectRequestIsNeverFollowedBecauseEveryLookupSpendsTheCodeBudget()
    {
        Assert.False(DeviceActivation.ShouldWatch(QuickConnect("approved"), approvedHere: true));
        Assert.False(DeviceActivation.ShouldWatch(QuickConnect(), approvedHere: false));
    }

    [Fact]
    public void TheWatchChecksEveryThreeSecondsForTwoMinutesThenEveryFifteen()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), DeviceActivation.WatchDelay(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(3), DeviceActivation.WatchDelay(TimeSpan.FromSeconds(119)));
        Assert.Equal(TimeSpan.FromSeconds(15), DeviceActivation.WatchDelay(TimeSpan.FromMinutes(2)));
    }

    // ----- Quick Connect profile choice -----

    [Fact]
    public void QuickConnectPicksTheActiveProfileFirstThenTheFirst()
    {
        var profiles = new List<Profile> { new() { Id = "p-laura", Name = "Laura" }, new() { Id = "p-kids", Name = "Kids", HasPin = true } };
        Assert.Equal("p-kids", DeviceActivation.SelectedProfileId(null, profiles, "p-kids"));
        Assert.Equal("p-laura", DeviceActivation.SelectedProfileId(null, profiles, "p-gone"));
        Assert.Equal("p-kids", DeviceActivation.SelectedProfileId("p-kids", profiles, "p-laura"));
    }

    [Fact]
    public void AProfilePinIsAskedForUnlessThatProfileIsAlreadyUnlockedHere()
    {
        var kids = new Profile { Id = "p-kids", Name = "Kids", HasPin = true };
        var laura = new Profile { Id = "p-laura", Name = "Laura" };
        Assert.True(DeviceActivation.NeedsPin(kids, "p-laura"));
        Assert.False(DeviceActivation.NeedsPin(kids, "p-kids"));
        Assert.False(DeviceActivation.NeedsPin(laura, "p-kids"));
        Assert.False(DeviceActivation.CanApproveQuickConnect(false, kids, needsPin: true, pin: " "));
        Assert.True(DeviceActivation.CanApproveQuickConnect(false, kids, needsPin: true, pin: "1234"));
        Assert.False(DeviceActivation.CanApproveQuickConnect(true, laura, needsPin: false, pin: ""));
        Assert.True(DeviceActivation.CanApproveQuickConnect(false, laura, needsPin: false, pin: ""));
        Assert.False(DeviceActivation.CanApproveQuickConnect(false, null, needsPin: false, pin: ""));
    }

    // ----- The requests -----

    [Fact]
    public async Task ASignedInLookupCarriesTheSessionAndTheCompactCode()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var (api, client, _) = SignedIn(http);
        var details = await api.DeviceLookupAsync(client.CaptureContext(), null, DeviceActivation.CompactCode("4821 7730"));
        Assert.Equal("pending", details.Status);
        var seen = Assert.Single(wire.Seen);
        Assert.Equal("GET /api/v2/auth/device?code=48217730", seen.Line);
        Assert.True(seen.HasBearer);
        await api.DeviceLookupAsync(client.CaptureContext(), "link-token", null);
        Assert.Equal("GET /api/v2/auth/device?token=link-token", wire.Seen[1].Line);
    }

    [Fact]
    public async Task ADecisionIsSentOnceAndANotSignedInAnswerIsNotRetriedThroughARefresh()
    {
        using var wire = new Wire { DecisionStatus = HttpStatusCode.Unauthorized }; using var http = new HttpClient(wire);
        var (api, client, _) = SignedIn(http);
        var error = await Assert.ThrowsAsync<ApiException>(() => api.DeviceDecideAsync(client.CaptureContext(), approve: true, null, "48217730"));
        Assert.Equal(401, error.StatusCode);
        var posts = wire.Seen.Where(request => request.Line.StartsWith("POST")).ToList();
        Assert.Single(posts);
        Assert.Equal("POST /api/v2/auth/device/approve", posts[0].Line);
        Assert.Equal("""{"code":"48217730"}""", posts[0].Body);
    }

    [Fact]
    public async Task ADeclineAndATokenDecisionSendOnlyWhatTheyNeed()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var (api, client, _) = SignedIn(http);
        await api.DeviceDecideAsync(client.CaptureContext(), approve: false, null, "48217730");
        await api.DeviceDecideAsync(client.CaptureContext(), approve: true, "link-token", "ignored");
        Assert.Equal("POST /api/v2/auth/device/deny", wire.Seen[0].Line);
        Assert.Equal("""{"code":"48217730"}""", wire.Seen[0].Body);
        Assert.Equal("POST /api/v2/auth/device/approve", wire.Seen[1].Line);
        Assert.Equal("""{"token":"link-token"}""", wire.Seen[1].Body);
    }

    [Fact]
    public async Task ADecisionNeverLeavesAfterTheAccountOrServerChanged()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var (api, client, auth) = SignedIn(http);
        var owner = client.CaptureContext();
        auth.Logout();
        await Assert.ThrowsAsync<OperationCanceledException>(() => api.DeviceDecideAsync(owner, approve: true, null, "48217730"));
        Assert.Empty(wire.Seen);
        Assert.False(api.IsCurrentContext(owner));
    }

    [Fact]
    public async Task AQuickConnectApprovalForAnotherProfileCarriesThatProfileAndItsPinProofOnlyForThisRequest()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var (api, client, _) = SignedIn(http);
        var owner = client.CaptureContext();
        var authority = api.CaptureProfileAuthority(owner, "p-kids", "kids-proof");
        var decision = await api.ApproveQuickConnectAsync(authority, "482177");
        Assert.Equal("approved", decision.Status);
        var seen = Assert.Single(wire.Seen);
        Assert.Equal("POST /api/v2/auth/device/approve-handoff", seen.Line);
        Assert.Equal("p-kids", seen.ProfileId);
        Assert.Equal("kids-proof", seen.ProfileProof);
        Assert.Equal("""{"code":"482177"}""", seen.Body);
        Assert.True(seen.HasBearer);
    }

    [Fact]
    public async Task AQuickConnectApprovalForTheActiveProfileUsesTheSessionsOwnProfile()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var (api, client, _) = SignedIn(http);
        await api.ApproveQuickConnectAsync(client.CaptureContext(), "482177");
        var seen = Assert.Single(wire.Seen);
        Assert.Equal("POST /api/v2/auth/device/approve-handoff", seen.Line);
        Assert.Equal("p-laura", seen.ProfileId);
    }

    [Fact]
    public async Task AProfileCapturedBeforeASwitchCannotBeUsedAfterIt()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var (api, client, auth) = SignedIn(http);
        var authority = api.CaptureProfileAuthority(client.CaptureContext(), "p-kids", null);
        auth.Logout();
        await Assert.ThrowsAsync<OperationCanceledException>(() => api.ApproveQuickConnectAsync(authority, "482177"));
        Assert.Empty(wire.Seen);
    }

    [Fact]
    public void LookupDetailsReadTheRequestTimeAndTheServerName()
    {
        var details = JsonSerializer.Deserialize<DeviceLoginLookupResponse>(
            """{"status":"canceled","requested_at":"2026-01-02T03:04:05.678Z","server_name":"Media Room","client_purpose":"device_login"}""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        Assert.Equal("canceled", details.Status);
        Assert.Equal("2026-01-02T03:04:05.678Z", details.RequestedAt);
        Assert.Equal("Media Room", details.ServerName);
    }

    private static (AuthApi Api, SiloApiClient Client, AuthService Auth) SignedIn(HttpClient http)
    {
        var client = new SiloApiClient(http);
        client.SetBaseUrl(Wire.Server);
        var api = new AuthApi(client);
        var auth = new AuthService(client, api);
        auth.SetTokens("fixture-access", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "u1", Username = "laura", Role = "user" });
        auth.SelectProfile("p-laura", profile: new() { Id = "p-laura", Name = "Laura", IsPrimary = true });
        return (api, client, auth);
    }

    private sealed record Seen(string Line, string? Body, bool HasBearer, string? ProfileId, string? ProfileProof);

    private sealed class Wire : HttpMessageHandler
    {
        internal const string Server = "https://activate-device.invalid";
        internal readonly List<Seen> Seen = [];
        internal HttpStatusCode DecisionStatus = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            Seen.Add(new(request.Method + " " + request.RequestUri!.PathAndQuery, body, request.Headers.Authorization != null,
                request.Headers.TryGetValues("X-Profile-Id", out var id) ? id.Single() : null,
                request.Headers.TryGetValues("X-Profile-Token", out var proof) ? proof.Single() : null));
            if (request.Method == HttpMethod.Get)
                return Reply("""{"status":"pending","user_code":"4821-7730","device_name":"Living room TV"}""", HttpStatusCode.OK);
            if (DecisionStatus != HttpStatusCode.OK)
                return Reply("""{"type":"https://siloserver.org/docs/api/v2/problems/unauthorized","status":401}""", DecisionStatus);
            return Reply("""{"status":"approved"}""", HttpStatusCode.OK);
        }

        private static HttpResponseMessage Reply(string json, HttpStatusCode status)
            => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, status == HttpStatusCode.OK ? "application/json" : "application/problem+json") };
    }
}
