using System.Globalization;
using System.Text.Json;

namespace SiloPlayer.Core.Services;

/// <summary>Identity candidates belong to one inventory, file selection and library choice.</summary>
public sealed class ItemLibraryScope(int? pageLibraryId, bool split, bool hasInventory)
{
    private Dictionary<string, int?> _files = [];
    private Dictionary<string, FileInventory> _inventory = [];
    private readonly record struct FileInventory(string Path, string Root, string Season, string Episode);
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
            if (SelectedLibraryId.HasValue) return SelectedLibraryId;
            if (pageLibraryId.HasValue && libraries.Contains(pageLibraryId.Value)) return pageLibraryId;
            if (pageLibraryId.HasValue && !split) return null;
            return libraries.Count == 1 ? libraries[0] : _picked;
        }
    }
    private int? SelectedLibraryId => split && _selected.Count > 0 && _selected.All(id => _files.TryGetValue(id, out var library) && library.HasValue)
        && _selected.Select(id => _files[id]).Distinct().ToArray() is { Length: 1 } libraries ? libraries[0] : null;
    public bool AllowsChoice => hasInventory && Loaded && LibraryIds.Count > 1 && !SelectedLibraryId.HasValue
        && (!pageLibraryId.HasValue || split && !LibraryIds.Contains(pageLibraryId.Value));
    public bool NeedsChoice => hasInventory && Loaded && LibraryIds.Count > 1 && !LibraryId.HasValue && (!pageLibraryId.HasValue || split);
    public bool CanSearch => Loaded && (!hasInventory || LibraryId.HasValue || LibraryIds.Count == 0);
    public bool ReplaceFiles(IEnumerable<JsonElement> files)
    {
        var snapshot = files.ToArray();
        var next = snapshot.ToDictionary(file => file.GetProperty("id").ToString(), file =>
            file.TryGetProperty("library_id", out var value) && int.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? (int?)id : null);
        static string Value(JsonElement file, string name) => file.TryGetProperty(name, out var value) ? value.ToString() : "";
        // Complete AdminItemFile contract: IDs/membership plus the paths and episode identity used by Split.
        var inventory = snapshot.ToDictionary(file => file.GetProperty("id").ToString(), file =>
            new FileInventory(Value(file, "file_path"), Value(file, "observed_root_path"), Value(file, "season_number"), Value(file, "episode_number")));
        var changed = !Loaded || next.Count != _files.Count || next.Any(pair => !_files.TryGetValue(pair.Key, out var library) || library != pair.Value)
            || inventory.Any(pair => !_inventory.TryGetValue(pair.Key, out var previous) || previous != pair.Value);
        Loaded = true;
        if (!changed) return false;
        _files = next;
        _inventory = inventory;
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
    public bool IsCurrent(Scope scope) => CanSearch && IsInventoryCurrent(scope);
    public bool IsInventoryCurrent(Scope scope) => Loaded && scope == Capture();
    public readonly record struct Scope(long Revision, int? LibraryId);
}
