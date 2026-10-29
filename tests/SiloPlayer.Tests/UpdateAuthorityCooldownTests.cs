using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class UpdateAuthorityCooldownTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "silo-authority-cooldown-" + Guid.NewGuid().ToString("N"));
    private readonly Clock clock = new();
    private string Store => Path.Combine(root, ".silo-update", "github-cooldown.json");
    public UpdateAuthorityCooldownTests()
    {
        var version = Path.Combine(root, "versions", "1.2.464.0"); Directory.CreateDirectory(version);
        var entries = new List<UpdateFileEntry>();
        foreach (var name in new[] { "SiloPlayer.exe", "SiloPlayer.dll" })
        {
            var bytes = Encoding.UTF8.GetBytes("synthetic " + name); File.WriteAllBytes(Path.Combine(version, name), bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)); entries.Add(new(name, bytes.Length, hash, entries.Count, 1, hash));
        }
        SideBySideInstallation.Initialize(root, new(1, "1.2.464.0", 2, new string('A', 64), entries));
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(-100, 5)]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(45, 45)]
    [InlineData(360, 360)]
    [InlineData(10000, 360)]
    public void RecordPersistsBoundedAbsoluteDeadlineAndReadDoesNotSlideIt(int? minutes, int expected)
    {
        var deadline = UpdateAuthorityCooldown.Record(root, minutes is null ? null : TimeSpan.FromMinutes(minutes.Value), clock);
        Assert.Equal(clock.GetUtcNow().AddMinutes(expected), deadline);
        var bytes = File.ReadAllBytes(Store);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(deadline, UpdateAuthorityCooldown.Read(root, clock));
        Assert.Equal(bytes, File.ReadAllBytes(Store));
        clock.Advance(TimeSpan.FromMinutes(expected - 1));
        Assert.Null(UpdateAuthorityCooldown.Read(root, clock));
        Assert.Equal(bytes, File.ReadAllBytes(Store));
    }

    [Fact]
    public void NewShorterLimitCannotReduceTheExistingDeadline()
    {
        var deadline = UpdateAuthorityCooldown.Record(root, TimeSpan.FromHours(2), clock);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(deadline, UpdateAuthorityCooldown.Record(root, TimeSpan.FromSeconds(1), clock));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(360)]
    public void AdvancingClockUsesOneInstantForTheRecordedBound(int minutes)
    {
        var advancing = new AdvancingClock();
        var deadline = UpdateAuthorityCooldown.Record(root, TimeSpan.FromMinutes(minutes), advancing);
        using var record = JsonDocument.Parse(File.ReadAllBytes(Store));
        var recordedAt = record.RootElement.GetProperty("RecordedAt").GetDateTimeOffset();
        var until = record.RootElement.GetProperty("Until").GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromMinutes(minutes), until - recordedAt);
        Assert.Equal(advancing.Start.AddMinutes(minutes), deadline);
        Assert.Equal(deadline, UpdateAuthorityCooldown.Read(root, advancing));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("too-long")]
    [InlineData("too-short")]
    [InlineData("future-record")]
    [InlineData("before-record")]
    [InlineData("schema")]
    [InlineData("malformed")]
    [InlineData("oversized")]
    public void InvalidOrOutOfBoundsRecordsAreReadOnlyAndIgnored(string kind)
    {
        var now = clock.GetUtcNow();
        var start = kind == "future-record" ? now.AddMinutes(1) : now;
        var until = kind switch
        {
            "expired" => now,
            "too-long" => now.AddHours(7),
            "too-short" => now.AddMinutes(1),
            "before-record" => now.AddMinutes(-1),
            _ => now.AddHours(1)
        };
        var bytes = kind switch
        {
            "malformed" => Encoding.UTF8.GetBytes("{not-json"),
            "oversized" => Encoding.UTF8.GetBytes(new string(' ', 4096)),
            _ => JsonSerializer.SerializeToUtf8Bytes(new { Schema = kind == "schema" ? 2 : 1, RecordedAt = start, Until = until })
        };
        File.WriteAllBytes(Store, bytes);
        Assert.Null(UpdateAuthorityCooldown.Read(root, clock));
        Assert.Equal(bytes, File.ReadAllBytes(Store));
    }

    [Fact]
    public void HardLinkedStoreCannotReadOrReplaceForeignContents()
    {
        var foreign = Path.Combine(root, "foreign.json"); File.WriteAllText(foreign, "synthetic foreign sentinel");
        Assert.True(CreateHardLink(Store, foreign, IntPtr.Zero));
        Assert.Null(UpdateAuthorityCooldown.Read(root, clock));
        Assert.ThrowsAny<Exception>(() => UpdateAuthorityCooldown.Record(root, TimeSpan.FromHours(1), clock));
        Assert.Equal("synthetic foreign sentinel", File.ReadAllText(foreign));
    }

    [Fact]
    public void MissingStoreDoesNotCreateMetadataOrRequireAnUnelevatedWrite()
    {
        Assert.False(File.Exists(Store));
        Assert.Null(UpdateAuthorityCooldown.Read(root, clock));
        Assert.False(File.Exists(Store));
    }

    public void Dispose() => Directory.Delete(root, true);
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
    private sealed class AdvancingClock : TimeProvider
    {
        public DateTimeOffset Start { get; } = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        private long calls;
        public override DateTimeOffset GetUtcNow() => Start.AddTicks(calls++);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string path, string existing, IntPtr reserved);
}
