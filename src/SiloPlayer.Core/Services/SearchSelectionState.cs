namespace SiloPlayer.Core.Services;

/// <summary>Keyboard selection follows identity when independently loaded groups arrive.</summary>
public sealed class SearchSelectionState
{
    private string[] _keys = [];
    public int Index { get; private set; } = -1;

    public void Replace(IEnumerable<string> keys)
    {
        var selected = Index >= 0 && Index < _keys.Length ? _keys[Index] : null;
        _keys = keys.ToArray();
        Index = selected == null ? -1 : Array.IndexOf(_keys, selected);
    }

    public void ClearSelection() => Index = -1;

    public int Move(int direction)
    {
        if (_keys.Length == 0) return Index = -1;
        return Index = Math.Clamp(Index + (direction < 0 ? -1 : 1), -1, _keys.Length - 1);
    }
}
