using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Downloads;
namespace SiloPlayer.Tests;
public sealed class BatchDownloadOwnershipTests
{
    [Theory]
    [InlineData(null, null, true)] [InlineData(true,true,true)]
    [InlineData(false,true,false)] [InlineData(true,false,false)]
    public void EffectiveAvailabilityHonorsKnownRefusals(bool? allowed,bool? bounded,bool expected)
        => Assert.Equal(expected,new DownloadCapability {Enabled=true,DownloadAllowed=true,SeasonDownload=true,Allowed=allowed,BoundedCreation=bounded}.CanCreateBatch);

    [Fact]
    public async Task TerminalResponseFromRetiredProfileCannotSucceed()
    {
        using var wire=new Wire(); using var http=new HttpClient(wire); var client=new SiloApiClient(http); client.SetBaseUrl("https://batch-test.invalid");
        wire.Read = request => { client.SetProfile("replacement"); return "{\"items\":[],\"page\":{\"has_more\":false}}"; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new DownloadsApi(client).CreateDownloadsAsync(new(){ContentId="series",Series=true}));
        Assert.Single(wire.Paths);
    }
    [Fact]
    public async Task CancelledAcceptedPageNeverDispatchesNextCursor()
    {
        using var wire=new Wire(); using var http=new HttpClient(wire); var client=new SiloApiClient(http); client.SetBaseUrl("https://batch-test.invalid"); using var owner=new CancellationTokenSource();
        wire.Read = request => { owner.Cancel(); return "{\"items\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"next\"}}"; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new DownloadsApi(client).CreateDownloadsAsync(new(){ContentId="series",Series=true},owner.Token));
        Assert.Single(wire.Paths);
    }
    [Theory] [InlineData(null)] [InlineData(0)] [InlineData(2)]
    public async Task AggregateTemplatesUseAccessibleEpisodeVersionsAndLibraryScope(int? season)
    {
        using var wire=new Wire(); using var http=new HttpClient(wire); var client=new SiloApiClient(http); client.SetBaseUrl("https://batch-test.invalid");
        wire.Read = request => request.RequestUri!.AbsolutePath.EndsWith("/versions")
            ? "{\"items\":[{\"file_id\":\"11\",\"resolution\":\"1080p\"},{\"file_id\":\"12\",\"resolution\":\"2160p\"},{\"file_id\":\"13\"}]}"
            : request.RequestUri.AbsolutePath.EndsWith("/seasons") ? "{\"items\":[{\"season_number\":1}]}"
            : "{\"items\":[{\"content_id\":\"unplayable\",\"files\":[]},{\"content_id\":\"ep/1\",\"files\":[{\"file_id\":\"11\"},{\"file_id\":\"12\"},{\"file_id\":\"13\",\"unreadable\":true}]}]}";
        var versions=await new DownloadsApi(client).GetBatchTemplateVersionsAsync("series/1",season,7);
        Assert.Equal(new[]{11,12},versions.Select(v=>v.FileId));
        Assert.All(wire.Paths,path=>Assert.Contains("library_id=7",path));
        Assert.Contains(wire.Paths,path=>path.Contains("ep%2F1/versions",StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(wire.Paths,path=>path.Contains("unplayable/versions"));
        Assert.Equal(season.HasValue?2:3,wire.Paths.Count);
    }
    [Fact]
    public async Task LegacyNoTemplatesIsSupported()
    {
        using var wire=new Wire {Read = _=>"{\"items\":[]}"}; using var http=new HttpClient(wire); var client=new SiloApiClient(http); client.SetBaseUrl("https://batch-test.invalid");
        Assert.Empty(await new DownloadsApi(client).GetBatchTemplateVersionsAsync("series",0,null));
    }
    private sealed class Wire:HttpMessageHandler
    {
        internal List<string> Paths=[]; internal Func<HttpRequestMessage,string> Read = _=>"{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        { Paths.Add(request.RequestUri!.PathAndQuery); var body=Read(request); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(body)}); }
    }
}
