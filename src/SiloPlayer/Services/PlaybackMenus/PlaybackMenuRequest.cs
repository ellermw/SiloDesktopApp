using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SiloPlayer.Services.PlaybackMenus;

internal readonly record struct PlaybackMenuAnchor(double Left, double Top, double Right, double Bottom);

internal sealed record PlaybackMenuRequest(string OwnerToken, long Sequence, PlaybackMenuAnchor Anchor)
{
    internal string Kind { get; init; } = "audio";

    internal static bool TryParse(string[] message, [NotNullWhen(true)] out PlaybackMenuRequest? request)
    {
        request = null;
        if (message.Length != 9 || message[0] is not ("silo-native-subtitle-open" or "silo-native-audio-open" or "silo-native-quality-open" or "silo-native-chapters-open" or "silo-native-speed-open" or "silo-native-more-open" or "silo-native-party-open")
            || !Guid.TryParseExact(message[1], "N", out var scope) || scope == Guid.Empty
            || !long.TryParse(message[2], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            || sequence is <= 0 or > 9_007_199_254_740_991)
            return false;

        var values = new double[6];
        for (var i = 0; i < values.Length; i++)
            if (!double.TryParse(message[i + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                || !double.IsFinite(values[i])) return false;
        var (x, y, width, height, surfaceWidth, surfaceHeight) =
            (values[0], values[1], values[2], values[3], values[4], values[5]);
        if (surfaceWidth is <= 0 or > 32768 || surfaceHeight is <= 0 or > 32768
            || x < 0 || y < 0 || width <= 0 || height <= 0
            || x + width > surfaceWidth || y + height > surfaceHeight)
            return false;

        request = new PlaybackMenuRequest(message[1], sequence,
            new PlaybackMenuAnchor(x / surfaceWidth, y / surfaceHeight,
                (x + width) / surfaceWidth, (y + height) / surfaceHeight)) { Kind = message[0][12..^5] };
        return true;
    }
}

/// <summary>UI-dispatcher-owned presentation identity; contains no playback/account data.</summary>
internal sealed class PlaybackMenuRequestGate
{
    private string? _ownerToken;
    private long _latestSequence;
    private PlaybackMenuRequest? _active;

    internal void ResetOwner(string token)
    {
        if (string.Equals(_ownerToken, token, StringComparison.Ordinal)) return;
        _ownerToken = token;
        _latestSequence = 0;
        _active = null;
    }

    internal bool TryOpen(PlaybackMenuRequest request)
    {
        if (!string.Equals(request.OwnerToken, _ownerToken, StringComparison.Ordinal)
            || request.Sequence <= _latestSequence) return false;
        _latestSequence = request.Sequence;
        _active = request;
        return true;
    }

    internal bool IsCurrent(PlaybackMenuRequest request) => ReferenceEquals(_active, request);

    internal bool TryClose(string token, long sequence)
    {
        if (_active is null || _active.Sequence != sequence
            || !string.Equals(_active.OwnerToken, token, StringComparison.Ordinal)) return false;
        _active = null;
        return true;
    }
}
