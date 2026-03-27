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
        services.AddSingleton<HttpClient>(sp =>
        {
            var settingsService = sp.GetRequiredService<SettingsService>();
            var settings = settingsService.Load();
            var baseUrl = settings.Servers.Count > 0
                ? settings.Servers.OrderByDescending(s => s.LastUsed).First().Url
                : "https://localhost";
            return new HttpClient { BaseAddress = new Uri(baseUrl) };
        });

        services.AddSingleton<ContinuumApiClient>(sp =>
        {
            var http = sp.GetRequiredService<HttpClient>();
            return new ContinuumApiClient(http);
        });

        // API wrappers
        services.AddSingleton<AuthApi>(sp => new AuthApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<HomeApi>(sp => new HomeApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<CatalogApi>(sp => new CatalogApi(sp.GetRequiredService<ContinuumApiClient>()));

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

        return services.BuildServiceProvider();
    }
}
