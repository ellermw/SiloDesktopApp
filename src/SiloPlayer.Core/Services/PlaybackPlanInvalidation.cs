using System.Text.Json;

namespace SiloPlayer.Core.Services;

public sealed record PlaybackPlanInvalidation(string PlanId, string Reason)
{
    public static bool TryCreate(
        IReadOnlyDictionary<string, JsonElement>? payload,
        out PlaybackPlanInvalidation invalidation)
    {
        invalidation = new PlaybackPlanInvalidation("", "");
        if (payload == null ||
            !payload.TryGetValue("plan_id", out var planIdElement) ||
            planIdElement.ValueKind != JsonValueKind.String ||
            !payload.TryGetValue("reason", out var reasonElement) ||
            reasonElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var planId = planIdElement.GetString()?.Trim();
        var reason = reasonElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(planId) || string.IsNullOrWhiteSpace(reason))
            return false;

        invalidation = new PlaybackPlanInvalidation(planId, reason);
        return true;
    }
}
