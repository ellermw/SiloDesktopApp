using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class ProtectedProfileLoginTests
{
    [Fact]
    public async Task ValidNativePinSelectsProtectedProfileAndSendsProofOnNextRead()
    {
        using var fixture = new Fixture();
        await fixture.Vm.SelectProfileCommand.ExecuteAsync(fixture.Protected);
        Assert.True(fixture.Vm.IsPinRequired);
        Assert.Null(fixture.Client.ProfileId);
        fixture.Vm.Pin = "2468";
        await fixture.Vm.VerifyPinCommand.ExecuteAsync(null);
        Assert.False(fixture.Vm.IsPinRequired);
        Assert.Null(fixture.Vm.PinErrorMessage);
        Assert.Equal("locked", fixture.Auth.SelectedProfileId);
        Assert.Equal("locked", fixture.Wire.ReadProfile);
        Assert.Equal("fixture-profile-proof", fixture.Wire.ReadProof);
        Assert.Equal(1, fixture.Activations);
        Assert.Equal("", fixture.Vm.Pin);
    }

    [Fact]
    public async Task IncorrectPinDoesNotSelectOrReadProtectedProfile()
    {
        using var fixture = new Fixture();
        await fixture.Vm.SelectProfileCommand.ExecuteAsync(fixture.Protected);
        fixture.Vm.Pin = "0000";
        await fixture.Vm.VerifyPinCommand.ExecuteAsync(null);
        Assert.True(fixture.Vm.IsPinRequired);
        Assert.Equal("Incorrect PIN", fixture.Vm.PinErrorMessage);
        Assert.Null(fixture.Auth.SelectedProfileId);
        Assert.Equal(0, fixture.Activations);
        Assert.Null(fixture.Wire.ReadProfile);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        public Wire Wire { get; } = new();
        public SiloApiClient Client { get; }
        public AuthService Auth { get; }
        public ProfileSelectViewModel Vm { get; }
        public Profile Protected { get; } = new() { Id = "locked", Name = "Protected fixture", HasPin = true };
        public int Activations;
        public Fixture()
        {
            _http = new(Wire); Client = new(_http); Client.SetBaseUrl("https://protected-profile.fixture.invalid");
            Client.BeginAuthenticationSession("fixture-account-proof");
            var authApi = new AuthApi(Client); Auth = new(Client, authApi, credentialStore: null);
            Vm = new(authApi, new(Client), new(Client), Auth,
                new SettingsService(Path.Combine(Path.GetTempPath(), "SiloProtectedProfileTests", Guid.NewGuid().ToString("N"))));
            Vm.ProfileSelected += () => Activations++;
        }
        public void Dispose() { Vm.CancelProfileLoad(); Auth.Dispose(); _http.Dispose(); }
    }

    private sealed class Wire : HttpMessageHandler
    {
        public string? ReadProfile, ReadProof;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "protected-profile.fixture.invalid") throw new InvalidOperationException("Unexpected network");
            if (request.RequestUri.AbsolutePath.EndsWith("/verify-pin"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var valid = body.RootElement.GetProperty("pin").GetString() == "2468";
                return Reply(valid ? """{"valid":true,"profile_token":"fixture-profile-proof","expires_at":null}""" : """{"valid":false,"expires_at":null}""");
            }
            ReadProfile = request.Headers.TryGetValues("X-Profile-Id", out var ids) ? ids.Single() : null;
            ReadProof = request.Headers.TryGetValues("X-Profile-Token", out var proofs) ? proofs.Single() : null;
            return Reply("""{"items":[]}""");
        }
        private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
