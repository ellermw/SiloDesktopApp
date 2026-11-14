using System.Collections.Concurrent;
using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Synthetic admin/user principals and a scripted HTTP handler for Admin Monitor tests. No live data.</summary>
internal sealed class AdminMonitorKit : IDisposable
{
    public AdminMonitorHttp Http { get; }
    public SiloApiClient Client { get; }
    public AuthService Auth { get; }
    private readonly HttpClient _httpClient;

    private AdminMonitorKit(AdminMonitorHttp http)
    {
        Http = http;
        _httpClient = new HttpClient(http);
        Client = new SiloApiClient(_httpClient);
        Client.SetBaseUrl("https://silo.test");
        Auth = new AuthService(Client, new AuthApi(Client));
        Auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
    }

    /// <summary>An admin account on its primary profile unless told otherwise.</summary>
    public static AdminMonitorKit Create(string role = "admin", bool primary = true, bool resolvedProfile = true, string accountId = "1")
    {
        var kit = new AdminMonitorKit(new AdminMonitorHttp());
        kit.Auth.SetCurrentUser(new UserInfo { Id = accountId, Username = "synthetic", Role = role });
        if (resolvedProfile)
            kit.Auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = primary });
        else
            kit.Auth.SelectProfile("p1");
        return kit;
    }

    public void SetRole(string role) => Auth.SetCurrentUser(new UserInfo { Id = Auth.CurrentUser!.Id, Username = "synthetic", Role = role });

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public void Dispose()
    {
        Auth.Dispose();
        _httpClient.Dispose();
    }
}

internal sealed class AdminMonitorHttp : HttpMessageHandler
{
    public ConcurrentQueue<string> Requests { get; } = new();
    public ConcurrentQueue<string> Bodies { get; } = new();
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        (_, _) => Task.FromResult(AdminMonitorKit.Json("{}"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue($"{request.Method} {request.RequestUri!.PathAndQuery}");
        Bodies.Enqueue(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        return await Respond(request, cancellationToken);
    }
}
