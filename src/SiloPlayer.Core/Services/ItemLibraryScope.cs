using System.Globalization;
using System.Text.Json;

namespace SiloPlayer.Core.Services;

/// <summary>Identity candidates belong to one inventory, file selection and library choice.</summary>
public sealed class ItemLibraryScope(int? pageLibraryId, bool split, bool hasInventory)
{
    private Dictionary<string, int?> _files = [];
    private HashSet<string> _selected = [];
    private int? _picked;
    private long _revision;
    public IReadOnlyList<int> LibraryIds => _files.Values.OfType<int>().Distinct().Order().ToArray();
    public bool Loaded { get; private set; } = !hasInventory;
    public int? LibraryId
    {
        get
        {
            if (!hasInventory) return pageLibraryId;
            var libraries = LibraryIds;
            if (split && _selected.Count > 0 && _selected.All(id => _files.TryGetValue(id, out var library) && library.HasValue))
            {
                var selectedLibraries = _selected.Select(id => _files[id]).Distinct().ToArray();
                if (selectedLibraries.Length == 1) return selectedLibraries[0];
            }
            if (pageLibraryId.HasValue && libraries.Contains(pageLibraryId.Value)) return pageLibraryId;
            if (pageLibraryId.HasValue && !split) return null;
            return libraries.Count == 1 ? libraries[0] : _picked;
        }
    }
    public bool NeedsChoice => hasInventory && Loaded && LibraryIds.Count > 1 && !LibraryId.HasValue && (!pageLibraryId.HasValue || split);
    public bool CanSearch => Loaded && (!hasInventory || LibraryId.HasValue || !pageLibraryId.HasValue && LibraryIds.Count == 0);
    public bool ReplaceFiles(IEnumerable<JsonElement> files)
    {
        var next = files.ToDictionary(file => file.GetProperty("id").ToString(), file =>
            file.TryGetProperty("library_id", out var value) && int.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? (int?)id : null);
        var changed = !Loaded || next.Count != _files.Count || next.Any(pair => !_files.TryGetValue(pair.Key, out var library) || library != pair.Value);
        Loaded = true;
        if (!changed) return false;
        _files = next;
        _selected.RemoveWhere(id => !_files.ContainsKey(id));
        if (_picked.HasValue && !LibraryIds.Contains(_picked.Value)) _picked = null;
        _revision++;
        return true;
    }
    public bool SelectFiles(IEnumerable<string> ids)
    {
        var next = ids.ToHashSet(StringComparer.Ordinal);
        if (_selected.SetEquals(next)) return false;
        _selected = next; _revision++; return true;
    }
    public bool Pick(int? libraryId)
    {
        var next = libraryId.HasValue && LibraryIds.Contains(libraryId.Value) ? libraryId : null;
        if (_picked == next) return false;
        _picked = next; _revision++; return true;
    }
    public Scope Capture() => new(_revision, LibraryId);
    public bool IsCurrent(Scope scope) => CanSearch && scope == Capture();
    public readonly record struct Scope(long Revision, int? LibraryId);
}
