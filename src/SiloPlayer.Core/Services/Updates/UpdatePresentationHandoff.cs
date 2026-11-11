using System.Globalization;

namespace SiloPlayer.Core.Services.Updates;

/// <summary>Presentation only. Both pipe peers must already be authenticated by the caller.</summary>
public static class UpdatePresentationHandoff
{
    public static async Task AcceptAsync(Stream pipe, Action<long> swap, CancellationToken ct)
    {
        var offer = await UpdateHostProtocol.ReadAsync(pipe, ct);
        if (offer.Kind != "presentation-ready" || !long.TryParse(offer.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var hwnd) || hwnd <= 0)
            throw new InvalidDataException("Invalid update presentation handoff.");
        ct.ThrowIfCancellationRequested();
        // The caller checks that HWND belongs to the authenticated helper before retiring its safe view.
        swap(hwnd);
        await UpdateHostProtocol.WriteAsync(pipe, new("present"), ct);
        if ((await UpdateHostProtocol.ReadAsync(pipe, ct)).Kind != "visible")
            throw new InvalidDataException("The update window did not accept presentation.");
    }

    public static async Task OfferAsync(Stream pipe, long hwnd, Action startTracking, CancellationToken ct)
    {
        if (hwnd <= 0) throw new InvalidDataException("Invalid update window.");
        await UpdateHostProtocol.WriteAsync(pipe, new("presentation-ready", Text: hwnd.ToString(CultureInfo.InvariantCulture)), ct);
        if ((await UpdateHostProtocol.ReadAsync(pipe, ct)).Kind != "present")
            throw new InvalidDataException("Invalid update presentation acceptance.");
        startTracking();
        await UpdateHostProtocol.WriteAsync(pipe, new("visible"), ct);
    }
}
