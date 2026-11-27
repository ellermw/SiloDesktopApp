using System.Globalization;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminPlaybackField(string Key, string Group, string Label, string Description, string Kind,
    string Default, int Min = 0, int Max = 0, bool Advanced = false, string[]? Options = null);

/// <summary>Exact setting names/types/defaults and WebUI conditionals from official 217ff25, not decoder preferences.</summary>
public static class AdminPlaybackSettingsRules
{
    public static IReadOnlyList<AdminPlaybackField> Fields { get; } = Array.AsReadOnly(new AdminPlaybackField[]
    {
        new("playback.watched_threshold", "watch", "watch.markWatched", "watch.markWatchedDescription", "number", "90", 1, 100),
        new("playback.min_resume_threshold", "watch", "watch.minResume", "watch.minResumeDescription", "number", "5", 1, 99),
        new("playback.transcode_enabled", "transcoding", "transcoding.enabled", "transcoding.enabledDescription", "toggle", "true"),
        new("playback.hw_accel", "transcoding", "hwAccel.label", "hwAccel.description", "select", "auto", Options: ["auto", "qsv", "vaapi", "nvenc", "videotoolbox", "none"]),
        new("allow_4k_transcode", "transcoding", "transcoding.allow4k", "transcoding.allow4kDescription", "toggle", "true"),
        new("playback.prefer_lower_version_transcode", "transcoding", "transcoding.preferLower", "transcoding.preferLowerDescription", "toggle", "true"),
        new("playback.allow_hevc_encoding", "transcoding", "transcoding.allowHevc", "transcoding.allowHevcDescription", "toggle", "false"),
        new("playback.transcode_hardware_tone_map_enabled", "transcoding", "transcoding.hardwareToneMap", "transcoding.hardwareToneMapHint", "toggle", "true"),
        new("playback.transcode_software_tone_map_enabled", "transcoding", "transcoding.softwareToneMap", "transcoding.softwareToneMapHint", "toggle", "true"),
        new("enable_transcode_throttle", "transcoding", "transcoding.throttle", "transcoding.throttleDescription", "toggle", "true"),
        new("transcode_throttle_seconds", "transcoding", "transcoding.bufferAhead", "", "number", "300", 60, 86400),
        new("playback.transcode_dir", "transcoding", "transcoding.dir", "transcoding.dirDescription", "path", "/tmp/silo-transcode"),
        new("playback.ffmpeg_path", "transcoding", "transcoding.ffmpegPath", "transcoding.ffmpegPathDescription", "path", "", Advanced: true),
        new("playback.hw_device", "transcoding", "devices.heading", "", "devices", "", Advanced: true),
        new("playback.segment_retention_seconds", "transcoding", "transcoding.backBuffer", "transcoding.backBufferHint", "number", "600", 0, 86400, true),
        new("playback.copy_keyframe_playlist", "transcoding", "transcoding.copyKeyframePlaylist", "transcoding.copyKeyframePlaylistDescription", "toggle", "false", Advanced: true),
        new("playback.bandwidth_probe_automatic", "speedTests", "speedTests.automatic", "speedTests.automaticDescription", "toggle", "true"),
        new("playback.bandwidth_probe_max_age_days", "speedTests", "speedTests.maxAge", "speedTests.maxAgeDescription", "number", "7", 1, 30),
        new("playback.preview_image_width", "previews", "previews.width", "previews.widthDescription", "number", "300", 160, 640),
        new("playback.trickplay_interval_seconds", "previews", "previews.interval", "previews.intervalDescription", "number", "10", 5, 60),
        new("playback.chapter_thumbnail_workers", "previews", "previews.chapterWorkers", "previews.chapterWorkersDescription", "number", "1", 1, 1024),
        new("playback.chapter_thumbnail_execution", "previews", "previews.chapterExecution", "", "select", "local", Options: ["local", "prefer_transcode_nodes", "transcode_nodes_only"]),
        new("playback.trickplay_execution", "previews", "previews.trickplayExecution", "", "select", "local", Options: ["local", "prefer_transcode_nodes", "transcode_nodes_only"]),
        new("playback.trickplay_workers", "previews", "previews.trickplayWorkers", "previews.trickplayWorkersDescription", "number", "1", 1, 64, true),
        new("playback.trickplay_node_capacity", "previews", "previews.trickplayNodeCapacity", "previews.trickplayNodeCapacityDescription", "number", "1", 1, 64, true),
        new("playback.chapter_thumbnail_hdr_policy", "previews", "previews.hdrPolicy", "previews.hdrPolicyDescription", "select", "best_effort", Advanced: true, Options: ["best_effort", "disabled"]),
        new("playback.chapter_thumbnail_software_tone_map_enabled", "previews", "previews.softwareToneMap", "previews.softwareToneMapDescription", "toggle", "true", Advanced: true),
        new("markers.mode", "markers", "markers.mode", "markers.modeDescription", "select", "both", Options: ["off", "local", "both", "online"]),
        new("markers.detect_intros", "markers", "markers.detectIntros", "markers.detectIntrosDescription", "toggle", "true"),
        new("markers.detect_credits", "markers", "markers.detectCredits", "markers.detectCreditsDescription", "toggle", "true"),
        new("markers.online_storage", "markers", "markers.onlineStorage", "markers.onlineStorageStored", "select", "stored", Options: ["stored", "on_demand"]),
        new("markers.lazy_playback", "markers", "markers.lazyPlayback", "markers.lazyPlaybackBoth", "toggle", "true"),
        new("markers.detection_workers", "markers", "markers.detectionWorkers", "markers.detectionWorkersDescription", "number", "1", 1, 64),
    });
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(Fields.Select(field => field.Key).ToArray());
    public static IReadOnlyList<string> RoutingKeys { get; } = Array.AsReadOnly(new[] { "playback.routing.direct_play_egress", "playback.routing.remux_execution", "playback.routing.remux_egress", "playback.routing.video_transcode_execution", "playback.routing.video_transcode_egress" });
    public static IReadOnlyList<string> ExecutionOptions { get; } = Array.AsReadOnly(new[] { "prefer_worker", "prefer_transcode", "worker_only", "prefer_api", "api_only" });
    public static IReadOnlyList<string> EgressOptions { get; } = Array.AsReadOnly(new[] { "prefer_proxy", "proxy_only", "prefer_api", "api_only" });
    public static string Get(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) && value.Length > 0 ? value
        : Fields.FirstOrDefault(field => field.Key == key)?.Default ?? (key.EndsWith("_egress") ? "prefer_proxy" : "prefer_transcode");
    /// <summary>Whether the row's condition shows it, ignoring any edit it holds (see <see cref="IsShown"/>).</summary>
    public static bool Visible(string key, IReadOnlyDictionary<string, string> values, bool supportsTrickplay, bool detectionKinds) => IsShown(key, values, supportsTrickplay, detectionKinds, null);

    /// <summary>
    /// Whether a row is on screen (the condition, plus the edit rule below). A capability the server lacks hides its rows outright (they are not in the official form's keys either). A row a switch or
    /// mode hides stays in view, marked as changed, while it holds an unsaved edit.
    /// </summary>
    public static bool IsShown(string key, IReadOnlyDictionary<string, string> values, bool supportsTrickplay, bool detectionKinds, IReadOnlyCollection<string>? dirty)
    {
        if (CapabilityHidden(key, supportsTrickplay, detectionKinds)) return false;
        var mode = Get(values, "markers.mode"); var online = mode is "both" or "online"; var local = mode is "both" or "local";
        var shown = key switch
        {
            "playback.prefer_lower_version_transcode" => Get(values, "allow_4k_transcode") == "true",
            "transcode_throttle_seconds" => Get(values, "enable_transcode_throttle") == "true",
            "markers.detect_intros" or "markers.detect_credits" => local && detectionKinds,
            "markers.detection_workers" => local,
            "markers.online_storage" => online,
            "markers.lazy_playback" => local || online && Get(values, "markers.online_storage") == "stored",
            _ => true,
        };
        return shown || dirty?.Contains(key) == true;
    }

    public static string LazyPlaybackDescription(string mode, string storage) => mode switch
    {
        "online" => "markers.lazyPlaybackOnline",
        "both" when storage == "stored" => "markers.lazyPlaybackBoth",
        _ => "markers.lazyPlaybackLocal",
    };
    /// <summary>A row the server's capabilities remove: it cannot be shown whatever its draft holds.</summary>
    public static bool CapabilityHidden(string key, bool supportsTrickplay, bool detectionKinds) =>
        !supportsTrickplay && (key == "playback.preview_image_width" || key.StartsWith("playback.trickplay_", StringComparison.Ordinal))
        || !detectionKinds && key is "markers.detect_intros" or "markers.detect_credits";
    public static IReadOnlyDictionary<string, string> Preset(string name)
    {
        string[] values = name switch
        {
            "standard" => ["prefer_proxy", "prefer_transcode", "prefer_proxy", "prefer_transcode", "prefer_proxy"],
            "gpu" => ["prefer_api", "prefer_api", "prefer_api", "prefer_worker", "prefer_proxy"],
            "central" => ["api_only", "prefer_worker", "api_only", "prefer_worker", "api_only"],
            _ => throw new ArgumentException("Unknown routing preset.", nameof(name)),
        };
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(RoutingKeys.Zip(values).ToDictionary(pair => pair.First, pair => pair.Second));
    }
    public static IReadOnlyDictionary<string, string> Validate(IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> dirty)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in dirty)
        {
            var value = values.GetValueOrDefault(key, "");
            if (RoutingKeys.Contains(key)) { if (!(key.EndsWith("_egress") ? EgressOptions : ExecutionOptions).Contains(value)) errors[key] = "Choose a routing policy."; continue; }
            var field = Fields.FirstOrDefault(field => field.Key == key);
            if (field == null) { errors[key] = "This setting is not owned by this page."; continue; }
            if (field.Kind == "toggle" && value is not ("true" or "false")) errors[key] = "Choose on or off.";
            if (field.Options != null && !field.Options.Contains(value)) errors[key] = "Choose one of the supported values.";
            if (field.Kind == "number" && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < field.Min || number > field.Max)) errors[key] = $"Enter a whole number from {field.Min} to {field.Max}.";
            if (key == "playback.preview_image_width" && int.TryParse(value, out var width) && width % 2 != 0) errors[key] = "Enter an even number of pixels.";
            if (key == "playback.segment_retention_seconds" && int.TryParse(value, out var seconds) && seconds is > 0 and < 120) errors[key] = "Enter 0 or a whole number from 120 to 86400.";
            if (value.Length > 8192 || value.Any(char.IsControl)) errors[key] = "Enter a single-line value of at most 8192 characters.";
        }
        if (dirty.Contains("playback.min_resume_threshold") || dirty.Contains("playback.watched_threshold"))
            if (int.TryParse(Get(values, "playback.min_resume_threshold"), out var resume) && int.TryParse(Get(values, "playback.watched_threshold"), out var watched) && resume >= watched) errors["playback.min_resume_threshold"] = "Minimum resume progress must be less than the watched threshold.";
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(errors);
    }
    /// <summary>
    /// Dirty keys whose row cannot be shown at all (the server lacks the capability): nobody can see or correct them, so the draft goes back to its saved value
    /// and is never sent. A row a condition hides stays visible while it is dirty (see <see cref="Visible"/>), so it is not in this list.
    /// </summary>
    public static IReadOnlyList<string> HiddenDirtyKeys(IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> dirty, bool? supportsTrickplay, bool? detectionKinds) =>
        Array.AsReadOnly(dirty.Where(key => CapabilityHidden(key, supportsTrickplay ?? true, detectionKinds ?? true)).ToArray());

    // ---- Hardware device picker (official PlaybackSettings.tsx and playbackSettings.utils.ts) ----

    /// <summary>
    /// The picker rows: detected devices first, then the saved and staged paths detection does not list, so a saved device keeps its row when the draft
    /// drops it and a configured-but-missing one stays deselectable.
    /// </summary>
    public static IReadOnlyList<string> DeviceRows(IEnumerable<string> detected, string? saved, string? draft) =>
        Array.AsReadOnly(detected.Concat(ParseDevices(saved)).Concat(ParseDevices(draft)).Distinct(StringComparer.Ordinal).Take(64).ToArray());

    /// <summary>The order a toggle keeps: the saved devices in their saved order, then the other rows in row order.</summary>
    public static IReadOnlyList<string> DeviceOrder(string? saved, IReadOnlyList<string> rows)
    {
        var savedDevices = ParseDevices(saved);
        return Array.AsReadOnly(savedDevices.Concat(rows.Where(path => !savedDevices.Contains(path))).ToArray());
    }

    /// <summary>Balancing is QSV/VAAPI only: software encoding has no device, and NVENC addresses GPUs by CUDA index.</summary>
    public static bool DevicePickerApplies(string acceleration, bool cuda) => acceleration != "none" && !cuda;

    /// <summary>Shown while the mode applies, and also while a staged device list holds an edit, so Save never writes a list the admin cannot see.</summary>
    public static bool ShowsDevicePicker(int rows, bool applies, bool dirty) => rows > 0 && (applies || dirty);

    public static bool DeviceChanged(string? draft, string? saved, string path) => ParseDevices(draft).Contains(path) != ParseDevices(saved).Contains(path);

    /// <summary>A staged list that matches the saved devices and differs only in order; its selected rows then carry the change.</summary>
    public static bool OrderOnlyDraft(bool dirty, IEnumerable<string> rows, string? draft, string? saved) => dirty && !rows.Any(path => DeviceChanged(draft, saved, path));

    /// <summary>A row in view only because of an unsaved edit under a mode the picker does not apply to (it carries the unsaved-change mark).</summary>
    public static bool DeviceKeptForEdit(bool applies, string path, string? draft, string? saved, bool orderOnly) =>
        !applies && (DeviceChanged(draft, saved, path) || orderOnly && ParseDevices(draft).Contains(path));

    /// <summary>
    /// Under a mode the picker does not apply to it only takes the list back toward the saved one: any device can be switched off and only a saved device on,
    /// so the saved list is always reachable and no render path can be added for NVENC. A managed list is locked whole.
    /// </summary>
    public static bool DeviceSwitchLocked(bool managed, bool applies, string path, string? draft, string? saved) =>
        managed || !applies && !ParseDevices(draft).Contains(path) && !ParseDevices(saved).Contains(path);

    /// <summary>
    /// What a toggle stages. Landing on the saved devices stages the saved text itself, so the form can tell it undoes the edit however that text is spaced.
    /// </summary>
    public static string NextDeviceDraft(string? draft, string? saved, string path, IReadOnlyList<string> order)
    {
        var next = ToggleDevice(draft ?? "", path, order);
        return next == string.Join(",", ParseDevices(saved)) ? saved ?? "" : next;
    }
    public static IReadOnlyList<string> ParseDevices(string? value) => Array.AsReadOnly((value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray());
    public static string ToggleDevice(string value, string path, IReadOnlyList<string> detected)
    {
        var selected = ParseDevices(value).ToList();
        // Empty is automatic, not every GPU selected. Explicit toggles choose their own list.
        if (!selected.Remove(path)) selected.Add(path);
        return string.Join(",", selected.OrderBy(item => { var index = detected.ToList().IndexOf(item); return index < 0 ? int.MaxValue : index; }));
    }
}
