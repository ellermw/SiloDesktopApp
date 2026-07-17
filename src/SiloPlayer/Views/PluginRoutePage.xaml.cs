using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Views;

public sealed partial class PluginRoutePage : Page
{
    public sealed record NavigationArgs(int InstallationId, string RoutePath, string Label);

    public PluginRoutePage()
    {
        InitializeComponent();
        Unloaded += (_, _) => PluginWebView.Close();
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
            await PluginWebView.EnsureCoreWebView2Async();
            var requestPrefix = apiClient.BaseUrl.TrimEnd('/') + "/*";
            PluginWebView.CoreWebView2.AddWebResourceRequestedFilter(
                requestPrefix,
                CoreWebView2WebResourceContext.All);
            PluginWebView.CoreWebView2.WebResourceRequested += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(apiClient.AccessToken))
                    eventArgs.Request.Headers.SetHeader("Authorization", $"Bearer {apiClient.AccessToken}");
                if (!string.IsNullOrWhiteSpace(apiClient.ProfileId))
                    eventArgs.Request.Headers.SetHeader("X-Profile-Id", apiClient.ProfileId);
                if (!string.IsNullOrWhiteSpace(apiClient.ProfileToken))
                    eventArgs.Request.Headers.SetHeader("X-Profile-Token", apiClient.ProfileToken);
            };
            PluginWebView.CoreWebView2.NavigationCompleted += (_, eventArgs) =>
            {
                LoadingRing.Visibility = Visibility.Collapsed;
                if (!eventArgs.IsSuccess)
                {
                    ErrorText.Text = $"WebView navigation failed: {eventArgs.WebErrorStatus}";
                    ErrorPanel.Visibility = Visibility.Visible;
                }
            };

            var path = args.RoutePath.EndsWith("/*", StringComparison.Ordinal)
                ? args.RoutePath[..^2]
                : args.RoutePath;
            if (!path.StartsWith('/')) path = "/" + path;
            var url = $"{apiClient.BaseUrl.TrimEnd('/')}/api/v1/plugins/{args.InstallationId}{path}";
            url += url.Contains('?') ? "&theme=dark" : "?theme=dark";
            PluginWebView.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            LoadingRing.Visibility = Visibility.Collapsed;
            ErrorText.Text = ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }
}
