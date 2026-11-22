using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Read-only view of the draft the storage rules need (the controller's form, or a test double).</summary>
public interface IAdminStorageDraft
{
    string Get(string key);
    string GetPersisted(string key);
    bool IsDirty(string key);
}

/// <summary>What the transition review shows and sends, derived exactly as official StorageSettings.tsx does.</summary>
public sealed record AdminStorageTransitionPlan(
    bool PublicLocationChanging, bool PrivateLocationChanging, bool PrivateOnly,
    bool SourceHealthUnavailable, bool TargetS3MissingPrivateBucket, bool PrivateEndpointMissing)
{
    public bool CopyPolicyUnavailable => SourceHealthUnavailable || TargetS3MissingPrivateBucket;
}

/// <summary>
/// Pure storage-location rules ported from official <c>web/src/pages/admin-settings/StorageSettings.tsx</c>
/// (silo-server 8f93594ce19723dde8a204be21ba59a24ef06e3b). No I/O and no secrets beyond the draft it is given.
/// </summary>
public static partial class AdminStorageRules
{
    public const string Backend = "artwork.storage_backend";
    public const string LocalPath = "artwork.local_path";
    public const string CacheImages = "metadata.cache_images";
    public const string UrlAuth = "s3.public_url_auth";
    public const string TokenParam = "s3.public_token_param";
    public const string TokenTtl = "s3.public_token_ttl";
    public const string LocalPathExample = "/var/lib/silo/artwork";

    public static IReadOnlyList<string> PublicS3Keys { get; } = Array.AsReadOnly(new[]
    {
        "s3.public_endpoint", "s3.public_region", "s3.public_path_style", "s3.public_bucket", "s3.public_key_prefix",
        "s3.public_access_key", "s3.public_secret_key", "s3.public_read_endpoint", UrlAuth, "s3.public_token_secret", TokenParam, TokenTtl,
    });
    /// <summary>Shown only for the link modes that use them, behind public storage's Advanced disclosure.</summary>
    public static IReadOnlyList<string> PublicS3AdvancedKeys { get; } = Array.AsReadOnly(new[] { "s3.public_read_endpoint", "s3.public_token_secret", TokenParam, TokenTtl });
    public static IReadOnlyList<string> PublicS3IdentityKeys { get; } = Array.AsReadOnly(new[] { "s3.public_endpoint", "s3.public_bucket", "s3.public_key_prefix" });
    public static IReadOnlyList<string> PrivateS3Keys { get; } = Array.AsReadOnly(new[]
    {
        "s3.private_endpoint", "s3.private_region", "s3.private_path_style", "s3.private_bucket", "s3.private_key_prefix",
        "s3.private_access_key", "s3.private_secret_key",
    });
    public static IReadOnlyList<string> PrivateS3IdentityKeys { get; } = Array.AsReadOnly(new[] { "s3.private_endpoint", "s3.private_bucket", "s3.private_key_prefix" });
    public static IReadOnlyList<string> S3IdentityKeys { get; } = Array.AsReadOnly(PublicS3IdentityKeys.Concat(PrivateS3IdentityKeys).ToArray());
    public static IReadOnlyList<string> StorageGroupKeys { get; } = Array.AsReadOnly(new[] { Backend, LocalPath, CacheImages });
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(StorageGroupKeys.Concat(PublicS3Keys).Concat(PrivateS3Keys).ToArray());
    /// <summary>Write-only credentials: never read back, never echoed and erased after an acknowledged save.</summary>
    public static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly(new[]
    {
        "s3.public_access_key", "s3.public_secret_key", "s3.public_token_secret", "s3.private_access_key", "s3.private_secret_key",
    });
    public static IReadOnlySet<string> LocationKeys { get; } = new HashSet<string>(new[] { Backend, LocalPath }.Concat(S3IdentityKeys), StringComparer.Ordinal);
    public static IReadOnlySet<string> TransitionKeys { get; } = new HashSet<string>(new[] { Backend, LocalPath }.Concat(PublicS3Keys).Concat(PrivateS3Keys), StringComparer.Ordinal);

    [GeneratedRegex(@"^([a-z][a-z0-9+.-]*)://([^/?#]+)(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EndpointParts();

    /// <summary>Official cleanPath (Go path.Clean semantics) from settingsPathDefaults.ts.</summary>
    public static string CleanPath(string path)
    {
        var rooted = path.StartsWith('/');
        var resolved = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (resolved.Count > 0 && resolved[^1] != "..") resolved.RemoveAt(resolved.Count - 1);
                else if (!rooted) resolved.Add("..");
                continue;
            }
            resolved.Add(segment);
        }
        var cleaned = (rooted ? "/" : "") + string.Join('/', resolved);
        return cleaned == "" ? "." : cleaned;
    }

    /// <summary>
    /// Compares location fields the way the server names stores: endpoint scheme/host and bucket ignore case, a key
    /// prefix ignores its slashes; explicit ports and endpoint paths (including a trailing slash) are preserved.
    /// </summary>
    public static string NormalizeLocationValue(string key, string raw)
    {
        var value = JsTrim(raw);
        if (key == LocalPath) return value.StartsWith('/') ? CleanPath(value) : value;
        if (key.EndsWith("_key_prefix", StringComparison.Ordinal)) return value.Trim('/');
        if (key.EndsWith("_bucket", StringComparison.Ordinal)) return value.ToLowerInvariant();
        if (key.EndsWith("_endpoint", StringComparison.Ordinal))
        {
            var parts = EndpointParts().Match(value);
            if (!parts.Success) return value.ToLowerInvariant();
            var authority = parts.Groups[2].Value;
            var hostStart = authority.LastIndexOf('@') + 1;
            return parts.Groups[1].Value.ToLowerInvariant() + "://" + authority[..hostStart] + authority[hostStart..].ToLowerInvariant() + parts.Groups[3].Value;
        }
        return value;
    }

    /// <summary>"s3" when S3 is chosen, or Automatic with a public bucket; otherwise "local".</summary>
    public static string EffectiveBackend(string backend, string publicBucket)
    {
        var selected = JsTrim(backend).ToLowerInvariant();
        return selected == "s3" || (selected != "local" && JsTrim(publicBucket) != "") ? "s3" : "local";
    }

    /// <summary>What Backend shows: the running backend once storage is locked, unless a different choice is staged.</summary>
    public static string SelectedBackend(IAdminStorageDraft draft, AdminArtworkStorageStatus status) =>
        status.Locked && status.Backend.Length > 0 && !draft.IsDirty(Backend) ? status.Backend : Or(draft.Get(Backend), "auto");

    /// <summary>The recorded location edits are compared with: the running backend, or the committed one awaiting restart.</summary>
    public static string LockedEffectiveBackend(IAdminStorageDraft draft, AdminArtworkStorageStatus status, AdminStorageTransitionJob? latest) =>
        latest?.Phase == "restart_pending" ? EffectiveBackend(draft.GetPersisted(Backend), draft.GetPersisted("s3.public_bucket"))
            : status.Backend == "s3" ? "s3" : "local";

    public static string DraftEffectiveBackend(IAdminStorageDraft draft) => EffectiveBackend(draft.Get(Backend), draft.Get("s3.public_bucket"));

    public static bool PublicBackendChangePending(IAdminStorageDraft draft, AdminArtworkStorageStatus status, AdminStorageTransitionJob? latest) =>
        status.Locked && DraftEffectiveBackend(draft) != LockedEffectiveBackend(draft, status, latest) && DraftEffectiveBackend(draft) == "s3";

    /// <summary>A typed-then-reverted value stays dirty, so compare with what is stored before treating it as a new location.</summary>
    public static bool LocationKeyChanged(IAdminStorageDraft draft, string key)
    {
        if (!draft.IsDirty(key)) return false;
        // With no private bucket before or after, a leftover endpoint or prefix names no location and saves directly.
        if (key != "s3.private_bucket" && key.StartsWith("s3.private_", StringComparison.Ordinal) &&
            JsTrim(draft.Get("s3.private_bucket")) == "" && JsTrim(draft.GetPersisted("s3.private_bucket")) == "")
            return false;
        return NormalizeLocationValue(key, draft.Get(key)) != NormalizeLocationValue(key, draft.GetPersisted(key));
    }

    /// <summary>Whether Save must route through a managed transition review rather than a settings write.</summary>
    public static bool LocationChangePending(IAdminStorageDraft draft, AdminArtworkStorageStatus status, AdminStorageTransitionJob? latest)
    {
        var privateLocked = status.Locked || status.PrivateLocked;
        if (status.Locked)
        {
            var locked = LockedEffectiveBackend(draft, status, latest);
            if (DraftEffectiveBackend(draft) != locked) return true;
            IEnumerable<string> keys = locked == "s3" ? S3IdentityKeys : new[] { LocalPath }.Concat(PrivateS3IdentityKeys);
            return keys.Any(key => LocationKeyChanged(draft, key));
        }
        return privateLocked && PrivateS3IdentityKeys.Any(key => LocationKeyChanged(draft, key));
    }

    public static bool CurrentSourceIsS3(AdminArtworkStorageStatus status) => status.Backend == "s3";
    public static bool CurrentSourceUsesS3(IAdminStorageDraft draft, AdminArtworkStorageStatus status) =>
        CurrentSourceIsS3(status) || JsTrim(draft.GetPersisted("s3.private_bucket")) != "";

    public static int CountDirty(IAdminStorageDraft draft, IEnumerable<string> keys) => keys.Count(draft.IsDirty);

    /// <summary>Public storage serves only S3 and Automatic, so Local disk hides it unless one of its fields holds an edit.</summary>
    public static bool ShowPublicStorage(IAdminStorageDraft draft, string selectedBackend) =>
        selectedBackend != "local" || CountDirty(draft, PublicS3Keys) > 0;

    /// <summary>Local disk shows private storage only while a saved or staged private bucket holds that data.</summary>
    public static bool ShowPrivateStorage(IAdminStorageDraft draft, string selectedBackend) =>
        selectedBackend != "local" || JsTrim(draft.GetPersisted("s3.private_bucket")) != "" || CountDirty(draft, PrivateS3Keys) > 0;

    /// <summary>Official <c>advancedCount</c>: CDN address for non-presigned links, plus three Cloudflare token settings.</summary>
    public static int PublicAdvancedCount(string urlAuth) => (urlAuth != "presigned" ? 1 : 0) + (urlAuth == "cloudflare_token" ? 3 : 0);

    /// <summary>Official <c>linkModeUses</c>: the CDN address serves every link mode but signed links; the token settings belong to Cloudflare token links.</summary>
    public static bool LinkModeUses(string urlAuth, string advancedKey) =>
        advancedKey == "s3.public_read_endpoint" ? urlAuth != "presigned" : urlAuth == "cloudflare_token";

    /// <summary>
    /// Official <c>showsAdvanced</c> (#4696): a row the selected link mode does not use stays in view while it holds an unsaved edit, so Save
    /// never writes a value the admin cannot see. The draft keeps its value, secret included, and is not cleared by the mode change.
    /// </summary>
    public static bool ShowsAdvanced(IAdminStorageDraft draft, string advancedKey) =>
        LinkModeUses(DisplayUrlAuth(draft.Get(UrlAuth)), advancedKey) || draft.IsDirty(advancedKey);

    /// <summary>Official <c>keptForEdit</c>: the row is shown only because it holds an edit, so it carries the unsaved-change mark.</summary>
    public static bool KeptForEdit(IAdminStorageDraft draft, string advancedKey) =>
        !LinkModeUses(DisplayUrlAuth(draft.Get(UrlAuth)), advancedKey) && draft.IsDirty(advancedKey);

    /// <summary>Official <c>advancedCount</c> after #4696: the rows the Advanced disclosure actually shows.</summary>
    public static int PublicAdvancedCount(IAdminStorageDraft draft) => PublicS3AdvancedKeys.Count(key => ShowsAdvanced(draft, key));

    public static string DisplayUrlAuth(string value) => Or(value, "presigned");
    public static string DisplayTokenParam(string value) => Or(value, "verify");
    public static string DisplayTokenTtl(string value) => Or(value, "10800");

    /// <summary>Which location-change explanation the S3 group shows while one of its identity fields is dirty.</summary>
    public static string LocationNoteKey(bool isPublic, string? lockedBackend, bool publicBackendChangePending) =>
        publicBackendChangePending || lockedBackend == "s3" || (!isPublic && lockedBackend != null)
            ? publicBackendChangePending ? "web.admin.settings.storage.s3.locationChangeFromLocal"
                : !isPublic && lockedBackend != "s3" ? "web.admin.settings.storage.s3.locationChangePrivate"
                : "web.admin.settings.storage.s3.locationChangeS3"
            : isPublic ? "web.admin.settings.storage.s3.locationUnlockedPublic" : "web.admin.settings.storage.s3.locationUnlockedPrivate";

    public static AdminStorageTransitionPlan Plan(IAdminStorageDraft draft, AdminArtworkStorageStatus status, string targetBackend, AdminStorageSourceHealth? sourceHealth, bool sourceHealthFailed, string? reviewedLocalPath = null)
    {
        var currentS3 = CurrentSourceIsS3(status);
        // The review's path is submitted even when the form itself has no staged path edit.
        var localPathChanging = reviewedLocalPath == null ? LocationKeyChanged(draft, LocalPath) :
            NormalizeLocationValue(LocalPath, reviewedLocalPath) != NormalizeLocationValue(LocalPath, draft.GetPersisted(LocalPath));
        var persistedPrivate = JsTrim(draft.GetPersisted("s3.private_bucket")) != "";
        var publicChanging = targetBackend != (currentS3 ? "s3" : "local") ||
            (targetBackend == "s3" && PublicS3IdentityKeys.Any(key => LocationKeyChanged(draft, key))) ||
            (targetBackend == "local" && localPathChanging);
        // Avatars, diagnostics and job artifacts move when the private bucket changes, when S3 is disabled while a
        // private bucket holds them, and when a local install without one moves to S3.
        var privateChanging = PrivateS3IdentityKeys.Any(key => LocationKeyChanged(draft, key)) ||
            (targetBackend == "local" && currentS3 && persistedPrivate) ||
            (targetBackend == "local" && !currentS3 && !persistedPrivate && localPathChanging) ||
            (targetBackend == "s3" && !currentS3 && !persistedPrivate);
        var sourceUsesS3 = CurrentSourceUsesS3(draft, status);
        var sourceUnavailable = sourceUsesS3 && (sourceHealth?.Reachable == false || sourceHealthFailed);
        var mayHaveAvatars = !currentS3 || sourceHealth?.PrivateConfigured == true || persistedPrivate;
        var missingPrivate = targetBackend == "s3" && mayHaveAvatars && JsTrim(draft.Get("s3.private_bucket")) == "";
        // Leaving S3 discards staged private S3 values instead of submitting them.
        var endpointMissing = !(targetBackend == "local" && currentS3) &&
            JsTrim(draft.Get("s3.private_bucket")) != "" && JsTrim(draft.Get("s3.private_endpoint")) == "";
        return new(publicChanging, privateChanging, privateChanging && !publicChanging, sourceUnavailable, missingPrivate, endpointMissing);
    }

    /// <summary>
    /// The exact transition request values (official <c>handleStorageTransition</c>). Leaving S3 clears the private
    /// bucket on the server, so a local target sends staged private keys only when the source is local.
    /// Environment-owned keys are excluded even when the value comes from the review rather than a dirty field.
    /// </summary>
    public static IReadOnlyDictionary<string, string> TransitionValues(IAdminStorageDraft draft, AdminArtworkStorageStatus status, string targetBackend, string localPath,
        IEnumerable<string>? managedByEnv = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { [Backend] = targetBackend };
        if (targetBackend == "local")
        {
            values[LocalPath] = localPath;
            if (!CurrentSourceIsS3(status))
                foreach (var key in PrivateS3Keys.Where(draft.IsDirty)) values[key] = draft.Get(key);
        }
        else foreach (var key in PublicS3Keys.Concat(PrivateS3Keys).Where(draft.IsDirty)) values[key] = draft.Get(key);
        foreach (var key in managedByEnv ?? []) values.Remove(key);
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(values);
    }

    /// <summary>Keys a successful local-from-S3 transition also resets (staged S3 edits no longer apply).</summary>
    public static IEnumerable<string> KeysResetAfterTransition(IAdminStorageDraft draft, AdminArtworkStorageStatus status, string targetBackend, IEnumerable<string> sent)
    {
        var reset = new HashSet<string>(sent, StringComparer.Ordinal);
        if (targetBackend == "local" && CurrentSourceIsS3(status)) reset.UnionWith(PublicS3Keys.Concat(PrivateS3Keys).Where(draft.IsDirty));
        return reset;
    }

    /// <summary>
    /// Keys a Save may write while storage lock status is unknown: a location edit may need a transition, which
    /// also carries its storage credentials, so all of them are held back together.
    /// </summary>
    public static IReadOnlyList<string> SafeKeysWhileStatusUnknown(IEnumerable<string> dirtyKeys)
    {
        var dirty = dirtyKeys.ToArray();
        var held = dirty.Any(LocationKeys.Contains) ? TransitionKeys : LocationKeys;
        return dirty.Where(key => !held.Contains(key)).ToArray();
    }

    public static string PhaseKey(string? phase) => phase switch
    {
        "queued" => "web.admin.settings.storage.phase.queued",
        "checking_target" => "web.admin.settings.storage.phase.checkingTarget",
        "copying" => "web.admin.settings.storage.phase.copying",
        "verifying" => "web.admin.settings.storage.phase.verifying",
        "committing" => "web.admin.settings.storage.phase.committing",
        "restart_pending" => "web.admin.settings.storage.phase.restartPending",
        "completed" => "web.admin.settings.storage.phase.completed",
        "failed" => "web.admin.settings.storage.phase.failed",
        "canceled" => "web.admin.settings.storage.phase.canceled",
        _ => "web.admin.settings.storage.phase.preparing",
    };

    public static string FailureKey(string? category) => category switch
    {
        "preparation_failed" => "web.admin.settings.storage.failure.preparation",
        "target_check_failed" => "web.admin.settings.storage.failure.targetCheck",
        "copy_failed" => "web.admin.settings.storage.failure.copy",
        "verification_failed" => "web.admin.settings.storage.failure.verification",
        "commit_failed" => "web.admin.settings.storage.failure.commit",
        _ => "web.admin.settings.storage.failure.unknown",
    };

    public static string RecoveryTitleKey(string? state) => state switch
    {
        "running" => "web.admin.settings.storage.recovery.titleRunning",
        "waiting_retry" => "web.admin.settings.storage.recovery.titleWaitingRetry",
        "blocked" => "web.admin.settings.storage.recovery.titleBlocked",
        _ => "web.admin.settings.storage.recovery.titlePending",
    };

    public static string RecoveryMessageKey(string? state) => state switch
    {
        "running" => "web.admin.settings.storage.recovery.messageRunning",
        "waiting_retry" => "web.admin.settings.storage.recovery.messageWaitingRetry",
        "blocked" => "web.admin.settings.storage.recovery.messageBlocked",
        _ => "web.admin.settings.storage.recovery.messagePending",
    };

    /// <summary>Policy rows in their official order: (value, title key, description key).</summary>
    public static IReadOnlyList<(string Value, string TitleKey, string DescriptionKey)> PolicyRows(bool privateOnly, bool privateChanging) => privateOnly
        ? new (string, string, string)[]
        {
            ("preserve_uploads", "web.admin.settings.storage.policy.privatePreserve", "web.admin.settings.storage.policy.privatePreserveDescription"),
            ("start_fresh", "web.admin.settings.storage.policy.startFresh", "web.admin.settings.storage.policy.privateStartFreshDescription"),
            ("migrate_all", "web.admin.settings.storage.policy.privateMigrateAll", "web.admin.settings.storage.policy.privateMigrateAllDescription"),
        }
        : new (string, string, string)[]
        {
            ("preserve_uploads", "web.admin.settings.storage.policy.preserve", privateChanging ? "web.admin.settings.storage.policy.preserveWithAvatarsDescription" : "web.admin.settings.storage.policy.preserveDescription"),
            ("start_fresh", "web.admin.settings.storage.policy.startFresh", privateChanging ? "web.admin.settings.storage.policy.startFreshWithAvatarsDescription" : "web.admin.settings.storage.policy.startFreshDescription"),
            ("migrate_all", "web.admin.settings.storage.policy.migrateAll", "web.admin.settings.storage.policy.migrateAllDescription"),
        };

    /// <summary>The "Before you continue" bullet keys in their official order.</summary>
    public static IReadOnlyList<string> NoteKeys(AdminStorageTransitionPlan plan, string targetBackend, bool currentSourceIsS3, string policy)
    {
        var notes = new List<string> { "web.admin.settings.storage.notes.metadataRetained" };
        if (!plan.PrivateOnly) { notes.Add("web.admin.settings.storage.notes.backfill"); notes.Add("web.admin.settings.storage.notes.sidecar"); }
        notes.Add(targetBackend == "local" && currentSourceIsS3
            ? policy switch
            {
                "start_fresh" => "web.admin.settings.storage.notes.toLocalStartFresh",
                "preserve_uploads" => "web.admin.settings.storage.notes.toLocalPreserve",
                _ => "web.admin.settings.storage.notes.toLocalMigrateAll",
            }
            : plan.PrivateOnly ? "web.admin.settings.storage.notes.privateOnly"
            : plan.PrivateLocationChanging ? "web.admin.settings.storage.notes.privateChanging"
            : "web.admin.settings.storage.notes.privateUnchanged");
        notes.Add("web.admin.settings.storage.notes.restartRequired");
        return notes;
    }

    /// <summary>JavaScript <c>String.prototype.trim</c>: Unicode white space plus BOM.</summary>
    public static string JsTrim(string? value) => (value ?? "").Trim().Trim('﻿').Trim();
    private static string Or(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;
}
