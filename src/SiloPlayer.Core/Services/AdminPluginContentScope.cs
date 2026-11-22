using System.Globalization;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>
/// The address space one Admin plugin page may use, from WebUI <c>pluginRouteHref</c> and
/// <c>buildPluginHref</c>: the page opens at
/// <c>/api/v2/plugin-content/plugins/{installation}{route without "/*"}?theme=midnight-cinema</c>
/// on the connected server, and its documents, frames and requests stay inside that
/// installation's pages (<c>…/plugins/{id}/</c>) and static assets (<c>…/plugin-assets/{id}/</c>).
/// Everything else (other origins, other installations, server APIs, file:, data: documents,
/// app-local schemes) is outside. Pure policy: no network, no credentials.
/// </summary>
public sealed class AdminPluginContentScope
{
    /// <summary>The WebUI's <c>document.documentElement.dataset.theme</c> (index.html data-theme).</summary>
    public const string Theme = "midnight-cinema";
    private const int MaxUrlLength = 8192;
    private readonly Uri _server;
    private readonly string _pages;
    private readonly string _pagesRoot;
    private readonly string _assets;

    private AdminPluginContentScope(Uri server, AdminPluginApp app)
    {
        _server = server;
        App = app;
        var id = app.InstallationId.ToString(CultureInfo.InvariantCulture);
        ContentParent = PluginContentPaths.BasePath(server) + PluginContentPaths.ContentPrefix;
        _pagesRoot = ContentParent + "/plugins/" + id;
        _pages = _pagesRoot + "/";
        _assets = ContentParent + "/plugin-assets/" + id + "/";
        var route = app.RoutePath.EndsWith("/*", StringComparison.Ordinal) ? app.RoutePath[..^2] : app.RoutePath;
        StartUri = new Uri(server.GetLeftPart(UriPartial.Authority) + _pagesRoot + route + "?theme=" + Theme);
        if (!AllowsDocument(StartUri)) throw new InvalidDataException("Invalid Admin plugin route.");
    }

    public AdminPluginApp App { get; }

    /// <summary>The server's plugin-content parent path: the launch cookie's path.</summary>
    public string ContentParent { get; }

    /// <summary>The page the WebUI opens for this navigation entry.</summary>
    public Uri StartUri { get; }

    /// <summary>The scheme, host and port the plugin content is served from (the connected server).</summary>
    public Uri Origin => new(_server.GetLeftPart(UriPartial.Authority) + "/");

    public static AdminPluginContentScope Create(string serverBaseUrl, AdminPluginApp app)
    {
        if (app.InstallationId <= 0 || !AdminPluginAppsApi.SafePath(app.RoutePath, pattern: true))
            throw new InvalidDataException("Invalid Admin plugin route.");
        if (!Uri.TryCreate(serverBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var server) ||
            server.Scheme is not ("http" or "https") || server.UserInfo.Length != 0 || server.Query.Length != 0 || server.Fragment.Length != 0)
            throw new InvalidDataException("Invalid server address.");
        return new AdminPluginContentScope(server, app);
    }

    /// <summary>A top-level or frame navigation (including a redirect) the page may follow.</summary>
    public bool AllowsDocument(Uri target) => SameOrigin(target) && (IsPage(target.AbsolutePath) || IsAsset(target.AbsolutePath));

    /// <summary>A subresource, fetch, form or worker request the page may send.</summary>
    public bool AllowsResource(Uri target, string method)
    {
        if (!SameOrigin(target)) return false;
        var path = target.AbsolutePath;
        if (IsPage(path)) return true;
        // The asset mount serves GET only.
        return IsAsset(path) && method.ToUpperInvariant() is "GET" or "HEAD";
    }

    /// <summary>The issued cookie path must be exactly the plugin-content parent: never "/", the API, or another host's path.</summary>
    public bool IsLaunchCookiePath(string path) => string.Equals(path, ContentParent, StringComparison.Ordinal);

    private bool IsPage(string path) => path == _pagesRoot || path.StartsWith(_pages, StringComparison.Ordinal);
    private bool IsAsset(string path) => path.StartsWith(_assets, StringComparison.Ordinal);

    private bool SameOrigin(Uri target)
    {
        if (!target.IsAbsoluteUri || target.OriginalString.Length > MaxUrlLength || target.UserInfo.Length != 0) return false;
        if (target.Scheme is not ("http" or "https")) return false;
        if (!string.Equals(target.Scheme, _server.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.Host, _server.Host, StringComparison.OrdinalIgnoreCase) || target.Port != _server.Port) return false;
        // Encoded separators, dots or NUL are refused rather than reinterpreted by the server.
        return PluginContentPaths.HasPlainSegments(target.AbsolutePath) && !NativeApiRetirement.IsRetiredResource(target.AbsoluteUri);
    }
}
