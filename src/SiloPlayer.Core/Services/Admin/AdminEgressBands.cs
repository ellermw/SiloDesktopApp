using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>What the band rules need to know about one egress source: the API, or a stream node (official <c>EgressSourceIdentity</c>).</summary>
public sealed record EgressSourceIdentity(string Key, string Kind, string Name, string? NodeId);

/// <summary>
/// The egress band rules the Dashboard's Egress widget and the Insights Bandwidth tab share (official <c>egressBands.ts</c>, c59798116):
/// how many sources get a band of their own, which colour slot each takes and what its legend says. A source's colour comes from the source
/// itself (the API, or the node's id), not from its rank, so a node keeps its colour as others go idle; four slots cannot keep more than four
/// sources apart, so on a clash the API and then the lower node id keep the colour and the other takes the next free one.
/// </summary>
public static class AdminEgressBands
{
    /// <summary>How many sources get a band of their own; the rest share "Other" so the legend stays one line.</summary>
    public const int MaxNamedBands = 4;

    /// <summary>The chart colour slots of named bands. Slot 1 (teal) is skipped because the total view uses it for Downloads.</summary>
    public static IReadOnlyList<int> BandColorSlots { get; } = [0, 2, 3, 4];

    public static EgressSourceIdentity Identity(DashboardEgressSource source) => new(source.Key, source.Kind, source.Name, source.NodeId);

    public static EgressSourceIdentity Identity(AdminInsightsEgressSource source) => new(source.Key, source.Kind, source.Name, source.NodeId);

    /// <summary>
    /// Legend labels by source key. Node names need not be unique, so a name two sources share gets each node's id appended; a node id of
    /// "0" is skipped (it is not an identity). <paramref name="apiLabel"/> replaces the API source's own name (Insights says "API server").
    /// </summary>
    public static IReadOnlyDictionary<string, string> Labels(IReadOnlyList<EgressSourceIdentity> sources, string? apiLabel = null)
    {
        var names = sources.Select(source => (Source: source, Name: source.Kind == "api" && !string.IsNullOrEmpty(apiLabel) ? apiLabel : source.Name)).ToList();
        var uses = names.GroupBy(item => item.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (source, name) in names)
            labels[source.Key] = uses[name] > 1 && source.NodeId != null && source.NodeId != "0"
                ? DashboardText.T("web.admin.dashboard.widgets.egress.duplicateNodeName", ("name", name), ("id", source.NodeId))
                : name;
        return labels;
    }

    private static int PreferredIndex(EgressSourceIdentity source)
    {
        if (source.Kind == "api") return 0;
        return long.TryParse(source.NodeId, out var id) && id >= 0 ? (int)(id % BandColorSlots.Count) : 0;
    }

    /// <summary>
    /// The chart colour slot of each source, claimed in identity order (the API, then nodes by ascending id) so a collision always resolves
    /// the same way across both Dashboard widgets and Insights charts (official <c>assignEgressSourceSlots</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, int> Slots(IReadOnlyList<EgressSourceIdentity> sources)
    {
        static double NodeNumber(EgressSourceIdentity source) => double.TryParse(source.NodeId, out var number) ? number : 0;
        var byIdentity = sources.OrderByDescending(source => source.Kind == "api").ThenBy(NodeNumber).ToList();
        var taken = new HashSet<int>();
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in byIdentity)
        {
            var preferred = PreferredIndex(source);
            for (var offset = 0; offset < BandColorSlots.Count; offset++)
            {
                var index = (preferred + offset) % BandColorSlots.Count;
                if (taken.Add(index))
                {
                    slots[source.Key] = BandColorSlots[index];
                    break;
                }
            }
        }
        return slots;
    }
}
