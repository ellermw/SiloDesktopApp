using System.Globalization;

namespace SiloPlayer.Core.Services;

/// <summary>Who started playback: the viewer, or an unattended countdown.</summary>
public enum PlaybackStartTrigger { Viewer, Automatic }

/// <summary>
/// The client-local <c>player.passout_threshold</c> contract: an integer 0–20,
/// default 3, where 0 never asks. It is stored on this device only and never
/// written through the server settings API.
/// </summary>
public static class StillWatchingThreshold
{
    public const int Default = 3;
    public const int Minimum = 0;
    public const int Maximum = 20;

    public static int Normalize(int? stored)
        => stored is int value && value >= Minimum && value <= Maximum ? value : Default;

    // Explicit seams for the shared catalog (settings contract label and
    // description; value 0 is "web.settings.value.disabled"). English fallbacks.
    public const string LabelFallback = "Still watching prompt";
    public const string DescriptionFallback = "How many episodes auto-play before Silo asks whether you are still watching. 0 never asks.";
    public const string DisabledValueKey = "web.settings.value.disabled";

    public static string ChoiceLabel(int threshold) => threshold switch
    {
        <= 0 => "Disabled",
        1 => "1 episode",
        _ => threshold.ToString(CultureInfo.InvariantCulture) + " episodes",
    };
}

/// <summary>
/// Pass-out protection for one viewing session (upstream #3917,
/// web/src/player/stillWatching.ts). With threshold N, N automatic advances in
/// a row are allowed and the next one waits for the viewer. Any viewer start or
/// playback interaction clears the count. The count is in memory because it
/// describes only this session on this device.
/// </summary>
public sealed class StillWatchingSession
{
    private readonly object _gate = new();
    private int _automaticStarts;
    private string? _expectedAutomaticContentId;
    // The outgoing title that already advanced unattended. Credits and the
    // ended countdown are two boundaries of the same title; only one advances.
    private string? _advancedSourceContentId;

    /// <summary>Raised after the count changes, outside the lock.</summary>
    public event Action? Changed;

    public int AutomaticStarts { get { lock (_gate) return _automaticStarts; } }

    /// <summary>Whether the next automatic start must wait for the viewer.</summary>
    public bool IsGated(int threshold)
    {
        var limit = StillWatchingThreshold.Normalize(threshold);
        lock (_gate) return limit > 0 && _automaticStarts >= limit;
    }

    /// <summary>A pause, seek, explicit next/previous or other viewer action.</summary>
    public void RecordViewerInteraction()
    {
        bool changed;
        lock (_gate)
        {
            changed = _automaticStarts != 0;
            _automaticStarts = 0;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Claims an unattended advance away from <paramref name="sourceContentId"/>.
    /// Returns false when a Watch Party owns advancement, when the viewer has
    /// to be asked first, or when that title already advanced unattended.
    /// <paramref name="nextContentId"/> is null for a shuffle whose server pick
    /// is not known yet; announce it later with <see cref="ExpectAutomaticStart"/>.
    /// </summary>
    public bool TryBeginAutomaticAdvance(string sourceContentId, string? nextContentId, int threshold, bool watchParty)
    {
        if (watchParty || string.IsNullOrEmpty(sourceContentId)) return false;
        var limit = StillWatchingThreshold.Normalize(threshold);
        lock (_gate)
        {
            if (limit > 0 && _automaticStarts >= limit) return false;
            if (string.Equals(_advancedSourceContentId, sourceContentId, StringComparison.Ordinal)) return false;
            _advancedSourceContentId = sourceContentId;
            _automaticStarts++;
            _expectedAutomaticContentId = nextContentId;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Whether <see cref="TryBeginAutomaticAdvance"/> would claim this advance now.</summary>
    public bool CanBeginAutomaticAdvance(string sourceContentId, int threshold, bool watchParty)
    {
        if (watchParty || string.IsNullOrEmpty(sourceContentId)) return false;
        var limit = StillWatchingThreshold.Normalize(threshold);
        lock (_gate)
            return !(limit > 0 && _automaticStarts >= limit) &&
                !string.Equals(_advancedSourceContentId, sourceContentId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Withdraws a claim on <paramref name="sourceContentId"/> whose start never
    /// happened (the pick failed, was cancelled or was superseded): the attempt
    /// no longer counts, the title's other boundary may still advance, and no
    /// announcement is left for a later viewer start. A claim already used up by
    /// <see cref="ConsumeStart"/> is left alone.
    /// </summary>
    public void CancelAutomaticAdvance(string sourceContentId)
    {
        bool changed;
        lock (_gate)
        {
            if (_advancedSourceContentId == null ||
                !string.Equals(_advancedSourceContentId, sourceContentId, StringComparison.Ordinal)) return;
            _advancedSourceContentId = null;
            _expectedAutomaticContentId = null;
            // A viewer interaction since the claim has already cleared the count.
            changed = _automaticStarts != 0;
            if (changed) _automaticStarts--;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Announces that the next start of this title is not the viewer's.</summary>
    public void ExpectAutomaticStart(string contentId)
    {
        lock (_gate) _expectedAutomaticContentId = contentId;
    }

    /// <summary>
    /// Called by every playback start. The announced automatic title is used up
    /// on a match; any other start is the viewer's choice and clears the count.
    /// Returns whether the start was automatic.
    /// </summary>
    public bool ConsumeStart(string contentId)
    {
        bool automatic, changed = false;
        lock (_gate)
        {
            automatic = _expectedAutomaticContentId is { } expected &&
                string.Equals(expected, contentId, StringComparison.Ordinal);
            _expectedAutomaticContentId = null;
            _advancedSourceContentId = null;
            if (!automatic && _automaticStarts != 0)
            {
                _automaticStarts = 0;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
        return automatic;
    }
}

/// <summary>
/// "Ends 2:38 AM" (upstream usePlayerEndTime.ts). The live wall-clock
/// estimate runs in the production OSC; the host resolves the profile's clock
/// and supplies the label template through these explicit seams.
/// </summary>
public static class PlaybackEndTime
{
    /// <summary>Catalog key upstream reuses for the label; argument <c>time</c>.</summary>
    public const string LabelKey = "player.scrubber.endsAt";
    public const string FallbackLabelTemplate = "Ends {time}";

    /// <summary>"12h" or "24h": the profile's explicit choice, else the display culture.</summary>
    public static string ResolveClock(string? preference, CultureInfo culture)
    {
        if (string.Equals(preference, "12h", StringComparison.OrdinalIgnoreCase)) return "12h";
        if (string.Equals(preference, "24h", StringComparison.OrdinalIgnoreCase)) return "24h";
        return HasTwelveHourSpecifier(culture.DateTimeFormat.ShortTimePattern) ? "12h" : "24h";
    }

    // Only an unquoted, unescaped "h" is the 12-hour specifier: fr-CA's 24-hour
    // "HH 'h' mm" carries a literal one.
    private static bool HasTwelveHourSpecifier(string pattern)
    {
        var quote = '\0';
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '\'' or '"') quote = c;
            else if (c == '\\') i++;
            else if (c == 'h') return true;
        }
        return false;
    }
}
