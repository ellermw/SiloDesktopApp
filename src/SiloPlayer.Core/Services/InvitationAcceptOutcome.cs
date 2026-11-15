using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>What a failed invitation acceptance tells the invitee about the account.</summary>
public enum InvitationAcceptFailure
{
    /// <summary>
    /// A 5xx, lost response, timeout, transport failure or unrecognised problem. The account may have been created,
    /// so acceptance is never retried blindly: the invitation is reloaded first.
    /// </summary>
    Uncertain,
    /// <summary>The server refused before anything committed (rate limit); the invitation is unspent and the invitee may try again.</summary>
    RateLimited,
    /// <summary>The server cannot create the profile the invitation requires; refused before anything committed.</summary>
    ProfileUnavailable,
    /// <summary>The address is already in use (conflict); refused before anything committed.</summary>
    EmailTaken,
    /// <summary>The request did not validate (password or address); refused before anything committed.</summary>
    Validation,
    /// <summary>Password sign-in is off on this server; the invitation stays unspent but cannot be accepted here.</summary>
    LocalLoginDisabled,
    /// <summary>404 or an already-used/expired code: the invitation no longer exists for acceptance.</summary>
    LinkUnavailable,
}

/// <summary>
/// Classifies an acceptance failure the way the current WebUI does (<c>InviteClaim.tsx</c>): only definite, uncommitted
/// refusals may be retried. Everything else may have created the account.
/// </summary>
public static class InvitationAcceptOutcome
{
    public static InvitationAcceptFailure Classify(Exception exception)
    {
        if (exception is not ApiException api) return InvitationAcceptFailure.Uncertain;
        var type = api.ProblemType ?? api.ErrorCode;
        if (type == "local_login_disabled") return InvitationAcceptFailure.LocalLoginDisabled;
        if (type == "rate_limited" || api.StatusCode == 429) return InvitationAcceptFailure.RateLimited;
        if (type == "capability_unsupported") return InvitationAcceptFailure.ProfileUnavailable;
        if (type == "conflict") return InvitationAcceptFailure.EmailTaken;
        if (type is "validation_failed" or "weak_password") return InvitationAcceptFailure.Validation;
        if (api.StatusCode == 404 || type is "already_used" or "not_found") return InvitationAcceptFailure.LinkUnavailable;
        return InvitationAcceptFailure.Uncertain;
    }

    /// <summary>True when no account was created and the same invitation may be submitted again as-is or corrected.</summary>
    public static bool IsRetryable(InvitationAcceptFailure failure)
        => failure is InvitationAcceptFailure.RateLimited or InvitationAcceptFailure.ProfileUnavailable
            or InvitationAcceptFailure.EmailTaken or InvitationAcceptFailure.Validation;
}
