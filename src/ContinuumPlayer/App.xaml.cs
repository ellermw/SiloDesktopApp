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
    private DispatcherTimer? _uiLagTimer;
    private long _lastUiLagTick;
    private int _uiLagSample;

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
        // Global unhandled exception handler -- write to crash log instead of silently dying.
        // Walks the entire InnerException chain so XamlParseException reasons (which are
        // usually nested) are captured, not just the top-level "RangeBase.Value" message.
        this.UnhandledException += (sender, args) =>
        {
            args.Handled = true;
            var crashLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer", "crash.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(crashLog)!);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{DateTime.Now}");
            sb.AppendLine($"args.Message: {args.Message}");
            sb.AppendLine();

            var ex = args.Exception;
            int depth = 0;
            while (ex != null)
            {
                sb.AppendLine($"--- Exception depth {depth} ---");
                sb.AppendLine($"Type: {ex.GetType().FullName}");
                sb.AppendLine($"HResult: 0x{ex.HResult:X8}");
                sb.AppendLine($"Message: {ex.Message}");
                if (ex.Data.Count > 0)
                {
                    foreach (System.Collections.DictionaryEntry entry in ex.Data)
                        sb.AppendLine($"  Data[{entry.Key}] = {entry.Value}");
                }
                sb.AppendLine($"StackTrace: {ex.StackTrace}");
                sb.AppendLine();
                ex = ex.InnerException;
                depth++;
                if (depth > 10) break;
            }

            File.WriteAllText(crashLog, LocalLog.RedactSensitiveData(sb.ToString()));
        };

        _window = new MainWindow();
        MainWindowInstance = (MainWindow)_window;
        _window.Activate();

        StartUiThreadLagDetector();
    }

    public static string PerfBreadcrumb { get; private set; } = "";

    public static void SetPerfBreadcrumb(string value) => PerfBreadcrumb = value;

    private void StartUiThreadLagDetector()
    {
        _lastUiLagTick = Environment.TickCount64;
        _uiLagTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uiLagTimer.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            var delay = now - _lastUiLagTick;
            _lastUiLagTick = now;
            if (delay < 500) return;

            try
            {
                var managedMb = GC.GetTotalMemory(false) / (1024 * 1024);
                LocalLog.AppendLine(
                    "ui_lag.txt",
                    $"sample={++_uiLagSample} | delay_ms={delay} | managed_mb={managedMb} | breadcrumb={PerfBreadcrumb}");
            }
            catch { }
        };
        _uiLagTimer.Start();
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
            if (string.IsNullOrWhiteSpace(settings.DeviceId))
            {
                settings.DeviceId = $"continuum-desktop-{Guid.NewGuid():N}";
                settingsService.Save(settings);
            }
            client.SetDeviceMetadata(
                settings.DeviceId,
                Environment.MachineName,
                "windows");

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
        services.AddSingleton<WatchProvidersApi>(sp => new WatchProvidersApi(sp.GetRequiredService<ContinuumApiClient>()));
        services.AddTransient<PlaybackManager>();

        // Auth service
        services.AddSingleton<AuthService>(sp => new AuthService(
            sp.GetRequiredService<ContinuumApiClient>(),
            sp.GetRequiredService<AuthApi>()));

        // Event channel client (realtime WebSocket for history_import, sessions, etc.)
        services.AddSingleton<EventChannelClient>();

        // Image service
        services.AddSingleton(new ImageService(imageCacheDir));

        // Theme service
        services.AddSingleton<ThemeService>();

        // Navigation
        services.AddSingleton<NavigationService>();

        // F2: Application-wide toast notifications. MainWindow registers its
        // ToastContainer with this service on construction.
        services.AddSingleton<ToastService>();

        // Card overlay prefs (kill switch + admin defaults + user override).
        // Populated lazily on first PosterCard bind.
        services.AddSingleton<CardOverlayService>();

        // Player service (owns mpv lifecycle, not tied to page navigation)
        services.AddSingleton<PlayerService>();

        // Watch-Together coordinator — bridges room VM and PlayerService so
        // synced transport commands reach mpv. Must be constructed on the UI
        // thread; resolved lazily by WatchTogetherRoomPage on load.
        services.AddSingleton<WatchTogetherCoordinator>();

        // ViewModels
        services.AddTransient<ServerSelectViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<SignupViewModel>();
        services.AddTransient<ActivateDeviceViewModel>();
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
        services.AddTransient<CalendarViewModel>();
        services.AddTransient<PersonDetailViewModel>();
        services.AddTransient<CollectionsViewModel>();
        services.AddTransient<CollectionEditorViewModel>();
        services.AddTransient<DownloadsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<WatchTogetherJoinViewModel>();
        services.AddTransient<WatchTogetherRoomViewModel>();
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
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminPluginsViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminInviteCodesViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminMaintenanceViewModel>();
        services.AddTransient<ContinuumPlayer.ViewModels.Admin.AdminSubtitleProvidersViewModel>();

        var provider = services.BuildServiceProvider();

        // Wire automatic token refresh on 401 responses
        var apiClient = provider.GetRequiredService<ContinuumApiClient>();
        var authService = provider.GetRequiredService<AuthService>();
        apiClient.SetTokenRefresher(ct => authService.TryRefreshAsync(ct));

        return provider;
    }
}
