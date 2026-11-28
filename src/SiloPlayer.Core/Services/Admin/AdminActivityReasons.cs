using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminActivityReason(string Code, string Label, string Description);

/// <summary>The closed server vocabulary at official 217ff25a. Preserve server order; never infer a cause.</summary>
public static class AdminActivityReasons
{
    public static IReadOnlyList<AdminActivityReason> Get(AdminActivitySession session) =>
        FromCodes(session.AdaptationReasons);

    public static IReadOnlyList<AdminActivityReason> FromCodes(IReadOnlyList<string>? codes) =>
        (codes ?? []).Select(code => (Code: code, Key: code switch
        {
            "video_codec_unsupported" => "videoCodecUnsupported",
            "video_capability_unconfirmed" => "videoCapabilityUnconfirmed",
            "dynamic_range_unsupported" => "dynamicRangeUnsupported",
            "resolution_limit" => "resolutionLimit",
            "bandwidth_limit" => "bandwidthLimit",
            "buffering" => "buffering",
            "bandwidth_cap" => "bandwidthCap",
            "server_bitrate_limit" => "serverBitrateLimit",
            "quality_selected" => "qualitySelected",
            "subtitle_burn_in" => "subtitleBurnIn",
            "direct_play_failed" => "directPlayFailed",
            "audio_codec_unsupported" => "audioCodecUnsupported",
            "audio_channels_unsupported" => "audioChannelsUnsupported",
            "device_workaround" => "deviceWorkaround",
            "audio_track_selection" => "audioTrackSelection",
            "container_unsupported" => "containerUnsupported",
            "subtitle_unsupported" => "subtitleUnsupported",
            _ => null,
        })).Where(reason => reason.Key != null).Select(reason => new AdminActivityReason(reason.Code,
            AdminText.T($"web.admin.activity.reason.{reason.Key}.label"), AdminText.T($"web.admin.activity.reason.{reason.Key}.description"))).ToArray();
}
