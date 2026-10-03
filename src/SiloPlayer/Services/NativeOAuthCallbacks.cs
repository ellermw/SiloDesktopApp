namespace SiloPlayer.Services;

/// <summary>Protocol activation enters here on the UI dispatcher; only pending attempts can accept it.</summary>
public static class NativeOAuthCallbacks
{
    private static readonly Dictionary<Guid, Func<string, Task<bool>>> Handlers = [];
    public static IDisposable Register(Func<string, Task<bool>> handler)
    {
        var id = Guid.NewGuid(); lock (Handlers) Handlers.Add(id, handler);
        return new Registration(id);
    }
    public static async Task<bool> TryHandleAsync(string uri)
    {
        Func<string, Task<bool>>[] pending; lock (Handlers) pending = Handlers.Values.ToArray();
        foreach (var handler in pending) if (await handler(uri)) return true;
        return false;
    }
    private sealed class Registration(Guid id) : IDisposable
    { public void Dispose() { lock (Handlers) Handlers.Remove(id); } }
}
