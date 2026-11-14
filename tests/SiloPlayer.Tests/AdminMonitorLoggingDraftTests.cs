using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminMonitorLoggingDraftTests
{
    [Fact]
    public void DisclosureChoiceSurvivesNewPagesButDirtyForceDoesNotPersist()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SiloMonitorLogs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var draft = new LoggingDraft(new LoggingDisclosurePreferences(directory));
            draft.Load(new(new Dictionary<string, string>(), "\"one\""));
            draft.ToggleServerAdvanced();
            draft.Set("opslog.max_rows", "2");
            Assert.True(draft.RetentionAdvancedOpen);
            var reopened = new LoggingDraft(new LoggingDisclosurePreferences(directory));
            Assert.True(reopened.ServerAdvancedOpen);
            Assert.False(reopened.RetentionAdvancedOpen);
            reopened.ToggleServerAdvanced();
            Assert.False(new LoggingDraft(new LoggingDisclosurePreferences(directory)).ServerAdvancedOpen);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }

    [Fact]
    public void DiscardDuringSaveHydratesTheAcknowledgedServerValueWithoutLosingLaterEdits()
    {
        var draft = new LoggingDraft();
        draft.Load(new(new Dictionary<string, string> { ["server.log_level"] = "info" }, "\"one\""));
        draft.Set("server.log_level", "debug");
        var intent = draft.BeginSave();
        draft.Discard();
        draft.Acknowledge(intent, new(new() { Values = new() { ["server.log_level"] = "debug" } }, "\"two\""));
        Assert.Equal("debug", draft.Get("server.log_level"));
        Assert.Empty(draft.DirtyKeys);
    }

    [Fact]
    public void DraftProjectsOnlyLoggingKeysAndSavesOneDirtyBatch()
    {
        var draft = new LoggingDraft();
        draft.Load(new(new Dictionary<string, string> { ["server.log_level"] = "info", ["opslog.max_rows"] = "1000000", ["secret.api_key"] = "never retain" }, "\"one\""));
        Assert.DoesNotContain("secret.api_key", draft.Values.Keys);
        draft.Set("server.log_level", "debug");
        draft.Set("opslog.max_rows", "9223372036854775807");
        var intent = draft.BeginSave();
        Assert.Equal("\"one\"", intent.ETag);
        Assert.Equal(2, intent.Values.Count);
        Assert.True(draft.RetentionAdvancedOpen);
        Assert.Throws<ArgumentException>(() => draft.Set("secret.api_key", "x"));
    }

    [Fact]
    public void AcknowledgedSaveKeepsNewerEditsAndAdvancesValidator()
    {
        var draft = new LoggingDraft();
        draft.Load(new(new Dictionary<string, string> { ["server.log_level"] = "info" }, "\"one\""));
        draft.Set("server.log_level", "debug");
        var intent = draft.BeginSave();
        draft.Set("server.log_level", "error");
        draft.Acknowledge(intent, new(new() { Values = new() { ["server.log_level"] = "debug" }, RestartRequired = true }, "\"two\""));
        Assert.Equal("error", draft.Values["server.log_level"]);
        Assert.Single(draft.DirtyKeys);
        Assert.Equal("\"two\"", draft.BeginSave().ETag);
        Assert.True(draft.RestartRequired);
        draft.Discard();
        Assert.Equal("debug", draft.Values["server.log_level"]);
    }

    [Theory]
    [InlineData("opslog.max_rows", "9223372036854775808")]
    [InlineData("opslog.retention_days", "2147483648")]
    [InlineData("opslog.max_size_mb", "0")]
    [InlineData("server.log_level", "trace")]
    public void InvalidDraftCannotCreateSaveIntent(string key, string value)
    {
        var draft = new LoggingDraft();
        draft.Load(new(new Dictionary<string, string>(), "\"one\""));
        draft.Set(key, value);
        Assert.Throws<ArgumentException>(() => draft.BeginSave());
    }

    [Fact]
    public void RestartPrefixesAndDirtyAdvancedDisclosureRemainIndependent()
    {
        var draft = new LoggingDraft();
        draft.Load(new(new Dictionary<string, string>(), "\"one\""));
        draft.SetRestartKeys(new() { Keys = ["server.log_level"], Prefixes = ["opslog."] });
        Assert.True(draft.NeedsRestart("opslog.max_rows"));
        Assert.False(draft.NeedsRestart("policy.decision_log_verbosity"));
        draft.Set("server.log_quiet", "metadata, scanner");
        Assert.True(draft.ServerAdvancedOpen);
        draft.ToggleServerAdvanced();
        Assert.False(draft.ServerAdvancedOpen);
        draft.Discard();
        draft.Set("server.log_quiet", "scanner");
        Assert.True(draft.ServerAdvancedOpen);
        draft.Clear();
        Assert.Empty(draft.Values);
        Assert.Empty(draft.DirtyKeys);
    }
}
