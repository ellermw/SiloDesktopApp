namespace SiloPlayer.Core.Services;

public static class ExternalSignInErrors
{
    public static string Describe(string reason, bool linking = false) => (linking, reason) switch
    {
        (_, "not_permitted") => "Your account at the sign-in provider isn't allowed to use this server.",
        (_, "account_required") => "You don't have an account on this server yet. Ask an admin to add you.",
        (_, "email_in_use") => "An account with this email already exists. Ask an admin to connect it to the sign-in provider.",
        (true, "identity_linked_elsewhere") => "That provider account is already connected to another account on this server.",
        (_, "identity_linked_elsewhere") => "That provider account is already connected to another account.",
        (_, "account_disabled") => "This account is disabled.",
        (_, "provider_unavailable") => "The sign-in provider can't be reached right now. Try again later.",
        (true, "state_invalid") => "Connecting didn't finish in this browser. Try again from this page.",
        (_, "state_invalid") => "The sign-in didn't finish in this browser. Start again from this page.",
        (true, "session_expired") => "Connecting took too long. Try again from this page.",
        (_, "session_expired") => "The sign-in took too long. Start again from this page.",
        (true, "already_linked") => "Your account is already connected to this sign-in provider.",
        (_, "already_linked") => "This account is already connected to the sign-in provider.",
        _ => linking ? "Connecting the sign-in provider failed. Try again." : "Sign-in with the provider failed. Try again."
    };
}
