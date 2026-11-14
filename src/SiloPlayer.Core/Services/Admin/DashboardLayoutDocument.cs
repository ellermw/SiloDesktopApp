using System.Text;
using System.Text.Json;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The v1 layout document and the arrangement edits. Version 1 stays version
/// 1 with rows and per-widget windows: both are additive, and sanitizing fills
/// an absent value from the widget's defaults. Server and cache documents are
/// sanitized identically; unknown ids drop, renamed ids resolve, duplicates
/// collapse onto the first, spans/rows clamp to the registry.
/// </summary>
public static class DashboardLayoutDocument
{
    /// <summary>Returns null when the value is not a v1 layout document at all ("no layout here").</summary>
    public static IReadOnlyList<DashboardLayoutEntry>? Sanitize(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.Object } root) return null;
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
            || !version.TryGetDouble(out var versionNumber) || versionNumber != 1) return null;
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DashboardLayoutEntry>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String) continue;
            var widget = DashboardRegistry.Find(idElement.GetString());
            if (widget == null || !seen.Add(widget.Id)) continue;
            result.Add(new DashboardLayoutEntry(
                widget.Id,
                ClampAxis(entry, "span", widget.MinSpan, widget.MaxSpan, widget.DefaultSpan),
                ClampAxis(entry, "rows", widget.MinRows, widget.MaxRows, widget.DefaultRows),
                SanitizeRange(entry.TryGetProperty("range", out var range) ? range : null, widget)));
        }
        return result;
    }

    public static IReadOnlyList<DashboardLayoutEntry>? Sanitize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return Sanitize(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The document as sent to the server and the cache: {"version":1,"entries":[...]}.</summary>
    public static string Serialize(IReadOnlyList<DashboardLayoutEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteStartArray("entries");
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("id", entry.Id);
                writer.WriteNumber("span", entry.Span);
                writer.WriteNumber("rows", entry.Rows);
                if (entry.Range is { } range) writer.WriteString("range", DashboardRanges.WireName(range));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static bool SameArrangement(IReadOnlyList<DashboardLayoutEntry> left, IReadOnlyList<DashboardLayoutEntry> right)
        => left.Count == right.Count && left.Zip(right).All(pair => pair.First == pair.Second);

    /// <summary>Moves a placed widget in front of <paramref name="beforeId"/>, or to the end.</summary>
    public static IReadOnlyList<DashboardLayoutEntry> Move(IReadOnlyList<DashboardLayoutEntry> entries, string id, string? beforeId)
    {
        if (id == beforeId) return entries;
        var moving = entries.FirstOrDefault(entry => entry.Id == id);
        if (moving == null) return entries;
        var without = entries.Where(entry => entry.Id != id).ToList();
        var index = beforeId == null ? -1 : without.FindIndex(entry => entry.Id == beforeId);
        if (index == -1) without.Add(moving);
        else without.Insert(index, moving);
        return SameArrangement(without, entries) ? entries : without;
    }

    /// <summary>Resizes one or both axes; an omitted axis keeps its value. Each axis clamps independently.</summary>
    public static IReadOnlyList<DashboardLayoutEntry> Resize(IReadOnlyList<DashboardLayoutEntry> entries, string id, int? span, int? rows)
    {
        var widget = DashboardRegistry.Find(id);
        if (widget == null) return entries;
        var changed = false;
        var next = entries.Select(entry =>
        {
            if (entry.Id != id) return entry;
            var nextSpan = span is { } s ? Math.Clamp(s, widget.MinSpan, widget.MaxSpan) : entry.Span;
            var nextRows = rows is { } r ? Math.Clamp(r, widget.MinRows, widget.MaxRows) : entry.Rows;
            if (nextSpan == entry.Span && nextRows == entry.Rows) return entry;
            changed = true;
            return entry with { Span = nextSpan, Rows = nextRows };
        }).ToArray();
        return changed ? next : entries;
    }

    /// <summary>Changes a widget's window; a window the widget does not offer is ignored.</summary>
    public static IReadOnlyList<DashboardLayoutEntry> SetRange(IReadOnlyList<DashboardLayoutEntry> entries, string id, DashboardWidgetRange range)
    {
        var widget = DashboardRegistry.Find(id);
        if (widget?.Ranges == null || !widget.Ranges.Allowed.Contains(range)) return entries;
        var changed = false;
        var next = entries.Select(entry =>
        {
            if (entry.Id != id || entry.Range == range) return entry;
            changed = true;
            return entry with { Range = range };
        }).ToArray();
        return changed ? next : entries;
    }

    public static IReadOnlyList<DashboardLayoutEntry> Remove(IReadOnlyList<DashboardLayoutEntry> entries, string id)
        => entries.Any(entry => entry.Id == id) ? entries.Where(entry => entry.Id != id).ToArray() : entries;

    /// <summary>Places a hidden widget at its defaults, in front of <paramref name="beforeId"/> or appended.</summary>
    public static IReadOnlyList<DashboardLayoutEntry> Add(IReadOnlyList<DashboardLayoutEntry> entries, string id, string? beforeId = null)
    {
        var widget = DashboardRegistry.Find(id);
        if (widget == null || entries.Any(entry => entry.Id == widget.Id)) return entries;
        var added = DashboardRegistry.DefaultEntry(widget);
        var list = entries.ToList();
        var index = beforeId == null ? -1 : list.FindIndex(entry => entry.Id == beforeId);
        if (index == -1) list.Add(added);
        else list.Insert(index, added);
        return list;
    }

    /// <summary>Widgets not on the dashboard, in registry order.</summary>
    public static IReadOnlyList<DashboardWidgetDefinition> Hidden(IReadOnlyList<DashboardLayoutEntry> entries)
    {
        var visible = entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        return DashboardRegistry.Widgets.Where(widget => !visible.Contains(widget.Id)).ToArray();
    }

    /// <summary>The window to render: the entry's own, else the registry default (entries can predate ranges).</summary>
    public static DashboardWidgetRange EffectiveRange(DashboardLayoutEntry entry)
        => entry.Range ?? DashboardRegistry.Find(entry.Id)?.Ranges?.Default ?? DashboardWidgetRange.Day;

    private static int ClampAxis(JsonElement entry, string name, int min, int max, int fallback)
    {
        if (!entry.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)
            || double.IsNaN(number) || double.IsInfinity(number))
            return fallback;
        return (int)Math.Clamp(Math.Round(number, MidpointRounding.AwayFromZero), min, max);
    }

    private static DashboardWidgetRange? SanitizeRange(JsonElement? value, DashboardWidgetDefinition widget)
    {
        if (widget.Ranges == null) return null;
        if (value is { ValueKind: JsonValueKind.String } text && DashboardRanges.Parse(text.GetString()) is { } range
            && widget.Ranges.Allowed.Contains(range))
            return range;
        return widget.Ranges.Default;
    }
}
