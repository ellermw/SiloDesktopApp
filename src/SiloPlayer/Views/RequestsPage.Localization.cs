using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Views;

public sealed partial class RequestsPage
{
    private static string RequestDates(MediaRequest request)
    {
        var updated = LastChange(request);
        return AppStrings.Get(updated == null ? "web.requests.hub.requested" : "web.requests.hub.requestedUpdated",
            new Dictionary<string, object?> { ["created"] = FormatDate(request.CreatedAt), ["updated"] = updated },
            fallback: "Requested " + FormatDate(request.CreatedAt) + (updated == null ? "" : " · Updated " + updated));
    }
}
