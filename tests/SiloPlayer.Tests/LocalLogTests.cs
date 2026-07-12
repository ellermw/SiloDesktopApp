using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class LocalLogTests
{
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
