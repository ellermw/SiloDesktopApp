using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Helpers;

/// <summary>Binds a small admin poster through the shared artwork caches and load limits.</summary>
internal sealed class AdminPosterArtwork
{
    private readonly FrameworkElement _host;
    private readonly MonitorAuthorityLease _lease;
    private readonly int _width;
    private readonly Action<ImageSource?> _show;
    private string? _url;
    private BitmapImage? _bitmap;
    private DecodedArtworkCache.Key? _key;
    private CancellationTokenSource? _pending;

    internal AdminPosterArtwork(FrameworkElement host, MonitorAuthorityLease lease, int width, Action<ImageSource?> show)
    {
        _host = host;
        _lease = lease;
        _width = width;
        _show = show;
        _host.Loaded += (_, _) => Start();
        _host.Unloaded += (_, _) => Cancel();
    }

    internal static string? WebUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;

    internal void SetUrl(string? url)
    {
        url = WebUrl(url);
        var changed = url != _url;
        _url = url;
        var images = App.Services.GetService<ImageService>();
        var client = App.Services.GetService<SiloApiClient>();
        var key = url != null && images != null && _lease.IsCurrent
            ? DecodedArtworkCache.Capture(images, client, ImageService.NormalizeImageUrlForCache(url), _width, false, _lease.Epoch)
            : null;
        if (key != _key)
        {
            Cancel();
            _key = key;
            Show(DecodedArtworkCache.TryGet(key, out var cached) ? cached : null);
        }
        if (changed) Start();
    }

    private void Start()
    {
        if (!_host.IsLoaded || !_lease.IsCurrent || _bitmap != null || _pending != null || _url == null || _key == null) return;
        var http = App.Services.GetService<HttpClient>();
        if (http == null) return;
        var pending = _pending = CancellationTokenSource.CreateLinkedTokenSource(_lease.Token);
        _ = LoadAsync(_url, _key, http, pending);
    }

    private async Task LoadAsync(string url, DecodedArtworkCache.Key key, HttpClient http, CancellationTokenSource pending)
    {
        var ct = pending.Token;
        bool Current() => !ct.IsCancellationRequested && _lease.IsCurrent && DecodedArtworkCache.IsCurrent(key);
        try
        {
            if (DecodedArtworkCache.TryGet(key, out var cached)) { Show(cached); return; }
            var artwork = await LibraryGridCard.ImageLoadThrottle.RunAsync(token => Current()
                ? key.Images.GetImageDiskPathOrBytesAsync("admin-poster:" + _lease.AccountId, "poster", url, http, token, waitForSharedDownload: true)
                : Task.FromResult<(string? Path, byte[]? Bytes)>(default), ct);
            if (artwork is (null, null) || !Current()) return;

            await LibraryGridCard.BitmapCreateLock.WaitAsync(ct);
            try
            {
                if (!Current()) return;
                if (DecodedArtworkCache.TryGet(key, out cached)) { Show(cached); return; }
                using Stream input = artwork.Path is { } path ? File.OpenRead(path) : new MemoryStream(artwork.Bytes!);
                using var stream = input.AsRandomAccessStream();
                var bitmap = new BitmapImage { DecodePixelWidth = _width, DecodePixelType = DecodePixelType.Logical };
                await bitmap.SetSourceAsync(stream);
                if (!Current()) return;
                DecodedArtworkCache.Remember(key, bitmap, _host, visibleOnly: false);
                Show(bitmap);
            }
            finally { LibraryGridCard.BitmapCreateLock.Release(); }
        }
        catch (Exception) { }
        finally
        {
            if (ReferenceEquals(_pending, pending))
            {
                _pending = null;
                pending.Dispose();
                // A server can renew a signed URL while its old request is in flight.
                if (url != _url) Start();
            }
        }
    }

    private void Show(BitmapImage? bitmap)
    {
        if (ReferenceEquals(_bitmap, bitmap)) return;
        _bitmap = bitmap;
        _show(bitmap);
    }

    private void Cancel()
    {
        var pending = _pending;
        _pending = null;
        pending?.Cancel();
        pending?.Dispose();
    }
}
