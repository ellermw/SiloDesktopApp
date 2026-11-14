using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Toast outcome of a session action; <see cref="OptimisticPaused"/> is set only for a dispatched applied pause/resume.</summary>
public sealed record AdminActivityCommandOutcome(bool Success, string Message, bool? OptimisticPaused = null);

/// <summary>Toast wording for session actions (WebUI AdminSessionActions.tsx), keeping fallback, replay and revoke facts distinct.</summary>
public static class AdminActivityCommandText
{
    public static string PrimaryLabel(bool paused) =>
        AdminText.T(paused ? "web.admin.sessions.playback.action.resume" : "web.admin.sessions.playback.action.pause");

    public static AdminActivityCommandOutcome FromReceipt(string action, AdminActivityCommandReceipt receipt)
    {
        if (action == "message")
            return new(true, AdminText.T(receipt.Outcome == "replayed"
                ? "web.admin.sessions.playback.toast.messageReplayed"
                : "web.admin.sessions.playback.toast.messageSent"));
        if (receipt.Outcome == "replayed")
            return new(true, AdminText.T(receipt.Delivery == "fallback_scheduled"
                ? "web.admin.sessions.playback.toast.replayedFallback"
                : "web.admin.sessions.playback.toast.replayed", new { action }));
        bool? paused = receipt.Delivery == "dispatched" && action == "pause" ? true
            : receipt.Delivery == "dispatched" && action == "resume" ? false : null;
        var key = receipt.Delivery == "fallback_scheduled"
            ? $"web.admin.sessions.playback.toast.{action}Fallback"
            : $"web.admin.sessions.playback.toast.{action}Sent";
        return new(true, AdminText.T(key), paused);
    }

    public static AdminActivityCommandOutcome FromTerminate(AdminActivityTerminateReceipt receipt) =>
        new(true, AdminText.T("web.admin.sessions.playback.toast.terminateReceipt", new
        {
            revoked = receipt.AlreadyRevoked ? "already" : receipt.AuthorityRevoked ? "revoked" : "other",
            notified = receipt.ClientNotified ? "yes" : "other",
        }));

    /// <summary>English shows the server's problem detail, as the WebUI does for its English catalog.</summary>
    public static string DescribeFailure(string action, Exception error) => error switch
    {
        OperationCanceledException => AdminText.T("web.admin.sessions.playback.failure.staleAuthority"),
        ApiException { StatusCode: 409 } conflict => AdminText.T("web.admin.sessions.playback.failure.notApplied", new
        {
            action,
            reason = string.IsNullOrWhiteSpace(conflict.Message) || conflict.Message.StartsWith("HTTP ", StringComparison.Ordinal)
                ? AdminText.T("web.admin.sessions.playback.failure.conflictReason")
                : conflict.Message,
        }),
        ApiException { StatusCode: 404 } => AdminText.T("web.admin.sessions.playback.failure.ended"),
        ApiException { ErrorCode: "dependency_unavailable" } unavailable =>
            string.IsNullOrWhiteSpace(unavailable.Message) || unavailable.Message.StartsWith("HTTP ", StringComparison.Ordinal)
                ? AdminText.T("web.admin.sessions.playback.failure.unavailable")
                : unavailable.Message,
        ApiException api => api.Message,
        _ when !string.IsNullOrWhiteSpace(error.Message) => error.Message,
        _ => AdminText.T("web.admin.sessions.playback.failure.generic"),
    };
}
