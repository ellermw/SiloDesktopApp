using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Services;
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

        // Apply saved theme before the first window renders
        try
        {
            var themeService = _services.GetRequiredService<ThemeService>();
            themeService.ApplySavedTheme();
        }
        catch { /* first launch, no saved theme */ }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        // Global unhandled exception handler -- write to crash log instead of silently dying
        this.UnhandledException += (sender, args) =>
        {
            args.Handled = true;
            var crashLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer", "crash.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(crashLog)!);
            File.WriteAllText(crashLog, $"{DateTime.Now}\nUnhandled: {args.Exception}\n");
        };

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
        services.AddSingleton<PlaybackApi>(sp => new PlaybackApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<AdminApi>(sp => new AdminApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<PeopleApi>(sp => new PeopleApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<CollectionsApi>(sp => new CollectionsApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<DownloadsApi>(sp => new DownloadsApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<HistoryImportApi>(sp => new HistoryImportApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<RecommendationsApi>(sp => new RecommendationsApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<ApiKeysApi>(sp => new ApiKeysApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddSingleton<PluginsApi>(sp => new PluginsApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddTransient<PlaybackManager>();

        // Auth service
        services.AddSingleton<AuthService>(sp => new AuthService(
            sp.GetRequiredService<ContinuumApiClient>(),
            sp.GetRequiredService<AuthApi>()));

        // Image service
        services.AddSingleton(new ImageService(imageCacheDir));

        // Theme service
        services.AddSingleton<ThemeService>();

        // Navigation
        services.AddSingleton<NavigationService>();

        // Player service (owns mpv lifecycle, not tied to page navigation)
        services.AddSingleton<PlayerService>();

        // ViewModels
        services.AddTransient<ServerSelectViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<SignupViewModel>();
        services.AddTransient<SetupWizardViewModel>();
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
        services.AddTransient<PersonDetailViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminDashboardViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminActivityViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminUsersViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminUserDetailViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminLogsViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminLibrariesViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminCollectionsViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminSectionsViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminTasksViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminTaskDetailViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminNodesViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminSettingsDetailViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminApiKeysViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminRecommendationsViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminPlaybackHistoryViewModel>();

        return services.BuildServiceProvider();
    }
}
