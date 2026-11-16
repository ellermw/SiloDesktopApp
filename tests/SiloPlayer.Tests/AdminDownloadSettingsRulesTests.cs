using SiloPlayer.Core.Services.Admin;
using R = SiloPlayer.Core.Services.Admin.AdminDownloadSettingsRules;

namespace SiloPlayer.Tests;

/// <summary>
/// Manage > Downloads > Settings rules (official DownloadsSettings.tsx, settingsPathDefaults.ts and LimitField.tsx, plus
/// the server's NormalizeAdminSetting/LoadFromDB at silo-server abd85d0c) on synthetic values only.
/// </summary>
public sealed class AdminDownloadSettingsRulesTests
{
    private static IReadOnlyDictionary<string, string> Values(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    private static AdminDownloadFieldError? Check(string key, string value, params (string, string)[] others)
    {
        var values = Values([(key, value), .. others]);
        return R.Validate(values, [key]).GetValueOrDefault(key);
    }

    [Fact]
    public void GroupsAreTheThirteenOfficialKeysInOfficialOrder()
    {
        Assert.Equal(["download.enabled", "download.transcode_enabled", "download.max_concurrent_prepares", "download.artifact_dir",
            "download.artifact_max_bytes", "download.artifact_disk_ceiling_percent", "download.server_bandwidth_mbps"], R.EssentialKeys);
        Assert.Equal(["download.user_bandwidth_mbps", "download.max_concurrent_per_user", "download.max_per_period", "download.period_duration"], R.PerUserKeys);
        Assert.Equal(["download.local_transcode_fallback", "download.artifact_cache_hours"], R.WholeServerKeys);
        Assert.Equal(13, R.Keys.Count);
        Assert.Equal(R.EssentialKeys.Concat(R.PerUserKeys).Concat(R.WholeServerKeys), R.Keys);
        Assert.DoesNotContain(R.TranscodeDir, R.Keys); // read-only: the Playback page owns it
        Assert.Equal([R.TranscodeDir], AdminDownloadSettingsController.ReadOnlyKeys);
    }

    // ---- settingsPathDefaults.test.ts: expectations captured from config.EffectiveDownloadArtifactDir

    [Theory]
    [InlineData("/mnt/downloads", "/mnt/fast/transcode", "/mnt/downloads")]
    [InlineData("", "", "/tmp/silo-download-artifacts")]
    [InlineData("", "/tmp/silo-transcode", "/tmp/silo-download-artifacts")]
    [InlineData("", "/mnt/fast/transcode", "/mnt/fast/silo-download-artifacts")]
    [InlineData("", "/transcode", "/silo-download-artifacts")]
    [InlineData("", "/mnt/fast/transcode/", "/mnt/fast/silo-download-artifacts")] // trailing slash must not nest inside the transcode root
    [InlineData("", "/", "/silo-download-artifacts")]
    [InlineData("", "/mnt//fast/transcode", "/mnt/fast/silo-download-artifacts")]
    [InlineData("", "/mnt/fast/../transcode", "/mnt/silo-download-artifacts")]
    [InlineData("", "relative/transcode", "relative/silo-download-artifacts")]
    [InlineData("", "transcode", "silo-download-artifacts")]
    public void BlankArtifactDirectoryDerivesASiblingOfTheTranscodeDirectory(string artifactDir, string transcodeDir, string expected) =>
        Assert.Equal(expected, R.EffectiveArtifactDirectory(artifactDir, transcodeDir));

    // ---- LimitField.tsx scaled units

    [Theory]
    [InlineData("53687091200", "53.687")]
    [InlineData("100000000000", "100")]
    [InlineData("1500000000", "1.5")]
    [InlineData("0", "0")]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("not a number", "not a number")]
    [InlineData("1234", "0")] // below the three-decimal MB granularity
    [InlineData("1500", "0")]
    [InlineData("2500000", "0.003")]
    public void StoredBytesAreShownAsDecimalGigabytes(string stored, string expected) => Assert.Equal(expected, R.ToDisplayUnits(stored, R.BytesPerGigabyte));

    [Theory]
    [InlineData("100", "100000000000")]
    [InlineData("1.5", "1500000000")]
    [InlineData("1.", "1000000000")]
    [InlineData(".5", "500000000")]
    [InlineData("1.50", "1500000000")]
    [InlineData("1.0005", "1000500000")]
    [InlineData("0.0000000005", "1")] // half a byte rounds up, never down through float drift
    [InlineData("1e1", "10000000000")]
    [InlineData("  50  ", "50000000000")]
    [InlineData("", "")]
    [InlineData("abc", "abc")] // kept as typed so the validator can say why
    [InlineData("-5", "-5000000000")]
    public void TypedGigabytesAreStoredAsExactBytes(string typed, string expected) => Assert.Equal(expected, R.ToStoredUnits(typed, R.BytesPerGigabyte));

    [Fact]
    public void UnscaledLimitsPassThroughUnchanged()
    {
        Assert.Equal(" 12 ", R.ToDisplayUnits(" 12 ", 1));
        Assert.Equal("1.5", R.ToStoredUnits("1.5", 1)); // the server, not the box, rejects a fraction on a count
    }

    [Fact]
    public void ScaledConversionDoesNotOverflowOnHugeText()
    {
        Assert.Equal("100000000000000000000000000", R.ToStoredUnits("100000000000000000000000000", R.BytesPerGigabyte));
        Assert.Equal("1e999", R.ToStoredUnits("1e999", R.BytesPerGigabyte));
    }

    [Fact]
    public void UnlimitedIsTheZeroSentinelAndNothingElse()
    {
        Assert.True(R.IsUnlimited("0"));
        Assert.True(R.IsUnlimited(" 0 "));
        Assert.False(R.IsUnlimited(""));
        Assert.False(R.IsUnlimited("00"));
        Assert.False(R.IsUnlimited("1"));
    }

    [Theory]
    [InlineData("0", 1L)]
    [InlineData("00", 1L)]
    [InlineData("+0", 1L)]
    [InlineData("-0", 1L)]
    [InlineData("  +00  ", 1L)]
    [InlineData("0.0000000004", R.BytesPerGigabyte)]
    [InlineData("-0.0000000004", R.BytesPerGigabyte)]
    public void TypedZeroLimitsStageAnInvalidBlankInsteadOfUnlimited(string text, long scale)
    {
        var draft = R.ToLimitDraft(text, scale);
        Assert.Equal("", draft);
        foreach (var key in R.Keys.Where(R.IsLimitKey))
            Assert.Equal(AdminDownloadFieldProblem.Blank, Check(key, draft)!.Problem);
    }

    [Theory]
    [InlineData("+12", 1L, "+12")]
    [InlineData("1.5", 1L, "1.5")]
    [InlineData("abc", 1L, "abc")]
    [InlineData("0.5", R.BytesPerGigabyte, "500000000")]
    public void TypedNonzeroAndInvalidLimitsKeepTheirStoredText(string text, long scale, string expected) =>
        Assert.Equal(expected, R.ToLimitDraft(text, scale));

    // ---- server normalizers

    [Theory]
    [InlineData("download.max_concurrent_prepares", "0", true)] // 0 = the built-in worker count, never "unlimited"
    [InlineData("download.max_concurrent_prepares", "2", true)]
    [InlineData("download.max_concurrent_prepares", "+3", true)]
    [InlineData("download.max_concurrent_prepares", "-1", false)]
    [InlineData("download.max_concurrent_prepares", "1.5", false)]
    [InlineData("download.max_concurrent_prepares", "1e2", false)]
    [InlineData("download.max_concurrent_prepares", "", false)]
    [InlineData("download.max_concurrent_prepares", "abc", false)]
    [InlineData("download.max_concurrent_prepares", "9223372036854775807", true)]
    [InlineData("download.max_concurrent_prepares", "9223372036854775808", false)]
    [InlineData("download.artifact_max_bytes", "0", true)]
    [InlineData("download.artifact_max_bytes", "53687091200", true)]
    [InlineData("download.artifact_max_bytes", "-1", false)]
    [InlineData("download.max_concurrent_per_user", "3", true)]
    [InlineData("download.max_concurrent_per_user", "-3", false)]
    [InlineData("download.max_per_period", "0", true)]
    [InlineData("download.artifact_disk_ceiling_percent", "49", false)]
    [InlineData("download.artifact_disk_ceiling_percent", "50", true)]
    [InlineData("download.artifact_disk_ceiling_percent", "85", true)]
    [InlineData("download.artifact_disk_ceiling_percent", "95", true)]
    [InlineData("download.artifact_disk_ceiling_percent", "96", false)]
    [InlineData("download.artifact_cache_hours", "0", true)]
    [InlineData("download.artifact_cache_hours", "72", true)]
    [InlineData("download.artifact_cache_hours", "720", true)]
    [InlineData("download.artifact_cache_hours", "721", false)]
    [InlineData("download.artifact_cache_hours", "-1", false)]
    [InlineData("download.server_bandwidth_mbps", "0", true)]
    [InlineData("download.server_bandwidth_mbps", "73786976294838", true)]
    [InlineData("download.server_bandwidth_mbps", "73786976294839", false)]
    [InlineData("download.user_bandwidth_mbps", "73786976294839", false)]
    [InlineData("download.user_bandwidth_mbps", "1000", true)]
    public void IntegerKeysFollowTheServerBounds(string key, string value, bool valid) => Assert.Equal(valid, Check(key, value) == null);

    [Fact]
    public void ProblemsDistinguishBlankNonIntegerAndOutOfRange()
    {
        Assert.Equal(AdminDownloadFieldProblem.Blank, Check(R.ServerBandwidth, "")!.Problem);
        Assert.Equal(AdminDownloadFieldProblem.NotInteger, Check(R.ServerBandwidth, "1.5")!.Problem);
        Assert.Equal(AdminDownloadFieldProblem.OutOfRange, Check(R.ServerBandwidth, "-4")!.Problem);
        Assert.Equal(AdminDownloadFieldProblem.OutOfRange, Check(R.MaxPerPeriod, "99999999999999999999999")!.Problem);
        var ceiling = Check(R.DiskCeilingPercent, "40")!;
        Assert.Equal((50L, 95L), (ceiling.Min, ceiling.Max));
        Assert.Equal("Enter a whole number from 50 to 95.", R.Describe(R.DiskCeilingPercent, ceiling));
        Assert.Equal("Enter a whole number, 0 or more.", R.Describe(R.MaxPerPeriod, Check(R.MaxPerPeriod, "-1")!));
        Assert.Equal("Enter a number or choose Unlimited.", R.Describe(R.UserBandwidth, Check(R.UserBandwidth, "")!));
        Assert.Equal("Enter a size in GB, 0 or more.", R.Describe(R.ArtifactMaxBytes, Check(R.ArtifactMaxBytes, "-5000000000")!));
        Assert.Equal($"Enter a whole number from 0 to {AdminText.FormatNumber(73_786_976_294_838)}.", R.Describe(R.ServerBandwidth, Check(R.ServerBandwidth, "-9")!));
    }

    // ---- period_duration: "Nd" or Go time.ParseDuration, and it must be positive

    [Theory]
    [InlineData("24h", true)]
    [InlineData("168h", true)]
    [InlineData("7d", true)]
    [InlineData("30d", true)]
    [InlineData("1h30m", true)]
    [InlineData("90m", true)]
    [InlineData("1.5h", true)]
    [InlineData("300ms", true)]
    [InlineData("1us", true)]
    [InlineData("1µs", true)]
    [InlineData("1μs", true)]
    [InlineData("100ns", true)]
    [InlineData("+24h", true)]
    [InlineData(".5s", true)]
    [InlineData("0", false)]       // parses, but the server requires a positive duration
    [InlineData("0s", false)]
    [InlineData("0d", false)]
    [InlineData("-1h", false)]
    [InlineData("", false)]
    [InlineData("24", false)]      // no unit
    [InlineData("h", false)]
    [InlineData("1d2h", false)]    // Go has no "d" unit; only a bare whole number of days
    [InlineData("1.5d", false)]
    [InlineData("-3d", false)]
    [InlineData("1 h", false)]
    [InlineData("abc", false)]
    [InlineData("1w", false)]
    [InlineData("9999999999999h", false)] // overflows int64 nanoseconds
    public void PeriodLengthFollowsTheServerDurationGrammarAndMustBePositive(string value, bool valid) =>
        Assert.Equal(valid, Check(R.PeriodDuration, value) == null);

    [Fact]
    public void DurationProblemsAreSpecific()
    {
        Assert.Equal(AdminDownloadFieldProblem.NotDuration, Check(R.PeriodDuration, "abc")!.Problem);
        Assert.Equal(AdminDownloadFieldProblem.DurationNotPositive, Check(R.PeriodDuration, "0s")!.Problem);
        Assert.Equal(AdminDownloadFieldProblem.DurationNotPositive, Check(R.PeriodDuration, "-1h")!.Problem);
        Assert.Equal(86_400_000_000_000L, R.ParseDurationNanoseconds("1d"));
        Assert.Equal(5_400_000_000_000L, R.ParseDurationNanoseconds("1h30m"));
        Assert.Equal(1_500_000L, R.ParseDurationNanoseconds("1.5ms"));
        Assert.Null(R.ParseDurationNanoseconds("1d2h"));
    }

    [Fact]
    public void ADayCountMultipliesLikeGoInt64WithoutAnOverflowCheck()
    {
        // Go's day branch multiplies days by 24h with no overflow check, so the server judges the wrapped value; so do we.
        var wrapped = unchecked(999_999_999_999L * 24 * 3_600_000_000_000L);
        Assert.Equal(wrapped, R.ParseDurationNanoseconds("999999999999d"));
        Assert.Equal(wrapped <= 0, Check(R.PeriodDuration, "999999999999d") != null);
        Assert.Null(R.ParseDurationNanoseconds("99999999999999999999d")); // beyond int64: Atoi fails, invalid
    }

    // ---- period pair dependency

    [Fact]
    public void LimitingDownloadsPerPeriodNeedsAPositivePeriod()
    {
        var limited = Values((R.MaxPerPeriod, "5"), (R.PeriodDuration, "24h"));
        Assert.Empty(R.Validate(limited, [R.MaxPerPeriod]));
        // The period itself is rejected first; the pair rule adds nothing on top of a key already in error.
        var invalid = R.Validate(Values((R.MaxPerPeriod, "5"), (R.PeriodDuration, "0s")), [R.PeriodDuration]);
        Assert.Equal(AdminDownloadFieldProblem.DurationNotPositive, invalid[R.PeriodDuration].Problem);
        // A saved legacy period the admin never touched is not blamed: only an edited member opens the pair.
        Assert.Empty(R.Validate(Values((R.MaxPerPeriod, "5"), (R.PeriodDuration, "0s"), (R.CacheHours, "72")), [R.CacheHours]));
    }

    [Fact]
    public void AMissingPeriodUsesTheServerDefaultUnlessItWasEditedToBlank()
    {
        Assert.Empty(R.Validate(Values((R.MaxPerPeriod, "5"), (R.PeriodDuration, "")), [R.MaxPerPeriod]));
        var blanked = R.Validate(Values((R.MaxPerPeriod, "5"), (R.PeriodDuration, "")), [R.PeriodDuration]);
        Assert.Equal(AdminDownloadFieldProblem.NotDuration, blanked[R.PeriodDuration].Problem);
        // Unlimited downloads per period needs no period at all.
        Assert.Empty(R.Validate(Values((R.MaxPerPeriod, "0"), (R.PeriodDuration, "24h")), [R.MaxPerPeriod, R.PeriodDuration]));
    }

    // ---- prepared file directory

    [Theory]
    [InlineData("", true)]
    [InlineData("/mnt/downloads", true)]
    [InlineData("  /mnt/downloads  ", true)]
    [InlineData("D:\\Downloads", true)]
    [InlineData("D:/Downloads", true)]
    [InlineData("\\\\server\\share", true)]
    [InlineData("downloads", false)]
    [InlineData("./downloads", false)]
    [InlineData("../downloads", false)]
    [InlineData("D:downloads", false)]
    public void RelativeDirectoriesAreRejectedByBothServerPlatforms(string path, bool valid) =>
        Assert.Equal(valid, Check(R.ArtifactDir, path) == null);

    [Fact]
    public void OnlyEditedKeysAreValidated()
    {
        var values = Values((R.DiskCeilingPercent, "12"), (R.CacheHours, "5000"), (R.ArtifactMaxBytes, "-1"));
        Assert.Empty(R.Validate(values, []));
        Assert.Equal([R.CacheHours], R.Validate(values, [R.CacheHours]).Keys);
        Assert.Equal(3, R.Validate(values, [R.DiskCeilingPercent, R.CacheHours, R.ArtifactMaxBytes]).Count);
    }

    [Fact]
    public void ToggleKeysAreNeverBlockedByTheValidator()
    {
        var values = Values((R.Enabled, "maybe"), (R.TranscodeEnabled, ""), (R.LocalTranscodeFallback, ""));
        Assert.Empty(R.Validate(values, [R.Enabled, R.TranscodeEnabled, R.LocalTranscodeFallback]));
    }
}
