using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One run of a rich catalog message: <see cref="Tag"/> is <c>""</c>, <c>link</c> or <c>muted</c>.</summary>
public sealed record AdminTextRun(string Tag, string Text);

/// <summary>Display text for Configure › Network access (official NetworkAccessSettings.tsx).</summary>
public static class AdminNetworkAccessText
{
    private static readonly Dictionary<string, string> StateKeys = new(StringComparer.Ordinal)
    {
        [AdminNetworkAccessHostStatus.Disconnected] = "web.admin.settings.network.state.disconnected",
        [AdminNetworkAccessHostStatus.AwaitingAuthorization] = "web.admin.settings.network.state.awaitingAuthorization",
        [AdminNetworkAccessHostStatus.Connecting] = "web.admin.settings.network.state.connecting",
        [AdminNetworkAccessHostStatus.Connected] = "web.admin.settings.network.state.connected",
        [AdminNetworkAccessHostStatus.ErrorState] = "web.admin.settings.network.state.error",
        [AdminNetworkAccessHostStatus.Unavailable] = "web.admin.settings.network.state.unavailable",
    };

    public static string T(string key, object? values = null) => AdminText.T(key, values);

    /// <summary>Plain wording for a published state; an unpublished provider state is shown as sent (official stateLabel).</summary>
    public static string State(string state) => StateKeys.TryGetValue(state, out var key) ? T(key) : state;

    /// <summary>Official hostLabel: "{name} · {role}", or the role alone for an unnamed host.</summary>
    public static string HostLabel(AdminNetworkAccessHost host)
    {
        var role = T(host.Role == "api" ? "web.admin.settings.network.host.apiServer" : "web.admin.settings.network.host.proxyNode");
        return host.Name.Length > 0 ? T("web.admin.settings.network.host.namedRole", new { name = host.Name, role }) : role;
    }

    /// <summary>
    /// Splits a catalog pattern on its <c>&lt;tag&gt;…&lt;/tag&gt;</c> markup first, then formats each
    /// segment, so a value supplied by a plugin or server can never introduce a link.
    /// </summary>
    public static IReadOnlyList<AdminTextRun> Rich(string key, object? values = null)
    {
        var pattern = AdminText.Pattern(key);
        var runs = new List<AdminTextRun>();
        var position = 0;
        while (position < pattern.Length)
        {
            var open = pattern.IndexOf('<', position);
            var close = open < 0 ? -1 : pattern.IndexOf('>', open);
            var tag = close > open + 1 ? pattern[(open + 1)..close] : "";
            var end = tag.Length > 0 && tag.All(char.IsLetter) ? pattern.IndexOf("</" + tag + ">", close, StringComparison.Ordinal) : -1;
            if (end < 0)
            {
                Add(runs, "", pattern[position..], values);
                break;
            }
            if (open > position) Add(runs, "", pattern[position..open], values);
            Add(runs, tag, pattern[(close + 1)..end], values);
            position = end + tag.Length + 3;
        }
        return runs;
    }

    private static void Add(List<AdminTextRun> runs, string tag, string segment, object? values)
    {
        var text = AdminText.Format(segment, Values(values));
        if (text.Length > 0) runs.Add(new(tag, text));
    }

    private static IReadOnlyDictionary<string, object?> Values(object? values)
    {
        if (values is null) return new Dictionary<string, object?>();
        if (values is IReadOnlyDictionary<string, object?> ready) return ready;
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in values.GetType().GetProperties()) result[property.Name] = property.GetValue(values);
        return result;
    }
}
