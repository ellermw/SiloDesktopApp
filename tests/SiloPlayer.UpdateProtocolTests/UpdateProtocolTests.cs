using SiloPlayer.Core.Services.Updates;
using System.Text;

namespace SiloPlayer.UpdateProtocolTests;

public sealed class UpdateProtocolTests
{
    [Fact]
    public void Elevated_helper_requires_privileged_ownership_and_no_unprivileged_write_grants()
    {
        var admin=new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid,null);
        var users=new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid,null);
        var acl=new System.Security.AccessControl.DirectorySecurity();
        acl.SetOwner(admin);
        acl.AddAccessRule(new(admin,System.Security.AccessControl.FileSystemRights.FullControl,System.Security.AccessControl.AccessControlType.Allow));
        acl.AddAccessRule(new(users,System.Security.AccessControl.FileSystemRights.ReadAndExecute,System.Security.AccessControl.AccessControlType.Allow));
        Assert.True(UpdateInstallation.IsPrivilegedAcl(acl));
        acl.AddAccessRule(new(users,System.Security.AccessControl.FileSystemRights.WriteData,System.Security.AccessControl.AccessControlType.Allow));
        Assert.False(UpdateInstallation.IsPrivilegedAcl(acl));
        acl.SetOwner(users);Assert.False(UpdateInstallation.IsPrivilegedAcl(acl));
    }
    [Fact]
    public void Activation_requires_visible_handoff_installed_files_normal_close_and_parent_exit()
    {
        var handoff=new UpdateHandoffState();
        Assert.Throws<InvalidOperationException>(()=>handoff.ConfirmClose());
        handoff.Show(); handoff.Staged();
        Assert.False(handoff.CanActivate(parentExited:true));
        handoff.ConfirmClose();
        Assert.False(handoff.CanActivate(parentExited:false));
        Assert.True(handoff.CanActivate(parentExited:true));
        handoff.Activated(parentExited:true);
        Assert.Throws<InvalidOperationException>(()=>handoff.Staged());
    }

    [Fact]
    public async Task Pipe_peer_identity_rejects_stale_start_and_different_process()
    {
        using var current=System.Diagnostics.Process.GetCurrentProcess();
        var pipeName="silo-test-"+Guid.NewGuid().ToString("N");
        using var server=UpdatePipeIdentity.CreateServer(pipeName);
        using var client=new System.IO.Pipes.NamedPipeClientStream(".",pipeName,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(),client.ConnectAsync());
        UpdatePipeIdentity.Authenticate(server,true,current.Id,current.StartTime.ToUniversalTime().Ticks,Environment.ProcessPath!);
        Assert.Throws<InvalidDataException>(()=>UpdatePipeIdentity.Authenticate(server,true,current.Id,1,Environment.ProcessPath!));
        Assert.Throws<InvalidDataException>(()=>UpdatePipeIdentity.Authenticate(server,true,1,current.StartTime.ToUniversalTime().Ticks,Environment.ProcessPath!));
    }
    [Theory]
    [InlineData(0, 0, 1280, 720, 400, 260, 440, 230)]
    [InlineData(-1600, 100, 1200, 800, 400, 260, -1200, 370)]
    [InlineData(100, -900, 500, 400, 400, 260, 150, -830)]
    public void Center_uses_actual_owner_rectangle(int x,int y,int w,int h,int dw,int dh,int expectedX,int expectedY)
    {
        Assert.Equal((expectedX,expectedY), UpdateWindowPlacement.Center(x,y,w,h,dw,dh));
    }

    [Fact]
    public async Task Framed_protocol_roundtrips_without_waiting_for_pipe_close()
    {
        using var stream = new MemoryStream();
        await UpdateHostProtocol.WriteAsync(stream,new UpdateHostMessage("staged", "abc"),default);
        stream.Position=0;
        var message=await UpdateHostProtocol.ReadAsync(stream,default);
        Assert.Equal("staged",message.Kind); Assert.Equal("abc",message.TransactionId);
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(65537)] [InlineData(int.MaxValue)]
    public async Task Framed_protocol_rejects_unbounded_lengths_before_allocation(int length)
    {
        using var stream=new MemoryStream(BitConverter.GetBytes(length));
        await Assert.ThrowsAsync<InvalidDataException>(()=>UpdateHostProtocol.ReadAsync(stream,default));
    }

    [Fact]
    public async Task Framed_protocol_rejects_truncated_and_unknown_messages()
    {
        using var truncated=new MemoryStream(new byte[]{12,0,0,0,2});
        await Assert.ThrowsAsync<EndOfStreamException>(()=>UpdateHostProtocol.ReadAsync(truncated,default));
        using var stream=new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(()=>UpdateHostProtocol.WriteAsync(stream,new UpdateHostMessage("run-arbitrary-command"),default));
    }

    [Fact]
    public void Registered_root_resolution_only_accepts_exact_version_layout()
    {
        var root=Path.Combine(Path.GetTempPath(),"silo-protocol-"+Guid.NewGuid().ToString("N"));
        Assert.Equal(Path.GetFullPath(root),UpdateInstallation.MatchRegisteredRoot(Path.Combine(root,"versions","1.2.500.0","SiloPlayer.exe"),new[]{root}));
        Assert.Equal(Path.GetFullPath(root),UpdateInstallation.MatchRegisteredRoot(Path.Combine(root,"SiloPlayer.exe"),new[]{root}));
        Assert.Null(UpdateInstallation.MatchRegisteredRoot(Path.Combine(root,"versions","..","SiloPlayer.exe"),new[]{root}));
        Assert.Null(UpdateInstallation.MatchRegisteredRoot(Path.Combine(root,"other","1.2.500.0","SiloPlayer.exe"),new[]{root}));
        Assert.Null(UpdateInstallation.MatchRegisteredRoot(Path.Combine(root,"versions","1.2.500","SiloPlayer.exe"),new[]{root}));
    }
}
