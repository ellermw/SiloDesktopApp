using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class AccountPasswordTests
{
    private const string Allowed = """{"state":"available","allowed":true,"requires_current_password":true,"minimum_password_length":8,"maximum_password_bytes":72}""";
    private const string SupportsSessions = """{"state":"available","allowed":true,"sign_out_other_sessions":true,"requires_current_password":true,"minimum_password_length":8,"maximum_password_bytes":72}""";

    [Fact]
    public async Task LegacyDefaultCancellationArgumentRemainsSourceCompatibleAndOmitsSessionMember()
    {
        var (_, client, handler) = Create();
        var api = new AccountPasswordApi(client);
        await api.ChangePasswordAsync(client.CaptureContext(), "old password", "new password", default);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.False(body.RootElement.TryGetProperty("sign_out_other_sessions", out _));
        Assert.Equal("POST /api/v2/account/password", Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task AdvertisedSessionOptionIsAvailableToThePasswordForm()
    {
        var (vm, _, handler) = Create();
        handler.Capability = SupportsSessions;
        await vm.LoadAsync();
        Assert.True(vm.CanSignOutOtherSessions);
        Assert.False(vm.SignOutOtherSessions);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(null, true)]
    [InlineData(false, true)]
    public async Task SessionMemberIsSentOnlyWhenAdvertisedAndSelected(bool? support, bool selected)
    {
        var (vm, client, handler) = Create();
        client.SetAccessToken("synthetic-current-session");
        handler.Capability = support is null ? Allowed : Allowed[..^1] +
            $",\"sign_out_other_sessions\":{support.Value.ToString().ToLowerInvariant()}}}";
        await vm.LoadAsync();
        Fill(vm);
        vm.SignOutOtherSessions = selected;
        await vm.SubmitAsync();
        using var body = JsonDocument.Parse(handler.Body!);
        var sent = body.RootElement.TryGetProperty("sign_out_other_sessions", out var member);
        Assert.Equal(support == true && selected, sent);
        if (sent) Assert.True(member.GetBoolean());
        Assert.Equal(sent ? "Password changed. Other devices are signed out." : "Password changed", vm.SuccessMessage);
        Assert.False(vm.SignOutOtherSessions);
        Assert.Equal("synthetic-current-session", client.AccessToken);
        Assert.Equal(2, handler.Requests.Count); // No local revocation/sign-out calls.
    }

    [Theory]
    [InlineData("available", false)]
    [InlineData("not_configured", true)]
    public async Task AdvertisedOptionCannotAuthorizeUnavailableOrReadOnlyForm(string state, bool allowed)
    {
        var (vm, _, handler) = Create();
        handler.Capability = $$"""{"state":"{{state}}","allowed":{{allowed.ToString().ToLowerInvariant()}},"sign_out_other_sessions":true,"minimum_password_length":8,"maximum_password_bytes":72}""";
        await vm.LoadAsync();
        vm.SignOutOtherSessions = true;
        Fill(vm);
        await vm.SubmitAsync();
        Assert.False(vm.CanSignOutOtherSessions);
        Assert.False(vm.SignOutOtherSessions);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ChoiceResetsOnReloadDepartureAndAccountChange()
    {
        var (vm, client, handler) = Create();
        handler.Capability = SupportsSessions;
        await vm.LoadAsync();
        vm.SignOutOtherSessions = true;
        await vm.LoadAsync();
        Assert.False(vm.SignOutOtherSessions);
        vm.SignOutOtherSessions = true;
        vm.Deactivate();
        Assert.False(vm.SignOutOtherSessions);
        Assert.False(vm.CanSignOutOtherSessions);
        await vm.LoadAsync();
        vm.SignOutOtherSessions = true;
        client.BeginAuthenticationSession("replacement-synthetic-session");
        await vm.SubmitAsync();
        Assert.False(vm.SignOutOtherSessions);
        Assert.False(vm.CanSignOutOtherSessions);
        Assert.All(handler.Requests, request => Assert.StartsWith("GET", request));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingSubmitFreezesChoiceAndSuccessFeedback(bool selected)
    {
        var (vm, _, handler) = Create();
        handler.Capability = SupportsSessions;
        await vm.LoadAsync();
        Fill(vm);
        vm.SignOutOtherSessions = selected;
        handler.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var submission = vm.SubmitAsync();
        Assert.True(vm.IsSubmitting);
        vm.SignOutOtherSessions = !selected;
        Assert.Equal(selected, vm.SignOutOtherSessions);
        handler.Pending.SetResult(new(HttpStatusCode.NoContent));
        await submission;
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(selected, body.RootElement.TryGetProperty("sign_out_other_sessions", out _));
        Assert.Equal(selected ? "Password changed. Other devices are signed out." : "Password changed", vm.SuccessMessage);
    }

    [Fact]
    public async Task LateSupportedCapabilityCannotRestoreChoiceAfterReload()
    {
        var (vm, _, handler) = Create();
        handler.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = handler.Pending;
        var oldLoad = vm.LoadAsync();
        handler.Pending = null;
        await vm.LoadAsync(); // Older server on the next visit.
        pending.SetResult(new(HttpStatusCode.OK) { Content = new StringContent(SupportsSessions) });
        await oldLoad;
        Assert.True(vm.CanChangePassword);
        Assert.False(vm.CanSignOutOtherSessions);
        Assert.False(vm.SignOutOtherSessions);
    }

    [Fact]
    public async Task LateOptedInSubmitCannotOverwriteAnotherVisit()
    {
        var (vm, _, handler) = Create();
        handler.Capability = SupportsSessions;
        await vm.LoadAsync();
        Fill(vm);
        vm.SignOutOtherSessions = true;
        handler.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = handler.Pending;
        var submission = vm.SubmitAsync();
        handler.Pending = null;
        await vm.LoadAsync();
        Fill(vm);
        pending.SetResult(new(HttpStatusCode.NoContent));
        await submission;
        Assert.Null(vm.SuccessMessage);
        Assert.False(vm.SignOutOtherSessions);
        Assert.Equal("new password", vm.NewPassword);
    }

    [Fact]
    public async Task SuccessfulChangeUsesV2AndOriginatingProfileAndClearsSecrets()
    {
        var (vm, _, handler) = Create();
        await vm.LoadAsync();
        Fill(vm);
        await vm.SubmitAsync();
        Assert.Equal("POST /api/v2/account/password", handler.Requests.Last());
        Assert.Equal("primary", handler.Profile);
        Assert.Equal("profile-proof", handler.ProfileToken);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("old password", body.RootElement.GetProperty("current_password").GetString());
        Assert.Equal("new password", body.RootElement.GetProperty("new_password").GetString());
        Assert.False(body.RootElement.TryGetProperty("confirm_password", out _));
        Assert.Equal("Password changed", vm.SuccessMessage);
        Assert.Empty(vm.CurrentPassword);
        Assert.Empty(vm.NewPassword);
        Assert.Empty(vm.ConfirmPassword);
    }

    [Theory]
    [InlineData("😀😀😀😀", "😀😀😀😀", "at least 8 characters")]
    [InlineData("ééééééééééééééééééééééééééééééééééééé", "ééééééééééééééééééééééééééééééééééééé", "at most 72 bytes")]
    [InlineData("new password", "different", "do not match")]
    public async Task InvalidPasswordDoesNotSendMutation(string password, string confirmation, string error)
    {
        var (vm, _, handler) = Create();
        await vm.LoadAsync();
        Fill(vm);
        vm.NewPassword = password;
        vm.ConfirmPassword = confirmation;
        await vm.SubmitAsync();
        Assert.Contains(error, vm.ErrorMessage);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UnicodeScalarMinimumAndUtf8ByteBoundaryAreAccepted()
    {
        var (vm, _, handler) = Create();
        await vm.LoadAsync();
        Fill(vm);
        vm.NewPassword = vm.ConfirmPassword = string.Concat(Enumerable.Repeat("😀", 18));
        await vm.SubmitAsync();
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("Password changed", vm.SuccessMessage);
    }

    [Theory]
    [InlineData("available", false)]
    [InlineData("not_configured", true)]
    public async Task RestrictedOrUnavailableCapabilityCannotSubmit(string state, bool allowed)
    {
        var (vm, _, handler) = Create();
        handler.Capability = $$"""{"state":"{{state}}","allowed":{{allowed.ToString().ToLowerInvariant()}},"minimum_password_length":8,"maximum_password_bytes":72}""";
        await vm.LoadAsync();
        Fill(vm);
        await vm.SubmitAsync();
        Assert.False(vm.CanChangePassword);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MissingCurrentPasswordIsRejectedWithoutTrimmingPasswords()
    {
        var (vm, _, handler) = Create();
        await vm.LoadAsync();
        Fill(vm);
        vm.CurrentPassword = "";
        await vm.SubmitAsync();
        Assert.Single(handler.Requests);
        Assert.Contains("Current password", vm.ErrorMessage);
        vm.CurrentPassword = " ";
        await vm.SubmitAsync();
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CapabilityFailureFailsClosedAndCanBeRetried()
    {
        var (vm, _, handler) = Create();
        handler.GetStatus = HttpStatusCode.ServiceUnavailable;
        await vm.LoadAsync();
        Assert.False(vm.CanChangePassword);
        Assert.Contains("could not be loaded", vm.ErrorMessage);
        handler.GetStatus = HttpStatusCode.OK;
        await vm.LoadAsync();
        Assert.True(vm.CanChangePassword);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task UsesServerLimitsInsteadOfHardcodedPolicyAndClearsOnDeparture()
    {
        var (vm, _, handler) = Create();
        handler.Capability = """{"state":"available","allowed":true,"minimum_password_length":3,"maximum_password_bytes":6}""";
        await vm.LoadAsync();
        Fill(vm);
        vm.NewPassword = vm.ConfirmPassword = "ééé";
        await vm.SubmitAsync();
        Assert.Equal("Password changed", vm.SuccessMessage);
        Fill(vm);
        vm.Deactivate();
        Assert.Empty(vm.CurrentPassword);
        Assert.Empty(vm.NewPassword);
        Assert.Empty(vm.ConfirmPassword);
        Assert.Null(vm.SuccessMessage);
        Assert.False(vm.CanChangePassword);
    }

    [Fact]
    public async Task FailurePreservesFieldsForRetryAndThenSuccessClearsThem()
    {
        var (vm, _, handler) = Create();
        await vm.LoadAsync();
        Fill(vm);
        handler.PostStatus = HttpStatusCode.UnprocessableEntity;
        await vm.SubmitAsync();
        Assert.NotEmpty(vm.ErrorMessage!);
        Assert.Equal("old password", vm.CurrentPassword);
        Assert.False(vm.IsSubmitting);
        handler.PostStatus = HttpStatusCode.NoContent;
        await vm.SubmitAsync();
        Assert.Empty(vm.CurrentPassword);
    }

    [Fact]
    public async Task SwitchedProfileCannotUsePreviouslyLoadedCapability()
    {
        var (vm, client, handler) = Create();
        await vm.LoadAsync();
        Fill(vm);
        client.SetProfile("secondary");
        await vm.SubmitAsync();
        Assert.Single(handler.Requests);
        Assert.Empty(vm.CurrentPassword);
        Assert.False(vm.CanChangePassword);
    }

    [Fact]
    public async Task LateCapabilityAfterDepartureDoesNotReenableForm()
    {
        var (vm, _, handler) = Create();
        handler.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = vm.LoadAsync();
        vm.Deactivate();
        handler.Pending.SetResult(new(HttpStatusCode.OK) { Content = new StringContent(Allowed) });
        await loading;
        Assert.False(vm.CanChangePassword);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LateSubmissionCannotReportSuccessAfterProfileSwitchOrAllowDuplicateSubmission()
    {
        var (vm, client, handler) = Create();
        await vm.LoadAsync();
        Fill(vm);
        handler.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var submission = vm.SubmitAsync();
        await vm.SubmitAsync();
        Assert.Equal(2, handler.Requests.Count);
        client.SetProfile("secondary");
        handler.Pending.SetResult(new(HttpStatusCode.NoContent));
        await submission;
        Assert.Null(vm.SuccessMessage);
        Assert.Empty(vm.CurrentPassword);
        Assert.False(vm.CanChangePassword);
    }

    [Fact]
    public async Task ContextBoundApiRejectsAStaleRequestBeforeItReachesNetwork()
    {
        var (_, client, handler) = Create();
        var origin = client.CaptureContext();
        client.SetProfile("secondary");
        var api = new AccountPasswordApi(client);
        await Assert.ThrowsAsync<OperationCanceledException>(() => api.ChangePasswordAsync(origin, "old", "new password"));
        Assert.Empty(handler.Requests);
    }

    private static void Fill(AccountPasswordViewModel vm)
    {
        vm.CurrentPassword = "old password";
        vm.NewPassword = vm.ConfirmPassword = "new password";
    }

    private static (AccountPasswordViewModel, SiloApiClient, Handler) Create()
    {
        var handler = new Handler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.example");
        client.SetProfile("primary", "profile-proof");
        return (new AccountPasswordViewModel(new AccountPasswordApi(client)), client, handler);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public string Capability = Allowed;
        public HttpStatusCode GetStatus = HttpStatusCode.OK;
        public HttpStatusCode PostStatus = HttpStatusCode.NoContent;
        public string? Body, Profile, ProfileToken;
        public TaskCompletionSource<HttpResponseMessage>? Pending;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            Profile = request.Headers.GetValues("X-Profile-Id").Single();
            ProfileToken = request.Headers.TryGetValues("X-Profile-Token", out var tokens) ? tokens.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            if (Pending is not null) return await Pending.Task; // Intentionally ignores cancellation: stale server response.
            return request.Method == HttpMethod.Get
                ? new(GetStatus) { Content = new StringContent(Capability) }
                : new(PostStatus) { Content = new StringContent("""{"detail":"Current password is incorrect."}""") };
        }
    }
}
