using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Services.Admin.Users;

public static class AdminUsersPresentation
{
    public static bool IsVideoTranscode(JsonObject session)
    {
        if (session.Text("effective_play_method").Length > 0) return session.Text("effective_play_method") == "transcode";
        var decision = session.Text("video_decision");
        var method = (decision.Length == 0 ? session.Text("play_method") : decision).Trim();
        return method == "transcode";
    }
    public static bool MatchesDownload(JsonObject row, string filter, bool android) => filter switch
    {
        "" => true,
        "on_device" => row.Text("status") == "completed",
        "requested" => android && row.Text("status") is "ready" or "downloading",
        "in_progress" => !android && row.Text("status") is "preparing" or "ready" or "downloading",
        _ => row.Text("status") == filter
    };
}
