using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class PickerManagementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddUsesPrimaryAuthorityWithoutChangingViewer(bool protectedPrimary)
    {
        using var f = new Fixture(protectedPrimary);
        await f.Vm.LoadProfilesCommand.ExecuteAsync(null);
        await f.Vm.BeginManagementAsync(adding: true);
        Assert.Equal(protectedPrimary, f.Vm.IsPinRequired);
        if (protectedPrimary)
        {
            Assert.Null(f.Vm.AddSession);
            f.Vm.Pin = "1234"; await f.Vm.VerifyPinCommand.ExecuteAsync(null);
        }
        var session = Assert.IsType<ProfileAddSession>(f.Vm.AddSession);
        var saved = await session.SaveAsync(new() { Name = "New" }, null);
        Assert.Equal("created", saved.Id);
        Assert.Equal("viewer", f.Client.ProfileId);
        Assert.Equal("viewer", f.Auth.SelectedProfileId);
        Assert.Equal("viewer", f.Local.Load().LastProfileId);
        Assert.Equal(0, f.HomeEvents); Assert.Equal(0, f.ManageEvents);
        Assert.Equal(1, f.Wire.Creates); Assert.Equal(1, f.Wire.Libraries);
        Assert.All(f.Wire.PrimaryRequests, request => Assert.Equal("primary", request.Profile));
        Assert.All(f.Wire.PrimaryRequests, request => Assert.Equal(protectedPrimary ? "synthetic-proof" : null, request.Proof));
    }

    [Fact]
    public async Task AcceptedCreateAndFailedAvatarRetriesOnlyAvatar()
    {
        using var f = new Fixture(false); await f.Vm.LoadProfilesCommand.ExecuteAsync(null);
        await f.Vm.BeginManagementAsync(true); var session = f.Vm.AddSession!;
        var avatar = new ProfileAvatarUpload("avatar.png", [1, 2], "image/png");
        f.Wire.FailAvatar = true;
        await Assert.ThrowsAsync<ApiException>(() => session.SaveAsync(new() { Name = "New" }, avatar));
        Assert.Equal("created", session.CreatedProfile?.Id);
        f.Wire.FailAvatar = false;
        Assert.Equal("created", (await session.SaveAsync(new() { Name = "New" }, avatar)).Id);
        Assert.Equal(1, f.Wire.Creates); Assert.Equal(2, f.Wire.Avatars);
    }

    [Fact]
    public async Task CancelAfterAcceptedCreateRefreshesPickerWithoutSelectingCreatedProfile()
    {
        using var f = new Fixture(false); await f.Vm.LoadProfilesCommand.ExecuteAsync(null);
        await f.Vm.BeginManagementAsync(true); var session = f.Vm.AddSession!;
        f.Wire.FailAvatar = true;
        await Assert.ThrowsAsync<ApiException>(() => session.SaveAsync(new() { Name = "New" }, new("fixture.png", [1], "image/png")));
        session.Cancel(); await f.Vm.FinishAddAsync(session, false);
        Assert.Contains(f.Vm.Profiles, profile => profile.Id == "created");
        Assert.Equal("viewer", f.Client.ProfileId); Assert.Equal("viewer", f.Local.Load().LastProfileId);
        Assert.Equal(1, f.Wire.Creates); Assert.Equal(1, f.Wire.Avatars);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManageSelectsPrimaryAndRoutesWithoutHomeOrLibrary(bool hasPin)
    {
        using var f = new Fixture(hasPin); await f.Vm.LoadProfilesCommand.ExecuteAsync(null);
        await f.Vm.BeginManagementAsync(false);
        if (hasPin) { f.Vm.Pin = "1234"; await f.Vm.VerifyPinCommand.ExecuteAsync(null); }
        Assert.Equal("primary", f.Auth.SelectedProfileId);
        Assert.Equal("primary", f.Local.Load().LastProfileId);
        Assert.Equal(1, f.ManageEvents); Assert.Equal(0, f.HomeEvents); Assert.Equal(0, f.Wire.Libraries);
    }

    [Theory]
    [InlineData("pin")]
    [InlineData("libraries")]
    [InlineData("create")]
    [InlineData("avatar")]
    public async Task CanceledLateCompletionCannotPublishOrStartNextWrite(string stage)
    {
        using var f = new Fixture(stage == "pin"); await f.Vm.LoadProfilesCommand.ExecuteAsync(null);
        f.Wire.Hold = stage;
        Task pending;
        if (stage == "pin")
        { await f.Vm.BeginManagementAsync(true); f.Vm.Pin = "1234"; pending = f.Vm.VerifyPinCommand.ExecuteAsync(null); }
        else if (stage == "libraries") pending = f.Vm.BeginManagementAsync(true);
        else
        {
            await f.Vm.BeginManagementAsync(true);
            pending = f.Vm.AddSession!.SaveAsync(new() { Name = "New" }, new("avatar.png", [1], "image/png"));
        }
        await f.Wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Vm.CancelProfileLoad(); f.Wire.Release.TrySetResult();
        try { await pending; } catch (OperationCanceledException) { }
        Assert.Null(f.Vm.AddSession); Assert.Equal("viewer", f.Client.ProfileId);
        Assert.Equal(0, f.HomeEvents); Assert.Equal(0, f.ManageEvents);
        if (stage == "create") Assert.Equal(0, f.Wire.Avatars);
    }

    [Fact]
    public async Task RetiredAccountCannotPublishAndDetached403CannotClearViewerOrReplay401()
    {
        using var f = new Fixture(false); await f.Vm.LoadProfilesCommand.ExecuteAsync(null);
        await f.Vm.BeginManagementAsync(true); var session = f.Vm.AddSession!;
        var clears = 0; var refreshes = 0;
        f.Client.SetProfileVerificationRequiredHandler(_ => clears++);
        f.Client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        f.Wire.CreateStatus = HttpStatusCode.Forbidden;
        await Assert.ThrowsAsync<ApiException>(() => session.SaveAsync(new() { Name = "New" }, null));
        Assert.Equal(0, clears); Assert.Equal("viewer", f.Client.ProfileId);
        f.Wire.CreateStatus = HttpStatusCode.Unauthorized;
        await Assert.ThrowsAsync<ApiException>(() => session.SaveAsync(new() { Name = "New" }, null));
        Assert.Equal(0, refreshes); Assert.Equal(2, f.Wire.Creates);
        f.Client.BeginAuthenticationSession("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SaveAsync(new() { Name = "New" }, null));
        Assert.Equal(2, f.Wire.Creates);
    }

    [Fact]
    public async Task MissingPrimaryAndCurrentTimeoutRemainVisibleAndRetryable()
    {
        using var f = new Fixture(false); await f.Vm.LoadProfilesCommand.ExecuteAsync(null);
        f.Vm.Profiles.Clear(); f.Vm.Profiles.Add(new() { Id = "viewer" });
        await f.Vm.BeginManagementAsync(true); Assert.Contains("primary", f.Vm.ErrorMessage);
        await f.Vm.LoadProfilesCommand.ExecuteAsync(null); f.Wire.TimeoutLibraries = true;
        await f.Vm.BeginManagementAsync(true);
        Assert.NotNull(f.Vm.ErrorMessage); Assert.False(f.Vm.IsManagementBusy);
        f.Wire.TimeoutLibraries = false; await f.Vm.BeginManagementAsync(true); Assert.NotNull(f.Vm.AddSession);
    }

    [Fact]
    public async Task EmptyBootstrapStillSelectsFirstCreatedProfile()
    {
        using var f = new Fixture(false);
        await f.Vm.CreateProfileCommand.ExecuteAsync(new CreateProfileRequest { Name = "First" });
        Assert.Equal("created", f.Auth.SelectedProfileId); Assert.Equal("created", f.Local.Load().LastProfileId);
        Assert.Equal(1, f.HomeEvents); Assert.Equal(0, f.ManageEvents);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("avatar")]
    public async Task AccountRetirementDuringWriteCannotReturnProfileOrWriteAvatar(string stage)
    {
        using var f = new Fixture(false); await f.Vm.LoadProfilesCommand.ExecuteAsync(null);
        await f.Vm.BeginManagementAsync(true); var session = f.Vm.AddSession!;
        f.Wire.Hold = stage;
        var pending = session.SaveAsync(new() { Name = "New" }, new("fixture.png", [1], "image/png"));
        await f.Wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Client.BeginAuthenticationSession("replacement"); f.Wire.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(session.IsCurrent); Assert.Equal(0, f.HomeEvents); Assert.Equal(0, f.ManageEvents);
        if (stage == "create") Assert.Equal(0, f.Wire.Avatars);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Wire Wire; public readonly SiloApiClient Client; public readonly AuthService Auth;
        public readonly ProfileSelectViewModel Vm; public readonly SettingsService Local;
        public int HomeEvents; public int ManageEvents;
        private readonly HttpClient _http;
        public Fixture(bool hasPin)
        {
            Wire = new(hasPin); _http = new(Wire); Client = new(_http); Client.SetBaseUrl("https://picker.invalid");
            Client.BeginAuthenticationSession("synthetic-access");
            var api = new AuthApi(Client); Auth = new(Client, api); Auth.SelectProfile("viewer");
            Local = new(Path.Combine(Path.GetTempPath(), "SiloPickerTests", Guid.NewGuid().ToString("N")));
            Local.Load().LastProfileId = "viewer";
            Vm = new(api, new(Client), new(Client), Auth, Local);
            Vm.ProfileSelected += () => HomeEvents++; Vm.ManageProfilesSelected += () => ManageEvents++;
        }
        public void Dispose() { Vm.CancelProfileLoad(); Auth.Dispose(); _http.Dispose(); }
    }

    private sealed class Wire(bool hasPin) : HttpMessageHandler
    {
        public int Creates, Avatars, Libraries; public bool FailAvatar, TimeoutLibraries;
        public HttpStatusCode CreateStatus = HttpStatusCode.OK;
        public string Hold = "";
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(string? Profile, string? Proof)> PrimaryRequests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            var stage = path.EndsWith("verify-pin") ? "pin" : path.EndsWith("libraries") ? "libraries"
                : path.EndsWith("avatar") ? "avatar" : r.Method == HttpMethod.Post ? "create" : "profiles";
            if (stage is "libraries" or "create" or "avatar")
                PrimaryRequests.Add((r.Headers.GetValues("X-Profile-Id").Single(), r.Headers.TryGetValues("X-Profile-Token", out var proof) ? proof.Single() : null));
            if (stage == "create") Creates++; if (stage == "avatar") Avatars++; if (stage == "libraries") Libraries++;
            if (Hold == stage) { Entered.TrySetResult(); await Release.Task; }
            if (stage == "libraries" && TimeoutLibraries) throw new OperationCanceledException("synthetic HTTP timeout");
            if (stage == "create" && CreateStatus != HttpStatusCode.OK) return Reply(new { error = "profile_unverified" }, CreateStatus);
            if (stage == "avatar" && FailAvatar) return Reply(new { error = "upload_failed" }, HttpStatusCode.ServiceUnavailable);
            return stage switch
            {
                "profiles" => Reply(new { items = Creates == 0
                    ? new[] { new { id = "primary", name = "Primary", is_primary = true, has_pin = hasPin }, new { id = "viewer", name = "Viewer", is_primary = false, has_pin = false } }
                    : new[] { new { id = "primary", name = "Primary", is_primary = true, has_pin = hasPin }, new { id = "viewer", name = "Viewer", is_primary = false, has_pin = false }, new { id = "created", name = "New", is_primary = false, has_pin = false } }, avatar_upload_enabled = true }),
                "pin" => Reply(new { valid = true, profile_token = "synthetic-proof" }),
                "libraries" => Reply(new { items = new[] { new { id = "1", name = "Movies" } } }),
                _ => Reply(new { id = "created", name = "New" })
            };
        }
        private static HttpResponseMessage Reply(object value, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
