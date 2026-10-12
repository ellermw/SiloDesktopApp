using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.SiloCast;

namespace SiloPlayer.Tests;

public class SiloCastTransportTests
{
    [Fact]
    public void MessagePreservesAppleCamelCasePayloadKeys()
    {
        var payload = new JsonObject { ["serverURL"] = "https://silo.test", ["profileId"] = "p" };
        var message = SiloCastProtocol.CreateMessage("handoff_offer", "handoffOffer", payload);
        Assert.Equal("{\"v\":2,\"type\":\"handoff_offer\",\"handoffOffer\":{\"serverURL\":\"https://silo.test\",\"profileId\":\"p\"}}", message.ToJsonString());
        Assert.Null(payload.Parent);
    }

    [Fact]
    public void ServerIdentityOnlyTrimsWhitespaceAndTrailingSlashes()
    {
        Assert.Equal("aHR0cHM6Ly9TaWxvLnRlc3QvQVBJ", SiloCastProtocol.ServerId(" https://Silo.test/API/// \r\n"));
    }

    [Fact]
    public async Task PartialReadsAndCoalescedMessagesKeepFrameBoundaries()
    {
        var bytes = Frame("{\"v\":2,\"type\":\"ping\"}").Concat(Frame("{\"v\":2,\"type\":\"control\",\"control\":{\"name\":\"future_command\"}}")).ToArray();
        using var stream = new FragmentedStream(bytes, 3);
        Assert.Equal("ping", (await SiloCastFrameCodec.ReadAsync(stream, default))!["type"]!.GetValue<string>());
        Assert.Equal("future_command", (await SiloCastFrameCodec.ReadAsync(stream, default))!["control"]!["name"]!.GetValue<string>());
        Assert.Null(await SiloCastFrameCodec.ReadAsync(stream, default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1048577)]
    [InlineData(uint.MaxValue)]
    public async Task InvalidLengthRejectedBeforePayloadAllocation(uint length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, length);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => SiloCastFrameCodec.ReadAsync(stream, default));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task InvalidJsonAndNonObjectFramesAreRejected(string json)
    {
        using var stream = new MemoryStream(Frame(json));
        await Assert.ThrowsAsync<InvalidDataException>(() => SiloCastFrameCodec.ReadAsync(stream, default));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task EofMidHeaderOrPayloadIsRejected(int byteCount)
    {
        using var stream = new MemoryStream(Frame("{}")[..byteCount]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => SiloCastFrameCodec.ReadAsync(stream, default));
    }

    [Fact]
    public async Task OneMiBFrameAcceptedAndEncodedWithBigEndianByteLength()
    {
        var message = new JsonObject { ["data"] = new string('x', 1048576 - 11) };
        using var stream = new MemoryStream();
        await SiloCastFrameCodec.WriteAsync(stream, message, default);
        Assert.Equal(new byte[] { 0, 16, 0, 0 }, stream.ToArray()[..4]);
        Assert.Equal(1048580, stream.Length);
        stream.Position = 0;
        Assert.Equal(1048576 - 11, (await SiloCastFrameCodec.ReadAsync(stream, default))!["data"]!.GetValue<string>().Length);
    }

    [Fact]
    public async Task OversizedOutgoingFrameIsRejectedWithoutWriting()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => SiloCastFrameCodec.WriteAsync(stream, new JsonObject { ["data"] = new string('x', 1048576) }, default));
        Assert.Equal(0, stream.Length);
    }

    private static byte[] Frame(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private sealed class FragmentedStream(byte[] bytes, int fragmentSize) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, fragmentSize)], cancellationToken);
    }
}
