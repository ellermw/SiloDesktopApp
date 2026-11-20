using SiloPlayer.Core.Services.Admin;
using R = SiloPlayer.Core.Services.Admin.AdminLibrarySettingsRules;

namespace SiloPlayer.Tests;

/// <summary>Official LibraryMetadataSettings.tsx/admin_settings.go contract at silo-server 217ff25a.</summary>
public sealed class AdminLibrarySettingsRulesTests
{
    private static IReadOnlyDictionary<string, string> Values(params (string Key, string Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

    [Fact]
    public void OwnsExactlyTheThirteenOfficialLibraryMetadataKeysInPageOrder()
    {
        Assert.Equal([
            R.ScopeVersionsToLibrary, R.FallbackLanguage,
            R.RealtimeMonitoring, R.ScannerWorkers, R.MatcherWorkers, R.ImageWorkers, R.PersonRefreshPerMinute, R.MatcherBatchSize,
            R.UnratedContent, R.ExtraRatingSources,
            R.SubtitleAutoSync, R.SubtitleSyncExecution, R.SubtitleSyncNodeCapacity,
        ], R.Keys);
        Assert.Equal([R.PersonRefreshPerMinute, R.MatcherBatchSize], R.ScannerAdvancedKeys);
        Assert.Equal([R.SubtitleSyncExecution, R.SubtitleSyncNodeCapacity], R.SubtitleAdvancedKeys);
        Assert.Equal(new Dictionary<string, string>
        {
            [R.ScannerWorkers] = "8", [R.ImageWorkers] = "0", [R.PersonRefreshPerMinute] = "120",
            [R.MatcherWorkers] = "8", [R.MatcherBatchSize] = "500",
        }, R.WorkerDefaults);
    }

    [Theory]
    [InlineData("scanner.workers", "1", true)]
    [InlineData("scanner.workers", "1024", true)]
    [InlineData("scanner.workers", "0", false)]
    [InlineData("matcher.workers", "1025", false)]
    [InlineData("metadata.image_workers", "0", true)]
    [InlineData("metadata.image_workers", "256", true)]
    [InlineData("metadata.image_workers", "257", false)]
    [InlineData("metadata.person_refresh_per_minute", "1", true)]
    [InlineData("metadata.person_refresh_per_minute", "6000", true)]
    [InlineData("metadata.person_refresh_per_minute", "6001", false)]
    [InlineData("matcher.batch_size", "1", true)]
    [InlineData("matcher.batch_size", "100000", true)]
    [InlineData("matcher.batch_size", "100001", false)]
    [InlineData("subtitles.sync_node_capacity", "1", true)]
    [InlineData("subtitles.sync_node_capacity", "1024", true)]
    [InlineData("subtitles.sync_node_capacity", "1025", false)]
    [InlineData("matcher.batch_size", "1.5", false)]
    [InlineData("matcher.batch_size", "", false)]
    public void IntegerFieldsMirrorServerBounds(string key, string value, bool valid)
    {
        var errors = R.Validate(Values((key, value)), [key]);
        Assert.Equal(valid, errors.Count == 0);
    }

    [Theory]
    [InlineData("metadata.fallback_language", "en", true)]
    [InlineData("metadata.fallback_language", "pt", true)]
    [InlineData("metadata.fallback_language", "original", true)]
    [InlineData("metadata.fallback_language", "", true)]
    [InlineData("metadata.fallback_language", "pt-BR", false)]
    [InlineData("access.unrated_content", "hide", true)]
    [InlineData("access.unrated_content", "allow", true)]
    [InlineData("access.unrated_content", "show", false)]
    [InlineData("subtitles.sync_execution", "local", true)]
    [InlineData("subtitles.sync_execution", "prefer_transcode_nodes", true)]
    [InlineData("subtitles.sync_execution", "transcode_nodes_only", true)]
    [InlineData("subtitles.sync_execution", "remote", false)]
    public void SelectFieldsAcceptOnlyOfficialValues(string key, string value, bool valid) =>
        Assert.Equal(valid, R.Validate(Values((key, value)), [key]).Count == 0);

    [Theory]
    [InlineData("", true)]
    [InlineData("rt_critic", true)]
    [InlineData("rt_critic,kinopoisk", true)]
    [InlineData("rt_critic, letterboxd", true)]
    [InlineData("RT", false)]
    [InlineData("bad-source", false)]
    [InlineData("_bad", false)]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234567", false)]
    public void RatingListUsesTheServerIdentifierGrammar(string value, bool valid) =>
        Assert.Equal(valid, R.Validate(Values((R.ExtraRatingSources, value)), [R.ExtraRatingSources]).Count == 0);

    [Fact]
    public void RatingTogglesKeepOtherSourcesAndUndeclaredSavedChoicesReachable()
    {
        Assert.Equal(["rt_critic", "letterboxd", "rt_critic"], R.ParseRatingSources(" rt_critic,letterboxd,rt_critic "));
        Assert.Equal("rt_critic,letterboxd,kinopoisk", R.ToggleRatingSource("rt_critic,letterboxd", "kinopoisk", true));
        Assert.Equal("letterboxd,rt_critic", R.ToggleRatingSource("rt_critic,letterboxd,rt_critic", "rt_critic", true));
        Assert.Equal("letterboxd", R.ToggleRatingSource("rt_critic,letterboxd", "rt_critic", false));
        var listed = new[]
        {
            new AdminRatingSource("imdb", "IMDb", "IMDb", true, null),
            new AdminRatingSource("rt_critic", "Rotten Tomatoes critics", "RT", false, "MDBList"),
        };
        Assert.Equal(["letterboxd"], R.UndeclaredRatingSources(["rt_critic", "letterboxd"], listed));
        var group = Assert.Single(R.GroupRatingSources(listed));
        Assert.Equal("MDBList", group.Provider);
        Assert.Equal("rt_critic", Assert.Single(group.Sources).Source);
    }

    [Fact]
    public void ConditionalRowsAndWorkerRestoreMatchTheOfficialForm()
    {
        Assert.False(R.ShowRealtimeMonitoring("true", false));
        Assert.True(R.ShowRealtimeMonitoring("false", false));
        Assert.True(R.ShowRealtimeMonitoring("true", true));
        Assert.False(R.ShowSubtitleCapacity("local", false));
        Assert.True(R.ShowSubtitleCapacity("local", true));
        Assert.True(R.ShowSubtitleCapacity("prefer_transcode_nodes", false));
        Assert.False(R.HasWorkerOverrides(key => R.WorkerDefaults[key]));
        Assert.False(R.HasWorkerOverrides(_ => ""));
        Assert.True(R.HasWorkerOverrides(key => key == R.ScannerWorkers ? "4" : R.WorkerDefaults[key]));
    }

    [Fact]
    public void OnlyDirtyValuesAreValidatedAndErrorsNameTheOfficialRange()
    {
        var values = Values((R.ScannerWorkers, "0"), (R.ImageWorkers, "257"), (R.UnratedContent, "invalid"));
        Assert.Empty(R.Validate(values, []));
        Assert.Equal([R.ImageWorkers], R.Validate(values, [R.ImageWorkers]).Keys);
        var error = R.Validate(values, [R.ScannerWorkers])[R.ScannerWorkers];
        Assert.Equal(AdminLibrarySettingProblem.OutOfRange, error.Problem);
        Assert.Equal((1L, 1024L), (error.Min, error.Max));
        Assert.Equal("Enter a whole number from 1 to 1,024.", R.Describe(error));
    }
}
