using System.Net;
using System.Text;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

public sealed class AdminUserAvatarTests
{
    [Fact]
    public void StylesAndSeedBatchMatchPinnedOfficialModel()
    {
        Assert.Equal(new[] { "identicon", "initials", "bottts-neutral", "fun-emoji", "pixel-art-neutral" }, AdminUserAvatars.Styles);
        var first = AdminUserAvatars.Batch("identicon");
        Assert.Equal(18, first.Count);
        Assert.Equal("preset:dicebear:identicon:pepper-pebble", first[0].Reference);
        Assert.Equal("Pepper Pebble", first[0].Label);
        Assert.NotEqual(first[0], AdminUserAvatars.Batch("identicon", 1)[0]);
        Assert.Equal(first[0], AdminUserAvatars.Batch("unsupported")[0]);
        Assert.Equal(first[0], AdminUserAvatars.Batch("identicon", 32)[0]);
        Assert.Equal("fun-emoji", AdminUserAvatars.Style("preset:dicebear:fun-emoji:pepper-otter"));
    }

    [Fact]
    public async Task PreviewUsesOnlyKnownPresetOriginsWithoutAccountHeaders()
    {
        var handler = new PreviewHandler();
        using var http = new HttpClient(handler);
        using var images = new AdminUserAvatarImages(http);
        var bytes = await images.ReadAsync("preset:dicebear:fun-emoji:pepper otter", new Uri("https://silo.invalid/base/"));
        Assert.NotEmpty(bytes);
        Assert.Equal("https://api.dicebear.com/9.x/fun-emoji/svg?seed=pepper%20otter&size=128&radius=24&backgroundType=gradientLinear", handler.Requests.Single());
        await images.ReadAsync("preset:avatar-3", new Uri("https://silo.invalid/base/"));
        Assert.Equal("https://silo.invalid/base/profile-avatars/avatar-3.svg", handler.Requests.Last());
        foreach (var reference in new[] { "https://elsewhere.invalid/image.svg", "preset:avatar-9", "preset:dicebear:unknown:seed", "preset:dicebear:initials:" })
            await Assert.ThrowsAsync<ArgumentException>(() => images.ReadAsync(reference, new Uri("https://silo.invalid/")));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("https://silo.invalid", "")]
    [InlineData("https://silo.invalid/", "")]
    [InlineData("https://silo.invalid/silo", "/silo")]
    [InlineData("https://silo.invalid/silo/", "/silo")]
    [InlineData("https://silo.invalid/silo/?unused=1#fragment", "/silo")]
    public void LegacyPreviewsPreserveConfiguredServerPrefix(string server, string prefix) =>
        Assert.Equal("https://silo.invalid" + prefix + "/profile-avatars/avatar-1.svg",
            AdminUserAvatarImages.PreviewUri("preset:avatar-1", new Uri(server)).AbsoluteUri);

    [Fact]
    public async Task PreviewRejectsRedirectsOversizeAndExternalSvgResources()
    {
        var handler = new PreviewHandler();
        using var http = new HttpClient(handler);
        using var images = new AdminUserAvatarImages(http);
        handler.Status = HttpStatusCode.Redirect;
        await Assert.ThrowsAsync<HttpRequestException>(() => images.ReadAsync("preset:avatar-1", new Uri("https://silo.invalid/")));
        handler.Status = HttpStatusCode.OK;
        handler.Svg = new string(' ', AdminUserAvatarImages.MaximumBytes + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => images.ReadAsync("preset:avatar-1", new Uri("https://silo.invalid/")));
        handler.Svg = """<svg xmlns="http://www.w3.org/2000/svg"><image href="https://other.invalid/pixel"/></svg>""";
        await Assert.ThrowsAsync<InvalidDataException>(() => images.ReadAsync("preset:avatar-1", new Uri("https://silo.invalid/")));
        handler.Svg = """<!DOCTYPE svg [<!ENTITY external SYSTEM 'https://other.invalid/entity'>]><svg xmlns="http://www.w3.org/2000/svg"/>""";
        await Assert.ThrowsAsync<InvalidDataException>(() => images.ReadAsync("preset:avatar-1", new Uri("https://silo.invalid/")));
    }

    [Fact]
    public async Task PreviewHonorsRetiredForegroundCancellation()
    {
        var handler = new PreviewHandler { Hold = true };
        using var http = new HttpClient(handler);
        using var images = new AdminUserAvatarImages(http);
        using var retired = new CancellationTokenSource();
        var pending = images.ReadAsync("preset:avatar-1", new Uri("https://silo.invalid/"), retired.Token);
        await handler.Started.Task;
        retired.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Single(handler.Requests);
    }

    private sealed class PreviewHandler : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        internal string Svg { get; set; } = """<svg xmlns="http://www.w3.org/2000/svg"><rect width="128" height="128"/></svg>""";
        internal bool Hold { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Null(request.Headers.Referrer);
            Requests.Add(request.RequestUri!.AbsoluteUri);
            Started.TrySetResult(true);
            if (Hold) await Task.Delay(Timeout.Infinite, token);
            return new(Status) { Content = new StringContent(Svg, Encoding.UTF8, "image/svg+xml") };
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("１２３４")]
    public void EditorRequiresFourAsciiDigitsWithoutChangingServerPinContract(string pin)
    {
        Assert.Throws<ArgumentException>(() => AdminUserAvatars.ValidateEditorPin(pin));
        AdminUserAvatars.ValidateEditorPin("0123");
    }
}
