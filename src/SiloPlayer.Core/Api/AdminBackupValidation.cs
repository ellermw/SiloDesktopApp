using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Fail-closed checks for every Backups response: required fields present, enums known,
/// sizes and counts non-negative, identifiers in their official shapes. A response that
/// fails is treated as invalid, never partially shown.
/// </summary>
public static class AdminBackupValidation
{
    private static readonly HashSet<string> States = ["available", "disabled", "not_configured", "unsupported"];
    private static readonly HashSet<string> Kinds = ["nightly", "manual", "before-restore", "before-upgrade"];
    private static readonly HashSet<string> Statuses = ["complete", "complete_with_warnings", "in_progress", "incomplete", "unverified", "unreadable"];
    private static readonly HashSet<string> Operations = ["backup", "delete", "restore", "upload"];
    private static readonly HashSet<string> LastRunStatuses = ["complete", "complete_with_warnings", "failed", "interrupted"];
    private static readonly HashSet<string> UploadStatuses = ["uploading", "checking", "complete", "rejected"];
    private static readonly HashSet<string> Sections = ["libraries", "plugins", "settings", "collections", "home_sections", "users"];
    private static readonly HashSet<string> ItemKinds = ["settings_page", "plugin", "library", "collection", "template_collection", "item_artwork", "branding", "home_section", "account"];
    private static readonly HashSet<string> ItemStatuses = ["on_this_server", "not_on_this_server", "differs", "folder_missing", "ambiguous", "needs_another_key"];
    private static readonly HashSet<string> ProfileStatuses = ["on_this_server", "not_on_this_server", "differs", "ambiguous"];
    private static readonly HashSet<string> Outcomes = ["restored", "unchanged", "left_out", "failed"];
    public static readonly IReadOnlyList<string> DataTypeIds = ["watch_history", "ratings", "favorites", "collections", "dismissals", "preferences", "notifications"];

    private static InvalidDataException Invalid(string what) => new("Invalid backup " + what + ".");
    private static void Check(bool condition, string what) { if (!condition) throw Invalid(what); }
    private static bool NonNegative(long? value) => value is >= 0;

    public static void Require(AdminBackupCapabilities value)
    {
        Check(value.State != null && States.Contains(value.State) && value.Allowed != null && value.Destinations != null &&
              value.Destinations.All(destination => destination is "default" or "s3") && value.Encryption != null && value.Upload != null &&
              value.MaxUploadBytes is >= 0 and <= AdminBackupIdentifiers.MaxUploadBytes && value.SelectiveRestore != null && value.Download != null &&
              value.Revision != null, "capabilities");
    }

    public static void Require(AdminBackupList value)
    {
        Check(value.ServerId != null && value.Destination is "default" or "s3" && value.DestinationAvailable != null && value.Encrypted != null &&
              value.KeyCheck != null && value.KeyConfirmed != null && value.Enabled != null && value.Busy != null && value.Keep is >= 1 and <= 60 &&
              value.Items != null && value.OtherServerIds != null && value.Warnings != null, "list");
        Check(value.Warnings!.All(warning => !string.IsNullOrEmpty(warning.Code) && warning.Message != null), "warning");
        if (value.Active is { } active)
            Check(active.Operation != null && Operations.Contains(active.Operation) && active.StartedAt != null && active.Progress is >= 0 and <= 100 &&
                  (active.BackupId == null || AdminBackupIdentifiers.IsBackupId(active.BackupId)) &&
                  (active.UploadId == null || AdminBackupIdentifiers.IsUploadId(active.UploadId)), "active operation");
        if (value.LastRun is { } last)
            Check(!string.IsNullOrEmpty(last.Id) && last.Kind != null && Kinds.Contains(last.Kind) && last.Status != null && LastRunStatuses.Contains(last.Status) &&
                  last.StartedAt != null, "last run");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.Items!)
        {
            Require(item);
            Check(ids.Add(item.Id!), "list identity");
        }
    }

    public static void Require(AdminBackup value)
    {
        Check(AdminBackupIdentifiers.IsBackupId(value.Id) && value.Kind != null && Kinds.Contains(value.Kind) && value.Status != null && Statuses.Contains(value.Status) &&
              value.CreatedAt != null && NonNegative(value.SizeBytes) && value.Location != null && NonNegative(value.BlobCount) && value.MissingBlobs != null &&
              NonNegative(value.MissingBlobsTotal) && value.RestoreSetOmitted != null && value.Objects != null && value.Encrypted != null &&
              value.KeyMatches != null && value.Clean != null, "item");
        Check(value.Objects!.All(entry => entry.Name != null && AdminBackupIdentifiers.ObjectNames.Contains(entry.Name) && NonNegative(entry.SizeBytes) &&
              entry.Sha256 is { Length: 64 } && entry.Sha256.All(Uri.IsHexDigit)), "stored object");
        Check(value.Objects!.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() == value.Objects!.Length, "stored objects");
        Check(value.MissingBlobs!.All(blob => blob.Key != null && blob.Source != null && blob.Store is "assets" or "operational"), "missing file");
        Check(value.RestoreSetOmitted!.All(Valid), "omitted item");
        Check(value.KeyCheck == null || AdminBackupIdentifiers.IsKeyCheck(value.KeyCheck), "key check");
    }

    private static bool Valid(AdminBackupOmitted omitted) => omitted.Section == "users" && omitted.Id != null && omitted.Label != null &&
        omitted.Reason is "too_large" or "export_failed" && omitted.Message != null;

    public static void Require(AdminBackupDownloadLink link, string scope)
    {
        Check(link.Name != null && AdminBackupIdentifiers.ObjectNames.Contains(link.Name) && NonNegative(link.SizeBytes) && !string.IsNullOrEmpty(link.Url) &&
              link.ExpiresAt != null, "download link");
        if (scope == "restore-set") Check(link.Name == "restore-set.tar", "restore set link");
    }

    public static void Require(AdminBackupUpload value)
    {
        Check(AdminBackupIdentifiers.IsUploadId(value.Id) && value.Status != null && UploadStatuses.Contains(value.Status) &&
              value.SizeBytes is >= 1 and <= AdminBackupIdentifiers.MaxUploadBytes &&
              value.PartSize is >= AdminBackupIdentifiers.MinPartSize and <= AdminBackupIdentifiers.MaxPartSize &&
              value.PartCount == AdminBackupUploadMath.PartCount(value.SizeBytes!.Value, value.PartSize!.Value) &&
              value.PartCount <= AdminBackupIdentifiers.MaxPartIndex + 1 && value.ReceivedParts is >= 0 && value.ReceivedParts <= value.PartCount &&
              value.ReceivedBytes is >= 0 && value.ReceivedBytes <= value.SizeBytes && value.CreatedAt != null && value.UpdatedAt != null && value.Location != null,
              "upload");
        Check(value.Digest == null || AdminBackupIdentifiers.IsDigest(value.Digest), "upload digest");
        if (value.Rejection is { } rejection) Check(rejection.Reason != null && rejection.Message != null, "upload rejection");
        if (value.Source is { } source) Check(source.ServerId != null && source.ServerName != null && source.SiloVersion != null && source.CreatedAt != null, "upload source");
        if (value.Status == "complete") Check(AdminBackupIdentifiers.IsDigest(value.Digest), "complete upload");
    }

    public static void Require(AdminBackupRestoreSet value, AdminBackupRestoreSourceRef source)
    {
        Check(AdminBackupIdentifiers.IsDigest(value.Digest) && value.FormatVersion != null && value.ServerId != null && value.ServerName != null &&
              value.SiloVersion != null && value.CreatedAt != null && value.SameServer != null && value.KeyMatches != null && value.Authenticated != null &&
              value.MissingBlobs != null && value.Omitted != null && value.NeverRestoredSettings != null && value.LibraryMap != null && value.Sections != null,
              "restore set");
        // The preview must answer for the source this page opened.
        Check(source.Upload ? value.UploadId == source.Id && value.BackupId == null : value.BackupId == source.Id && value.UploadId == null, "restore set source");
        Check(value.Omitted!.All(Valid), "restore set omission");
        Check(value.LibraryMap!.All(map => !string.IsNullOrEmpty(map.BackupId) && map.Name != null && map.Status != null && map.Selectable != null), "library map");
        var sections = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in value.Sections!)
        {
            Check(section.Id != null && Sections.Contains(section.Id) && sections.Add(section.Id) && NonNegative(section.Count) && section.Items != null, "section");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in section.Items!) { Require(item); Check(ids.Add(item.Id!), "item identity"); }
        }
    }

    private static void Require(AdminBackupRestoreItem item)
    {
        Check(!string.IsNullOrEmpty(item.Id) && item.Kind != null && ItemKinds.Contains(item.Kind) && item.Label != null && item.Status != null &&
              ItemStatuses.Contains(item.Status) && item.Selectable != null && item.Requires != null && item.RestartRequired != null &&
              item.ExternalService != null && item.MissingBlobs != null, "restore item");
        foreach (var setting in item.Settings ?? [])
            Check(setting.Key != null && setting.Class is "portable" or "external_service" && setting.Status != null && setting.Masked != null &&
                  setting.BackupValue != null && setting.CurrentValue != null && setting.RestartRequired != null, "restore setting");
        if (item.Plugin is { } plugin) Check(plugin.Version != null && plugin.Enabled != null && plugin.ConfigKeys != null && plugin.ConfigRestorable != null, "plugin");
        if (item.Library is { } library) Check(library.Type != null && library.Paths != null, "library");
        // Change lists are additive enums: a code this client does not know is shown as sent, never refused. Like the official client, a null
        // entry is skipped and a negative or absent missing_collection_items reads as 0; the server emits neither.
        if (item.Collection is { } collection)
            Check(NonNegative(collection.Items) && NonNegative(collection.MissingItems) && NonNegative(collection.UnknownItems), "collection");
        if (item.HomeSection is { } section)
            Check(section.Scope is "home" or "library" && section.SectionType != null, "home section");
        if (item.Account is { } account)
        {
            Check(account.Username != null && account.Email != null && account.Role is "admin" or "user" && account.Admin != null && account.Match != null &&
                  !string.IsNullOrEmpty(account.Target) && account.PolicyDifferences != null && NonNegative(account.LibrariesLeftOut) &&
                  NonNegative(account.ProfileRoom) && account.Profiles != null, "account");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in account.Profiles!)
            {
                Check(!string.IsNullOrEmpty(profile.Id) && ids.Add(profile.Id) && profile.Name != null && profile.Status != null &&
                      ProfileStatuses.Contains(profile.Status) && profile.Selectable != null && profile.Match != null && !string.IsNullOrEmpty(profile.Target) &&
                      profile.IsPrimary != null && profile.Restricted != null && NonNegative(profile.LibrariesLeftOut) && profile.NoLibraryAccess != null &&
                      profile.HasPin != null && profile.DataTypes != null, "profile");
                Check(profile.DataTypes!.All(data => data.Id != null && DataTypeIds.Contains(data.Id) && NonNegative(data.Count) && (data.Pending == null || data.Pending >= 0)), "data type");
            }
        }
    }

    public static void Require(AdminBackupRestoreJob job)
    {
        Check(AdminBackupIdentifiers.IsJobId(job.Id) && !string.IsNullOrEmpty(job.Kind) && !string.IsNullOrEmpty(job.State) && job.Terminal != null &&
              job.Cancelable != null && job.CreatedAt != null, "job");
        if (job.Progress is { } progress) Check(NonNegative(progress.Current) && NonNegative(progress.Total), "job progress");
        if (job.BackupRestoreResult is { } report)
        {
            Check(report.LibraryMap != null && report.Items != null && report.Counts is { } counts && NonNegative(counts.Restored) && NonNegative(counts.Unchanged) &&
                  NonNegative(counts.LeftOut) && NonNegative(counts.Failed) && report.Interrupted != null, "restore report");
            Check(report.Items!.All(item => item.Section != null && Sections.Contains(item.Section) && item.Id != null && item.Kind != null && item.Label != null &&
                  item.Outcome != null && Outcomes.Contains(item.Outcome) && item.RestartRequired != null && item.RestartKeys != null && item.Notes != null &&
                  item.MissingBlobs != null), "report item");
            Check(report.Items!.All(item => item.Account == null || item.Account.Created != null && item.Account.Profiles != null && item.Account.Unmatched != null &&
                  item.Account.Hidden != null && item.Account.Profiles.All(profile => profile.Id != null && profile.Label != null && profile.Created != null &&
                  profile.Outcome is "restored" or "unchanged" or "left_out" && profile.Notes != null && profile.Data != null &&
                  profile.Data.All(data => data.Type != null && DataTypeIds.Contains(data.Type) && NonNegative(data.Added) && NonNegative(data.Updated) &&
                      NonNegative(data.Unchanged) && NonNegative(data.LeftOut) && data.Notes != null))), "report account");
        }
    }

    /// <summary>Whether a backup_restore job names exactly this source (its queued source, or its report).</summary>
    public static bool NamesSource(AdminBackupRestoreJob job, AdminBackupRestoreSourceRef source)
    {
        var backupId = job.BackupRestoreSource?.BackupId ?? job.BackupRestoreResult?.BackupId;
        var uploadId = job.BackupRestoreSource?.UploadId ?? job.BackupRestoreResult?.UploadId;
        return source.Upload ? uploadId == source.Id : backupId == source.Id;
    }

    /// <summary>
    /// Server prose shown to the admin: redacted of credential-looking text and of any value the
    /// admin typed this attempt, bounded in length. Provider diagnostics are never echoed raw.
    /// </summary>
    public static string SafeServerText(string text, params string?[] secrets)
    {
        // The typed values are replaced on the raw text first: a secret holding a character Plain drops or folds (a tab, a direction mark)
        // would no longer match once the text was cleaned.
        var raw = text;
        foreach (var secret in secrets.Where(value => !string.IsNullOrEmpty(value) && value.Length >= 4).Distinct(StringComparer.Ordinal).OrderByDescending(value => value!.Length))
            raw = raw.Replace(secret!, "<redacted>", StringComparison.Ordinal);
        var safe = LocalLog.RedactDiagnosticText(AdminBackupText.Plain(raw, int.MaxValue));
        return safe.Length > 500 ? safe[..500] + "…" : safe;
    }
}
