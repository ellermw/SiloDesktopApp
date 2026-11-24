using System.Globalization;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

/// <summary>
/// A line of the Activate device page: its official catalog key and the official English wording
/// (filled in) shown when the interface language's catalog does not have the key.
/// </summary>
public sealed record ActivationText(string Key, string English, IReadOnlyDictionary<string, object?>? Arguments = null)
{
    public string Render() => AppStrings.Get(Key, Arguments, English);

    /// <summary>Builds the text from the official English template, filling <c>{name}</c> placeholders.</summary>
    public static ActivationText Of(string key, string template, params (string Name, object Value)[] arguments)
    {
        var values = arguments.ToDictionary(pair => pair.Name, pair => (object?)pair.Value, StringComparer.Ordinal);
        var english = template;
        foreach (var (name, value) in arguments)
            english = english.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", StringComparison.Ordinal);
        return new(key, english, values.Count == 0 ? null : values);
    }
}

/// <summary>Why the lookup of a code failed (the web page's <c>LoadError</c>).</summary>
public enum ActivationLoadError { None, NotFound, Failed, RateLimited, Impersonating }

/// <summary>What a failed approve or decline means for the page.</summary>
public enum DecisionFailureKind { TooManyCodes, Impersonation, Moved, Failed }

/// <summary>What to do after a decision failed: reload the request, and what to tell the person.</summary>
/// <param name="Reload">Look the request up again (the reload keeps the last card when it fails).</param>
/// <param name="Error">The error to show at once; null when the request moved on and the reload decides.</param>
/// <param name="ErrorIfUnchanged">Shown after the reload when the request is still pending or could not be reloaded.</param>
public sealed record DecisionFailurePlan(bool Reload, ActivationText? Error, ActivationText? ErrorIfUnchanged);

/// <summary>
/// The rules of the Activate device page, mapped from the official WebUI page
/// (web/src/pages/ActivateDevice.tsx and QuickConnectApproval.tsx at silo-server 3b2b32168, #4652):
/// which state a lookup or decision leads to and which message key and wording each state shows.
/// No UI, no I/O; the view model applies the results.
/// </summary>
public static class DeviceActivation
{
    public const int DeviceCodeLength = 8;
    public const int QuickConnectCodeLength = 6;
    public const string QuickConnectPurpose = "jellyfin_quick_connect";
    public const string DeviceLoginPurpose = "device_login";

    /// <summary>After approving, the page follows the request every few seconds, then less often.</summary>
    public static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan WatchSlowInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan WatchFastWindow = TimeSpan.FromMinutes(2);

    // ----- Codes -----

    /// <summary>The code's characters, grouped 4+4 with a space as typed ("4821 7730").</summary>
    public static string FormatCode(string? value) => DeviceCodeText.Format(value);

    /// <summary>The code without grouping, as the API takes it.</summary>
    public static string CompactCode(string? value) => DeviceCodeText.Compact(value);

    /// <summary>A device sign-in (eight characters) or a Quick Connect code (six digits) is whole.</summary>
    public static bool IsCompleteCode(string compact) => compact.Length is DeviceCodeLength or QuickConnectCodeLength;

    /// <summary>A request's code as the card shows it: a Quick Connect code ungrouped, the rest 4+4.</summary>
    public static string DisplayCode(string? value)
    {
        var compact = CompactCode(value);
        return compact.Length == QuickConnectCodeLength ? compact : FormatCode(compact);
    }

    /// <summary>A six-digit Quick Connect code is found only for a signed-in account.</summary>
    public static bool NeedsAccount(string? token, string compactCode) => string.IsNullOrEmpty(token) && compactCode.Length == QuickConnectCodeLength;

    // ----- Request kind -----

    public static bool IsQuickConnect(DeviceLoginLookupResponse? details)
        => string.Equals(details?.ClientPurpose, QuickConnectPurpose, StringComparison.Ordinal);

    /// <summary>A request from a Silo app sending playback to this device; that app approves it, not this page.</summary>
    public static bool IsRemotePlayback(DeviceLoginLookupResponse? details)
        => details != null && !IsQuickConnect(details)
            && (!string.Equals(details.ClientPurpose, DeviceLoginPurpose, StringComparison.Ordinal) || details.Temporary);

    // ----- Lookup -----

    public static ActivationLoadError LoadErrorFor(int status) => status switch
    {
        404 => ActivationLoadError.NotFound,
        429 => ActivationLoadError.RateLimited,
        403 => ActivationLoadError.Impersonating,
        _ => ActivationLoadError.Failed,
    };

    // ----- Watching -----

    /// <summary>Whether the page follows the request: one approved here, or a pending remote-playback one. Never a Quick Connect one.</summary>
    public static bool ShouldWatch(DeviceLoginLookupResponse? details, bool approvedHere)
    {
        if (details == null || IsQuickConnect(details)) return false;
        return (approvedHere && details.Status == "approved") || (IsRemotePlayback(details) && details.Status == "pending");
    }

    public static TimeSpan WatchDelay(TimeSpan sinceWatchStarted) => sinceWatchStarted < WatchFastWindow ? WatchInterval : WatchSlowInterval;

    // ----- Card lines -----

    /// <summary>The question above the card; none once the request expired.</summary>
    public static ActivationText? Heading(DeviceLoginLookupResponse details)
    {
        if (details.Status == "expired") return null;
        var device = details.DeviceName?.Trim() ?? "";
        if (IsRemotePlayback(details))
            return device.Length > 0
                ? ActivationText.Of("web.auth.activate.playOn", "Play on {device}?", ("device", device))
                : ActivationText.Of("web.auth.activate.playOnUnknown", "Play on this device?");
        return device.Length > 0
            ? ActivationText.Of("web.auth.activate.signInDevice", "Allow {device} to sign in?", ("device", device))
            : ActivationText.Of("web.auth.activate.signInDeviceUnknown", "Allow this device to sign in?");
    }

    public static string PlatformLabel(string platform) => platform.ToLowerInvariant() switch
    {
        "tvos" => "Apple TV",
        "android-tv" or "androidtv" => "Android TV",
        "roku" => "Roku",
        _ => platform,
    };

    /// <summary>"Apple TV · requested 5m ago", "Requested 5m ago" or just the platform; empty when there is nothing to say.</summary>
    public static string DeviceLine(DeviceLoginLookupResponse details, DateTimeOffset now)
    {
        var device = details.DeviceName ?? "";
        var rawPlatform = string.IsNullOrEmpty(details.DevicePlatform) ? "" : PlatformLabel(details.DevicePlatform);
        var platform = rawPlatform.Length > 0 && !string.Equals(rawPlatform, device, StringComparison.OrdinalIgnoreCase) ? rawPlatform : "";
        var ago = RelativeTime(details.RequestedAt, now);
        if (platform.Length > 0)
        {
            var parts = new List<string> { platform };
            if (ago != null) parts.Add(ActivationText.Of("web.auth.activate.requestedAgo", "requested {time}", ("time", ago)).Render());
            return string.Join(" · ", parts);
        }
        return ago != null ? ActivationText.Of("web.auth.activate.requestedAgoStandalone", "Requested {time}", ("time", ago)).Render() : "";
    }

    /// <summary>The web page's relative time ("just now", "5m ago", "3h ago", "2d ago"); null for a missing or unreadable value.</summary>
    public static string? RelativeTime(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value) || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)) return null;
        var diff = (now - at).TotalMilliseconds;
        var minutes = Round(diff / 60_000);
        if (minutes < 1) return AppStrings.Get("web.common.date.justNow", fallback: "just now");
        if (minutes < 60) return Ago("web.common.date.minutesAgo", minutes, "m");
        var hours = Round(diff / 3_600_000);
        if (hours < 24) return Ago("web.common.date.hoursAgo", hours, "h");
        return Ago("web.common.date.daysAgo", Round(diff / 86_400_000), "d");

        static long Round(double value) => (long)Math.Floor(value + 0.5);
        static string Ago(string key, long count, string unit)
        {
            var text = count.ToString("N0", AppStrings.FormatCulture);
            return AppStrings.Get(key, new Dictionary<string, object?> { ["count"] = count, ["count_text"] = text }, text + unit + " ago");
        }
    }

    // ----- Results -----

    /// <summary>The line announced once the request is no longer pending, in its device, Quick Connect or remote-playback form.</summary>
    public static ActivationText? Result(DeviceLoginLookupResponse details, bool approvedHere, string? approvedProfile)
    {
        var remote = IsRemotePlayback(details);
        var quickConnect = IsQuickConnect(details);
        var device = details.DeviceName?.Trim() ?? "";
        switch (details.Status)
        {
            case "approved":
                if (remote) return ActivationText.Of("web.auth.activate.result.remoteApproved", "This request was approved in the Silo app.");
                if (quickConnect)
                {
                    if (string.IsNullOrEmpty(approvedProfile))
                        return ActivationText.Of("web.auth.activate.result.quickConnectApproved", "This device was approved and is finishing sign-in.");
                    return device.Length > 0
                        ? ActivationText.Of("web.auth.activate.result.quickConnectDone", "Done. {device} is signing in as {profile}.", ("device", device), ("profile", approvedProfile))
                        : ActivationText.Of("web.auth.activate.result.quickConnectDoneUnknown", "Done. This device is signing in as {profile}.", ("profile", approvedProfile));
                }
                if (!approvedHere) return ActivationText.Of("web.auth.activate.result.tvApproved", "This device was approved and is finishing sign-in.");
                return device.Length > 0
                    ? ActivationText.Of("web.auth.activate.result.deviceDone", "Done. {device} is signing in.", ("device", device))
                    : ActivationText.Of("web.auth.activate.result.tvDone", "Done. Your device is signing in.");
            case "consumed":
                if (remote) return ActivationText.Of("web.auth.activate.result.remoteApproved", "This request was approved in the Silo app.");
                if (quickConnect) return ActivationText.Of("web.auth.activate.result.quickConnectSignedIn", "That device is signed in.");
                if (!approvedHere) return ActivationText.Of("web.auth.activate.result.tvAlreadySignedIn", "This device is already signed in.");
                return device.Length > 0
                    ? ActivationText.Of("web.auth.activate.result.deviceSignedIn", "{device} is signed in.", ("device", device))
                    : ActivationText.Of("web.auth.activate.result.tvSignedIn", "Your device is signed in.");
            case "denied":
                return remote
                    ? ActivationText.Of("web.auth.activate.result.remoteDeclined", "This request was declined.")
                    : ActivationText.Of("web.auth.activate.result.declined", "This sign-in was declined.");
            case "canceled":
                return remote
                    ? ActivationText.Of("web.auth.activate.result.remoteCanceled", "The TV stopped waiting. Send playback from the Silo app again.")
                    : ActivationText.Of("web.auth.activate.result.canceled", "This device stopped waiting. Start sign-in again on it.");
            case "expired":
                if (remote) return ActivationText.Of("web.auth.activate.result.remoteExpired", "This request expired. Send playback from the Silo app again.");
                return quickConnect
                    ? ActivationText.Of("web.auth.activate.result.quickConnectExpired", "This code expired. Start Quick Connect again in the app.")
                    : ActivationText.Of("web.auth.activate.result.expired", "This code expired. Get a new code on the device and try again.");
            default:
                return null;
        }
    }

    // ----- Errors -----

    public static ActivationText NotFound { get; } = ActivationText.Of("web.auth.activate.notFound", "We couldn't find that code. Check the code and try again.");
    public static ActivationText LookupFailed { get; } = ActivationText.Of("web.auth.activate.lookupFailed", "Couldn't look up this code. Try again.");

    public static ActivationText DecisionFailed(bool approve) => approve
        ? ActivationText.Of("web.auth.activate.approveFailed", "Couldn't approve sign-in. Try again.")
        : ActivationText.Of("web.auth.activate.declineFailed", "Couldn't decline. Try again.");

    /// <summary>"Stop viewing as {username} to approve this sign-in ..." for an admin viewing as another account.</summary>
    public static ActivationText Impersonation(string? username) => string.IsNullOrWhiteSpace(username)
        ? ActivationText.Of("web.auth.activate.impersonatingUnknown", "Stop viewing as this user to approve this sign-in. Only the account itself can approve devices.")
        : ActivationText.Of("web.auth.activate.impersonating", "Stop viewing as {username} to approve this sign-in. Only the account itself can approve devices.", ("username", username));

    /// <summary>What to tell a person whose account has tried too many Quick Connect codes.</summary>
    public static ActivationText TooManyCodes(double? retryAfterSeconds)
    {
        if (retryAfterSeconds is not > 0) return ActivationText.Of("web.auth.quickConnect.tooManyCodes", "You've tried too many codes. Try again later.");
        var minutes = Math.Max(1, (long)Math.Ceiling(retryAfterSeconds.Value / 60));
        return ActivationText.Of("web.auth.quickConnect.tooManyCodesMinutes",
            minutes == 1 ? "You've tried too many codes. Try again in {minutes} minute." : "You've tried too many codes. Try again in {minutes} minutes.",
            ("minutes", minutes));
    }

    /// <summary>A PIN lockout is the profile's own limit, not the code budget.</summary>
    public static ActivationText PinLockout(double? retryAfterSeconds)
    {
        var minutes = Math.Max(1, (long)Math.Ceiling((retryAfterSeconds is > 0 ? retryAfterSeconds.Value : 60) / 60));
        return ActivationText.Of("web.auth.quickConnect.pinLockout",
            minutes == 1 ? "Too many incorrect PINs. Try again in {count} minute." : "Too many incorrect PINs. Try again in {count} minutes.",
            ("count", minutes));
    }

    public static ActivationText WrongPin { get; } = ActivationText.Of("web.auth.quickConnect.wrongPin", "That PIN isn't right.");
    public static ActivationText SessionEnded { get; } = ActivationText.Of("web.auth.quickConnect.sessionEnded", "Your session ended. Sign in again to approve.");

    // ----- Decisions -----

    public static DecisionFailureKind ClassifyDecisionFailure(int status, string? problemType)
    {
        if (status == 429) return DecisionFailureKind.TooManyCodes;
        // Only the account's own login session decides for a device, never an admin viewing as someone;
        // trying again cannot help.
        if (status == 403 && problemType == "permission_denied") return DecisionFailureKind.Impersonation;
        // A conflict or gone usually means the request moved on; the reload shows why.
        if (status is 404 or 409 or 410) return DecisionFailureKind.Moved;
        return DecisionFailureKind.Failed;
    }

    /// <summary>After an approve or decline of a device sign-in fails.</summary>
    public static DecisionFailurePlan PlanDecisionFailure(bool isQuickConnect, bool approve, int status, string? problemType, double? retryAfterSeconds, string? username)
    {
        var kind = ClassifyDecisionFailure(status, problemType);
        var moved = kind == DecisionFailureKind.Moved;
        // Every Quick Connect lookup spends the account's code budget, so a decision on one reloads only when the request moved on.
        var reload = !isQuickConnect || moved;
        return kind switch
        {
            DecisionFailureKind.TooManyCodes => new(reload, TooManyCodes(retryAfterSeconds), null),
            DecisionFailureKind.Impersonation => new(reload, Impersonation(username), null),
            // If the request is still pending after the reload, the server refused the decision itself.
            DecisionFailureKind.Moved => new(true, null, DecisionFailed(approve)),
            _ => new(reload, DecisionFailed(approve), null),
        };
    }

    /// <summary>After a Quick Connect approval for a chosen profile failed. Moved means reload the request.</summary>
    public static (DecisionFailureKind Kind, ActivationText? Error) ClassifyQuickConnectFailure(int status, string? problemType, double? retryAfterSeconds)
    {
        if (status == 429) return (DecisionFailureKind.TooManyCodes, TooManyCodes(retryAfterSeconds));
        if (status is 404 or 409 or 410) return (DecisionFailureKind.Moved, null);
        if (problemType == "permission_denied")
            return (DecisionFailureKind.Impersonation, ActivationText.Of("web.auth.quickConnect.impersonating", "Only the account itself can approve a device, not an admin viewing as it."));
        return (DecisionFailureKind.Failed, ActivationText.Of("web.auth.quickConnect.approveFailed", "Couldn't approve sign-in. Try again."));
    }

    // ----- Quick Connect profile choice -----

    /// <summary>The picked profile, else the one this session is using, else the first.</summary>
    public static string? SelectedProfileId(string? chosenId, IReadOnlyList<Profile> profiles, string? activeProfileId)
        => chosenId ?? (profiles.Any(profile => profile.Id == activeProfileId) ? activeProfileId : profiles.FirstOrDefault()?.Id);

    /// <summary>A profile with a PIN needs it, unless it is the profile this session already unlocked.</summary>
    public static bool NeedsPin(Profile? selected, string? activeProfileId) => selected is { HasPin: true } && selected.Id != activeProfileId;

    public static bool CanApproveQuickConnect(bool disabled, Profile? selected, bool needsPin, string? pin)
        => !disabled && selected != null && !(needsPin && string.IsNullOrWhiteSpace(pin));
}
