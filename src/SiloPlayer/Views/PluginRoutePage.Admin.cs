using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace SiloPlayer.Views;

public sealed partial class PluginRoutePage
{
    private AdminPluginContentSession? _adminContent;
    private MonitorAuthorityLease? _adminLease;
    private CancellationTokenRegistration _adminRetired;
    private bool _adminStopped;
    private const string AdminBoundaryMessage = "This Admin plugin can use only its own plugin-content pages and assets. External navigation, other installations and server APIs are blocked. This plugin may require a separately reviewed integration.";

    private async Task OpenAdminContentAsync(NavigationArgs args)
    {
        try
        {
            var authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
            _adminLease = authority.Capture(_resourceStop.Token);
            var lease = _adminLease;
            if (lease.Epoch != args.AdminEpoch) throw new MonitorAuthorityRetiredException();
            _adminRetired = lease.Token.Register(() => DispatcherQueue.TryEnqueue(() =>
            {
                if (!_active) return;
                _adminStopped = true;
                PluginWebView.Visibility = Visibility.Collapsed;
                ClosePluginWebView();
                ShowError("Admin authority changed. Reopen this plugin from the Admin sidebar to continue.");
            }));
            // Reconcile enabled/route eligibility again at launch, rather than trusting a
            // detached button or Frame history parameter from an earlier inventory read.
            var apps = await App.Services.GetRequiredService<AdminPluginAppsApi>().GetAppsAsync(lease);
            lease.ThrowIfRetired();
            if (!_active) return;
            var app = apps.FirstOrDefault(app => app.InstallationId == args.InstallationId && app.RoutePath == args.RoutePath);
            if (app == null)
            {
                ShowError("This Admin plugin route is no longer available. Return to Admin and refresh the Plugin Apps group.");
                return;
            }
            var session = new AdminPluginContentSession(App.Services.GetRequiredService<SiloApiClient>(), lease, app);
            _adminContent = session;
            // Keep this mode out of the ordinary plugin browser profile. InPrivate
            // storage is not retained, and disabling autofill cannot change user-mode
            // profile preferences. Never fall back to a default profile on failure.
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, null, null);
            lease.ThrowIfRetired();
            if (!_active) return;
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = "SiloAdminPluginApps";
            options.IsInPrivateModeEnabled = true;
            await PluginWebView.EnsureCoreWebView2Async(environment, options);
            lease.ThrowIfRetired();
            if (!_active) return;
            var core = PluginWebView.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NewWindowRequested += (_, e) => { e.Handled = true; ShowError(AdminBoundaryMessage); };
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => { e.Cancel = true; ShowError(AdminBoundaryMessage); };
            core.LaunchingExternalUriScheme += (_, e) => { e.Cancel = true; ShowError(AdminBoundaryMessage); };
            var refusedNavigations = new Dictionary<ulong, string>();
            string? resourceFailure = null;
            core.NavigationStarting += (_, e) =>
            {
                if (!_active || !lease.IsCurrent || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) || !session.CanNavigate(target))
                {
                    var message = lease.IsCurrent ? AdminBoundaryMessage : "Admin authority changed. Reopen this plugin from Admin.";
                    if (refusedNavigations.Count >= 64) refusedNavigations.Clear();
                    refusedNavigations[e.NavigationId] = message;
                    e.Cancel = true; ShowError(message);
                }
                else
                {
                    resourceFailure = null;
                    ErrorPanel.Visibility = Visibility.Collapsed;
                    LoadingRing.Visibility = Visibility.Visible;
                }
            };
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += async (_, e) =>
            {
                using var deferral = e.GetDeferral();
                try
                {
                    lease.ThrowIfRetired();
                    if (!_active || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var target)) throw new InvalidDataException();
                    var headers = new Dictionary<string, string>();
                    foreach (var name in new[] { "Accept", "Accept-Language", "Content-Type", "Range", "If-Range", "If-None-Match", "If-Modified-Since", "If-Match" })
                        if (e.Request.Headers.Contains(name)) headers[name] = e.Request.Headers.GetHeader(name);
                    using var body = e.Request.Content?.AsStreamForRead();
                    var response = await session.SendAsync(target, e.Request.Method, headers, body,
                        e.ResourceContext == CoreWebView2WebResourceContext.Document, _resourceStop.Token);
                    lease.ThrowIfRetired();
                    if (!_active || _adminStopped) return;
                    e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(response.Body).AsRandomAccessStream(),
                        response.Status, response.Reason, response.Headers);
                }
                catch (Exception ex)
                {
                    if (!_active || _adminStopped) return;
                    var message = !lease.IsCurrent ? "Admin authority changed. Reopen this plugin from Admin."
                        : ex is InvalidDataException ? AdminBoundaryMessage
                        : "The Admin plugin request failed. A submitted change may have reached the server; review its state before submitting again.";
                    resourceFailure = message;
                    RefuseResource(e, 403, message);
                }
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (!_active || !lease.IsCurrent) return;
                if (refusedNavigations.Remove(e.NavigationId, out var refusal)) { ShowError(refusal); return; }
                if (resourceFailure != null) { ShowError(resourceFailure); return; }
                LoadingRing.Visibility = Visibility.Collapsed;
                if (!e.IsSuccess || e.HttpStatusCode >= 400)
                    ShowError("This Admin plugin page could not be loaded. Check its installation and connection, then reopen it from Admin.");
            };
            PluginWebView.Source = session.StartUri;
        }
        catch (Exception ex)
        {
            if (_active) ShowError(ex is OperationCanceledException || _adminLease?.IsCurrent == false
                ? "Admin authority changed. Reopen this plugin from the Admin sidebar to continue."
                : "The Admin plugin could not be opened. Check the server connection and reopen it from Admin.");
        }
    }

    private void RetireAdminContent()
    {
        _adminRetired.Dispose();
        _adminContent?.Dispose();
        _adminContent = null;
        _adminLease?.Dispose();
        _adminLease = null;
    }
}
