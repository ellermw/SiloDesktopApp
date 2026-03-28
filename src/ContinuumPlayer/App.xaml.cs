using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer;

public partial class App : Application
{
    private static IServiceProvider? _services;
    private Window? _window;

    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Service provider not initialized.");

    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        this.InitializeComponent();
        _services = ConfigureServices();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        _window = new MainWindow();
        MainWindowInstance = (MainWindow)_window;
        _window.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // App data directory
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ContinuumPlayer");

        var imageCacheDir = Path.Combine(appDataDir, "ImageCache");

        // Core services
        services.AddSingleton(new SettingsService(appDataDir));
        services.AddSingleton<CredentialStore>();

        // HTTP client and API
        services.AddSingleton<HttpClient>(_ => new HttpClient());
        services.AddSingleton<ContinuumApiClient>(sp =>
        {
            var http = sp.GetRequiredService<HttpClient>();
            var client = new ContinuumApiClient(http);

            // Pre-set base URL from saved server if available
            var settingsService = sp.GetRequiredService<SettingsService>();
            var settings = settingsService.Load();
            if (settings.Servers.Count > 0)
            {
                var serverUrl = settings.Servers.OrderByDescending(s => s.LastUsed).First().Url;
                client.SetBaseUrl(serverUrl);
            }

            return client;
        });

        // API wrappers
        services.AddSingleton<AuthApi>(sp => new AuthApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<HomeApi>(sp => new HomeApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<CatalogApi>(sp => new CatalogApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<SettingsApi>(sp => new SettingsApi(sp.GetRequiredService<ContinuumApiClient>()));

        // Auth service
        services.AddSingleton<AuthService>(sp => new AuthService(
            sp.GetRequiredService<ContinuumApiClient>(),
            sp.GetRequiredService<AuthApi>()));

        // Image service
        services.AddSingleton(new ImageService(imageCacheDir));

        // Navigation
        services.AddSingleton<NavigationService>();

        // ViewModels
        services.AddTransient<ServerSelectViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<ProfileSelectViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<ItemDetailViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<FavoritesViewModel>();
        services.AddTransient<WatchlistViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<RecommendationsViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}
