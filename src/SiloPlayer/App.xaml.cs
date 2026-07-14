using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer;

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
                "SiloPlayer", "crash.txt");
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
            "SiloPlayer");

        var imageCacheDir = Path.Combine(appDataDir, "ImageCache");

        // Core services
        services.AddSingleton(new SettingsService(appDataDir));
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<ICredentialStore>(sp => sp.GetRequiredService<CredentialStore>());

        // HTTP client and API
        services.AddSingleton<HttpClient>(_ => new HttpClient());
        services.AddSingleton<SiloApiClient>(sp =>
        {
            var http = sp.GetRequiredService<HttpClient>();
            var client = new SiloApiClient(http);

            // Pre-set base URL from saved server if available
            var settingsService = sp.GetRequiredService<SettingsService>();
            var settings = settingsService.Load();
            if (string.IsNullOrWhiteSpace(settings.DeviceId))
            {
                settings.DeviceId = $"silo-desktop-{Guid.NewGuid():N}";
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
        services.AddSingleton<AuthApi>(sp => new AuthApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<HomeApi>(sp => new HomeApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<CatalogApi>(sp => new CatalogApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<SettingsApi>(sp => new SettingsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<WebhookSyncApi>(sp => new WebhookSyncApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<PlaybackApi>(sp => new PlaybackApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<AdminApi>(sp => new AdminApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<PeopleApi>(sp => new PeopleApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<CollectionsApi>(sp => new CollectionsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<DownloadsApi>(sp => new DownloadsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<RequestsApi>(sp => new RequestsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<NotificationsApi>(sp => new NotificationsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<HistoryImportApi>(sp => new HistoryImportApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<RecommendationsApi>(sp => new RecommendationsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<ApiKeysApi>(sp => new ApiKeysApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<PluginsApi>(sp => new PluginsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<WatchProvidersApi>(sp => new WatchProvidersApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<EbooksApi>(sp => new EbooksApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddTransient<PlaybackManager>();

        // Auth service
        services.AddSingleton<AuthService>(sp => new AuthService(
            sp.GetRequiredService<SiloApiClient>(),
            sp.GetRequiredService<AuthApi>(),
            sp.GetRequiredService<ICredentialStore>()));

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
        services.AddSingleton<AccessibilityService>();

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
        services.AddTransient<RecommendationSectionViewModel>();
        services.AddTransient<CalendarViewModel>();
        services.AddTransient<PersonDetailViewModel>();
        services.AddTransient<CollectionsViewModel>();
        services.AddTransient<CollectionEditorViewModel>();
        services.AddTransient<SmartCollectionWizardViewModel>();
        services.AddTransient<DownloadsViewModel>();
        services.AddTransient<RequestsViewModel>();
        services.AddTransient<NotificationsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<WatchTogetherJoinViewModel>();
        services.AddTransient<WatchTogetherRoomViewModel>();
        // Retain the last successful dashboard snapshot so returning from another
        // admin page paints immediately while a fresh snapshot loads in place.
        services.AddSingleton<SiloPlayer.ViewModels.Admin.AdminDashboardViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminActivityViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminUsersViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminUserDetailViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminLogsViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminLibrariesViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminCollectionsViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminSectionsViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminTasksViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminMarkerHistoryViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminAccessGroupsViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminDevicesViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminAutoscanViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminPolicyViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.TasteSeedViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.NotificationSettingsViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminTaskDetailViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminNodesViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminSettingsDetailViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminApiKeysViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminRecommendationsViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminPlaybackHistoryViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminRequestsViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminPluginsViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminInviteCodesViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminMaintenanceViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.Admin.AdminSubtitleProvidersViewModel>();
        services.AddTransient<SiloPlayer.Views.Admin.AdminSubtitlesPage>();

        var provider = services.BuildServiceProvider();

        // Wire automatic token refresh on 401 responses
        var apiClient = provider.GetRequiredService<SiloApiClient>();
        var authService = provider.GetRequiredService<AuthService>();
        apiClient.SetTokenRefresher(ct => authService.TryRefreshAsync(ct));
        apiClient.SetProfileVerificationRequiredHandler(authService.HandleProfileVerificationRequired);

        return provider;
    }
}
