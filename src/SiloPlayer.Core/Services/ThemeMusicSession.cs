namespace SiloPlayer.Core.Services;

public sealed class ThemeSongSet
{
    public string OwnerId { get; set; } = "";
    public List<ThemeSong> Items { get; set; } = [];
}
public sealed class ThemeSong { public string Id { get; set; } = ""; }
public sealed class ThemeAudioGrant
{
    public string Url { get; set; } = "";
    public string Delivery { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}
public interface IThemeAudioSink
{
    void Load(ThemeAudioGrant grant, bool loop);
    void Pause(); void Resume(); void Stop();
    void SetLoop(bool loop) { }
}

/// <summary>One audio owner across detail navigation. Pending grants never outlive that owner.</summary>
public sealed class ThemeMusicSession(Func<string, string, CancellationToken, Task<ThemeAudioGrant>> authorize, IThemeAudioSink sink) : IDisposable
{
    private CancellationTokenSource? _request;
    private long _generation;
    private string? _owner, _theme, _suppressedOwner;
    private bool _loop, _suspended, _loaded, _converted;
    public async Task SelectAsync(ThemeSongSet? selection, bool loop, CancellationToken ct = default)
    {
        var song = selection?.Items.FirstOrDefault();
        if (selection == null || song == null) { Stop(); return; }
        if (_suppressedOwner != selection.OwnerId) _suppressedOwner = null;
        if (_suppressedOwner == selection.OwnerId) return;
        _loop = loop;
        if (_owner == selection.OwnerId && _theme == song.Id)
        {
            _suspended = false;
            if (_loaded) { sink.SetLoop(_loop && !_converted); sink.Resume(); return; }
        }
        else { Stop(); _owner = selection.OwnerId; _theme = song.Id; }
        _suspended = false;
        await LoadAsync(ct);
    }
    private async Task LoadAsync(CancellationToken ct)
    {
        _request?.Cancel(); _request?.Dispose(); _request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _request.CancelAfter(TimeSpan.FromSeconds(8));
        var generation = ++_generation; var token = _request.Token;
        try
        {
            var grant = await authorize(_owner!, _theme!, token);
            if (generation != _generation || token.IsCancellationRequested || _suspended || _owner == _suppressedOwner) return;
            _converted = grant.Delivery == "converted"; sink.Load(grant, _loop && !_converted); _loaded = true;
        }
        catch (OperationCanceledException) { }
    }
    public async Task EndedAsync()
    { if (_loop && _converted && !_suspended && _owner != null && _owner != _suppressedOwner) await LoadAsync(CancellationToken.None); }
    public void Suspend() { _suspended = true; ++_generation; _request?.Cancel(); sink.Pause(); }
    public void Interrupt() { if (_owner != null) _suppressedOwner = _owner; Stop(); }
    public void Stop()
    { ++_generation; _request?.Cancel(); _request?.Dispose(); _request = null; sink.Stop(); _owner = _theme = null; _loaded = false; _suspended = false; }
    public void ResetAuthority() { Stop(); _suppressedOwner = null; }
    public void Dispose() => ResetAuthority();
}
