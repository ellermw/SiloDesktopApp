using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using System.Text;
using System.Runtime.InteropServices.WindowsRuntime;

namespace SiloPlayer.Views;

public sealed partial class PluginRoutePage : Page
{
    public sealed record NavigationArgs(int InstallationId, string RoutePath, string Label);
    private bool _active = true;
    private readonly PluginPublicResources _publicResources = new();
    private readonly CancellationTokenSource _resourceStop = new();

    public PluginRoutePage()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        { _active = false; _resourceStop.Cancel(); _publicResources.Dispose(); PluginWebView.Close(); };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not NavigationArgs args) return;
        if (App.MainWindowInstance is MainWindow window)
            window.SetDynamicTitle(args.Label);

        try
        {
            var apiClient = App.Services.GetRequiredService<SiloApiClient>();
            var owner = apiClient.CaptureContext();
            var origin = new Uri(owner.BaseUrl.TrimEnd('/') + "/");
            var refusedNavigations = new Dictionary<ulong, string>();
            await PluginWebView.EnsureCoreWebView2Async();
            if (!_active || !apiClient.IsCurrentContext(owner))
            {
                ShowError("The active account or profile changed. Reopen this plugin page to continue.");
                return;
            }
            // Popup windows share this profile's cookie jar but none of the
            // origin/credential filters below, so plugins may not open them.
            PluginWebView.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            PluginWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            PluginWebView.CoreWebView2.NewWindowRequested += (_, eventArgs) => eventArgs.Handled = true;
            PluginWebView.CoreWebView2.PermissionRequested += (_, eventArgs) =>
                eventArgs.State = CoreWebView2PermissionState.Deny;
            PluginWebView.CoreWebView2.AddWebResourceRequestedFilter(
                "*",
                CoreWebView2WebResourceContext.All);
            PluginWebView.CoreWebView2.WebResourceRequested += async (_, eventArgs) =>
            {
                if (!_active || !apiClient.IsCurrentContext(owner))
                {
                    RefuseResource(eventArgs, 403, "The active account or profile changed. Reopen this plugin page to continue.");
                    return;
                }
                if (!Uri.TryCreate(eventArgs.Request.Uri, UriKind.Absolute, out var target) ||
                    !SameOrigin(origin, target))
                {
                    // Document requests can arrive before NavigationStarting. Never
                    // let the public asset reader dispatch an external navigation.
                    if (eventArgs.ResourceContext == CoreWebView2WebResourceContext.Document)
                    {
                        RefuseResource(eventArgs, 403, "Plugin navigation must stay on this server. Open external links in your browser.");
                        return;
                    }
                    // Chromium attaches cookies after this event. Supply the public
                    // response ourselves so the browser never dispatches credentials.
                    using var deferral = eventArgs.GetDeferral();
                    try
                    {
                        if (target == null) throw new InvalidDataException("Invalid public resource address.");
                        var headers = new Dictionary<string, string>();
                        foreach (var name in new[] { "Accept", "Accept-Language", "Range", "If-Range", "If-None-Match", "If-Modified-Since", "Origin" })
                            if (eventArgs.Request.Headers.Contains(name)) headers[name] = eventArgs.Request.Headers.GetHeader(name);
                        var resource = await _publicResources.ReadAsync(target, origin, eventArgs.Request.Method, headers,
                            () => _active && apiClient.IsCurrentContext(owner), _resourceStop.Token);
                        if (_active)
                        {
                            var body = new MemoryStream(resource.Body);
                            eventArgs.Response = PluginWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                                body.AsRandomAccessStream(), resource.Status, resource.Reason, resource.Headers);
                        }
                    }
                    catch (Exception)
                    {
                        if (_active) RefuseResource(eventArgs, 403, apiClient.IsCurrentContext(owner)
                            ? "This external plugin resource could not be loaded without account credentials."
                            : "The active account or profile changed. Reopen this plugin page to continue.");
                    }
                    return;
                }
                if (NativeApiRetirement.IsRetiredResource(target.AbsoluteUri))
                {
                    RefuseResource(eventArgs, 410, "This plugin requested a retired API resource. Update the plugin and reopen this page.");
                    return;
                }
                try
                {
                    // Atomically validate the captured owner and snapshot its headers.
                    // A page from an earlier profile must never borrow the next one's proof.
                    using var request = apiClient.CreateAuthenticatedRequest(owner, new HttpMethod(eventArgs.Request.Method), target.AbsoluteUri);
                    foreach (var header in request.Headers)
                        eventArgs.Request.Headers.SetHeader(header.Key, string.Join(", ", header.Value));
                }
                catch (OperationCanceledException)
                { RefuseResource(eventArgs, 403, "The active account or profile changed. Reopen this plugin page to continue."); }
            };
            PluginWebView.CoreWebView2.NavigationStarting += (_, eventArgs) =>
            {
                string? error = null;
                if (!_active || !apiClient.IsCurrentContext(owner))
                    error = "The active account or profile changed. Reopen this plugin page to continue.";
                else if (!Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var target) || !SameOrigin(origin, target))
                    error = "Plugin navigation must stay on this server. Open external links in your browser.";
                else if (NativeApiRetirement.IsRetiredResource(target.AbsoluteUri))
                    error = "This plugin linked to a retired API resource. Update the plugin and reopen this page.";
                if (error != null)
                { refusedNavigations[eventArgs.NavigationId] = error; eventArgs.Cancel = true; ShowError(error); }
            };
            PluginWebView.CoreWebView2.NavigationCompleted += (_, eventArgs) =>
            {
                if (refusedNavigations.Remove(eventArgs.NavigationId, out var refusal))
                { ShowError(refusal); return; }
                LoadingRing.Visibility = Visibility.Collapsed;
                if (!eventArgs.IsSuccess || eventArgs.HttpStatusCode >= 400)
                {
                    ShowError(eventArgs.IsSuccess
                        ? $"The plugin server refused this page (HTTP {eventArgs.HttpStatusCode}). Reopen the plugin or check its access settings."
                        : $"Plugin navigation failed: {eventArgs.WebErrorStatus}");
                }
            };

            var path = NormalizeRoutePath(args.RoutePath);
            // Dynamic content has its own v2 mount outside finite native schemas.
            var url = $"{owner.BaseUrl.TrimEnd('/')}/api/v2/plugin-content/plugins/{args.InstallationId}{path}";
            url += "?theme=dark";
            PluginWebView.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            if (_active) ShowError(ex is OperationCanceledException
                ? "The active account or profile changed. Reopen this plugin page to continue."
                : "The plugin page could not be opened. Check the server connection and reopen the plugin.");
        }
    }

    private static bool SameOrigin(Uri origin, Uri target)
        => origin.Scheme == target.Scheme && origin.Host == target.Host && origin.Port == target.Port;

    private void RefuseResource(CoreWebView2WebResourceRequestedEventArgs args, int status, string message)
    {
        var body = new MemoryStream(Encoding.UTF8.GetBytes(message));
        args.Response = PluginWebView.CoreWebView2.Environment.CreateWebResourceResponse(body.AsRandomAccessStream(), status,
            status == 410 ? "Gone" : "Forbidden", "Content-Type: text/plain; charset=utf-8\r\nCache-Control: no-store");
        ShowError(message);
    }

    private void ShowError(string message)
    {
        LoadingRing.Visibility = Visibility.Collapsed;
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    internal static string NormalizeRoutePath(string routePath)
    {
        var path = routePath.EndsWith("/*", StringComparison.Ordinal)
            ? routePath[..^2]
            : routePath;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
            throw new InvalidOperationException("Plugin route paths cannot contain traversal segments.");

        return segments.Length == 0
            ? "/"
            : "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
    }
}
