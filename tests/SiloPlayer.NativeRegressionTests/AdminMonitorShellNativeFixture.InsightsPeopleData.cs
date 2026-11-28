using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// Synthetic People, Devices and Bandwidth reads (official c59798116 schema) for the Insights tabs and the person panel: 75 accounts that
/// page 50 at a time, a client matrix whose rows depend on the sample floor, a day grid with quiet days, and nodes that share a name. No
/// real data. A case turns a flag on and resets it.
/// </summary>
internal static partial class AdminMonitorShellNativeFixture
{
    private static class InsightsPeopleData
    {
        private const int AccountCount = 75;

        private static string Iso(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        private static string Param(string query, string name) =>
            Regex.Match(query, "[?&]" + name + "=([^&]*)") is { Success: true } match ? Uri.UnescapeDataString(match.Groups[1].Value) : "";

        private static (int Days, string Unit) Window(string query) => Param(query, "range") switch
        {
            "7d" => (7, "day"), "90d" => (90, "week"), "1y" => (364, "week"), "all" => (760, "month"), _ => (30, "day"),
        };

        private static JsonObject Coverage(DateTimeOffset now, bool empty) => new()
        {
            ["measured_sessions"] = empty ? 0 : 400, ["oldest_history_at"] = Iso(now.AddDays(-700)), ["oldest_measured_at"] = Iso(now.AddDays(-600)),
            ["oldest_metric_at"] = Iso(now.AddDays(-600)), ["oldest_route_facts_at"] = Iso(now.AddDays(-500)), ["route_facts_sessions"] = empty ? 0 : 380, ["total_sessions"] = empty ? 0 : 420,
        };

        private static JsonObject Count(string key, long sessions) => new() { ["key"] = key, ["sessions"] = sessions };

        // ── People ───────────────────────────────────────────

        /// <summary>The account list the page's account picker reads: the same names the People rows carry.</summary>
        public static string UsersPage() => new JsonObject
        {
            ["items"] = new JsonArray(Accounts(DateTimeOffset.UtcNow).Select(account => (JsonNode)new JsonObject { ["id"] = account.Id.ToString(CultureInfo.InvariantCulture), ["username"] = account.Name }).ToArray()),
            ["page"] = new JsonObject { ["has_more"] = false },
        }.ToJsonString();

        private sealed record Account(int Id, string Name, bool Live, int Profiles, string? Client, long Plays, long Watch, double Direct, double Completion, DateTimeOffset? Seen);

        private static Account[] Accounts(DateTimeOffset now) => Enumerable.Range(1, AccountCount).Select(id => new Account(
            id,
            id == 4 ? "a-remarkably-long-account-username-that-must-not-break-the-table-layout" : id == 1 ? "synthetic-admin" : "viewer-" + id.ToString("00", CultureInfo.InvariantCulture),
            id is 2 or 5 or 40,
            id % 3 == 0 ? 3 : id % 3 == 1 ? 1 : 0,
            id % 5 == 0 ? null : id % 2 == 0 ? "Silo Desktop · Windows" : "Silo for Roku · Roku OS",
            420 - id * 5, (420 - id * 5) * 2900L, (id % 10 + 1) / 11d, id % 4 == 0 ? 0.04 : 0.35 + id % 6 * 0.1,
            id % 7 == 0 ? null : id == 2 ? now.AddMinutes(-3) : now.AddHours(-id * 7))).ToArray();

        public static string People(DateTimeOffset now, string query, InsightsTabsState state)
        {
            var sort = Param(query, "sort") is { Length: > 0 } requested ? requested : "watch_time";
            var descending = Param(query, "dir") != "asc";
            var search = Param(query, "search");
            var limit = int.TryParse(Param(query, "limit"), out var parsedLimit) ? parsedLimit : 50;
            var offset = int.TryParse(Param(query, "offset"), out var parsedOffset) ? parsedOffset : 0;
            var all = state.Empty ? [] : Accounts(now).Where(account => search.Length == 0 || account.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
            Func<Account, double> key = sort switch
            {
                "plays" => account => account.Plays,
                "direct_pct" => account => account.Direct,
                "completion_pct" => account => account.Completion,
                "last_seen" => account => account.Seen?.ToUnixTimeSeconds() ?? 0,
                _ => account => account.Watch,
            };
            var ordered = (descending ? all.OrderByDescending(key) : all.OrderBy(key)).ThenBy(account => account.Id).ToArray();
            var accounts = new JsonArray(ordered.Skip(offset).Take(limit).Select(account => (JsonNode)AccountJson(account, now)).ToArray());
            var days = Window(query).Days;
            var end = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
            return new JsonObject
            {
                ["accounts"] = accounts, ["active_accounts"] = state.Empty ? 0 : AccountCount, ["active_profiles"] = state.Empty ? 0 : 118, ["coverage"] = Coverage(now, state.Empty),
                ["from"] = Iso(end.AddDays(-days)), ["live_accounts"] = state.Empty ? 0 : 3, ["to"] = Iso(end), ["top_profiles"] = new JsonArray(),
                ["total_accounts"] = all.Length, ["total_watch_seconds"] = state.Empty ? 0 : 3_300_000, ["tz"] = Param(query, "tz") is { Length: > 0 } tz ? tz : "UTC",
            }.ToJsonString();
        }

        private static JsonObject AccountJson(Account account, DateTimeOffset now)
        {
            var profiles = new JsonArray();
            for (var index = 1; index <= account.Profiles; index++)
                profiles.Add(new JsonObject
                {
                    ["completed_plays"] = 6, ["completion_rate"] = 0.5, ["direct_plays"] = index * 3, ["is_live"] = account.Live && index == 1, ["last_seen_at"] = index == 3 ? (JsonNode?)null : Iso(now.AddHours(-index * 5 - account.Id)),
                    ["measured_sessions"] = 10, ["plays"] = index == 2 ? 0 : 12, ["profile_id"] = "pf" + account.Id + "x" + index + "-abcdef", ["profile_name"] = index == 2 ? "" : index == 3 ? "Movie Night With A Long Profile Name" : "Kids",
                    ["remux_plays"] = 2, ["sessions"] = 14, ["transcode_plays"] = 1, ["watch_seconds"] = 14_400 * index,
                });
            var completed = (long)Math.Round(account.Plays * account.Completion);
            return new JsonObject
            {
                ["completed_plays"] = completed, ["completion_rate"] = account.Completion, ["direct_plays"] = (long)(account.Plays * account.Direct), ["direct_rate"] = account.Direct, ["episodes"] = account.Plays / 2,
                ["is_live"] = account.Live, ["last_seen_at"] = account.Seen is { } seen ? Iso(seen) : null, ["measured_sessions"] = 10, ["measured_share"] = 0.9, ["movies"] = account.Plays / 2,
                ["plays"] = account.Plays, ["profiles"] = profiles, ["remux_plays"] = 3, ["role"] = account.Id == 1 ? "admin" : "user", ["sessions"] = account.Plays + 4, ["top_client"] = account.Client,
                ["transcode_plays"] = 2, ["user_id"] = account.Id.ToString(CultureInfo.InvariantCulture), ["username"] = account.Name, ["watch_seconds"] = account.Watch,
            };
        }

        // ── Devices ──────────────────────────────────────────

        private static JsonObject Cell(string codec, double rate, long sessions, long transcodes) => new()
        {
            ["direct_rate"] = rate, ["direct_sessions"] = (long)(sessions * rate), ["remux_sessions"] = Math.Max(0, sessions - transcodes - (long)(sessions * rate)), ["sessions"] = sessions,
            ["source_video_codec"] = codec, ["transcode_sessions"] = transcodes,
        };

        private static JsonObject MatrixRow(string client, string platform, bool jellyfin, double rate, long total, params JsonObject[] cells) => new()
        {
            ["cells"] = new JsonArray(cells.Cast<JsonNode>().ToArray()), ["client_name"] = client, ["device_platform"] = platform, ["direct_rate"] = rate, ["direct_sessions"] = (long)(total * rate),
            ["is_jellyfin_client"] = jellyfin, ["remux_sessions"] = total / 10, ["total_sessions"] = total, ["transcode_sessions"] = total - (long)(total * rate) - total / 10,
        };

        public static string Devices(DateTimeOffset now, string query, InsightsTabsState state)
        {
            var (days, unit) = Window(query);
            var floor = int.TryParse(Param(query, "min_sessions"), out var parsedFloor) ? parsedFloor : 5;
            var scoped = Param(query, "user_id").Length > 0;
            var end = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
            var start = end.AddDays(-days);
            var empty = state.Empty;
            var rows = new List<JsonObject>
            {
                MatrixRow("Silo Desktop", "Windows", false, 0.93, 180, Cell("h264", 0.99, 90, 1), Cell("hevc", 0.91, 70, 6), Cell("av1", 0.7, 20, 6)),
                MatrixRow("Silo for Roku", "Roku OS", false, 0.62, 96, Cell("h264", 0.9, 50, 5), Cell("hevc", 0.3, 40, 28), Cell("vp9", 0.1, 6, 5)),
                MatrixRow("Jellyfin Web", "Chrome", true, 0.41, 60, Cell("h264", 0.55, 40, 18), Cell("hevc", 0.05, 20, 19)),
                MatrixRow("", "", false, 0.2, 14, Cell("h264", 0.2, 14, 11)),
            };
            if (floor <= 1 || scoped) rows.Add(MatrixRow("Silo for tvOS With A Remarkably Long Client Name", "Apple TV 4K (3rd generation) Platform Name", false, 0.8, 3, Cell("hevc", 0.8, 3, 0)));
            if (floor <= 1) rows.Add(MatrixRow("Test Harness", "Linux", false, 1, 1, Cell("h264", 1, 1, 0)));
            if (empty) rows.Clear();
            var buckets = new JsonArray();
            for (var day = start; day < end; day = unit == "day" ? day.AddDays(1) : day.AddDays(unit == "week" ? 7 : 30))
                buckets.Add(new JsonObject
                {
                    ["bucket_start"] = Iso(day), ["jellyfin_sessions"] = empty ? 0 : (day.DayOfYear % 5 == 0 ? 0 : 1 + day.DayOfYear % 3), ["label_date"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["native_sessions"] = empty ? 0 : day.DayOfYear % 4 == 0 ? 0 : 2 + day.DayOfYear % 6,
                });
            var versions = new JsonArray();
            if (!empty)
                for (var index = 0; index < 14; index++)
                    versions.Add(new JsonObject
                    {
                        ["client_name"] = index == 3 ? "" : index % 3 == 0 ? "Silo Desktop" : index % 3 == 1 ? "Silo for Roku" : "Jellyfin Web", ["client_version"] = $"1.{index}.{index * 3}-beta.{index}",
                        ["device_platform"] = index % 3 == 0 ? "Windows" : index % 3 == 1 ? "Roku OS" : "Chrome", ["is_jellyfin_client"] = index % 3 == 2, ["last_seen_at"] = index == 5 ? (JsonNode?)null : Iso(now.AddDays(-index)),
                        ["sessions"] = 90 - index * 6,
                    });
            JsonArray Counts(params (string Key, long Sessions)[] items) => empty ? new JsonArray() : new JsonArray(items.Select(item => (JsonNode)Count(item.Key, item.Sessions)).ToArray());
            var coverage = Coverage(now, empty);
            if (state.FullCoverage) coverage["route_facts_sessions"] = 420;
            return new JsonObject
            {
                ["adaptation_reasons"] = empty ? new JsonArray() : new JsonArray(Reason("video_codec_unsupported", 40, 0.5), Reason("bandwidth_limit", 18, 0.225), Reason("subtitle_burn_in", 12, 0.15), Reason("a_newer_server_reason", 10, 0.125)),
                ["audio"] = new JsonObject
                {
                    ["decisions"] = Counts(("direct", 210), ("remux", 40), ("transcode", 30)), ["delivered_channels"] = Counts(("stereo", 160), ("surround", 100), ("mono", 6), ("unknown", 14)),
                    ["languages"] = Counts(("en", 220), ("ja", 30), ("unknown", 20), ("zz-unknown-tag", 4)), ["sessions"] = empty ? 0 : 280,
                },
                ["bucket_unit"] = unit, ["client_versions"] = versions, ["codecs"] = empty ? new JsonArray() : new JsonArray("h264", "hevc", "av1", "vp9"), ["coverage"] = coverage,
                ["delivered_resolutions"] = Counts(("2160p", 20), ("1080p", 180), ("720p", 60), ("480p", 10), ("unknown", 4)), ["from"] = Iso(start),
                ["hw_accel_breakdown"] = Counts(("nvenc", 30), ("qsv", 12), ("vaapi", 6), ("videotoolbox", 2), ("none", 20), ("unknown", 4), ("amf", 3)),
                ["matrix"] = new JsonArray(rows.Select(row => (JsonNode)row).ToArray()), ["protocol_buckets"] = buckets,
                ["resolution_changes"] = empty ? new JsonArray() : new JsonArray(Change("2160p", "1080p", 14), Change("1080p", "720p", 9), Change("1080p", "480p", 2)),
                ["source_resolutions"] = Counts(("2160p", 70), ("1080p", 170), ("720p", 24), ("4320p", 2), ("unknown", 8)),
                ["subtitle_burn_in_codecs"] = Counts(("ass", 8), ("hdmv_pgs_subtitle", 3), ("subrip", 1)),
                ["subtitles"] = new JsonObject
                {
                    ["deliveries"] = Counts(("embedded", 40), ("burn_in", 12), ("hls_rendition", 8), ("sidecar", 6), ("future_delivery", 1)), ["forced_sessions"] = empty ? 0 : 9, ["hearing_impaired_sessions"] = empty ? 0 : 3,
                    ["languages"] = Counts(("en", 44), ("es", 14), ("unknown", 9)), ["sessions"] = empty ? 0 : 280, ["subtitled_sessions"] = empty ? 0 : 67,
                },
                ["summary"] = new JsonObject
                {
                    ["direct_rate"] = empty ? 0 : 0.72, ["direct_sessions"] = empty ? 0 : 300, ["hdr_tonemap_hw_sessions"] = empty ? 0 : 8, ["hdr_tonemap_sessions"] = empty ? 0 : 11, ["hdr_tonemap_sw_sessions"] = empty ? 0 : 3,
                    ["hw_transcode_sessions"] = empty ? 0 : 50, ["remux_sessions"] = empty ? 0 : 40, ["route_facts_sessions"] = empty ? 0 : 280, ["subtitle_burn_in_sessions"] = empty ? 0 : 12, ["sw_transcode_sessions"] = empty ? 0 : 24,
                    ["total_sessions"] = empty ? 0 : 420, ["transcode_sessions"] = empty ? 0 : 80,
                },
                ["to"] = Iso(end), ["tone_map_breakdown"] = Counts(("hardware", 8), ("software", 3)),
                ["top_transcoders"] = empty ? new JsonArray() : new JsonArray(Transcoder("Silo for Roku", "Roku OS", 33, 96), Transcoder("Jellyfin Web", "Chrome", 19, 60), Transcoder("", "", 11, 14), Transcoder("Silo Desktop", "Windows", 7, 180)),
                ["tz"] = Param(query, "tz") is { Length: > 0 } tz ? tz : "UTC",
            }.ToJsonString();

            static JsonObject Reason(string reason, long sessions, double share) => new() { ["reason"] = reason, ["sessions"] = sessions, ["share_of_adapted"] = share };
            static JsonObject Change(string from, string to, long sessions) => new() { ["from"] = from, ["sessions"] = sessions, ["to"] = to };
            static JsonObject Transcoder(string client, string platform, long transcodes, long total) => new()
            {
                ["client_name"] = client, ["device_platform"] = platform, ["is_jellyfin_client"] = client == "Jellyfin Web", ["total_sessions"] = total, ["transcode_sessions"] = transcodes,
            };
        }

        // ── Bandwidth ────────────────────────────────────────

        public static string Bandwidth(DateTimeOffset now, string query, InsightsTabsState state)
        {
            var (days, _) = Window(query);
            var end = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
            var start = end.AddDays(-days);
            var nodes = !state.NoRemoteNodes;
            var points = new JsonArray();
            long api = 0, node = 0, playback = 0, download = 0, peak = 0;
            var active = 0;
            var index = 0;
            if (!state.Empty)
                for (var day = start; day < end; day = day.AddDays(1), index++)
                {
                    if (index % 6 == 3) continue;
                    active++;
                    var apiBytes = (3 + index % 5) * 1_200_000_000L;
                    var edgeA = nodes ? (2 + index % 4) * 900_000_000L : 0;
                    var edgeB = nodes && index % 3 != 0 ? (1 + index % 3) * 700_000_000L : 0;
                    var far = nodes && index % 4 == 0 ? 300_000_000L : 0;
                    var small = nodes && index % 5 == 0 ? 120_000_000L : 0;
                    var dl = index % 4 == 1 ? 1_500_000_000L : 0;
                    var total = apiBytes + edgeA + edgeB + far + small;
                    var byBytes = new JsonObject { ["api"] = apiBytes };
                    var byPeak = new JsonObject { ["api"] = 20_000 + index % 7 * 4_000 };
                    if (nodes)
                    {
                        byBytes["node:2"] = edgeA; byPeak["node:2"] = 14_000 + index % 5 * 3_000;
                        if (edgeB > 0) { byBytes["node:3"] = edgeB; byPeak["node:3"] = 9_000 + index % 4 * 2_000; }
                        if (far > 0) { byBytes["node:9"] = far; byPeak["node:9"] = 6_000; }
                        if (small > 0) { byBytes["node:12"] = small; byPeak["node:12"] = 3_000; }
                    }
                    var dayPeak = 28_000L + index % 9 * 7_000;
                    peak = Math.Max(peak, dayPeak);
                    api += apiBytes; node += total - apiBytes; playback += total - dl; download += dl;
                    points.Add(new JsonObject
                    {
                        ["api_egress_bytes"] = apiBytes, ["bytes_by_source"] = byBytes, ["date"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["download_egress_bytes"] = dl, ["egress_bytes"] = total,
                        ["peak_download_kbps"] = dl > 0 ? 18_000 : 0, ["peak_egress_kbps"] = dayPeak, ["peak_kbps_by_source"] = byPeak, ["playback_egress_bytes"] = total - dl,
                    });
                }
            JsonObject Source(string key, string kind, string name, string? nodeId, string? type, long bytes, long peakKbps, double share) => new()
            {
                ["egress_bytes"] = bytes, ["key"] = key, ["kind"] = kind, ["name"] = name, ["node_id"] = nodeId, ["node_type"] = type, ["peak_egress_kbps"] = peakKbps, ["share_of_total"] = share,
            };
            var sum = Math.Max(1, api + node);
            var sources = new JsonArray();
            if (!state.Empty)
            {
                sources.Add(Source("api", "api", "api", null, null, api, 48_000, (double)api / sum));
                if (nodes)
                {
                    sources.Add(Source("node:2", "node", "Edge", "2", "proxy", node * 55 / 100, 31_000, node * 0.55 / sum));
                    sources.Add(Source("node:3", "node", "Edge", "3", "transcode", node * 30 / 100, 22_000, node * 0.30 / sum));
                    sources.Add(Source("node:9", "node", "Far", "9", null, node * 10 / 100, 6_000, node * 0.10 / sum));
                    sources.Add(Source("node:12", "node", "A Node With A Remarkably Long Display Name", "12", "proxy", node * 5 / 100, 3_000, node * 0.05 / sum));
                }
            }
            return new JsonObject
            {
                ["from"] = Iso(start), ["has_remote_nodes"] = nodes || state.Empty, ["oldest_metric_at"] = Iso(start.AddDays(days > 8 ? 4 : 0)), ["oldest_route_facts_at"] = Iso(now.AddDays(-500)), ["points"] = points, ["sources"] = sources,
                ["summary"] = new JsonObject
                {
                    ["active_days"] = active, ["api_egress_bytes"] = api, ["avg_daily_bytes"] = active == 0 ? 0 : (api + node) / active, ["download_egress_bytes"] = download, ["local_sessions"] = state.Empty ? 0 : 310,
                    ["node_egress_bytes"] = node, ["peak_download_kbps"] = download > 0 ? 18_000 : 0, ["peak_egress_at"] = state.Empty ? null : Iso(end.AddDays(-3).AddHours(-4)), ["peak_egress_kbps"] = peak,
                    ["playback_egress_bytes"] = playback, ["remote_sessions"] = state.Empty ? 0 : 96, ["total_egress_bytes"] = api + node, ["unknown_location_sessions"] = 14,
                },
                ["to"] = Iso(end), ["tz"] = "UTC",
            }.ToJsonString();
        }
    }
}
