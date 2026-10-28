using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class LocalLogTests
{
    [Fact]
    public void NullBatchCannotReplaceTheFailureBeingLogged()
    {
        var directory = CreateTempLogDir();
        try { Assert.Null(Record.Exception(() => LocalLog.AppendLines("failure.txt", null!, directory))); }
        finally { DeleteTempLogDir(directory); }
    }

    private const string SignedFixtureToken = "eyJhbGciOiJIUzI1NiJ9.eyJmaXh0dXJlIjp0cnVlfQ.c3ludGhldGlj";

    [Fact]
    public void EncodedQueryNames_AreRedactedInOrdinaryAndRelativeExceptionLogs()
    {
        var dir = CreateTempLogDir();
        try
        {
            const string relative = "/api/v2/stream/session?%73%74=fixture-encoded-stream&%64%74=fixture-encoded-download&seek=15";
            LocalLog.AppendLine("transport.txt", "GET " + relative, dir);
            LocalLog.AppendLines("transport.txt", ["GET /media?%54%4f%4b%45%4e=fixture-encoded-account&part=2"], dir);
            LocalLog.AppendException("transport.txt", new IOException("Failed " + relative), "fetch", logDirectory: dir);
            var log = File.ReadAllText(Path.Combine(dir, "transport.txt"));
            Assert.DoesNotContain("fixture-encoded-stream", log);
            Assert.DoesNotContain("fixture-encoded-download", log);
            Assert.DoesNotContain("fixture-encoded-account", log);
            Assert.Contains("%73%74=<redacted>", log);
            Assert.Contains("%64%74=<redacted>", log);
            Assert.Contains("seek=15", log);
            Assert.Contains("part=2", log);
        }
        finally { DeleteTempLogDir(dir); }
    }

    [Fact]
    public void OrdinaryLogWrites_RedactSignedPathsAndStreamDownloadQueries()
    {
        var dir = CreateTempLogDir();
        try
        {
            LocalLog.AppendLine("transport.txt", "HTTP failure: GET https://node.fixture/stream/transcode/" + SignedFixtureToken + "/master.m3u8?st=fixture-stream&dt=fixture-download&seek=15", dir);
            LocalLog.AppendLines("transport.txt", ["HEAD /downloads/file/%65" + SignedFixtureToken[1..] + "!", "opaque /resources/%65" + SignedFixtureToken[1..] + ")", "normal /media/feature.mkv seek=15 HTTP 502"], dir);
            var log = File.ReadAllText(Path.Combine(dir, "transport.txt"));
            Assert.DoesNotContain(SignedFixtureToken, log);
            Assert.DoesNotContain(SignedFixtureToken[1..], log);
            Assert.DoesNotContain("fixture-stream", log);
            Assert.DoesNotContain("fixture-download", log);
            Assert.Contains("normal /media/feature.mkv seek=15 HTTP 502", log);
            Assert.Contains("seek=15", log);
        }
        finally { DeleteTempLogDir(dir); }
    }

    [Fact]
    public void NestedDiagnosticFailure_RedactsRelativeSignedUrlsAndUnquotedStreamTokens()
    {
        var dir = CreateTempLogDir();
        try
        {
            LocalLog.AppendException("errors.txt", new IOException("relative /downloads/file/" + SignedFixtureToken,
                new HttpRequestException("st=fixture-stream dt: fixture-download /stream/subtitles/" + SignedFixtureToken + "/0")), "media", logDirectory: dir);
            var log = File.ReadAllText(Path.Combine(dir, "errors.txt"));
            Assert.DoesNotContain(SignedFixtureToken, log);
            Assert.DoesNotContain("fixture-stream", log);
            Assert.DoesNotContain("fixture-download", log);
            Assert.Contains("HttpRequestException", log);
            Assert.Contains("IOException", log);
        }
        finally { DeleteTempLogDir(dir); }
    }

    [Fact]
    public void AppendException_PreservesNestedFailureAndRedactsCredentials()
    {
        var dir = CreateTempLogDir();
        try
        {
            Exception inner;
            try { throw new NullReferenceException("Object reference not set to an instance of an object."); }
            catch (Exception caught) { inner = caught; }
            var failure = new AggregateException("Request https://private.test/path?signature=secret-signature Authorization: Bearer secret-bearer", inner,
                new SiloPlayer.Core.Api.ApiException("internal_error", "{\"password\":\"secret-password\"}", 500));
            failure.Data["sensitive"] = "secret-data";
            LocalLog.AppendException("errors.txt", failure, "dismissal", "action-123", dir);
            var log = File.ReadAllText(Path.Combine(dir, "errors.txt"));
            Assert.Contains("action-123", log);
            Assert.Contains("NullReferenceException", log);
            Assert.Contains("Object reference not set", log);
            Assert.Contains(nameof(AppendException_PreservesNestedFailureAndRedactsCredentials), log);
            Assert.Contains("status=500", log);
            Assert.Contains("internal_error", log);
            Assert.Contains("hresult=0x", log);
            foreach (var secret in new[] { "secret-signature", "secret-bearer", "secret-password", "secret-data", "private.test" })
                Assert.DoesNotContain(secret, log);
        }
        finally { DeleteTempLogDir(dir); }
    }

    [Fact]
    public async Task OperationScope_FollowsAwaitAndRestoresParentWithoutCrossTalk()
    {
        using var parent = LocalLog.BeginOperation("parent");
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(async index =>
        {
            using var child = LocalLog.BeginOperation($"child-{index}");
            await Task.Yield();
            return LocalLog.CurrentOperationId;
        }));
        Assert.Equal(new[] { "child-0", "child-1", "child-2" }, results);
        Assert.Equal("parent", LocalLog.CurrentOperationId);
    }

    [Fact]
    public void DiagnosticText_RedactsUnquotedSecretsAndBasicAuthorization()
    {
        var redacted = LocalLog.RedactDiagnosticText("password=fixture-password access_token: fixture-token Authorization: Basic fixture-basic https://fixture.invalid/stream?token=fixture-token&signature=fixture-signature");
        Assert.DoesNotContain("fixture-password", redacted);
        Assert.DoesNotContain("fixture-token", redacted);
        Assert.DoesNotContain("fixture-basic", redacted);
        Assert.DoesNotContain("fixture-signature", redacted);
    }

    [Fact]
    public void RedactSensitiveData_RemovesTokensAndSecrets()
    {
        var input = "Authorization: Bearer abc.def.ghi url=https://example.test/stream?token=secret-token&room_token=room-secret&x=1 body={\"refresh_token\":\"refresh-secret\",\"password\":\"p@ss\"}";

        var redacted = LocalLog.RedactSensitiveData(input);

        Assert.DoesNotContain("abc.def.ghi", redacted);
        Assert.DoesNotContain("secret-token", redacted);
        Assert.DoesNotContain("room-secret", redacted);
        Assert.DoesNotContain("refresh-secret", redacted);
        Assert.DoesNotContain("p@ss", redacted);
        Assert.Contains("Authorization: Bearer <redacted>", redacted);
        Assert.Contains("token=<redacted>", redacted);
        Assert.Contains("room_token=<redacted>", redacted);
        Assert.Contains("\"refresh_token\":\"<redacted>\"", redacted);
        Assert.Contains("\"password\":\"<redacted>\"", redacted);
    }

    [Fact]
    public void AppendLine_RotatesLogBeforeItGrowsWithoutBound()
    {
        var dir = CreateTempLogDir();
        try
        {
            var logPath = Path.Combine(dir, "state_trace.txt");
            File.WriteAllText(logPath, new string('x', 120));

            LocalLog.AppendLine("state_trace.txt", "next line", dir, maxBytes: 100, archiveCount: 1);

            Assert.True(File.Exists(logPath));
            Assert.True(File.Exists(Path.Combine(dir, "state_trace.txt.1")));
            Assert.Contains("next line", File.ReadAllText(logPath));
            Assert.Equal(120, new FileInfo(Path.Combine(dir, "state_trace.txt.1")).Length);
        }
        finally
        {
            DeleteTempLogDir(dir);
        }
    }

    private static string CreateTempLogDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "SiloPlayer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempLogDir(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }
}
