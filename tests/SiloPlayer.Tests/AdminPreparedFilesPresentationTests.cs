using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>The presentation rules of the official Downloads admin page (Silo Server 66b836f82). Synthetic data only.</summary>
public sealed class AdminPreparedFilesPresentationTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1000, "1.0 KB")]
    [InlineData(999_500, "1.0 MB")]
    [InlineData(14_200_000_000, "14 GB")]
    [InlineData(9_950_000_000, "9.9 GB")]
    public void StorageBytesUseOfficialDecimalUnits(long bytes, string expected) =>
        Assert.Equal(expected, AdminPreparedFilesPresentation.FormatBytes(bytes));

    [Fact]
    public void EveryOwnedKeyHasTheEnglishOfficialPatternAndRendersWithoutBraces()
    {
        Assert.True(AdminPreparedFilesText.Keys.Count >= 480);
        foreach (var key in AdminPreparedFilesText.Keys)
        {
            // Placeholder values are the names the pattern itself uses, so a render never leaves an unfilled brace.
            var text = AdminPreparedFilesText.Plain(key, new Dictionary<string, object?>());
            Assert.DoesNotContain("{", text);
        }
        Assert.Equal("Downloads", AdminPreparedFilesText.T("web.admin.downloads.page.title"));
        Assert.Equal("Preparing downloads, the prepared files on the server and nodes, and the copies on people's devices.", AdminPreparedFilesText.T("web.admin.downloads.page.subtitle"));
        Assert.Equal(["Storage", "Preparation", "Prepared files", "Device copies", "History"], new[] { "storage", "preparation", "files", "devices", "history" }.Select(AdminPreparedFilesText.Tab));
    }

    [Fact]
    public void RichPatternsSplitIntoBoldCodeAndPlainRuns()
    {
        var runs = AdminPreparedFilesText.Rich("web.admin.downloads.storage.card.diskUsed", new { used = "5 GB", total = "10 GB", percent = "50%" });
        Assert.Equal("5 GB", runs[0].Text);
        Assert.True(runs[0].Has("b"));
        Assert.Equal(" of 10 GB used (50%)", string.Concat(runs.Skip(1).Select(x => x.Text)));
        Assert.Equal("5 GB of 10 GB used (50%)", AdminPreparedFilesText.Plain("web.admin.downloads.storage.card.diskUsed", new { used = "5 GB", total = "10 GB", percent = "50%" }));
    }

    [Fact]
    public void StorageWarningsPrioritizeDiskTemporaryOfflineUntrackedReplicaAndStaleDevicePressure()
    {
        var storage = new AdminDownloadStorage
        {
            DiskCeilingPercent = 85, StaleDeviceDays = 14,
            Locations =
            [
                new() { Key = "server", Kind = "server", Name = "Server", Online = true, InUseBytes = 10, CachedBytes = 5, ReplicasDisagree = true,
                    Usage = new() { FSTotalBytes = 100, FSUsedBytes = 84, Bytes = 15, Ephemeral = true, SharesScratch = true } },
                new() { Key = "node:9", Kind = "node", Name = "gpu-1", Online = false, InUseBytes = 20, WaitingDownloads = 2,
                    UntrackedBytes = 7, StaleWaitingBytes = 6 },
            ],
        };

        var warnings = AdminPreparedFilesPresentation.StorageWarnings(storage);

        Assert.Equal(["fill:server", "ephemeral:server", "replicas:server", "offline:node:9", "untracked:node:9", "stale"], warnings.Select(w => w.Id));
        Assert.Equal("Server is at 84% disk.", warnings[0].Title);
        Assert.Equal("Clean-up deletes cached files early at 85%. Prepared files share this disk with transcode scratch, and at 95% the node stops taking playback sessions.", warnings[0].Body);
        Assert.Equal(["edit_location", "edit_location", "edit_location", "view_location", "review_untracked", "review_devices"], warnings.Select(w => w.Action));
        Assert.Equal("6 B is waiting on devices not seen in 14+ days.", warnings[5].Title);
    }

    [Fact]
    public void MeterSegmentsScaleRecordsToWhatTheDiskHoldsAndNeverExceedTheFilesystem()
    {
        var location = new AdminDownloadStorageLocation
        {
            InUseBytes = 600, CachedBytes = 600, UntrackedBytes = 100,
            Usage = new() { FSTotalBytes = 1000, FSUsedBytes = 900, Bytes = 800, PartialBytes = 0, OtherBytes = 0 },
        };

        var segments = AdminPreparedFilesPresentation.MeterSegments(location)!;

        Assert.Equal(["in_use", "cached", "untracked", "other", "free"], segments.Select(s => s.Kind));
        Assert.Equal([350, 350, 100, 100, 100], segments.Select(s => s.Bytes));
        Assert.Equal(1000, segments.Sum(s => s.Bytes));
        Assert.Null(AdminPreparedFilesPresentation.MeterSegments(new AdminDownloadStorageLocation()));
    }

    [Fact]
    public void RecordsDriftNeedsMoreThanOneGigabyteAndTwoPercent()
    {
        var location = new AdminDownloadStorageLocation { InUseBytes = 100_000_000_000, Usage = new() { Bytes = 101_500_000_000 } };
        Assert.Equal(0, AdminPreparedFilesPresentation.RecordsDrift(location));
        location.Usage!.Bytes = 104_000_000_000;
        Assert.Equal(4_000_000_000, AdminPreparedFilesPresentation.RecordsDrift(location));
    }

    [Fact]
    public void FileLabelsMatchTheOfficialTitleSubtitleRecipeUsageAndExpiry()
    {
        var file = new AdminPreparedFile
        {
            Format = "transcode", Resolution = "1080p", VideoCodec = "hevc", BitrateKbps = 10_000, Downloading = 1, Waiting = 2, Finished = 3,
            SeasonNumber = 2, EpisodeNumber = 5, EpisodeTitle = "Pilot",
        };
        Assert.Equal("Unknown title", AdminPreparedFilesPresentation.FileTitle(file));
        Assert.Equal("S2 · E5 Pilot", AdminPreparedFilesPresentation.FileSubtitle(file));
        Assert.Equal(new AdminFileRecipe("1080p HEVC", "10 Mbps"), AdminPreparedFilesPresentation.FileRecipe(file));
        Assert.Equal(new AdminFileRecipe("Remux to MP4", "original quality"), AdminPreparedFilesPresentation.FileRecipe(new AdminPreparedFile { Format = "remux" }));
        Assert.Equal("1 downloading · 2 waiting · 3 finished", AdminPreparedFilesPresentation.FileUsage(file));
        Assert.Equal("No downloads", AdminPreparedFilesPresentation.FileUsage(new AdminPreparedFile()));
        var now = DateTimeOffset.Parse("2026-11-20T00:00:00Z");
        Assert.Equal("now", AdminPreparedFilesPresentation.FormatExpiresIn(now.AddSeconds(30), now));
        Assert.Equal("in 22 h", AdminPreparedFilesPresentation.FormatExpiresIn(now.AddHours(22), now));
        Assert.Equal("in 3 d", AdminPreparedFilesPresentation.FormatExpiresIn(now.AddDays(3), now));
    }

    [Fact]
    public void HistoryAndDeviceLabelsFollowTheOfficialReasonItemAndStatusRules()
    {
        Assert.Equal("future_reason", AdminPreparedFilesPresentation.StorageEventReasonLabel("future_reason"));
        var revoked = new AdminDownloadStorageEvent { Reason = "revoked", Count = 2, LocationName = "Pixel", Account = new() { Username = "maya" }, Titles = [] };
        Assert.Contains("Pixel", AdminPreparedFilesPresentation.StorageEventItem(revoked));
        Assert.Contains("maya", AdminPreparedFilesPresentation.StorageEventItem(revoked));
        Assert.Equal("Dune", AdminPreparedFilesPresentation.StorageEventItem(new() { Reason = "budget", Count = 1, Titles = ["Dune"] }));
        Assert.Equal("Device", AdminPreparedFilesPresentation.StorageEventLocation(new() { Location = "device" }));
        Assert.Equal(("Finished", "neutral"), AdminPreparedFilesPresentation.EntryStatus(new() { Status = "completed" }));
        Assert.Equal(("Waiting", "active"), AdminPreparedFilesPresentation.EntryStatus(new() { Status = "ready" }));
        Assert.Equal(("mystery", "muted"), AdminPreparedFilesPresentation.EntryStatus(new() { Status = "mystery" }));
        Assert.Equal("10 Mbps", AdminPreparedFilesPresentation.QualityLabel("10mbps"));
        Assert.Equal("Original", AdminPreparedFilesPresentation.QualityLabel("original"));
        Assert.Equal("none active", AdminPreparedFilesPresentation.DeviceSummary(new AdminDownloadDevice()));
        Assert.Equal("2 finished, 1 waiting", AdminPreparedFilesPresentation.DeviceSummary(new AdminDownloadDevice { Finished = 2, Waiting = 1 }));
    }

    private static AdminDownloadPreparation Job(string state = "running", string format = "transcode") => new()
    {
        Id = "j1", State = state, Format = format, MediaTitle = "Dune", MediaType = "movie", MediaFileId = "42", Attempts = 1, MaxAttempts = 3,
        Source = new() { Container = "mkv", VideoCodec = "hevc", Resolution = "2160p", Hdr = true, FileSize = 2_147_483_648, DurationSeconds = 7200, AudioTracks = [new() { Codec = "truehd", Channels = 8 }] },
        Output = new() { Container = "mp4", VideoCodec = "h264", Resolution = "1080p", BitrateKbps = 10_000, AudioCodec = "aac", ToneMapMode = "hardware" },
        Requesters = [new() { UserId = "7", Username = "maya", ProfileName = "Maya", DeviceId = "phone", DeviceName = "Pixel", Status = "preparing" }],
    };

    [Fact]
    public void PreparationLabelsMatchTheOfficialRows()
    {
        var job = Job();
        Assert.Equal("Dune", AdminPreparationPresentation.Title(job));
        Assert.Equal("Movie", AdminPreparationPresentation.Subtitle(job));
        Assert.Equal("MKV · 2160p HEVC HDR · 2.0 GB", AdminPreparationPresentation.FormatSource(job));
        Assert.Equal("1080p H.264 · 10 Mbps · HDR → SDR", AdminPreparationPresentation.FormatOutput(job));
        Assert.Equal("MP4 · streams copied", AdminPreparationPresentation.FormatOutput(Job(format: "remux")));
        Assert.Equal(["running", "queued", "retrying", "paused", "failed"], AdminPreparationPresentation.States);
        Assert.Equal(3, AdminPreparationPresentation.ActiveCount(new() { Running = 1, Queued = 1, Retrying = 1, Paused = 5, FailedRecent = 5 }));
        var episode = Job();
        episode.SeriesName = "Severance"; episode.SeasonNumber = 2; episode.EpisodeNumber = 1; episode.EpisodeName = "";
        Assert.Equal("S02E01", AdminPreparationPresentation.Title(episode));
        Assert.Equal("S02E01 · Severance", AdminPreparationPresentation.Subtitle(episode));
        Assert.Equal("Severance S02E01", AdminPreparationPresentation.CompactTitle(episode));
    }

    [Theory]
    [InlineData(0, "under 1 min left")]
    [InlineData(59, "under 1 min left")]
    [InlineData(600, "about 10 min left")]
    [InlineData(3600, "about 1 h left")]
    [InlineData(5400, "about 1 h 30 min left")]
    public void RemainingTimeUsesTheOfficialTiers(double seconds, string expected) =>
        Assert.Equal(expected, AdminPreparationPresentation.FormatRemaining(seconds));
    [Fact]
    public void ProgressSpeedClockAndAgeHelpersFollowTheOfficialRules()
    {
        var job = Job();
        job.Progress = new() { EncodedSeconds = 1800, DurationSeconds = 7200, Speed = 2.5 };
        Assert.Equal(25, AdminPreparationPresentation.Percent(job));
        Assert.Equal(2160, AdminPreparationPresentation.RemainingSeconds(job));
        Assert.Equal("2.5×", AdminPreparationPresentation.FormatSpeed(2.5));
        Assert.Equal("12×", AdminPreparationPresentation.FormatSpeed(12.4));
        Assert.Equal("", AdminPreparationPresentation.FormatSpeed(0));
        Assert.Equal("1:05:09", AdminPreparationPresentation.FormatClock(3909));
        Assert.Equal("2:05", AdminPreparationPresentation.FormatClock(125));
        job.Progress = new() { DurationSeconds = 0 };
        Assert.Null(AdminPreparationPresentation.Percent(job));
        var now = DateTimeOffset.Parse("2026-11-20T12:00:00Z");
        Assert.Equal("now", AdminPreparationPresentation.FormatRetryIn(now.AddSeconds(-5), now));
        Assert.Equal("", AdminPreparationPresentation.FormatRetryIn(null, now));
    }

    [Fact]
    public void ActionSummariesAndCancelPromptsNameWhatChanged()
    {
        AdminPreparationActionResult R(string id, string outcome) => new() { Id = id, Outcome = outcome };
        Assert.Equal("Paused 2 jobs. 1 job was already paused.", AdminPreparationPresentation.SummarizeAction("pause", [R("a", "applied"), R("b", "applied"), R("c", "unchanged")]));
        Assert.Equal("Nothing changed.", AdminPreparationPresentation.SummarizeAction("cancel", []));

        var running = Job();
        var prompt = AdminPreparationPresentation.CancelPrompt([running]);
        Assert.Equal("Cancel preparing Dune?", prompt.Title);
        Assert.StartsWith("Encoding stops and its progress is lost. 1 waiting download fails with", prompt.Description);
        var failed = Job("failed");
        Assert.Equal("Remove the failed job for Dune?", AdminPreparationPresentation.CancelPrompt([failed]).Title);
        Assert.Equal("Remove", AdminPreparationPresentation.CancelPrompt([failed]).ConfirmLabel);
        Assert.Equal("Cancel 2 jobs?", AdminPreparationPresentation.CancelPrompt([running, failed]).Title);
    }

    [Theory]
    [InlineData(1, "Node gpu-01 1")]
    [InlineData(2, "Node gpu-01 2")]
    [InlineData(12, "Node gpu-01 12")]
    public void AWorkerChipIsAnnouncedAsSeparateWordsWithItsJobCount(int count, string expected)
    {
        // Official #5079: "Node gpu-01 2", not "Nodegpu-012" (the visible parts are separate blocks, so the name has to spell the spaces).
        Assert.Equal(expected, AdminPreparationPresentation.WorkerChipName(new AdminPreparationWorker("node:9", "Node", "gpu-01"), count));
    }

    [Fact]
    public void WorkerAndRequesterLabelsFallBackTheWayTheOfficialPageDoes()
    {
        var job = Job();
        job.Worker = new() { Kind = "node", NodeId = "9", Name = "gpu-1" };
        Assert.Equal(new AdminPreparationWorker("node:9", "Node", "gpu-1"), AdminPreparationPresentation.Worker(job));
        job.Worker = new() { Kind = "server", Name = "" };
        Assert.Equal("Local server", AdminPreparationPresentation.Worker(job)!.Name);
        job.Worker = null;
        Assert.Null(AdminPreparationPresentation.Worker(job));
        var web = new AdminDownloadPreparationRequester { UserId = "7", Username = "", DeviceId = "" };
        Assert.Equal("User #7", AdminPreparationPresentation.RequesterName(web));
        Assert.Equal("Web download", AdminPreparationPresentation.RequesterDevice(web));
        Assert.Contains("maya", AdminPreparationPresentation.SearchText(job));
    }
    [Theory]
    [InlineData("Alien <Directors>")]
    [InlineData("<b>Heat</b>")]
    [InlineData("A < B > C <code>")]
    [InlineData("marker \uE000 and \uE000a and <")]
    public void ValuesInRichPatternsAppearExactlyAsGivenAndNeverRestyleTheRest(string title)
    {
        var runs = AdminPreparedFilesText.Rich("web.admin.downloads.preparedFiles.dialog.cachedList",
            AdminPreparedFilesPresentation.With(AdminPreparedFilesPresentation.Count(2), ("names", title + ", Other")));

        Assert.Equal("2 cached", runs[0].Text);
        Assert.True(runs[0].Has("b"));
        var rest = string.Concat(runs.Skip(1).Select(x => x.Text));
        Assert.Equal(" · " + title + ", Other", rest);
        // The value's own angle brackets are never read as markup: only the pattern's bold tag styles anything.
        Assert.All(runs.Skip(1), run => Assert.Empty(run.Tags));
        Assert.Equal("2 cached · " + title + ", Other", AdminPreparedFilesText.Plain("web.admin.downloads.preparedFiles.dialog.cachedList",
            AdminPreparedFilesPresentation.With(AdminPreparedFilesPresentation.Count(2), ("names", title + ", Other"))));
    }

    [Fact]
    public void NodeNamesAndDirectoriesInRichPatternsKeepTheirCharacters()
    {
        var plain = AdminPreparedFilesText.Plain("web.admin.downloads.storage.card.pendingDir", new { dir = "/srv/<prep>/b", name = "gpu <1>" });
        Assert.Contains("/srv/<prep>/b", plain);
        Assert.Contains("gpu <1>", plain);
    }

    [Fact]
    public void NoFailureTimestampCanThrowFromTheTimerCalculationOrScheduleATightLoop()
    {
        var now = DateTimeOffset.Parse("2026-11-16T12:00:00Z");
        AdminDownloadPreparation Failed(DateTimeOffset at) => new() { Id = "f", State = "failed", FailedAt = at };

        // Within a day of the end of time: adding 24 hours would throw, and nothing expires then.
        Assert.Null(AdminPreparationPresentation.NextFailureExpiry([Failed(DateTimeOffset.MaxValue)]));
        Assert.Null(AdminPreparationPresentation.NextFailureExpiry([Failed(DateTimeOffset.MaxValue.AddHours(-23))]));
        Assert.Null(AdminPreparedFilesController.FailureReadDelay([Failed(DateTimeOffset.MaxValue)], now));
        Assert.NotNull(AdminPreparationPresentation.NextFailureExpiry([Failed(DateTimeOffset.MaxValue.AddHours(-25))]));

        // Far future, far past and exactly now all fall back to the one-minute cadence; never a zero or negative delay.
        foreach (var at in new[] { DateTimeOffset.MaxValue.AddHours(-25), DateTimeOffset.Parse("2099-01-01T00:00:00Z"), DateTimeOffset.MinValue, DateTimeOffset.Parse("2000-01-01T00:00:00Z"), now.AddHours(-24) })
            Assert.Equal(AdminPreparedFilesController.PreparationMaintenance, AdminPreparedFilesController.FailureReadDelay([Failed(at)], now));

        // A normal failure waits until it ages out of the server's list, plus a second.
        Assert.Equal(TimeSpan.FromHours(22) + TimeSpan.FromSeconds(1), AdminPreparedFilesController.FailureReadDelay([Failed(now.AddHours(-2))], now));
        Assert.Null(AdminPreparedFilesController.FailureReadDelay([new AdminDownloadPreparation { Id = "q", State = "queued" }], now));
    }
}
