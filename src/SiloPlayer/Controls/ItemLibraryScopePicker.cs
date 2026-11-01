using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

/// <summary>Uses only item inventory; never guesses from an account's first library.</summary>
internal sealed class ItemLibraryScopePicker : StackPanel
{
    private readonly MediaSplitApi _api;
    private readonly ApiRequestContext _context;
    private readonly string _itemId;
    private readonly bool _inventory;
    private readonly Func<bool> _current;
    private readonly CancellationToken _lifetime;
    private readonly SemaphoreSlim _refreshGate = new(1);
    private readonly ComboBox _libraries = new() { PlaceholderText = "Choose a library", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _refresh = new() { Content = "Refresh libraries", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _label = new() { Text = "Library", FontSize = 14 };
    private bool _changing, _available, _editing = true;
    public ItemLibraryScope State { get; }
    public IReadOnlyList<System.Text.Json.JsonElement> Files { get; private set; } = [];
    public event Action? Changed;
    public bool CanSearch => _available && _current() && State.CanSearch;
    public bool InventoryAvailable => _available && _current() && State.Loaded;
    public bool IsCurrent(ItemLibraryScope.Scope scope) => CanSearch && State.IsCurrent(scope);
    public bool IsInventoryCurrent(ItemLibraryScope.Scope scope) => InventoryAvailable && State.IsInventoryCurrent(scope);

    public ItemLibraryScopePicker(SiloApiClient client, ApiRequestContext context, string itemId, int? libraryId,
        bool split, bool inventory, Func<bool> current, CancellationToken lifetime)
    {
        _api = new(client); _context = context; _itemId = itemId; _inventory = inventory; _current = current; _lifetime = lifetime;
        State = new(libraryId, split, inventory); _available = !inventory;
        Spacing = 8; Visibility = inventory ? Visibility.Visible : Visibility.Collapsed;
        Children.Add(_label); Children.Add(_libraries); Children.Add(_refresh); Children.Add(_status);
        AutomationProperties.SetName(_libraries, "Identity search library"); AutomationProperties.SetName(_refresh, "Refresh item libraries");
        _libraries.SelectionChanged += (_, _) =>
        {
            if (_changing || !_editing || !_current() || !State.AllowsChoice) return;
            if (State.Pick((_libraries.SelectedItem as ComboBoxItem)?.Tag as int?)) { Changed?.Invoke(); Render(); }
        };
        _refresh.Click += async (_, _) => { try { await RefreshAsync(); } catch (OperationCanceledException) { } catch (Exception) { /* The status is shown by RefreshAsync. */ } };
    }
    public void SelectFiles(IEnumerable<string> files)
    { if (State.SelectFiles(files)) { Changed?.Invoke(); Render(); } }
    public void SetEditing(bool enabled, bool canRefresh = false) { _editing = enabled; _libraries.IsEnabled = enabled && _current() && State.AllowsChoice; _refresh.IsEnabled = (enabled || canRefresh) && _current(); }
    public async Task RefreshAsync()
    {
        _lifetime.ThrowIfCancellationRequested();
        if (!_current()) throw new OperationCanceledException("Identity action owner changed.", _lifetime);
        if (!_inventory) return;
        await _refreshGate.WaitAsync(_lifetime);
        try
        {
            if (!_current()) throw new OperationCanceledException("Identity action owner changed.", _lifetime);
            var files = await _api.GetFilesAsync(_context, _itemId, _lifetime);
            if (!_current()) throw new OperationCanceledException("Identity action owner changed.", _lifetime);
            var wasAvailable = _available; _available = true;
            var changed = State.ReplaceFiles(files);
            if (changed) Files = files;
            if (changed || !wasAvailable) Changed?.Invoke();
            Render();
        }
        catch
        {
            _available = false; Changed?.Invoke(); _status.Text = "Item libraries could not be verified. Refresh to try again."; _status.Visibility = Visibility.Visible; throw;
        }
        finally { _refreshGate.Release(); }
    }
    private void Render()
    {
        _changing = true;
        _libraries.Items.Clear();
        foreach (var id in State.LibraryIds) _libraries.Items.Add(new ComboBoxItem { Content = $"Library {id}", Tag = id });
        _libraries.SelectedItem = _libraries.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (int)item.Tag == State.LibraryId);
        _label.Visibility = _libraries.Visibility = State.AllowsChoice ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = !State.Loaded ? "Loading item libraries…" : State.NeedsChoice ? "Choose the library whose identity settings should be used." : !State.CanSearch ? "No readable item library is available in this scope." : State.LibraryIds.Count == 0 ? "No library is assigned. Server defaults will be used." : "";
        _status.Visibility = string.IsNullOrEmpty(_status.Text) ? Visibility.Collapsed : Visibility.Visible;
        _changing = false; SetEditing(_editing);
    }
}
