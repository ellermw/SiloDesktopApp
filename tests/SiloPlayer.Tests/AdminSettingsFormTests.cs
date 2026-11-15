using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminSettingsFormTests
{
    private static AdminSettingsForm Loaded() => new(["branding.server_name", "redis.url", "redis.db"], ["redis.url"],
        new Dictionary<string, string> { ["branding.server_name"] = "Silo", ["redis.db"] = "3" }, "\"v1\"");

    [Fact]
    public void RequiresStrongValidatorAndNeverRetainsReturnedSecret()
    {
        foreach (var etag in new[] { "", "*", "W/\"v1\"" })
            Assert.Throws<InvalidDataException>(() => new AdminSettingsForm(["redis.url"], ["redis.url"],
                new Dictionary<string, string> { ["redis.url"] = "secret" }, etag));
        var form = new AdminSettingsForm(["redis.url"], ["redis.url"],
            new Dictionary<string, string> { ["redis.url"] = "secret" }, "\"v1\"");
        Assert.Equal("", form.Get("redis.url"));
    }

    [Fact]
    public void RedisUrlPairsDisplayedDatabaseAndKeepSavedWithdrawsOnlyUnchangedDatabase()
    {
        var form = Loaded();
        form.StageRedisUrl("redis://example");
        Assert.Equal(new[] { "redis.db", "redis.url" }, form.DirtyKeys.Order());
        form.KeepSavedRedisUrl();
        Assert.Empty(form.DirtyKeys);
        form.Set("redis.db", "4");
        form.StageRedisUrl("");
        form.KeepSavedRedisUrl();
        Assert.Equal(new[] { "redis.db" }, form.DirtyKeys);
    }

    [Fact]
    public void SaveAcknowledgementUsesNewValidatorAndPreservesLaterEdits()
    {
        var form = Loaded();
        form.Set("branding.server_name", "First");
        form.StageRedisUrl("redis://example");
        var save = form.CaptureSave();
        form.Set("branding.server_name", "Later");
        form.Acknowledge(save, new Dictionary<string, string> { ["branding.server_name"] = "First" }, "\"v2\"", true);
        Assert.Equal("Later", form.Get("branding.server_name"));
        Assert.Equal("", form.Get("redis.url"));
        Assert.Equal(new[] { "branding.server_name" }, form.DirtyKeys);
        Assert.Equal("\"v2\"", form.CaptureSave().ETag);
        Assert.True(form.RestartRequired);
    }

    [Fact]
    public void ExplicitStageAndDiscardRespectBaseline()
    {
        var form = Loaded();
        form.Set("branding.server_name", "Changed");
        form.Set("branding.server_name", "Silo");
        Assert.Empty(form.DirtyKeys);
        form.Set("redis.url", "", stage: true);
        Assert.True(form.IsClearStaged("redis.url"));
        form.Discard();
        Assert.Empty(form.DirtyKeys);
        Assert.Equal("3", form.Get("redis.db"));
    }

    [Fact]
    public void EditBackToOldBaselineDuringSaveRemainsDirty()
    {
        var form = Loaded();
        form.Set("branding.server_name", "First");
        var save = form.CaptureSave();
        form.Set("branding.server_name", "Silo");
        Assert.Contains("branding.server_name", form.DirtyKeys);
        Assert.Throws<InvalidOperationException>(() => form.CaptureSave());
        form.Acknowledge(save, new Dictionary<string, string> { ["branding.server_name"] = "First" }, "\"v2\"", false);
        Assert.Equal("Silo", form.Get("branding.server_name"));
        Assert.Contains("branding.server_name", form.DirtyKeys);
    }

    [Fact]
    public void MissingOrdinaryValueEqualsEmptyAndSecretIntentIsRedacted()
    {
        var form = new AdminSettingsForm(["ordinary", "secret"], ["secret"], new Dictionary<string, string>(), "\"v1\"");
        form.Set("ordinary", "");
        Assert.Empty(form.DirtyKeys);
        form.Set("secret", "sensitive-example");
        var intent = form.CaptureSave();
        Assert.DoesNotContain("sensitive-example", intent.ToString());
        form.Discard();
        Assert.Empty(intent.Values);
        Assert.Throws<InvalidOperationException>(() => form.Acknowledge(intent, new Dictionary<string, string>(), "\"v2\"", false));
        Assert.Empty(form.DirtyKeys);
    }

    [Fact]
    public void LaterSecretEditSurvivesAcknowledgement()
    {
        var form = Loaded();
        form.Set("redis.url", "first");
        var intent = form.CaptureSave();
        form.Set("redis.url", "second");
        form.Acknowledge(intent, new Dictionary<string, string> { ["redis.url"] = "returned-secret" }, "\"v2\"", false);
        Assert.Equal("second", form.Get("redis.url"));
        Assert.Contains("redis.url", form.DirtyKeys);
        Assert.Empty(intent.Values);
    }
}
