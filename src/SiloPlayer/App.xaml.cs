using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using System.IO.Pipes;
using System.Text;

namespace SiloPlayer;

public partial class App : Application
{
    private static IServiceProvider? _services;
    private Window? _window;
    private DispatcherTimer? _uiLagTimer;
    private long _lastUiLagTick;
    private int _uiLagSample;
    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _activationPipeCts;
    private Task? _activationPipeTask;
    private AppInstanceNames _instanceNames = AppInstanceNames.Create(null);

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
        if (Environment.ProcessPath is { } executable && SiloPlayer.Core.Services.Updates.AppUpdateInstallGate.InstallationInProgress(executable))
        {
            SiloPlayer.Helpers.AppUpdateNotice.Show();
            Exit();
            return;
        }
        _instanceNames = AppInstanceNames.Create(Environment.GetEnvironmentVariable("SILO_QA_INSTANCE_ID"));
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: _instanceNames.MutexName,
            createdNew: out var isPrimaryInstance);
        if (!isPrimaryInstance)
        {
            ForwardActivationToPrimary(e.Arguments);
            LocalLog.AppendLine("auth_startup.txt", "secondary_instance_blocked | activation_forwarded");
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Exit();
            return;
        }

        // Global unhandled exception handler -- write to crash log instead of silently dying.
        // Walks the entire InnerException chain so XamlParseException reasons (which are
        // usually nested) are captured, not just the top-level "RangeBase.Value" message.
        this.UnhandledException += (sender, args) =>
        {
            args.Handled = true;
            // The process can still end after this handler; queued lines would be lost.
            using var writeThrough = LocalLog.BeginWriteThrough();
            LocalLog.AppendLines("crash.txt", new[]
            {
                $"utc={DateTime.UtcNow:O} version={typeof(App).Assembly.GetName().Version}",
                "message=" + LocalLog.RedactDiagnosticText(args.Message),
                "last UI operation=" + LocalLog.RedactDiagnosticText(PerfBreadcrumb),
            });
            LocalLog.AppendLines("crash.txt", GetUiDiagnosticSnapshot());
            if (args.Exception != null)
                LocalLog.AppendException("crash.txt", args.Exception, "App.UnhandledException");
        };

        // Exceptions on worker threads bypass Application.UnhandledException and end the process.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            using var writeThrough = LocalLog.BeginWriteThrough();
            LocalLog.AppendLines("crash.txt", new[]
            {
                $"utc={DateTime.UtcNow:O} version={typeof(App).Assembly.GetName().Version} terminating={args.IsTerminating}",
                "last UI operation=" + LocalLog.RedactDiagnosticText(PerfBreadcrumb),
            });
            if (args.ExceptionObject is Exception exception)
                LocalLog.AppendException("crash.txt", exception, "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
            LocalLog.AppendException("errors.txt", args.Exception, "TaskScheduler.UnobservedTaskException");

        _window = new MainWindow();
        MainWindowInstance = (MainWindow)_window;
        MainWindowInstance.ActivateFromArgument(e.Arguments);
        _window.Activate();
        StartActivationPipeListener();
        _window.Closed += (_, _) =>
        {
            _activationPipeCts?.Cancel();
            _activationPipeCts?.Dispose();
            _activationPipeCts = null;
        };

        StartUiThreadLagDetector();
    }

    private void ForwardActivationToPrimary(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument)) return;
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                _instanceNames.PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: false)
            {
                AutoFlush = true,
            };
            writer.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(argument)));
        }
        catch (Exception ex)
        {
            LocalLog.AppendLine("auth_startup.txt", $"activation_forward_failed | type={ex.GetType().Name}");
        }
    }

    private void StartActivationPipeListener()
    {
        _activationPipeCts = new CancellationTokenSource();
        _activationPipeTask = ListenForActivationsAsync(_activationPipeCts.Token);
    }

    private async Task ListenForActivationsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _instanceNames.PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                var encoded = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(encoded)) continue;
                var argument = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                MainWindowInstance?.DispatcherQueue.TryEnqueue(
                    () => MainWindowInstance?.ActivateFromArgument(argument));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                LocalLog.AppendLine("auth_startup.txt", $"activation_receive_failed | type={ex.GetType().Name}");
            }
        }
    }

    public static string PerfBreadcrumb { get; private set; } = "";

    // Kept in memory and written only on an unhandled exception. Record counts,
    // indices and lifecycle events rather than media titles or credentials.
    private static readonly Queue<string> UiDiagnosticEvents = new();

    public static string[] GetUiDiagnosticSnapshot()
    {
        lock (UiDiagnosticEvents)
            return UiDiagnosticEvents.Select(LocalLog.RedactDiagnosticText).ToArray();
    }

    public static void RecordUiDiagnostic(string value)
    {
        lock (UiDiagnosticEvents)
        {
            while (UiDiagnosticEvents.Count >= 64)
                UiDiagnosticEvents.Dequeue();
            UiDiagnosticEvents.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {value}");
        }
    }

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
        services.AddSingleton(_ => new SiloPlayer.Core.Services.Updates.AppUpdateService(
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None })
                { Timeout = Timeout.InfiniteTimeSpan },
            Path.Combine(appDataDir, "updates"), typeof(App).Assembly.GetName().Version!,
            message => LocalLog.AppendLine("updates.txt", message))
            {PreferIncrementalUpdates=UpdateInstallation.FindRegisteredRoot(Environment.ProcessPath!) is { } updateRoot && SideBySideInstallation.ReadState(updateRoot) is not null,
                InstalledPayloadDirectory=AppContext.BaseDirectory,InstallationRoot=UpdateInstallation.FindRegisteredRoot(Environment.ProcessPath!)});
        services.AddSingleton<IAppUpdateInstaller>(new WindowsAppUpdateInstaller(Path.Combine(appDataDir, "updates")));
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<ICredentialStore>(sp => sp.GetRequiredService<CredentialStore>());

        // HTTP client and API
        services.AddSingleton<HttpClient>(_ =>
        {
            // Browser fetch automatically negotiates compressed responses and
            // modern HTTP versions. Match that transport behavior so large
            // catalog/search payloads are not downloaded uncompressed or
            // serialized behind an HTTP/1.1 connection.
            return SiloHttpClientFactory.CreateClient();
        });
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
            client.SetClientMetadata(
                SiloApiClient.DefaultClientName,
                clientVersion: null);

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
        services.AddSingleton<NotInterestedService>(sp => Controls.NotInterestedActions.CreateService(sp.GetRequiredService<CatalogApi>()));
        services.AddSingleton<SettingsApi>(sp => new SettingsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<WebhookSyncApi>(sp => new WebhookSyncApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<PlexBrowserAuthApi>();
        services.AddSingleton<PlaybackApi>(sp => new PlaybackApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<MediaMaintenanceApi>(sp => new MediaMaintenanceApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<PeopleApi>(sp => new PeopleApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<CollectionsApi>(sp => new CollectionsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<DownloadsApi>(sp => new DownloadsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<RequestsApi>(sp => new RequestsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<NotificationsApi>(sp => new NotificationsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<HistoryImportApi>(sp => new HistoryImportApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<RecommendationsApi>(sp => new RecommendationsApi(sp.GetRequiredService<SiloApiClient>()));
        services.AddSingleton<ApiKeysApi>(sp => new ApiKeysApi(sp.GetRequiredService<SiloApiClient>()));
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
        services.AddSingleton<UICustomizationService>();
        services.AddSingleton(provider =>
        {
            var cache = new ItemDetailPrefetchCache(
                provider.GetRequiredService<CatalogApi>(),
                provider.GetRequiredService<AuthService>());
            DetailCacheInvalidation.Attach(cache);
            return cache;
        });
        services.AddSingleton<EpisodeSpoilerPreferences>();

        // Player service (owns mpv lifecycle, not tied to page navigation)
        services.AddSingleton<PlayerService>();
        services.AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>();
        services.AddSingleton<AccessibilityService>();

        // Watch-Together coordinator — bridges room VM and PlayerService so
        // synced transport commands reach mpv. Must be constructed on the UI
        // thread; resolved lazily by WatchTogetherRoomPage on load.
        services.AddSingleton<WatchTogetherCoordinator>();

        // ViewModels
        services.AddTransient<ServerSelectViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<SignupViewModel>();
        services.AddTransient<InviteClaimViewModel>();
        services.AddTransient<ActivateDeviceViewModel>();
        services.AddTransient<ProfileSelectViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddSingleton<ThemeMusicService>();
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
        services.AddTransient<SiloPlayer.ViewModels.TasteSeedViewModel>();
        services.AddTransient<SiloPlayer.ViewModels.NotificationSettingsViewModel>();

        var provider = services.BuildServiceProvider();

        // Wire automatic token refresh on 401 responses
        var apiClient = provider.GetRequiredService<SiloApiClient>();
        var authService = provider.GetRequiredService<AuthService>();
        apiClient.SetTokenRefresher(ct => authService.TryRefreshAsync(ct));
        apiClient.SetProfileVerificationRequiredHandler(authService.HandleProfileVerificationRequired);

        return provider;
    }
}
