using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using R = SiloPlayer.Core.Services.Admin.AdminStorageRules;

namespace SiloPlayer.Tests;

/// <summary>Pure storage-location rules (official StorageSettings.tsx at silo-server 8f93594c). Synthetic values only.</summary>
public sealed class AdminStorageRulesTests
{
    private sealed class Draft : IAdminStorageDraft
    {
        public readonly Dictionary<string, string> Saved = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Staged = new(StringComparer.Ordinal);
        public string Get(string key) => Staged.TryGetValue(key, out var value) ? value : Saved.GetValueOrDefault(key, "");
        public string GetPersisted(string key) => Saved.GetValueOrDefault(key, "");
        public bool IsDirty(string key) => Staged.ContainsKey(key);
    }

    private static readonly AdminArtworkStorageStatus LockedLocal = new(true, true, false, "local");
    private static readonly AdminArtworkStorageStatus LockedS3 = new(true, true, true, "s3");
    private static readonly AdminArtworkStorageStatus Unlocked = new(true, false, false, "local");
    private static AdminStorageSourceHealth Health(bool reachable, bool privateConfigured = false) =>
        new("s3", reachable, true, reachable, privateConfigured, reachable, true, null, false, null, null, 0);

    [Fact]
    public void ScopeIsExactlyTheOfficialStorageKeysWithFiveWriteOnlyCredentials()
    {
        Assert.Equal(22, R.Keys.Count);
        Assert.Equal(["artwork.storage_backend", "artwork.local_path", "metadata.cache_images"], R.Keys.Take(3));
        Assert.Equal(12, R.PublicS3Keys.Count);
        Assert.Equal(7, R.PrivateS3Keys.Count);
        Assert.Equal(["s3.public_access_key", "s3.public_secret_key", "s3.public_token_secret", "s3.private_access_key", "s3.private_secret_key"], R.Secrets);
        Assert.All(R.Secrets, key => Assert.Contains(key, R.Keys));
        Assert.DoesNotContain(R.CacheImages, R.TransitionKeys);
        Assert.Equal(21, R.TransitionKeys.Count);
        Assert.Equal(8, R.LocationKeys.Count);
    }

    [Theory]
    [InlineData("/var/lib/silo/artwork/", "/var/lib/silo/artwork")]
    [InlineData("/var//lib/./silo/../silo/artwork", "/var/lib/silo/artwork")]
    [InlineData("/..", "/")]
    [InlineData("../x/..", "..")]
    [InlineData("", ".")]
    public void CleanPathMatchesTheOfficialGoPathCleanPort(string path, string expected) => Assert.Equal(expected, R.CleanPath(path));

    [Theory]
    [InlineData("s3.public_endpoint", "HTTPS://User:Pw@S3.Example.COM:9000/Path/", "https://User:Pw@s3.example.com:9000/Path/")]
    [InlineData("s3.public_endpoint", "  Not A Url  ", "not a url")]
    [InlineData("s3.private_bucket", " Media ", "media")]
    [InlineData("s3.public_key_prefix", "//silo/art//", "silo/art")]
    [InlineData("artwork.local_path", "/data/./art/", "/data/art")]
    [InlineData("artwork.local_path", "relative/./x", "relative/./x")]
    [InlineData("s3.public_region", " Eu-West ", "Eu-West")]
    public void LocationValuesNormalizeLikeTheServerNamesStores(string key, string raw, string expected) =>
        Assert.Equal(expected, R.NormalizeLocationValue(key, raw));

    [Theory]
    [InlineData("s3", "", "s3")]
    [InlineData("local", "bucket", "local")]
    [InlineData("auto", "bucket", "s3")]
    [InlineData("", " ", "local")]
    [InlineData(" S3 ", "", "s3")]
    public void EffectiveBackendFollowsSelectionThenBucket(string backend, string bucket, string expected) =>
        Assert.Equal(expected, R.EffectiveBackend(backend, bucket));

    [Fact]
    public void RevertedOrCosmeticLocationEditsAreNotNewLocations()
    {
        var draft = new Draft();
        draft.Saved["s3.public_bucket"] = "media";
        draft.Staged["s3.public_bucket"] = "MEDIA ";
        Assert.False(R.LocationKeyChanged(draft, "s3.public_bucket"));
        draft.Staged["s3.public_bucket"] = "other";
        Assert.True(R.LocationKeyChanged(draft, "s3.public_bucket"));
        // With no private bucket before or after, a leftover endpoint names no location.
        draft.Staged["s3.private_endpoint"] = "https://elsewhere.example";
        Assert.False(R.LocationKeyChanged(draft, "s3.private_endpoint"));
        draft.Staged["s3.private_bucket"] = "private";
        Assert.True(R.LocationKeyChanged(draft, "s3.private_endpoint"));
    }

    [Fact]
    public void LockedStorageRoutesLocationEditsThroughAReviewButOtherEditsSaveDirectly()
    {
        var draft = new Draft();
        draft.Saved[R.LocalPath] = "/data/art";
        draft.Staged["s3.public_region"] = "eu";
        Assert.False(R.LocationChangePending(draft, LockedLocal, null));
        draft.Staged[R.LocalPath] = "/data/art/";
        Assert.False(R.LocationChangePending(draft, LockedLocal, null)); // cleaned path is the same store
        draft.Staged[R.LocalPath] = "/mnt/art";
        Assert.True(R.LocationChangePending(draft, LockedLocal, null));
        Assert.False(R.LocationChangePending(draft, Unlocked, null));
        draft.Staged.Remove(R.LocalPath);
        draft.Staged[R.Backend] = "s3";
        Assert.True(R.LocationChangePending(draft, LockedLocal, null));
        Assert.True(R.PublicBackendChangePending(draft, LockedLocal, null));
    }

    [Fact]
    public void PrivateLockAloneGuardsOnlyThePrivateIdentity()
    {
        var draft = new Draft();
        draft.Saved["s3.private_bucket"] = "private";
        var privateLocked = new AdminArtworkStorageStatus(true, false, true, "local");
        draft.Staged["s3.public_bucket"] = "public";
        Assert.False(R.LocationChangePending(draft, privateLocked, null));
        draft.Staged["s3.private_bucket"] = "moved";
        Assert.True(R.LocationChangePending(draft, privateLocked, null));
    }

    [Fact]
    public void CommittedTransitionAwaitingRestartComparesWithSavedSettings()
    {
        var draft = new Draft();
        draft.Saved[R.Backend] = "s3"; draft.Saved["s3.public_bucket"] = "new";
        var pending = new AdminStorageTransitionJob("j1", "completed", "restart_pending", 3, null, false);
        Assert.Equal("s3", R.LockedEffectiveBackend(draft, LockedLocal, pending));
        Assert.Equal("local", R.LockedEffectiveBackend(draft, LockedLocal, null));
        Assert.False(R.LocationChangePending(draft, LockedLocal, pending));
        Assert.True(R.LocationChangePending(draft, LockedLocal, null));
    }

    [Fact]
    public void UnknownLockStatusHoldsBackLocationsAndTheirCredentials()
    {
        Assert.Equal(["metadata.cache_images", "s3.public_region"], R.SafeKeysWhileStatusUnknown(["metadata.cache_images", "s3.public_region"]));
        Assert.Equal(["metadata.cache_images"], R.SafeKeysWhileStatusUnknown(["metadata.cache_images", "artwork.local_path", "s3.public_secret_key", "s3.public_region"]));
        Assert.Empty(R.SafeKeysWhileStatusUnknown(["s3.public_bucket"]));
    }

    [Fact]
    public void VisibilityFollowsBackendAndStagedEdits()
    {
        var draft = new Draft();
        Assert.False(R.ShowPublicStorage(draft, "local"));
        Assert.False(R.ShowPrivateStorage(draft, "local"));
        Assert.True(R.ShowPublicStorage(draft, "auto"));
        draft.Staged["s3.public_region"] = "x";
        Assert.True(R.ShowPublicStorage(draft, "local"));
        draft.Saved["s3.private_bucket"] = "kept";
        Assert.True(R.ShowPrivateStorage(draft, "local"));
        Assert.Equal("auto", R.SelectedBackend(draft, Unlocked));
        Assert.Equal("local", R.SelectedBackend(draft, LockedLocal));
        draft.Staged[R.Backend] = "s3";
        Assert.Equal("s3", R.SelectedBackend(draft, LockedLocal));
    }

    [Theory]
    [InlineData("presigned", 0)]
    [InlineData("public", 1)]
    [InlineData("cloudflare_token", 4)]
    public void AdvancedCountMatchesTheLinkMode(string mode, int count) => Assert.Equal(count, R.PublicAdvancedCount(mode));

    [Fact]
    public void DisplayedDefaultsDoNotStageValues()
    {
        Assert.Equal("presigned", R.DisplayUrlAuth(""));
        Assert.Equal("verify", R.DisplayTokenParam(""));
        Assert.Equal("10800", R.DisplayTokenTtl(""));
        Assert.Equal("public", R.DisplayUrlAuth("public"));
    }

    [Fact]
    public void LocalToS3PlanCopiesAvatarsOnlyWithAPrivateBucket()
    {
        var draft = new Draft();
        draft.Staged[R.Backend] = "s3"; draft.Staged["s3.public_bucket"] = "media";
        var plan = R.Plan(draft, LockedLocal, "s3", null, false);
        Assert.True(plan.PublicLocationChanging);
        Assert.True(plan.PrivateLocationChanging);
        Assert.False(plan.PrivateOnly);
        Assert.True(plan.TargetS3MissingPrivateBucket);
        Assert.True(plan.CopyPolicyUnavailable);
        draft.Staged["s3.private_bucket"] = "private";
        plan = R.Plan(draft, LockedLocal, "s3", null, false);
        Assert.False(plan.CopyPolicyUnavailable);
        Assert.True(plan.PrivateEndpointMissing);
        draft.Staged["s3.private_endpoint"] = "https://s3.example";
        Assert.False(R.Plan(draft, LockedLocal, "s3", null, false).PrivateEndpointMissing);
    }

    [Fact]
    public void UnreachableS3SourceAllowsOnlyStartFresh()
    {
        var draft = new Draft();
        draft.Saved["s3.private_bucket"] = "private";
        Assert.True(R.Plan(draft, LockedS3, "local", Health(false), false).SourceHealthUnavailable);
        Assert.True(R.Plan(draft, LockedS3, "local", null, true).SourceHealthUnavailable);
        Assert.False(R.Plan(draft, LockedS3, "local", Health(true), false).SourceHealthUnavailable);
    }

    [Fact]
    public void PrivateOnlyPlanUsesThePrivateWording()
    {
        var draft = new Draft();
        draft.Saved["s3.private_bucket"] = "old"; draft.Saved[R.Backend] = "s3"; draft.Saved["s3.public_bucket"] = "media";
        draft.Staged["s3.private_bucket"] = "new"; draft.Staged["s3.private_endpoint"] = "https://s3.example";
        var plan = R.Plan(draft, LockedS3, "s3", Health(true, true), false);
        Assert.True(plan.PrivateOnly);
        Assert.Equal(["web.admin.settings.storage.policy.privatePreserve", "web.admin.settings.storage.policy.startFresh", "web.admin.settings.storage.policy.privateMigrateAll"],
            R.PolicyRows(true, true).Select(row => row.TitleKey));
        Assert.Equal(["preserve_uploads", "start_fresh", "migrate_all"], R.PolicyRows(false, false).Select(row => row.Value));
        Assert.Equal(["web.admin.settings.storage.notes.metadataRetained", "web.admin.settings.storage.notes.privateOnly", "web.admin.settings.storage.notes.restartRequired"],
            R.NoteKeys(plan, "s3", true, "preserve_uploads"));
    }

    [Fact]
    public void TransitionValuesSendOnlyTheReviewedTargetAndStagedStorageKeys()
    {
        var draft = new Draft();
        draft.Staged["s3.public_bucket"] = "media"; draft.Staged["s3.public_secret_key"] = "synthetic-secret";
        draft.Staged["s3.private_bucket"] = "private"; draft.Staged["metadata.cache_images"] = "false";
        var toS3 = R.TransitionValues(draft, LockedLocal, "s3", "/ignored");
        Assert.Equal(new Dictionary<string, string>
        {
            ["artwork.storage_backend"] = "s3", ["s3.public_bucket"] = "media", ["s3.public_secret_key"] = "synthetic-secret", ["s3.private_bucket"] = "private",
        }, new Dictionary<string, string>(toS3));
        // Leaving S3 clears the private bucket on the server; staged private keys are not sent.
        Assert.Equal(new Dictionary<string, string> { ["artwork.storage_backend"] = "local", ["artwork.local_path"] = "/srv/art" },
            new Dictionary<string, string>(R.TransitionValues(draft, LockedS3, "local", "/srv/art")));
        // A local source keeps a staged private bucket on a local move.
        Assert.Equal(new Dictionary<string, string> { ["artwork.storage_backend"] = "local", ["artwork.local_path"] = "/srv/art", ["s3.private_bucket"] = "private" },
            new Dictionary<string, string>(R.TransitionValues(draft, LockedLocal, "local", "/srv/art")));
        var reset = R.KeysResetAfterTransition(draft, LockedS3, "local", ["artwork.storage_backend", "artwork.local_path"]).ToHashSet();
        Assert.Contains("s3.public_secret_key", reset);
        Assert.DoesNotContain("metadata.cache_images", reset);
    }

    [Fact]
    public void LocationNotesExplainTheirOwnLockState()
    {
        Assert.Equal("web.admin.settings.storage.s3.locationChangeFromLocal", R.LocationNoteKey(true, "local", true));
        Assert.Equal("web.admin.settings.storage.s3.locationChangeS3", R.LocationNoteKey(true, "s3", false));
        Assert.Equal("web.admin.settings.storage.s3.locationChangePrivate", R.LocationNoteKey(false, "local", false));
        Assert.Equal("web.admin.settings.storage.s3.locationUnlockedPublic", R.LocationNoteKey(true, null, false));
        Assert.Equal("web.admin.settings.storage.s3.locationUnlockedPrivate", R.LocationNoteKey(false, null, false));
    }

    [Fact]
    public void EveryStorageMessageKeyResolvesToCatalogText()
    {
        var keys = new List<string>();
        foreach (var phase in new string?[] { "queued", "checking_target", "copying", "verifying", "committing", "restart_pending", "completed", "failed", "canceled", null }) keys.Add(R.PhaseKey(phase));
        foreach (var category in new string?[] { "preparation_failed", "target_check_failed", "copy_failed", "verification_failed", "commit_failed", null }) keys.Add(R.FailureKey(category));
        foreach (var state in new string?[] { "running", "waiting_retry", "blocked", null }) { keys.Add(R.RecoveryTitleKey(state)); keys.Add(R.RecoveryMessageKey(state)); }
        foreach (var privateOnly in new[] { true, false }) foreach (var changing in new[] { true, false })
            foreach (var row in R.PolicyRows(privateOnly, changing)) { keys.Add(row.TitleKey); keys.Add(row.DescriptionKey); }
        keys.AddRange(["configure.native.storage.transitionUnknown", "configure.native.storage.cancelUnknown", "web.admin.settings.storage.reviewTransition",
            "web.admin.settings.storage.credentialStatusUnavailable", "web.admin.settings.storage.lockStatusUnavailable", "web.admin.settings.storage.dialog.queue"]);
        Assert.All(keys, key => Assert.NotEqual(key, AdminText.T(key)));
        Assert.Equal("1 object verified", AdminText.T("web.admin.settings.storage.verifiedObjects", AdminText.Count(1)));
        Assert.Equal("Storage transition queued (job-7).", AdminText.T("web.admin.settings.storage.transitionQueued", new { id = "job-7" }));
        Assert.Equal("Public bucket: media · https://s3.example", AdminText.T("web.admin.settings.storage.target.publicBucket", new { bucket = "media", endpoint = "https://s3.example" }));
    }
}
