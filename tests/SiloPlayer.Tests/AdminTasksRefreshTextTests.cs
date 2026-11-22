using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// The text the server sends as keys or raw values and the official client shows in the interface language
/// (official AdminTaskDetail.tsx, TaskStatusBadge.tsx, taskText.ts and itemTypeLabels.ts at 2972dc63c).
/// </summary>
public sealed class AdminTasksRefreshTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static AdminTaskDebtSample Sample(string title = "", string type = "", string series = "", int? season = null, int? episode = null, string id = "99") =>
        new("item", id, title, type, 1, Now, null, 1, "", series, season, episode);

    private static AdminTaskExecution Run(string status, string? message = null, params AdminTaskStep[] steps) =>
        new("1", "fixture", Now, Now, status, 0, message, steps, null);

    [Theory]
    [InlineData("library", "Library")]
    [InlineData("metadata", "Metadata")]
    [InlineData("system", "System")]
    [InlineData("future_category", "future_category")]
    [InlineData("", "")]
    public void TheCategoryBadgeShowsTheCatalogLabelAndAnUnknownCategoryAsSent(string category, string expected) =>
        Assert.Equal(expected, AdminTasksText.Category(category));

    [Theory]
    [InlineData("movie", "Movie")]
    [InlineData("series", "Series")]
    [InlineData("season", "Season")]
    [InlineData("episode", "Episode")]
    [InlineData("ebook", "Ebook")]
    [InlineData("audiobook", "Audiobook")]
    [InlineData("manga", "Manga")]
    [InlineData("podcast", "podcast")]
    [InlineData("Movie", "Movie")]
    [InlineData("", "item")]
    public void ItemTypesUseTheCatalogLabelUnknownTypesAreShownAsSentAndEmptyIsItem(string type, string expected) =>
        Assert.Equal(expected, AdminTasksText.ItemType(type));

    [Fact]
    public void EveryOfficialRefreshKeyIsInTheTextTableWithTheOfficialWording()
    {
        var expected = new Dictionary<string, string>
        {
            ["web.admin.common.taskStatus.failedDetails"] = "Task failed. Inspect administrator diagnostics for details.",
            ["web.admin.tasks.detail.refreshFailed"] = "Refresh failed. Inspect administrator diagnostics for details.",
            ["web.admin.tasks.detail.sampleEpisode"] = "{code} {title}",
            ["web.admin.tasks.detail.sampleSeriesEpisode"] = "{series} - {code} {title}",
            ["web.admin.tasks.detail.sampleTitle"] = "{series} - {name}",
            ["web.admin.tasks.task.backfill_attachment_bitrate.name"] = "Correct Bitrate of Files with Embedded Fonts",
        };
        foreach (var (key, text) in expected)
        {
            Assert.True(AdminTasksText.Has(key), key);
            Assert.Equal(text, AdminTasksText.Pattern(key));
        }
        Assert.StartsWith("Re-reads the header of MKV files with ASS subtitles", AdminTasksText.Pattern("web.admin.tasks.task.backfill_attachment_bitrate.description"));
        Assert.Equal("Correct Bitrate of Files with Embedded Fonts",
            AdminTasksText.TaskName(new("backfill_attachment_bitrate", "Server English", "", "system", AdminTaskStates.Idle, 0, true, null, [], null, null)));
    }

    [Fact]
    public void ARecentRefreshFailureShowsTheFixedSentenceWhateverTheServerSent() =>
        Assert.Equal("Refresh failed. Inspect administrator diagnostics for details.", AdminTasksText.RefreshFailed());

    [Fact]
    public void SamplesThatAreNotSeasonsOrEpisodesShowTheirTitleThenTheirContentId()
    {
        Assert.Equal("Synthetic Movie", AdminTasksText.SampleTitle(Sample("Synthetic Movie", "movie")));
        Assert.Equal("Synthetic Movie", AdminTasksText.SampleTitle(Sample("Synthetic Movie", "movie", series: "ignored without a season")));
        Assert.Equal("99", AdminTasksText.SampleTitle(Sample("", "movie")));
        Assert.Equal("77", AdminTasksText.SampleTitle(Sample("", "", id: "77")));
    }

    [Theory]
    [InlineData("Season 2", "Synthetic Show", 2, "Synthetic Show - Season 2")]
    [InlineData("", "Synthetic Show", 2, "Synthetic Show - Season 2")]
    [InlineData("Specials", "Synthetic Show", 0, "Synthetic Show - Specials")]
    [InlineData("", "Synthetic Show", 0, "Synthetic Show - Specials")]
    [InlineData("The Final Act", "Synthetic Show", 3, "Synthetic Show - The Final Act")]
    [InlineData("Season 5", "Synthetic Show", 3, "Synthetic Show - Season 5")]
    [InlineData("Season 4", "", 4, "Season 4")]
    [InlineData("", "", 0, "Specials")]
    [InlineData("The Final Act", "", 3, "The Final Act")]
    [InlineData("Extras", "Synthetic Show", 0, "Synthetic Show - Extras")]
    [InlineData("Extras", "", 0, "Extras")]
    [InlineData("Season 0", "Synthetic Show", 0, "Synthetic Show - Season 0")]
    public void ASeasonKeepsItsOwnNameAndAStoredEnglishDefaultBecomesTheCatalogName(string title, string series, int season, string expected) =>
        Assert.Equal(expected, AdminTasksText.SampleTitle(Sample(title, "season", series, season)));

    [Theory]
    [InlineData("Pilot", "Synthetic Show", 1, 2, "Synthetic Show - S01E02 Pilot")]
    [InlineData("Pilot", "", 1, 2, "S01E02 Pilot")]
    [InlineData("", "Synthetic Show", 10, 12, "Synthetic Show - S10E12")]
    [InlineData("", "", 0, 7, "S00E07")]
    [InlineData("Pilot", "", 0, 7, "S00E07 Pilot")]
    [InlineData("Pilot", "Synthetic Show", 0, 7, "Synthetic Show - S00E07 Pilot")]
    [InlineData("", "Synthetic Show", 0, 7, "Synthetic Show - S00E07")]
    [InlineData("Pilot", "Synthetic Show", 123, 456, "Synthetic Show - S123E456 Pilot")]
    [InlineData("Pilot", "Synthetic Show", 1, 0, "Synthetic Show - S01E00 Pilot")]
    public void AnEpisodeIsNamedFromItsPartsWithTwoDigitCodes(string title, string series, int season, int episode, string expected) =>
        Assert.Equal(expected, AdminTasksText.SampleTitle(Sample(title, "episode", series, season, episode)));

    [Theory]
    [InlineData("Pilot", "Synthetic Show", 5, "Pilot")]
    [InlineData("", "Synthetic Show", 5, "99")]
    [InlineData("", "", 0, "99")]
    public void AnEpisodeNumberWithoutASeasonNumberIsNotComposedAndShowsTheTitleThenTheContentId(string title, string series, int episode, string expected) =>
        Assert.Equal(expected, AdminTasksText.SampleTitle(Sample(title, "episode", series, season: null, episode: episode)));

    [Fact]
    public void ServerTitlesAreInsertedAsPlainTextNeverAsMessageSyntax()
    {
        Assert.Equal("{code} - S01E02 {title} <b>x</b>", AdminTasksText.SampleTitle(Sample("{title} <b>x</b>", "episode", "{code}", 1, 2)));
        Assert.Equal("{series} - {name} - Season 9", AdminTasksText.SampleTitle(Sample("{name} - Season 9", "season", "{series}", 1)));
    }

    [Fact]
    public void AFailedRunSaysTheFixedSentenceInItsTooltipAndHistoryCell()
    {
        const string fixedSentence = "Task failed. Inspect administrator diagnostics for details.";
        Assert.Equal(fixedSentence, AdminTasksText.StatusDetail(Run("failed", "Some raw server cause")));
        Assert.Equal(fixedSentence, AdminTasksText.StatusDetail(Run("failed")));
        Assert.Equal(fixedSentence, AdminTasksText.HistoryError(Run("failed", "Some raw server cause")));
        Assert.Equal(fixedSentence, AdminTasksText.HistoryError(Run("failed")));
    }

    [Fact]
    public void OtherStatusesKeepTheTrimmedServerMessageOrNone()
    {
        Assert.Equal("Cancelled by an administrator", AdminTasksText.StatusDetail(Run("cancelled", "  Cancelled by an administrator \n")));
        Assert.Null(AdminTasksText.StatusDetail(Run("completed")));
        Assert.Null(AdminTasksText.StatusDetail(Run("completed", "   ")));
        Assert.Equal("Cancelled by an administrator", AdminTasksText.HistoryError(Run("cancelled", "Cancelled by an administrator")));
        Assert.Equal(AdminTasksText.EmDash, AdminTasksText.HistoryError(Run("cancelled")));
    }

    [Fact]
    public void FailedStepNamesGoThroughTheTaskCatalogByStepKeyAndOtherNamesAreShownAsSent()
    {
        var text = AdminTasksText.HistoryError(Run("failed", "raw", new("cleanup_activity_log", "Server English", "failed"), new("plugin:7:feed", "Plugin feed", "failed"),
            new("scan_libraries", "Ignored", "completed")));
        Assert.Equal("Failed steps: Cleanup Activity Log and Plugin feed", text);
        Assert.Equal("Cleanup Activity Log", AdminTasksText.StepName(new("cleanup_activity_log", "Other", "failed")));
        Assert.Equal("Only the name", AdminTasksText.StepName(new("not in the catalog!", "Only the name", "failed")));
        Assert.Equal("Not in catalog", AdminTasksText.StepName(new("not_in_catalog", "Not in catalog", "failed")));
        Assert.Equal("", AdminTasksText.StepName(new("", "", "failed")));
    }
}
