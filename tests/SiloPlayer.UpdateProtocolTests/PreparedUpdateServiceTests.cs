using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.UpdateProtocolTests;

public sealed class PreparedUpdateServiceTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"silo-prepared-"+Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Authority_cooldown_blocks_checks_and_downloads_across_a_new_service_instance_until_exact_expiry()
    {
        Directory.CreateDirectory(root);
        var installed=Path.Combine(root,"versions","1.2.464.0");Directory.CreateDirectory(installed);
        await File.WriteAllTextAsync(Path.Combine(installed,"SiloPlayer.exe"),"synthetic exe");
        await File.WriteAllTextAsync(Path.Combine(installed,"SiloPlayer.dll"),"synthetic dll");
        var baseline=await UpdatePackBuilder.BuildAsync(installed,null,Path.Combine(root,"baseline"),new Version(1,2,464,0));
        SideBySideInstallation.Initialize(root,baseline);
        var clock=new Clock();
        using var wire=new Assets(null);using var http=new HttpClient(wire);
        var service=new AppUpdateService(http,Path.Combine(root,"cache"),new Version(1,2,464),timeProvider:clock){InstallationRoot=root};
        await service.CheckAsync();Assert.Equal(1,wire.MetadataRequests);
        var until=UpdateAuthorityCooldown.Record(root,TimeSpan.FromMinutes(45),clock);
        await service.DownloadAsync();await service.CheckAsync();
        Assert.Equal(0,wire.PackRequests);Assert.Equal(1,wire.MetadataRequests);Assert.NotNull(service.State.Error);
        var reopened=new AppUpdateService(http,Path.Combine(root,"new-cache"),new Version(1,2,464),timeProvider:clock){InstallationRoot=root};
        for(var i=0;i<3;i++){await reopened.CheckAsync();await reopened.DownloadAsync();clock.Advance(TimeSpan.FromMinutes(10));}
        Assert.Equal(1,wire.MetadataRequests);Assert.Equal(until,UpdateAuthorityCooldown.Read(root,clock));
        clock.Advance(TimeSpan.FromMinutes(15));await reopened.CheckAsync();
        Assert.Equal(2,wire.MetadataRequests);Assert.Null(reopened.State.Error);
    }
    [Fact]
    public async Task Side_by_side_policy_prepares_files_preserves_exact_check_and_reverifies()
    {
        var client=Path.Combine(root,"installed"); Directory.CreateDirectory(client);
        await File.WriteAllTextAsync(Path.Combine(client,"SiloPlayer.exe"),"synthetic exe");
        await File.WriteAllTextAsync(Path.Combine(client,"SiloPlayer.dll"),"synthetic assembly");
        var output=Path.Combine(root,"assets");
        await UpdatePackBuilder.BuildAsync(client,null,output,new Version(1,2,500,0));
        using var wire=new Assets(output); using var http=new HttpClient(wire);
        var service=new AppUpdateService(http,Path.Combine(root,"cache"),new Version(1,2,464,0))
            { PreferIncrementalUpdates=true, InstalledPayloadDirectory=client };
        await service.CheckAsync(); await service.DownloadAsync();
        Assert.True(service.State.Error is null,service.State.Error); Assert.NotNull(service.State.PreparedPackage); Assert.Null(service.State.ReadyInstaller);
        Assert.Equal(0,wire.PackRequests); Assert.True(await service.ReverifyReadyAsync());
        service.SetMode(AppUpdateMode.Automatic); Assert.True(service.ShouldInstallOnExit);
        var prepared=service.State.PreparedPackage;
        await service.CheckAsync(); Assert.Same(prepared,service.State.PreparedPackage);
        await File.WriteAllTextAsync(Path.Combine(prepared!.Directory,"client","SiloPlayer.dll"),"tampered");
        Assert.False(await service.ReverifyReadyAsync()); Assert.False(service.ShouldInstallOnExit);
    }

    [Fact]
    public async Task Side_by_side_full_fallback_rejects_unverified_installer_and_keeps_manual_recovery()
    {
        using var wire=new Assets(null); using var http=new HttpClient(wire);
        var service=new AppUpdateService(http,root,new Version(1,2,464,0)){PreferIncrementalUpdates=true};
        await service.CheckAsync(); await service.DownloadAsync();
        Assert.NotNull(service.State.Error); Assert.Null(service.State.ReadyInstaller); Assert.Equal(1,wire.PackRequests);
        Assert.True(service.State.RequiresFullInstaller); Assert.False(service.ShouldInstallOnExit);
    }

    private sealed class Assets(string? directory) : HttpMessageHandler
    {
        public int PackRequests;
        public int MetadataRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            byte[] bytes;
            if(request.RequestUri!.Host=="api.github.com")
            {
                MetadataRequests++;
                var assets=new List<object>{new {name="SiloInstaller-Windows-x64.exe",state="uploaded",size=3,browser_download_url="https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.500/"+"SiloInstaller-Windows-x64.exe",digest="sha256:"+new string('a',64)}};
                if(directory is not null) foreach(var name in new[]{UpdateManifestValidation.ManifestName,UpdateManifestValidation.PackName})
                {
                    var data=File.ReadAllBytes(Path.Combine(directory,name));
                    assets.Add(new{name,state="uploaded",size=data.Length,browser_download_url="https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.500/"+name,digest="sha256:"+Convert.ToHexString(SHA256.HashData(data))});
                }
                bytes=JsonSerializer.SerializeToUtf8Bytes(new{tag_name="v1.2.500",draft=false,prerelease=false,body="synthetic notes",published_at="2026-10-29T10:00:00Z",assets});
            }
            else { if(!request.RequestUri.AbsolutePath.EndsWith(".json"))PackRequests++; bytes=directory is null ? new byte[]{1,2,3} : File.ReadAllBytes(Path.Combine(directory,Path.GetFileName(request.RequestUri.AbsolutePath))); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
        }
    }
    private sealed class Clock:TimeProvider
    {
        private DateTimeOffset now=new(2026,10,29,12,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(TimeSpan amount)=>now+=amount;
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}




