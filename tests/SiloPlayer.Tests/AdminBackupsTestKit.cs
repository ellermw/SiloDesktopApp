using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Synthetic Backups contract bodies (official openapi at 92ee051) and a manual scheduler. No live data.</summary>
internal static class BackupJson
{
    internal const string BackupId = "20261115T040000Z-nightly";
    internal const string OtherBackupId = "20261114T040000Z-manual";
    internal const string UploadId = "0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b";
    internal const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string Sha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"; // SHA-256 of "test"

    internal static string Capabilities(string state = "available", bool upload = true, bool selective = true, string destinations = "\"default\",\"s3\"") =>
        $$"""{"state":"{{state}}","allowed":true,"destinations":[{{destinations}}],"encryption":true,"upload":{{Bool(upload)}},"max_upload_bytes":2147483648,"selective_restore":{{Bool(selective)}},"download":true,"revision":"r1"}""";

    internal static string Backup(string id = BackupId, string status = "complete", bool encrypted = false, string objects = "", bool keyMatches = true, string kind = "nightly") =>
        $$"""{"id":"{{id}}","kind":"{{kind}}","status":"{{status}}","created_at":"2026-11-15T04:00:00.000Z","size_bytes":4,"location":"/data/backups/{{id}}","blob_count":0,"missing_blobs":[],"missing_blobs_total":0,"restore_set_omitted":[],"objects":[{{(objects.Length > 0 ? objects : Object("db.dump", 4, Sha))}}],"encrypted":{{Bool(encrypted)}},"key_matches":{{Bool(keyMatches)}},"clean":true,"silo_version":"1.0.0"}""";

    internal static string Object(string name, long size, string sha) => $$"""{"name":"{{name}}","size_bytes":{{size}},"sha256":"{{sha}}"}""";

    internal static string List(string items = "", bool busy = false, string active = "", bool encrypted = false, bool keyConfirmed = true, string warnings = "", string lastRun = "") =>
        $$"""{"server_id":"server-1","destination":"default","destination_available":true,"encrypted":{{Bool(encrypted)}},"key_check":"abcd-ef01-2345-6789","key_confirmed":{{Bool(keyConfirmed)}},"enabled":true,"busy":{{Bool(busy)}},"keep":7,"items":[{{items}}],"other_server_ids":[],"warnings":[{{warnings}}]{{(active.Length > 0 ? ",\"active\":" + active : "")}}{{(lastRun.Length > 0 ? ",\"last_run\":" + lastRun : "")}}}""";

    internal static string Upload(string status = "uploading", long size = 3L << 20, long partSize = 1L << 20, long received = 0, string extra = "") =>
        $$"""{"id":"{{UploadId}}","status":"{{status}}","size_bytes":{{size}},"part_size":{{partSize}},"part_count":{{AdminBackupUploadMath.PartCount(size, partSize)}},"received_parts":{{received}},"received_bytes":{{Math.Min(size, received * partSize)}},"created_at":"2026-11-15T04:00:00.000Z","updated_at":"2026-11-15T04:00:00.000Z","location":"uploads/x"{{extra}}}""";

    internal static string Job(string id = "job-1", string state = "running", bool terminal = false, bool cancelable = true, string source = "", string report = "") =>
        $$"""{"id":"{{id}}","kind":"backup_restore","state":"{{state}}","terminal":{{Bool(terminal)}},"cancelable":{{Bool(cancelable)}},"created_at":"2026-11-15T05:00:00.000Z","library_ids":[],"artifact_size_bytes":0,"public_link_supported":false,"backup_restore_source":{{(source.Length > 0 ? source : "{\"backup_id\":\"" + BackupId + "\"}")}}{{(report.Length > 0 ? ",\"backup_restore_result\":" + report : "")}}}""";

    internal static string Report(string items = "", long restored = 1, long failed = 0, string extra = "") =>
        $$"""{"backup_id":"{{BackupId}}","library_map":[],"items":[{{items}}],"counts":{"restored":{{restored}},"unchanged":0,"left_out":0,"failed":{{failed}}},"interrupted":false{{extra}}}""";

    internal static string Item(string id, string kind = "settings_page", string status = "differs", bool selectable = true, string requires = "", string extra = "") =>
        $$"""{"id":"{{id}}","kind":"{{kind}}","label":"{{id}} label","status":"{{status}}","selectable":{{Bool(selectable)}},"requires":[{{requires}}],"restart_required":false,"external_service":false,"missing_blobs":[]{{extra}}}""";

    internal static string Profile(string id, bool primary = false, bool selectable = true, string target = "new") =>
        $$"""{"id":"{{id}}","name":"{{id}} name","status":"not_on_this_server","selectable":{{Bool(selectable)}},"match":"new","target":"{{target}}","is_primary":{{Bool(primary)}},"restricted":false,"libraries_left_out":0,"no_library_access":false,"has_pin":false,"data_types":[{"id":"watch_history","count":3},{"id":"preferences","count":1}]}""";

    internal static string Account(string id, string target, string profiles) => Item(id, "account", target == "new" ? "not_on_this_server" : "differs", true, "",
        $$""","account":{"username":"{{id}}","email":"{{id}}@example.invalid","role":"user","admin":false,"match":"new","target":"{{target}}","policy_differences":[],"libraries_left_out":0,"profile_room":5,"profiles":[{{profiles}}]}""");

    internal static string Section(string id, params string[] items) => $$"""{"id":"{{id}}","count":{{items.Length}},"items":[{{string.Join(",", items)}}]}""";

    internal static string RestoreSet(string sections, bool authenticated = true, string source = "\"backup_id\":\"" + BackupId + "\"", string digest = Digest) =>
        $$"""{{{source}},"digest":"{{digest}}","format_version":1,"server_id":"server-1","server_name":"Synthetic","silo_version":"1.0.0","created_at":"2026-11-15T04:00:00.000Z","same_server":true,"key_matches":true,"authenticated":{{Bool(authenticated)}},"missing_blobs":[],"omitted":[],"never_restored_settings":["server.id","s3."],"library_map":[],"sections":[{{sections}}]}""";

    internal static string Problem(string type, int status, string detail = "Synthetic problem.") =>
        $$"""{"type":"https://silo.dev/problems/{{type}}","title":"{{type}}","status":{{status}},"detail":"{{detail}}"}""";

    private static string Bool(bool value) => value ? "true" : "false";

    internal static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, string? location = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, status >= HttpStatusCode.BadRequest ? "application/problem+json" : "application/json") };
        if (location != null) response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }
}

internal sealed class BackupScheduler : IAdminScheduler
{
    private readonly List<(DateTimeOffset Due, TimeSpan Delay, Action Callback, Timer Token)> _timers = [];
    public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.Parse("2026-11-16T12:00:00Z");
    public IReadOnlyList<TimeSpan> Pending { get { lock (_timers) return _timers.Where(timer => !timer.Token.Disposed).Select(timer => timer.Delay).ToArray(); } }

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var token = new Timer();
        lock (_timers) _timers.Add((UtcNow + delay, delay, callback, token));
        return token;
    }

    /// <summary>Moves time forward and runs every due callback once, in order.</summary>
    public void Advance(TimeSpan by)
    {
        UtcNow += by;
        List<(DateTimeOffset Due, TimeSpan Delay, Action Callback, Timer Token)> due;
        lock (_timers)
        {
            due = _timers.Where(timer => timer.Due <= UtcNow).OrderBy(timer => timer.Due).ToList();
            foreach (var timer in due) _timers.Remove(timer);
        }
        foreach (var timer in due) if (!timer.Token.Disposed) timer.Callback();
    }

    internal sealed class Timer : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}

/// <summary>Waits briefly for asynchronous continuations in a controller under test.</summary>
internal static class Eventually
{
    internal static async Task True(Func<bool> condition, string label)
    {
        for (var attempt = 0; attempt < 400 && !condition(); attempt++) await Task.Delay(5);
        Assert.True(condition(), label);
    }
}
