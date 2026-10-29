using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class AppUpdateDownloadTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "silo-update-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Package = Enumerable.Range(0, 16384).Select(i => (byte)(i % 251)).ToArray();

    [Fact]
    public async Task VerifiedDownloadIsPromotedAndManualModeDoesNotArmExitInstallation()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var service = Create(http); await service.CheckAsync(); await service.DownloadAsync();
        var path = Assert.IsType<string>(service.State.ReadyInstaller);
        Assert.Equal(Package, await File.ReadAllBytesAsync(path));
        Assert.Null(service.State.Error); Assert.False(service.ShouldInstallOnExit);
        service.SetMode(AppUpdateMode.Automatic); Assert.True(service.ShouldInstallOnExit);
        service.SetMode(AppUpdateMode.NotifyOnly); Assert.False(service.ShouldInstallOnExit);
        Assert.NotNull(service.State.Release);
    }

    [Fact]
    public async Task UpdateNowDuringABackgroundCheckStillDownloadsThePackage()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var service = Create(http); await service.CheckAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.BeforeMetadata = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
        var check = service.CheckAsync();
        // Generous bounds: hashing and file moves run on a pool that the full
        // parallel suite can starve; a passing run still ends immediately.
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // The periodic six-hour check, or the dialog's own Check button, is in
        // flight when the user presses Update now.
        var download = service.DownloadAsync();
        release.TrySetResult();
        await download.WaitAsync(TimeSpan.FromSeconds(30)); await check;

        Assert.Equal(1, wire.Downloads);
        Assert.NotNull(service.State.ReadyInstaller);
        Assert.True(await service.ReverifyReadyAsync());
    }

    [Fact]
    public async Task PlaybackStillDefersAnAutomaticTransferQueuedBehindACheck()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var service = Create(http); service.SetMode(AppUpdateMode.Automatic); await service.CheckAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.BeforeMetadata = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
        var check = service.CheckAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var download = service.DownloadAsync(automatic: true);
        service.SetPlaybackActive(true);
        release.TrySetResult();
        await download.WaitAsync(TimeSpan.FromSeconds(5)); await check;

        Assert.Equal(0, wire.Downloads);
        Assert.Null(service.State.ReadyInstaller); Assert.Null(service.State.Error);
    }

    [Fact]
    public async Task BadChecksumNeverProducesAnExecutableReadyPackage()
    {
        using var wire = new Wire { AdvertisedDigest = AppUpdateTests.Digest }; using var http = new HttpClient(wire);
        var service = Create(http); await service.CheckAsync(); await service.DownloadAsync();
        Assert.Null(service.State.ReadyInstaller); Assert.NotNull(service.State.Error);
        Assert.Empty(Directory.GetFiles(_directory, "*.exe", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task TruncatedBodyIsNotAcceptedAsACompleteUpdate()
    {
        using var wire = new Wire { Response = (_, _) => Task.FromResult(Body(Package[..100])) }; using var http = new HttpClient(wire);
        var service = Create(http); await service.CheckAsync(); await service.DownloadAsync();
        Assert.Null(service.State.ReadyInstaller); Assert.NotNull(service.State.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterruptedTransferResumesCorrectRangeOrSafelyRestartsAFullResponse(bool supportsRange)
    {
        var request = 0;
        using var wire = new Wire { Response = (message, _) =>
        {
            if (++request == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Interrupted(Package)) });
            var start = message.Headers.Range!.Ranges.Single().From!.Value;
            Assert.Equal(4096, start);
            var response = Body(supportsRange ? Package[(int)start..] : Package);
            if (supportsRange)
            {
                response.StatusCode = HttpStatusCode.PartialContent;
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, Package.Length - 1, Package.Length);
            }
            return Task.FromResult(response);
        }};
        using var http = new HttpClient(wire); var service = Create(http); await service.CheckAsync();
        await service.DownloadAsync(); Assert.NotNull(service.State.Error); Assert.Null(service.State.ReadyInstaller);
        await service.DownloadAsync();
        Assert.Null(service.State.Error); Assert.Equal(Package, await File.ReadAllBytesAsync(service.State.ReadyInstaller!));
    }

    [Fact]
    public async Task AutomaticTransferDefersDuringPlaybackButExplicitManualDownloadIsAvailable()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var service = Create(http); service.SetMode(AppUpdateMode.Automatic); service.SetPlaybackActive(true);
        await service.CheckAsync(); await service.DownloadAsync(automatic: true);
        Assert.Equal(0, wire.Downloads); Assert.NotNull(service.State.Release);
        await service.DownloadAsync(); Assert.Equal(1, wire.Downloads); Assert.NotNull(service.State.ReadyInstaller);
    }

    [Fact]
    public async Task TamperedReadyPackageIsDisarmedBeforeExit()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var service = Create(http); service.SetMode(AppUpdateMode.Automatic);
        await service.CheckAsync(); await service.DownloadAsync();
        await File.WriteAllBytesAsync(service.State.ReadyInstaller!, [1, 2, 3]);
        Assert.False(await service.ReverifyReadyAsync());
        Assert.False(service.ShouldInstallOnExit); Assert.Null(service.State.ReadyInstaller);
    }

    [Fact]
    public async Task MissingOrWithdrawnReleaseDisarmsAutomaticInstall()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var service = Create(http); service.SetMode(AppUpdateMode.Automatic);
        await service.CheckAsync(); await service.DownloadAsync(); Assert.True(service.ShouldInstallOnExit);
        wire.Metadata = AppUpdateTests.Release(tag: "v1.2.334");
        await service.CheckAsync(); Assert.False(service.ShouldInstallOnExit); Assert.Null(service.State.Release);
    }

    [Fact]
    public async Task FailedMetadataRevalidationDisarmsExitButKeepsManualUpdateAccess()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var service = Create(http); service.SetMode(AppUpdateMode.Automatic);
        await service.CheckAsync(); await service.DownloadAsync(); Assert.True(service.ShouldInstallOnExit);
        wire.Metadata = AppUpdateTests.Release(digest: "invalid");
        await service.CheckAsync();
        Assert.False(service.ShouldInstallOnExit); Assert.NotNull(service.State.Release);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AutomaticTransferStopsWhenModeChangesOrPlaybackStarts(bool changeMode)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var wire = new Wire { Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new Waiting(Package, entered, release)) }) };
        using var http = new HttpClient(wire); var service = Create(http);
        service.SetMode(AppUpdateMode.Automatic); await service.CheckAsync();
        var download = service.DownloadAsync(automatic: true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (changeMode) service.SetMode(AppUpdateMode.NotifyOnly); else service.SetPlaybackActive(true);
        await download.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(service.State.IsDownloading); Assert.Null(service.State.ReadyInstaller);
        Assert.NotNull(service.State.Release); Assert.Null(service.State.Error);
        wire.Response = null;
        await service.DownloadAsync(); Assert.NotNull(service.State.ReadyInstaller);
    }

    [Fact]
    public async Task ExplicitTransferIsNotCanceledByAutomaticPreferencesOrPlayback()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var wire = new Wire { Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new Waiting(Package, entered, release)) }) };
        using var http = new HttpClient(wire); var service = Create(http);
        service.SetMode(AppUpdateMode.Automatic); await service.CheckAsync();
        var download = service.DownloadAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.SetMode(AppUpdateMode.NotifyOnly); service.SetPlaybackActive(true);
        Assert.False(download.IsCompleted);
        release.SetResult(); await download.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(service.State.ReadyInstaller);
    }

    [Fact]
    public async Task RedirectToAnUntrustedHostNeverDownloadsOrStagesIt()
    {
        using var wire = new Wire { Response = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://evil.invalid/installer.exe");
            return Task.FromResult(response);
        }};
        using var http = new HttpClient(wire); var service = Create(http); await service.CheckAsync(); await service.DownloadAsync();
        Assert.Null(service.State.ReadyInstaller); Assert.NotNull(service.State.Error); Assert.Equal(1, wire.Downloads);
    }

    private AppUpdateService Create(HttpClient http) => new(http, _directory, new Version(1, 2, 334));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IncorrectResumedRangeNeverPromotesPackage(bool wrongStart)
    {
        var requests = 0;
        using var wire = new Wire { Response = (_, _) =>
        {
            if (++requests == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StreamContent(new Interrupted(Package)) });
            var response = Body(Package[4096..]); response.StatusCode = HttpStatusCode.PartialContent;
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(wrongStart ? 4095 : 4096,
                Package.Length - 1, wrongStart ? Package.Length : Package.Length + 1);
            return Task.FromResult(response);
        }};
        using var http = new HttpClient(wire); var service = Create(http); await service.CheckAsync();
        await service.DownloadAsync(); await service.DownloadAsync();
        Assert.NotNull(service.State.Error); Assert.Null(service.State.ReadyInstaller);
        Assert.Empty(Directory.GetFiles(_directory, "*.exe", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task UnboundedResponseCannotWriteBeyondPublishedSize()
    {
        using var wire = new Wire { Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new Unbounded(Package.Concat(new byte[] { 99 }).ToArray())) }) };
        using var http = new HttpClient(wire); var service = Create(http); await service.CheckAsync(); await service.DownloadAsync();
        Assert.NotNull(service.State.Error); Assert.Null(service.State.ReadyInstaller);
        Assert.All(Directory.GetFiles(_directory, "*.part", SearchOption.AllDirectories), path => Assert.True(new FileInfo(path).Length <= Package.Length));
    }

    [Fact]
    public async Task TransferDeadlineReportsAnActionableErrorWithoutAutomaticRetryLoop()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var wire = new Wire { Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new Waiting(Package, entered, release)) }) };
        using var http = new HttpClient(wire); var clock = new DeadlineClock();
        var service = new AppUpdateService(http, _directory, new Version(1, 2, 334), timeProvider: clock);
        service.SetMode(AppUpdateMode.Automatic); await service.CheckAsync();
        var download = service.DownloadAsync(automatic: true); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Expire(); await download.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(service.State.IsDownloading); Assert.Null(service.State.ReadyInstaller);
        Assert.Contains("timed out", service.State.Error, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(service.State.Release); Assert.Equal(1, wire.Downloads);
    }

    [Fact]
    public async Task JunctionCacheIsRejectedWithoutWritingIntoItsTarget()
    {
        var target = Path.Combine(_directory, "target"); Directory.CreateDirectory(target);
        var link = Path.Combine(_directory, "linked");
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new System.Diagnostics.ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' -ErrorAction Stop | Out-Null" }) start.ArgumentList.Add(argument);
        using var junction = System.Diagnostics.Process.Start(start)!;
        await junction.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(junction.ExitCode == 0, await junction.StandardError.ReadToEndAsync());
        try
        {
            using var http = new HttpClient(new Wire()); var service = new AppUpdateService(http, link, new Version(1, 2, 334));
            await service.CheckAsync(); await service.DownloadAsync();
            Assert.NotNull(service.State.Error); Assert.Null(service.State.ReadyInstaller);
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally { Directory.Delete(link, recursive: false); }
    }

    [Fact]
    public void CleanupRemovesOnlyKnownGeneratedFilesForInstalledVersions()
    {
        var old = Path.Combine(_directory, "1.2.334.0"); Directory.CreateDirectory(old);
        var future = Path.Combine(_directory, "1.2.335.0"); Directory.CreateDirectory(future);
        var obsolete = Path.Combine(old, "SiloInstaller-Windows-x64.exe"); File.WriteAllText(obsolete, "old");
        var keep = Path.Combine(old, "keep-my-file.txt"); File.WriteAllText(keep, "preserve");
        var pending = Path.Combine(future, "SiloInstaller-Windows-x64.exe"); File.WriteAllText(pending, "pending");
        using var http = new HttpClient(new Wire()); Create(http).CleanupInstalledPackages();
        Assert.False(File.Exists(obsolete)); Assert.True(File.Exists(keep)); Assert.True(File.Exists(pending));
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private static HttpResponseMessage Body(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed class Wire : HttpMessageHandler
    {
        public string? Metadata;
        public string? AdvertisedDigest;
        public int Downloads;
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Response;
        public Func<CancellationToken, Task>? BeforeMetadata;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                if (BeforeMetadata is { } wait) await wait(ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Metadata ?? AppUpdateTests.Release(
                    digest: "sha256:" + (AdvertisedDigest ?? Convert.ToHexString(SHA256.HashData(Package)).ToLowerInvariant()), size: Package.Length), Encoding.UTF8, "application/json") };
            }
            Downloads++;
            return await (Response?.Invoke(request, ct) ?? Task.FromResult(Body(Package)));
        }
    }

    private sealed class Interrupted(byte[] bytes) : MemoryStream(bytes)
    {
        private bool _returned;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_returned) throw new IOException("Synthetic connection interruption.");
            _returned = true;
            return base.ReadAsync(buffer[..Math.Min(4096, buffer.Length)], ct);
        }
    }

    private sealed class Unbounded(byte[] bytes) : MemoryStream(bytes)
    { public override bool CanSeek => false; }

    private sealed class DeadlineClock : TimeProvider, IDisposable
    {
        private TimerCallback? _callback; private object? _state;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _callback = callback; _state = state; return new ClockTimer(); }
        public void Expire() => _callback!(_state);
        public void Dispose() { }
        private sealed class ClockTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Waiting(byte[] bytes, TaskCompletionSource entered, TaskCompletionSource release) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            entered.TrySetResult(); await release.Task.WaitAsync(ct);
            return await base.ReadAsync(buffer, ct);
        }
    }
}
