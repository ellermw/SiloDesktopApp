using System.IO.Pipes;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.UpdateProtocolTests;

public sealed class UpdatePresentationHandoffTests
{
    [Fact]
    public async Task Hidden_helper_waits_for_client_swap_before_starting_presentation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var name = "silo-test-" + Guid.NewGuid().ToString("N");
        using var server = UpdatePipeIdentity.CreateServer(name);
        // Both endpoints are fixture-owned; production authenticates both peers before this exchange.
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        var swapped = false; var started = false;
        var offer = UpdatePresentationHandoff.OfferAsync(client, 123, () =>
        {
            Assert.True(swapped); started = true;
        }, timeout.Token);
        Assert.False(started);
        await UpdatePresentationHandoff.AcceptAsync(server, handle =>
        {
            Assert.Equal(123, handle); Assert.False(started); swapped = true;
        }, timeout.Token);
        await offer;
        Assert.True(started);
    }

    [Theory]
    [InlineData("visible", "123")]
    [InlineData("presentation-ready", "0")]
    [InlineData("presentation-ready", "-1")]
    [InlineData("presentation-ready", "not-a-window")]
    public async Task Invalid_offer_cannot_retire_the_early_panel(string kind, string text)
    {
        using var stream = new MemoryStream();
        await UpdateHostProtocol.WriteAsync(stream, new(kind, Text: text), default);
        stream.Position = 0;
        var swapped = false;
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePresentationHandoff.AcceptAsync(stream, _ => swapped = true, default));
        Assert.False(swapped);
    }

    [Fact]
    public async Task Rejected_window_identity_cannot_authorize_presentation()
    {
        using var stream = new MemoryStream();
        await UpdateHostProtocol.WriteAsync(stream, new("presentation-ready", Text: "123"), default);
        stream.Position = 0;
        var size = stream.Length;
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePresentationHandoff.AcceptAsync(stream,
            _ => throw new InvalidDataException("Wrong helper HWND process"), default));
        Assert.Equal(size, stream.Length);
    }

    [Fact]
    public async Task Canceled_preparation_cannot_retire_its_panel()
    {
        using var stream=new MemoryStream();
        await UpdateHostProtocol.WriteAsync(stream,new("presentation-ready",Text:"123"),default);
        stream.Position=0;
        using var stop=new CancellationTokenSource(); stop.Cancel();
        var swapped=false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>UpdatePresentationHandoff.AcceptAsync(stream,_=>swapped=true,stop.Token));
        Assert.False(swapped);
    }

    [Fact]
    public async Task Hidden_helper_cannot_start_on_a_wrong_acceptance_frame()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var name="silo-test-"+Guid.NewGuid().ToString("N");
        using var server=UpdatePipeIdentity.CreateServer(name);
        using var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token),client.ConnectAsync(timeout.Token));
        var started=false;
        var offer=UpdatePresentationHandoff.OfferAsync(client,123,()=>started=true,timeout.Token);
        Assert.Equal("presentation-ready",(await UpdateHostProtocol.ReadAsync(server,timeout.Token)).Kind);
        await UpdateHostProtocol.WriteAsync(server,new("downloaded"),timeout.Token);
        await Assert.ThrowsAsync<InvalidDataException>(()=>offer);
        Assert.False(started);
    }
}
