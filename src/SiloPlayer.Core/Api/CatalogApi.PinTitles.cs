using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Api;

public partial class CatalogApi
{
    private readonly object _pinTitlesGate = new();
    private sealed class PinMetadataRead
    {
        public readonly TaskCompletionSource<object> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly CancellationTokenSource Cancellation = new();
        public int Waiters = 1;
    }

    private readonly Dictionary<(ApiRequestContext Owner, long Language, int Library, bool Collections), PinMetadataRead> _pinTitleReads = [];
    private readonly Dictionary<(ApiRequestContext Owner, long Language, int Library, bool Collections), (object Value, DateTime Loaded)> _pinTitles = [];
    private readonly Dictionary<(ApiRequestContext Owner, long Language, int Library, bool Collections), DateTime> _pinTitleAttempts = [];
    private static readonly TimeSpan PinTitleLifetime = TimeSpan.FromMinutes(5);

    public event Action? PinTitlesChanged;

    public string ResolvePinLabel(int? libraryId, string type, string id, string fallback)
    {
        if (libraryId is not > 0 || type is not ("section" or "collection")) return fallback;
        var owner = client.CaptureMonitorContext().Authority;
        var language = client.LocalizedReads.Generation;
        lock (_pinTitlesGate)
        {
            if (!_pinTitles.TryGetValue((owner, language, libraryId.Value, type == "collection"), out var cached)) return fallback;
            return cached.Value switch
            {
                HomeLayoutResponse layout => layout.Sections.FirstOrDefault(section => section.Id == id)?.DisplayTitle is { Length: > 0 } sectionTitle ? sectionTitle : fallback,
                LibraryTabResponse tab => tab.Collections.FirstOrDefault(collection => collection.Id == id)?.Title is { Length: > 0 } title ? title : fallback,
                _ => fallback,
            };
        }
    }

    public async Task EnsureLibraryPinTitlesAsync(int libraryId, bool collections, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = (client.CaptureMonitorContext().Authority, client.LocalizedReads.Generation, libraryId, collections);
        lock (_pinTitlesGate)
        {
            if ((_pinTitles.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.Loaded < PinTitleLifetime) ||
                (_pinTitleAttempts.TryGetValue(key, out var attempted) && DateTime.UtcNow - attempted < PinTitleLifetime)) return;
            _pinTitleAttempts[key] = DateTime.UtcNow;
        }
        if (collections) await GetLibraryCollectionsAsync(libraryId, ct).ConfigureAwait(false);
        else await GetLibraryLayoutAsync(libraryId, ct).ConfigureAwait(false);
    }

    private Task<T> ReadLibraryPinMetadataAsync<T>(int libraryId, bool collections, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var context = client.CaptureMonitorContext();
        var key = (Owner: context.Authority, Language: client.LocalizedReads.Generation, Library: libraryId, Collections: collections);
        PinMetadataRead read;
        lock (_pinTitlesGate)
        {
            foreach (var retired in _pinTitles.Keys.Where(candidate => candidate.Owner != key.Owner || candidate.Language != key.Language).ToArray())
                _pinTitles.Remove(retired);
            foreach (var retired in _pinTitleAttempts.Keys.Where(candidate => candidate.Owner != key.Owner || candidate.Language != key.Language).ToArray())
                _pinTitleAttempts.Remove(retired);
            if (_pinTitleReads.TryGetValue(key, out var pending))
            {
                read = pending;
                read.Waiters++;
            }
            else
            {
                read = new PinMetadataRead();
                _pinTitleReads.Add(key, read);
                _ = CompleteLibraryPinReadAsync<T>(context.Request, key, read);
            }
        }
        return AwaitLibraryPinReadAsync<T>(key, read, ct);
    }

    private async Task<T> AwaitLibraryPinReadAsync<T>(
        (ApiRequestContext Owner, long Language, int Library, bool Collections) key, PinMetadataRead read, CancellationToken ct)
    {
        try { return (T)await read.Completion.Task.WaitAsync(ct).ConfigureAwait(false); }
        finally
        {
            lock (_pinTitlesGate)
            {
                if (_pinTitleReads.TryGetValue(key, out var pending) && ReferenceEquals(pending, read) && --read.Waiters == 0)
                {
                    _pinTitleReads.Remove(key);
                    read.Cancellation.Cancel();
                }
            }
        }
    }

    private async Task CompleteLibraryPinReadAsync<T>(ApiRequestContext request,
        (ApiRequestContext Owner, long Language, int Library, bool Collections) key, PinMetadataRead read)
    {
        try
        {
            var path = key.Collections
                ? $"/api/v2/library/{key.Library}/collections"
                : $"/api/v2/library/{key.Library}/layout";
            var value = await client.SendRequestAsync<T>(request, HttpMethod.Get, path, null, read.Cancellation.Token).ConfigureAwait(false);
            lock (_pinTitlesGate)
            {
                read.Cancellation.Token.ThrowIfCancellationRequested();
                if (client.CaptureMonitorContext().Authority != key.Owner || client.LocalizedReads.Generation != key.Language)
                    throw new OperationCanceledException("Pin title authority changed.");
                _pinTitles[key] = (value!, DateTime.UtcNow);
                _pinTitleReads.Remove(key);
            }
            PinTitlesChanged?.Invoke();
            read.Completion.TrySetResult(value!);
        }
        catch (OperationCanceledException)
        {
            read.Completion.TrySetCanceled();
        }
        catch (Exception error)
        {
            read.Completion.TrySetException(error);
        }
        finally
        {
            lock (_pinTitlesGate)
                if (_pinTitleReads.TryGetValue(key, out var pending) && ReferenceEquals(pending, read)) _pinTitleReads.Remove(key);
            read.Cancellation.Dispose();
        }
    }
}
