using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public static class ProfilePinFeedback
{
    public static string? Lockout(Exception error)
    {
        if (error is not ApiException { StatusCode: 429 } refusal) return null;
        if (refusal.RetryAfterSeconds is not { } seconds || !double.IsFinite(seconds))
            return "Too many incorrect PINs. Try again later.";
        var minutes = Math.Max(1, Math.Ceiling(seconds / 60));
        return $"Too many incorrect PINs. Try again in {minutes:0} {(minutes == 1 ? "minute" : "minutes")}.";
    }
}
