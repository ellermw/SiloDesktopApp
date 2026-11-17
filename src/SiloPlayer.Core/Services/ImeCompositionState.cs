namespace SiloPlayer.Core.Services;

/// <summary>
/// Official #4287 (9eb3df6f3): a key that belongs to an input method editor
/// (IME) must not trigger app actions. Japanese and Chinese typists press Enter
/// to confirm a conversion, the arrow keys to pick a candidate and Escape to
/// cancel, so a custom key handler on a text field leaves those keys alone
/// while a composition is active.
/// </summary>
/// <remarks>
/// The WebUI checks <c>isComposing || keyCode === 229</c>. The native
/// equivalents are the TextBox composition events plus VK_PROCESSKEY (229),
/// which Windows reports for a key the IME consumed. Like Safari, WinUI can end
/// the composition before the confirming key reaches a handler, so the first
/// IME key that arrives within <see cref="ConfirmWindow"/> of the end is still
/// treated as the IME's. Any key consumes that allowance, and so does time, so
/// the next ordinary Enter acts exactly once even when the IME swallowed the
/// confirming key.
/// </remarks>
public sealed class ImeCompositionState
{
    /// <summary>VK_PROCESSKEY: the code Windows reports for a key the IME handled.</summary>
    public const int ProcessKey = 229;

    /// <summary>How long after the composition ends its confirming key may still arrive.</summary>
    public static readonly TimeSpan ConfirmWindow = TimeSpan.FromMilliseconds(200);

    private const int Back = 8, Enter = 13, Escape = 27, Left = 37, Up = 38, Right = 39, Down = 40;

    private readonly Func<long> _nowMilliseconds;
    private long? _endedAt;

    public ImeCompositionState() : this(static () => Environment.TickCount64) { }

    public ImeCompositionState(Func<long> nowMilliseconds) => _nowMilliseconds = nowMilliseconds;

    /// <summary>Whether an IME composition is active in the tracked field.</summary>
    public bool IsComposing { get; private set; }

    public void Start()
    {
        IsComposing = true;
        _endedAt = null;
    }

    public void End()
    {
        IsComposing = false;
        _endedAt = _nowMilliseconds();
    }

    /// <summary>Forgets any composition, for a field that lost focus mid-composition.</summary>
    public void Reset()
    {
        IsComposing = false;
        _endedAt = null;
    }

    /// <summary>
    /// Whether the handler must leave this key to the IME. Call it once per key
    /// event, before acting on the key: it consumes the confirming-key allowance.
    /// </summary>
    public bool ShouldIgnoreKey(int key) => ShouldIgnoreKey(key, key);

    /// <inheritdoc cref="ShouldIgnoreKey(int)"/>
    public bool ShouldIgnoreKey(int key, int originalKey)
    {
        if (IsComposing || key == ProcessKey || originalKey == ProcessKey) return true;
        if (_endedAt is not { } endedAt) return false;
        _endedAt = null;
        return _nowMilliseconds() - endedAt <= (long)ConfirmWindow.TotalMilliseconds && IsImeKey(key);
    }

    private static bool IsImeKey(int key) => key is Enter or Escape or Back or Left or Up or Right or Down;
}
