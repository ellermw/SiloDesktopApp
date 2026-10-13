using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class LocalLogTests
{
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
