using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;
    private CancellationTokenSource? _libraryLoadCts;
    private long _libraryLoadGeneration;
    public bool HasCurrentLibrarySnapshot { get; private set; }
    // Retry readiness is separate from the accepted snapshot used by the sidebar.
    public bool NeedsLibraryReload { get; private set; } = true;

    public MainViewModel(CatalogApi catalogApi, AuthService authService)
    {
        _catalogApi = catalogApi;
        _authService = authService;
    }

    public ObservableCollection<Library> Libraries { get; } = [];

    [ObservableProperty]
    private bool _isLoadingLibraries;

    [ObservableProperty]
    private string? _currentProfileName;

    [RelayCommand]
    private Task LoadLibrariesAsync()
        => ReloadLibrariesAsync();

    public void RetireLibraries()
    {
        HasCurrentLibrarySnapshot = false;
        NeedsLibraryReload = true;
        Interlocked.Increment(ref _libraryLoadGeneration);
        _libraryLoadCts?.Cancel();
        _libraryLoadCts = null;
        IsLoadingLibraries = false;
        Libraries.Clear();
    }

    public async Task ReloadLibrariesAsync(CancellationToken cancellationToken = default)
    {
        _libraryLoadCts?.Cancel();
        _libraryLoadCts?.Dispose();
        var loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _libraryLoadCts = loadCts;
        var generation = Interlocked.Increment(ref _libraryLoadGeneration);

        var context = _catalogApi.CaptureContext();
        NeedsLibraryReload = true;
        IsLoadingLibraries = true;
        try
        {
            var libraries = await _catalogApi.GetLibrariesAsync(loadCts.Token);
            if (loadCts.IsCancellationRequested ||
                generation != Volatile.Read(ref _libraryLoadGeneration) ||
                !_catalogApi.IsCurrentContext(context))
            {
                return;
            }

            HasCurrentLibrarySnapshot = true;
            NeedsLibraryReload = false;
            Libraries.Clear();
            foreach (var lib in libraries)
            {
                Libraries.Add(lib);
            }
        }
        catch (OperationCanceledException) when (loadCts.IsCancellationRequested)
        {
            // A profile/server transition superseded this result.
        }
        catch
        {
            // Keep the accepted snapshot, but leave NeedsLibraryReload set so
            // a later catch-up retries the failed replacement.
        }
        finally
        {
            if (generation == Volatile.Read(ref _libraryLoadGeneration))
            {
                IsLoadingLibraries = false;
                if (ReferenceEquals(_libraryLoadCts, loadCts))
                    _libraryLoadCts = null;
            }
            loadCts.Dispose();
        }
    }
}
