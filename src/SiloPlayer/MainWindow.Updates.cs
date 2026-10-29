using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;
using SiloPlayer.Services;

namespace SiloPlayer;

public sealed partial class MainWindow
{
    private AppUpdateService? _appUpdates;
    private CancellationTokenSource? _appUpdateLifetime;
    private Task _appUpdateLoop = Task.CompletedTask;
    private ContentDialog? _appUpdateDialog;
    private TextBlock? _updateStatusText;
    private ProgressBar? _updateProgress;
    private ComboBox? _updateModeCombo;
    private bool _refreshingUpdateMode;
    private bool _manualUpdateRequested;
    private bool _updateSessionEnding;
    private bool _updateCloseGuardAvailable;
    private bool _updateDialogBusy;
    private CancellationTokenSource? _manualUpdateConsent;
    private string? _announcedUpdate;
    private long _updatePaneToken, _updatePaneVisibleToken;
    private UpdateSubclassProc? _updateSubclass;
    private IntPtr _updateHwnd;
    private static SolidColorBrush UpdateGreen => Helpers.AppUpdateAppearance.Green();

    private void InitializeAppUpdates()
    {
        _appUpdates = App.Services.GetService<AppUpdateService>();
        if (_appUpdates is null) return;
        _appUpdates.SetMode(App.Services.GetRequiredService<SettingsService>().Load().UpdateMode);
        _appUpdateLifetime = new CancellationTokenSource();
        _appUpdates.Changed += OnAppUpdateChanged;
        _playerService.StateChanged += OnUpdatePlaybackChanged;
        _updatePaneToken = NavView.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => RefreshUpdatePresentation());
        _updatePaneVisibleToken = NavView.RegisterPropertyChangedCallback(NavigationView.IsPaneVisibleProperty, (_, _) => RefreshUpdatePresentation());
        _updateHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _updateSubclass = ObserveUpdateWindowMessage;
        _updateCloseGuardAvailable = SetUpdateSubclass(_updateHwnd, _updateSubclass, 0x53494C55, UIntPtr.Zero);
        if (!_updateCloseGuardAvailable)
            LocalLog.AppendLine("updates.txt", "session_guard_unavailable; automatic handoff disabled");
        RefreshUpdatePresentation();
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") is null)
            _appUpdateLoop = RunUpdateChecksAsync(_appUpdateLifetime.Token);
        RootGrid.Loaded += AppUpdateRootLoaded;
    }

    private void AppUpdateRootLoaded(object sender, RoutedEventArgs args)
    {
        RootGrid.Loaded -= AppUpdateRootLoaded;
        ReadUpdateResult();
        _=NotifySideBySideStartupAsync();
        RefreshUpdatePresentation();
    }

    private async Task RunUpdateChecksAsync(CancellationToken ct)
    {
        try
        {
            await Task.Run(() => _appUpdates!.CleanupInstalledPackages(), ct);
            await _appUpdates!.RunAutomaticChecksAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "background_update_check"); }
    }

    private void OnAppUpdateChanged(AppUpdateState state) => DispatcherQueue.TryEnqueue(RefreshUpdatePresentation);
    private void OnUpdatePlaybackChanged(PlayerState state)
    {
        _appUpdates?.SetPlaybackActive(state != PlayerState.Idle || _playerService.IsLoading);
        DispatcherQueue.TryEnqueue(RefreshUpdatePresentation);
    }

    private void RefreshUpdatePresentation()
    {
        if (_appUpdates is null || _appUpdateLifetime?.IsCancellationRequested != false) return;
        var state = _appUpdates.State;
        var available = state.Release is not null;
        UpdateAvailableButton.Foreground = Helpers.AppUpdateAppearance.Green("SidebarBackgroundBrush");
        UpdateAvailableButton.Visibility = available && NavView.IsPaneVisible && NavView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        ProfileUpdateDot.Visibility = available && !NavView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        StandaloneUpdateButton.Visibility = available && (!NavView.IsPaneVisible || (_isNarrowShell && !NavView.IsPaneOpen))
            && _playerService.State == PlayerState.Idle ? Visibility.Visible : Visibility.Collapsed;
        StandaloneUpdateButton.Margin = new Thickness(0, 12, _isNarrowShell ? 64 : 18, 0);
        ProfileNameText.MaxWidth = available && NavView.IsPaneOpen ? Math.Max(24, NavView.OpenPaneLength - 210) : double.PositiveInfinity;
        UpdateMenuButton.Content = available ? "Update Available — view changelog" : "Check for updates";
        UpdateMenuButton.Foreground = available ? Helpers.AppUpdateAppearance.Green("CardBackgroundBrush") : (Brush)Application.Current.Resources["PrimaryTextBrush"];
        if (available && RootGrid.IsLoaded && _playerService.State == PlayerState.Idle)
        {
            var key = state.Release!.Tag + state.Release.Sha256;
            if (_announcedUpdate != key)
            {
                _announcedUpdate = key;
                App.Services.GetRequiredService<ToastService>().Success($"Silo {state.Release.Version.ToString(3)} is available.",
                    "View update", () => ShowAppUpdateDialogAsync());
            }
        }
        RefreshUpdateDialog();
        _appUpdates.SetPlaybackActive(_playerService.State != PlayerState.Idle || _playerService.IsLoading);
        if (state.Release is not null && !state.IsChecking && !state.IsDownloading && state.ReadyInstaller is null && state.PreparedPackage is null && state.Error is null
            && _appUpdates.Mode == AppUpdateMode.Automatic && _playerService.State == PlayerState.Idle && !_playerService.IsLoading)
            _ = _appUpdates.DownloadAsync(automatic: true, ct: _appUpdateLifetime!.Token);
    }

    private async void AppUpdateButton_Click(object sender, RoutedEventArgs args)
    {
        ProfileFooterFlyout.Hide();
        await ShowAppUpdateDialogAsync(manualCheck: _appUpdates?.State.Release is null);
    }

    public async Task ShowAppUpdateDialogAsync(bool manualCheck = false)
    {
        if (_appUpdates is null || _appUpdateDialog is not null) return;
        var state = _appUpdates.State;
        var panel = new StackPanel { Spacing = 14, MinWidth = 0, MaxWidth = 640 };
        var versions = new TextBlock { Text = "Installed version " + typeof(App).Assembly.GetName().Version!.ToString(3), FontSize = 13, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(versions);
        panel.Children.Add(new TextBlock { Name = "AppUpdatePublishedDate", Text = state.Release is null ? "" : "Published " + Helpers.DateTimeDisplay.FormatDate(state.Release.PublishedAt), FontSize = 12 });
        _updateStatusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UpdateGreen, FontSize = 14 };
        panel.Children.Add(_updateStatusText);
        _updateProgress = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        panel.Children.Add(_updateProgress);
        var notes = new TextBlock { Name = "AppUpdateReleaseNotes", Text = state.Release?.Notes ?? "No new release notes are available yet.",
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 13 };
        panel.Children.Add(new ScrollViewer { Content = notes, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        panel.Children.Add(new HyperlinkButton { Name = "AppUpdateFullRelease", Content = "Full release notes on GitHub",
            HorizontalAlignment = HorizontalAlignment.Left, Visibility = state.Release is null ? Visibility.Collapsed : Visibility.Visible,
            NavigateUri = state.Release is null ? null : new Uri("https://github.com/ellermw/SiloDesktopApp/releases/tag/" + Uri.EscapeDataString(state.Release.Tag)) });
        panel.Children.Add(new TextBlock { Text = "Update preference", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _updateModeCombo = new ComboBox { Name = "AppUpdateModeCombo", HorizontalAlignment = HorizontalAlignment.Stretch };
        _updateModeCombo.Items.Add("Notify me; I'll choose when to update");
        _updateModeCombo.Items.Add("Download automatically; install when I close Silo");
        _updateModeCombo.SelectedIndex = (int)_appUpdates.Mode;
        _updateModeCombo.SelectionChanged += (_, _) =>
        {
            if (_refreshingUpdateMode || _updateModeCombo.SelectedIndex < 0) return;
            var mode = (AppUpdateMode)_updateModeCombo.SelectedIndex;
            var preferences = App.Services.GetRequiredService<SettingsService>(); var config = preferences.Load(); config.UpdateMode = mode; preferences.Save(config);
            _appUpdates.SetMode(mode);
        };
        panel.Children.Add(_updateModeCombo);
        panel.Children.Add(new TextBlock { Text = "Both modes notify you and let you update manually. Automatic downloads pause for playback and install only after normal app exit. Windows may request administrator approval.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        panel.Children.Add(new TextBlock { Text = "Silo stays open while the update is prepared and installed. Playback is saved before the final restart.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var check = new Button { Content = "Check for updates", HorizontalAlignment = HorizontalAlignment.Left };
        check.Click += async (_, _) => { await _appUpdates.CheckAsync(_appUpdateLifetime!.Token); RefreshUpdateDialog(); };
        panel.Children.Add(check);
        var dialog = new ContentDialog { Title = "Silo updates", XamlRoot = RootGrid.XamlRoot, Content = panel,
            CloseButtonText = "Later", PrimaryButtonText = "Update now", DefaultButton = ContentDialogButton.Close, MinWidth = 0 };
        // The outer dialog covers the complete XamlRoot. Constraining its width
        // centers the surface in a smaller left-aligned overlay after a resize.
        dialog.Resources["ContentDialogMaxWidth"]=720d;
        _appUpdateDialog = dialog;
        dialog.PrimaryButtonClick += InstallAppUpdate_Click;
        dialog.Closing += (_, _) => { if (!_manualUpdateRequested) _manualUpdateConsent?.Cancel(); };
        RefreshUpdateDialog();
        try
        {
            var show = dialog.ShowAsync();
            if (manualCheck) _ = _appUpdates.CheckAsync(_appUpdateLifetime!.Token);
            await show;
        }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "show_update_dialog"); }
        finally
        {
            if (!_manualUpdateRequested) _manualUpdateConsent?.Cancel();
            _appUpdateDialog = null; _updateModeCombo = null; _updateStatusText = null; _updateProgress = null;
        }
    }

    private void RefreshUpdateDialog()
    {
        if (_appUpdateDialog is null || _appUpdates is null || _updateStatusText is null) return;
        var state = _appUpdates.State;
        _updateStatusText.Text = state.Error ?? (state.IsChecking ? "Checking for updates…" : state.IsDownloading
            ? state.PreparationProgress is { } prep ? $"{prep.Stage} · {prep.CompletedFiles}/{prep.TotalFiles} files · {prep.CompletedBytes/1048576.0:0.0} MB downloaded" : $"Downloading {state.DownloadedBytes / 1048576.0:0.0} / {state.Release!.Size / 1048576.0:0.0} MB…"
            : state.Release is null ? "You're up to date." : state.ReadyInstaller is not null || state.PreparedPackage is not null ? "Ready to update to " + state.Release.Version.ToString(3)
            : "Update available: " + state.Release.Version.ToString(3));
        _updateStatusText.Foreground = state.Error is null ? UpdateGreen : (Brush)Application.Current.Resources["SecondaryTextBrush"];
        _updateProgress!.Visibility = state.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        _updateProgress.Value = state.PreparationProgress is {TotalFiles:>0} prepared ? 100d*prepared.CompletedFiles/prepared.TotalFiles : state.Release is { Size: > 0 } release ? 100d * state.DownloadedBytes / release.Size : 0;
        _appUpdateDialog.IsPrimaryButtonEnabled = state.Release is not null && !_updateDialogBusy;
        _appUpdateDialog.PrimaryButtonText = _updateDialogBusy ? "Preparing update…" : state.IsDownloading ? "Update when ready" : "Update now";
        foreach (var child in ((StackPanel)_appUpdateDialog.Content).Children)
        {
            if (child is ScrollViewer { Content: TextBlock notes }) notes.Text = state.Release?.Notes ?? "No new release notes are available.";
            if (child is TextBlock { Name: "AppUpdatePublishedDate" } date) date.Text = state.Release is null ? "" : "Published " + Helpers.DateTimeDisplay.FormatDate(state.Release.PublishedAt);
            if (child is HyperlinkButton { Name: "AppUpdateFullRelease" } link)
            {
                link.Visibility = state.Release is null ? Visibility.Collapsed : Visibility.Visible;
                link.NavigateUri = state.Release is null ? null : new Uri("https://github.com/ellermw/SiloDesktopApp/releases/tag/" + Uri.EscapeDataString(state.Release.Tag));
            }
        }
        _refreshingUpdateMode = true;
        try { _updateModeCombo!.SelectedIndex = (int)_appUpdates.Mode; } finally { _refreshingUpdateMode = false; }
    }

    private async void InstallAppUpdate_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_appUpdates is null || _updateDialogBusy) return;
        if (App.Services.GetService<IAppUpdateInstaller>()?.IsSupported != true)
        {
            App.Services.GetRequiredService<ToastService>().Error("Install Silo using its Windows installer before using in-app updates."); return;
        }
        _updateDialogBusy = true; RefreshUpdateDialog();
        using var consent = CancellationTokenSource.CreateLinkedTokenSource(_appUpdateLifetime!.Token);
        _manualUpdateConsent = consent;
        try
        {
            await _appUpdates.DownloadAsync(ct: consent.Token).WaitAsync(consent.Token);
            if (!await _appUpdates.ReverifyReadyAsync(consent.Token)) return;
            consent.Token.ThrowIfCancellationRequested();
            if(_appUpdates.State is {PreparedPackage:{ } package,Release:{ } release})
            {
                var staged=App.Services.GetService<IAppUpdateInstaller>() as IStagedAppUpdateInstaller;
                if(staged is null || !await staged.PrepareAsync(release,package,true,_updateHwnd.ToInt64(),consent.Token))
                {
                    _appUpdates.ApplyAuthorityCooldown(staged?.AuthorityCooldownUntil);
                    App.Services.GetRequiredService<ToastService>().Error(_appUpdates.State.Error??"The update could not start. You can retry or use the full installer from the release page.");return;
                }
            }
            _manualUpdateRequested = true;
            sender.Hide(); await CloseWithAppUpdatesAsync();
        }
        catch (OperationCanceledException) when (consent.IsCancellationRequested) { await _appUpdates.StopAsync(); }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "manual_update"); }
        finally { _manualUpdateConsent = null; _updateDialogBusy = false; RefreshUpdateDialog(); }
    }

    private async Task<bool> PrepareAppUpdateExitAsync()
    {
        if (_appUpdates is null) return true;
        var closeArmed=false;
        var allowClose=!_manualUpdateRequested || _updateSessionEnding;
        LocalLog.AppendLine("updates.txt", $"exit_requested manual={_manualUpdateRequested} automaticEligible={_appUpdates.ShouldInstallOnExit} sessionEnding={_updateSessionEnding} closeGuard={_updateCloseGuardAvailable}");
        try
        {
            await _appUpdates.StopAsync();
            if (!_manualUpdateRequested && !_updateCloseGuardAvailable) return allowClose;
            if (_updateSessionEnding || (!_manualUpdateRequested && !_appUpdates.ShouldInstallOnExit)) return allowClose;
            var installer = App.Services.GetService<IAppUpdateInstaller>();
            if (installer?.IsSupported != true || !await _appUpdates.ReverifyReadyAsync()) return allowClose;
            if(_appUpdates.State is {PreparedPackage:{ } package,Release:{ } release} && installer is IStagedAppUpdateInstaller staged)
            {
                if(!staged.HasPreparedHandoff && !await staged.PrepareAsync(release,package,_manualUpdateRequested,_updateHwnd.ToInt64(),CancellationToken.None))
                {_appUpdates.ApplyAuthorityCooldown(staged.AuthorityCooldownUntil);return allowClose;}
            }
            using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _playerService.CloseAsync().WaitAsync(drain.Token);
            await _playerService.PendingProgressSave.WaitAsync(drain.Token);
            if(installer is IStagedAppUpdateInstaller stagedInstaller && stagedInstaller.HasPreparedHandoff)
            {stagedInstaller.ArmNormalClose();closeArmed=true;allowClose=true;}
            var state = _appUpdates.State;
            if (state.Release is not null && state.ReadyInstaller is not null)
                allowClose=await installer.ScheduleAsync(state.Release, state.ReadyInstaller, _manualUpdateRequested, CancellationToken.None) || !_manualUpdateRequested;
            return allowClose;
        }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "close_update_handoff");return allowClose; }
        finally
        {
            if(!closeArmed && App.Services.GetService<IAppUpdateInstaller>() is IStagedAppUpdateInstaller abandoned)abandoned.CancelPreparedHandoff();
            if(allowClose){_appUpdateLifetime?.Cancel();await _appUpdateLoop;}
            else
            {
                _manualUpdateRequested=false;
                App.Services.GetRequiredService<ToastService>().Error("The update was deferred because playback could not be saved or the update could not be verified. Silo is still open; try again when ready.");
            }
        }
    }

    private void DetachAppUpdates()
    {
        _appUpdateLifetime?.Cancel();
        if (_appUpdates is not null) _appUpdates.Changed -= OnAppUpdateChanged;
        _playerService.StateChanged -= OnUpdatePlaybackChanged;
        RootGrid.Loaded -= AppUpdateRootLoaded;
        if (_updatePaneToken != 0) NavView.UnregisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, _updatePaneToken);
        if (_updatePaneVisibleToken != 0) NavView.UnregisterPropertyChangedCallback(NavigationView.IsPaneVisibleProperty, _updatePaneVisibleToken);
        if (_updateSubclass is not null) RemoveUpdateSubclass(_updateHwnd, _updateSubclass, 0x53494C55);
        _appUpdateDialog?.Hide();
    }

    private void ReadUpdateResult()
    {
        if (_appUpdates is null) return;
        try
        {
            var path = Path.Combine(_appUpdates.CacheDirectory, "last-result.json");
            AppUpdateDownloader.EnsureOrdinaryPath(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var state = json.RootElement.GetProperty("state").GetString();
            var message = json.RootElement.GetProperty("message").GetString() ?? "Open update details to try again.";
            if (state == "installed") App.Services.GetRequiredService<ToastService>().Success(message);
            else App.Services.GetRequiredService<ToastService>().Warning("Previous update: " + message);
            File.Delete(path);
        }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "read_update_result"); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr UpdateSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, UIntPtr data);
    private IntPtr ObserveUpdateWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, UIntPtr data)
    {
        try
        {
            if (message == 0x11) _updateSessionEnding = true;
            else if (message == 0x16) _updateSessionEnding = wParam != IntPtr.Zero;
        }
        catch (Exception ex) { LocalLog.AppendLine("updates.txt", "session_guard_failed kind=" + ex.GetType().Name); }
        return DefUpdateSubclass(window, message, wParam, lParam);
    }
    [DllImport("comctl32.dll", EntryPoint = "SetWindowSubclass")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetUpdateSubclass(IntPtr window, UpdateSubclassProc callback, nuint id, UIntPtr data);
    [DllImport("comctl32.dll", EntryPoint = "RemoveWindowSubclass")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveUpdateSubclass(IntPtr window, UpdateSubclassProc callback, nuint id);
    [DllImport("comctl32.dll", EntryPoint = "DefSubclassProc")]
    private static extern IntPtr DefUpdateSubclass(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
