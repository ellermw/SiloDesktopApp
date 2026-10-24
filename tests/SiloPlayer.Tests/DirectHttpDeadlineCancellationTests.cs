using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using SiloPlayer.Player;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class DirectHttpDeadlineCancellationTests
{
    [Fact]
    public void RecoveredResponseDoesNotRetainTerminalFailureInNativeSample()
    {
        var fixture=new Fixture(false);var attempts=0;
        using var reader=new DirectHttpReader("https://direct-deadline-fixture.invalid/media",()=>1,()=>new StatusRecoveryHandler(fixture,()=>++attempts));
        Assert.Equal(4096,reader.Read(new byte[4096],4096));
        Assert.Equal(2,attempts);
        using var mpv=new MpvPlayer();
        var type=typeof(MpvPlayer).GetNestedType("DirectRequest",BindingFlags.NonPublic)!;
        var request=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,["https://direct-deadline-fixture.invalid/media"],null)!;
        type.GetField("Reader")!.SetValue(request,reader);
        typeof(MpvPlayer).GetField("_directRequest",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(mpv,request);
        var sample=mpv.ReadMediaNetworkObservation();
        Assert.True(sample.Bytes>0);
        Assert.False(sample.TransportFailing);
        Assert.False(NativeBufferingSignalPolicy.IsOutage(true,sample.TransportFailing,1100));
        Assert.True(NativeBufferingSignalPolicy.IsOutage(true,sample.TransportFailing,4000));
        type.GetField("Failed")!.SetValue(request,true);
        Assert.True(mpv.ReadMediaNetworkObservation().TransportFailing);
        Assert.True(NativeBufferingSignalPolicy.IsOutage(true,mpv.ReadMediaNetworkObservation().TransportFailing,0));
    }
    [Fact]
    public async Task BodyDisposedByItsLocalDeadlineRetriesTheExactUnreadByteRange()
    {
        var fixture=new Fixture(arbitraryDispose:false);
        using var reader=new DirectHttpReader("https://direct-deadline-fixture.invalid/media",()=>1,()=>new Handler(fixture));
        var actual=await Task.Run(()=>ReadAll(reader)).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.True(fixture.Body.CancellationObserved);
        Assert.False(fixture.Body.CallerDisposedAtThrow);
        Assert.Equal(fixture.Bytes,actual);
        Assert.Equal(new long[]{0,4096},fixture.Offsets);
    }

    [Fact]
    public async Task ArbitrarilyDisposedBodyRemainsTerminalWithoutRetrying()
    {
        var fixture=new Fixture(arbitraryDispose:true);
        using var reader=new DirectHttpReader("https://direct-deadline-fixture.invalid/media",()=>1,()=>new Handler(fixture));
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>Task.Run(()=>ReadAll(reader)));
        Assert.False(fixture.Body.CancellationObserved);
        Assert.Single(fixture.Offsets);
    }

    [Fact]
    public async Task ReaderLifetimeCancellationReportsCancellationAndDoesNotSendAnotherRequest()
    {
        var fixture=new Fixture(arbitraryDispose:false);
        using var reader=new DirectHttpReader("https://direct-deadline-fixture.invalid/media",()=>1,()=>new Handler(fixture));
        var reading=Task.Run(()=>ReadAll(reader));
        await fixture.Body.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        reader.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>reading.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(fixture.Body.CancellationObserved);
        Assert.Single(fixture.Offsets);
    }
    [Fact]
    public async Task BytesReturnedAfterADeadlineArePreservedWithoutReadingTheExpiredResponseAgain()
    {
        var bytes=new byte[128*1024];new Random(2305).NextBytes(bytes);
        var body=new LateDeadlineStream(bytes);var offsets=new List<long>();
        using var reader=new DirectHttpReader("https://direct-deadline-fixture.invalid/media",()=>1,()=>new LateDataHandler(bytes,body,offsets));
        var actual=await Task.Run(()=>ReadAll(reader)).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.True(body.ExpiredWhenBytesReturned);
        Assert.Equal(bytes,actual);
        Assert.Equal(2,body.ReadCalls);
        Assert.Equal(new long[]{0,8192},offsets);
    }
    [Fact]
    public async Task LateUsefulProgressCannotResetAndReuseTheCanceledResponse()
    {
        var bytes=new byte[512*1024];new Random(2306).NextBytes(bytes);
        var body=new LateDeadlineStream(bytes,81920);var offsets=new List<long>();
        using var reader=new DirectHttpReader("https://direct-deadline-fixture.invalid/media",()=>90,()=>new LateDataHandler(bytes,body,offsets));
        var actual=await Task.Run(()=>ReadAll(reader,128*1024)).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.True(body.ExpiredWhenBytesReturned);
        Assert.Equal(bytes,actual);
        Assert.Equal(2,body.ReadCalls);
        Assert.Equal(new long[]{0,86016},offsets);
    }

    private static byte[] ReadAll(DirectHttpReader reader,int bufferSize=8192)
    {
        using var output=new MemoryStream();var buffer=new byte[bufferSize];
        for(;;){var count=reader.Read(buffer,buffer.Length);if(count==0)return output.ToArray();output.Write(buffer,0,count);}
    }
    private sealed class Fixture
    {
        public byte[] Bytes{get;}=new byte[128*1024];
        public List<long> Offsets{get;}=[];
        public DeadlineDisposedStream Body{get;}
        public Fixture(bool arbitraryDispose){new Random(2304).NextBytes(Bytes);Body=new(Bytes,arbitraryDispose);}
    }
    private sealed class Handler(Fixture fixture):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var offset=request.Headers.Range!.Ranges.Single().From!.Value;fixture.Offsets.Add(offset);
            var first=fixture.Offsets.Count==1;
            var content=new StreamContent(first?fixture.Body:new MemoryStream(fixture.Bytes,(int)offset,fixture.Bytes.Length-(int)offset,false));
            content.Headers.ContentRange=new ContentRangeHeaderValue(offset,fixture.Bytes.Length-1,fixture.Bytes.Length);
            content.Headers.ContentLength=fixture.Bytes.Length-offset;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent){Content=content});
        }
    }
    private sealed class StatusRecoveryHandler(Fixture fixture,Func<int> attempt):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(attempt()==1)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var content=new ByteArrayContent(fixture.Bytes);content.Headers.ContentRange=new ContentRangeHeaderValue(0,fixture.Bytes.Length-1,fixture.Bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent){Content=content});
        }
    }
    private sealed class LateDataHandler(byte[] bytes,Stream body,List<long> offsets):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var offset=request.Headers.Range!.Ranges.Single().From!.Value;offsets.Add(offset);
            var content=new StreamContent(offsets.Count==1?body:new MemoryStream(bytes,(int)offset,bytes.Length-(int)offset,false));
            content.Headers.ContentRange=new ContentRangeHeaderValue(offset,bytes.Length-1,bytes.Length);content.Headers.ContentLength=bytes.Length-offset;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent){Content=content});
        }
    }
    private sealed class LateDeadlineStream(byte[] bytes,int lateCount=4096):Stream
    {
        public int ReadCalls;public bool ExpiredWhenBytesReturned;
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>bytes.Length;public override long Position{get=>ReadCalls*4096;set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>ReadAsync(buffer.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {
            var call=++ReadCalls;
            if(call==2){await Task.Delay(5100);ExpiredWhenBytesReturned=ct.IsCancellationRequested;}
            if(call<=2){var count=call==1?4096:lateCount;bytes.AsMemory(call==1?0:4096,count).CopyTo(buffer);return count;}
            throw new ObjectDisposedException("HTTP response disposed by previous read deadline");
        }
        public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    private sealed class DeadlineDisposedStream(byte[] bytes,bool arbitraryDispose):Stream
    {
        private bool _sent,_disposed;
        public bool CancellationObserved,CallerDisposedAtThrow;
        public TaskCompletionSource Waiting{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>bytes.Length;public override long Position{get=>_sent?4096:0;set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>ReadAsync(buffer.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        {
            if(!_sent){_sent=true;bytes.AsMemory(0,4096).CopyTo(buffer);return 4096;}
            Waiting.TrySetResult();
            if(!arbitraryDispose)
            {
                try{await Task.Delay(Timeout.Infinite,cancellationToken);}
                catch(OperationCanceledException){CancellationObserved=true;CallerDisposedAtThrow=_disposed;throw new ObjectDisposedException("HTTP response body canceled at read deadline");}
            }
            throw new ObjectDisposedException("Unrelated disposed HTTP response body");
        }
        protected override void Dispose(bool disposing){_disposed=true;base.Dispose(disposing);}
        public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
