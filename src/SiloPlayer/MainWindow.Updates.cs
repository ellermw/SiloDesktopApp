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
    internal CancellationToken AppUpdateLifetimeToken => _appUpdateLifetime?.Token ?? new CancellationToken(true);
    private Task _appUpdateLoop = Task.CompletedTask;
    private ContentDialog? _appUpdateDialog;
    private StackPanel? _updateBodyPanel;
    private TextBlock? _updateStatusText;
    private ProgressBar? _updateProgress;
    private ComboBox? _updateModeCombo;
    private ComboBox? _updateChannelCombo;
    private TextBlock? _updateBetaWarning, _updateFullInstallerNote, _updateKeepOpenNote;
    private bool _refreshingUpdatePreferences;
    private bool _manualUpdateRequested;
    private bool _updateSessionEnding;
    private bool _updateCloseGuardAvailable;
    private bool _updateDialogBusy;
    private bool _updateWindowOwnsUpdate;
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
        LoadDeferredAppUpdate();
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
        TryResumeDeferredAppUpdate();
        // A running update window already shows this release; don't also announce it.
        if (available && RootGrid.IsLoaded && _playerService.State == PlayerState.Idle && !_updateWindowOwnsUpdate && !_updateDialogBusy)
        {
            var key = state.Release!.Tag + state.Release.Sha256;
            if (_announcedUpdate != key)
            {
                _announcedUpdate = key;
                App.Services.GetRequiredService<ToastService>().Success($"Silo {state.Release.DisplayVersion}{(state.Release.IsBeta ? " (Beta)" : "")} is available.",
                    "View update", () => ShowAppUpdateDialogAsync());
            }
        }
        RefreshUpdateDialog();
        _appUpdates.SetPlaybackActive(_playerService.State != PlayerState.Idle || _playerService.IsLoading);
        if (state.Release is not null && !state.RequiresFullInstaller && !state.IsChecking && !state.IsDownloading && state.ReadyInstaller is null && state.PreparedPackage is null && state.Error is null
            && _appUpdates.Mode == AppUpdateMode.Automatic && _playerService.State == PlayerState.Idle && !_playerService.IsLoading && !RequiresManualFullInstaller(state))
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
        _updateBodyPanel = panel;
        var installed = _appUpdates.InstalledReleaseVersion;
        var versions = new TextBlock { Text = installed is null ? $"Installed version {_appUpdates.InstalledDisplayVersion}"
            : $"Installed version {installed.Text} · {installed.Designation}", FontSize = 13, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(versions);
        panel.Children.Add(new TextBlock { Name = "AppUpdatePublishedDate", Text = state.Release is null ? "" : "Published " + Helpers.DateTimeDisplay.FormatDate(state.Release.PublishedAt), FontSize = 12 });
        _updateStatusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UpdateGreen, FontSize = 14 };
        panel.Children.Add(_updateStatusText);
        _updateProgress = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        panel.Children.Add(_updateProgress);
        var notes = new TextBlock { Name = "AppUpdateReleaseNotes", Text = state.Release?.Notes ?? "No new release notes are available yet.",
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 13 };
        panel.Children.Add(notes);
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
            if (_refreshingUpdatePreferences || _updateModeCombo.SelectedIndex < 0) return;
            var mode = (AppUpdateMode)_updateModeCombo.SelectedIndex;
            var preferences = App.Services.GetRequiredService<SettingsService>(); var config = preferences.Load(); config.UpdateMode = mode; preferences.Save(config);
            _appUpdates.SetMode(mode);
        };
        panel.Children.Add(_updateModeCombo);
        var updateService = _appUpdates;
        var channelCombo = new ComboBox { Name = "AppUpdateChannelCombo", Header = "Update channel", HorizontalAlignment = HorizontalAlignment.Stretch };
        channelCombo.Items.Add("Stable (default)"); channelCombo.Items.Add("Beta"); channelCombo.SelectedIndex = (int)updateService.Channel;
        _updateChannelCombo = channelCombo;
        AutomationProperties.SetName(channelCombo, "Desktop update channel");
        channelCombo.SelectionChanged += async (_, _) =>
        {
            if (_refreshingUpdatePreferences || channelCombo.SelectedIndex < 0 || _updateDialogBusy) return;
            var channel = (AppUpdateChannel)channelCombo.SelectedIndex;
            var ct = AppUpdateLifetimeToken;
            try
            {
                await updateService.SetChannelAsync(channel, ct);
                ct.ThrowIfCancellationRequested();
                if (updateService.Channel != channel || updateService.IsChangingChannel) return;
                await updateService.CheckAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "dialog_update_channel"); }
            finally { RefreshUpdateDialog(); }
        };
        panel.Children.Add(channelCombo);
        _updateBetaWarning = new TextBlock { Name = "AppUpdateBetaWarning", Text = "Beta releases may contain bugs. Choose Stable for regular approved updates.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
        panel.Children.Add(_updateBetaWarning);
        panel.Children.Add(new TextBlock { Text = "Both modes notify you and let you update manually. Automatic downloads pause for playback and install only after normal app exit. Windows may request administrator approval.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        _updateKeepOpenNote = new TextBlock { Text = "Silo stays open while a compatible update is prepared and installed. Playback is saved before the final restart.", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        panel.Children.Add(_updateKeepOpenNote);
        var check = new Button { Name = "AppUpdateCheckButton", Content = "Check for updates", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        check.Click += async (_, _) => { await _appUpdates.CheckAsync(_appUpdateLifetime!.Token); RefreshUpdateDialog(); };
        var scroll = new ScrollViewer { Name = "AppUpdateBodyScroll", Content = panel, MinWidth = 0, MinHeight = 0,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var content = new Grid { Name = "AppUpdateDialogLayout", MinWidth = 0, MaxWidth = 640 };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _updateFullInstallerNote = new TextBlock { Name = "AppUpdateFullInstallerNote", Text = "This update needs the full installer. Silo must close first; playback will be saved. Choose Install full update to continue.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
        content.Children.Add(scroll); Grid.SetRow(_updateFullInstallerNote, 1); content.Children.Add(_updateFullInstallerNote); Grid.SetRow(check, 2); content.Children.Add(check);
        var dialog = new ContentDialog { Title = "Silo updates", XamlRoot = RootGrid.XamlRoot, Content = content,
            CloseButtonText = "Later", PrimaryButtonText = "Update now", DefaultButton = ContentDialogButton.Close, MinWidth = 0 };
        // The outer dialog covers the complete XamlRoot. Constraining its width
        // centers the surface in a smaller left-aligned overlay after a resize.
        dialog.Resources["ContentDialogMaxWidth"]=720d;
        _appUpdateDialog = dialog;
        dialog.PrimaryButtonClick += InstallAppUpdate_Click;
        // Closing the dialog cancels an update it owns, never one the update window took over.
        dialog.Closing += (_, _) => { if (!_manualUpdateRequested && !_updateWindowOwnsUpdate) _manualUpdateConsent?.Cancel(); };
        // Reserve space for the dialog title, action footer and outer margins.
        // A finite body viewport keeps long notes/preferences scrollable while
        // Check for updates stays outside the scroller and above the footer.
        void SizeBody() => content.MaxHeight = Math.Max(0, Math.Min(480, (RootGrid.XamlRoot?.Size.Height ?? RootGrid.ActualHeight) - 240));
        SizeChangedEventHandler resized = (_, _) => SizeBody();
        RootGrid.SizeChanged += resized;
        try
        {
            SizeBody(); RefreshUpdateDialog();
            var show = dialog.ShowAsync();
            if (manualCheck) _ = _appUpdates.CheckAsync(_appUpdateLifetime!.Token);
            await show;
        }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "show_update_dialog"); }
        finally
        {
            RootGrid.SizeChanged -= resized;
            if (!_manualUpdateRequested && !_updateWindowOwnsUpdate) _manualUpdateConsent?.Cancel();
            _appUpdateDialog = null; _updateBodyPanel = null; _updateModeCombo = null; _updateStatusText = null; _updateProgress = null;
            _updateChannelCombo = null; _updateBetaWarning = null; _updateFullInstallerNote = null; _updateKeepOpenNote = null;
        }
    }

    private bool RequiresManualFullInstaller(AppUpdateState state)
    {
        // Legacy clients always use the full installer, even before a download
        // has discovered a versioned migration fallback.
        if (state.Release is null || (!state.RequiresFullInstaller && _appUpdates?.PreferIncrementalUpdates != false)) return false;
        try { return App.Services.GetService<IAppUpdateInstaller>()?.CanScheduleFullInstaller != true; }
        catch (Exception ex)
        {
            LocalLog.AppendException("updates.txt", ex, "full_installer_capability_failed");
            return true;
        }
    }

    private void RefreshUpdateDialog()
    {
        if (_appUpdateDialog is null || _appUpdates is null || _updateStatusText is null || _updateBodyPanel is null) return;
        var state = _appUpdates.State;
        _updateStatusText.Text = state.Error ?? (_appUpdates.IsChangingChannel ? "Switching update channel…" : state.IsChecking ? "Checking for updates…" : state.IsDownloading
            ? state.PreparationProgress is { } prep ? $"{prep.Stage} · {prep.CompletedFiles}/{prep.TotalFiles} files · {prep.CompletedBytes/1048576.0:0.0} MB downloaded" : $"Downloading {state.DownloadedBytes / 1048576.0:0.0} / {state.Release!.Size / 1048576.0:0.0} MB…"
            : state.Release is null ? state.ChannelNotice ?? "You're up to date." : state.ReadyInstaller is not null || state.PreparedPackage is not null ? "Ready to update to " + state.Release.DisplayVersion + (state.Release.IsBeta ? " · Beta" : " · Stable")
            : "Update available: " + state.Release.DisplayVersion + (state.Release.IsBeta ? " · Beta" : " · Stable"));
        if (state.Error is null && !state.IsChecking && !state.IsDownloading && state.Release is not null && state.ChannelNotice is { } notice)
            _updateStatusText.Text += "\n" + notice;
        _updateStatusText.Foreground = state.Error is null ? UpdateGreen : (Brush)Application.Current.Resources["SecondaryTextBrush"];
        _updateProgress!.Visibility = state.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        _updateProgress.Value = state.PreparationProgress is {TotalFiles:>0} prepared ? 100d*prepared.CompletedFiles/prepared.TotalFiles : state.Release is { Size: > 0 } release ? 100d * state.DownloadedBytes / release.Size : 0;
        var manualInstaller = RequiresManualFullInstaller(state);
        _appUpdateDialog.IsPrimaryButtonEnabled = state.Release is not null && !_updateDialogBusy && !_appUpdates.IsChangingChannel && !manualInstaller;
        _appUpdateDialog.PrimaryButtonText = manualInstaller ? "Manual installer required" : _updateDialogBusy ? "Preparing update…" : state.IsDownloading ? "Update when ready"
            : state.RequiresFullInstaller ? state.ReadyInstaller is not null ? "Install full update" : "Download full update" : "Update now";
        if (_updateChannelCombo is not null) _updateChannelCombo.IsEnabled = !_updateDialogBusy && !_appUpdates.IsChangingChannel;
        if (_updateBetaWarning is not null) _updateBetaWarning.Visibility = _appUpdates.Channel == AppUpdateChannel.Beta || _appUpdates.InstalledReleaseVersion?.IsPrerelease == true ? Visibility.Visible : Visibility.Collapsed;
        if (_updateFullInstallerNote is not null)
        {
            _updateFullInstallerNote.Visibility = state.RequiresFullInstaller || manualInstaller ? Visibility.Visible : Visibility.Collapsed;
            _updateFullInstallerNote.Text = manualInstaller ? UpdateInstallation.ManualFullInstallerMessage
                : "This update needs the full installer. Silo must close first; playback will be saved. Choose Install full update to continue.";
        }
        if (_updateKeepOpenNote is not null) _updateKeepOpenNote.Visibility = state.RequiresFullInstaller || manualInstaller ? Visibility.Collapsed : Visibility.Visible;
        foreach (var child in _updateBodyPanel.Children)
        {
            if (child is TextBlock { Name: "AppUpdateReleaseNotes" } notes) notes.Text = state.Release?.Notes ?? "No new release notes are available.";
            if (child is TextBlock { Name: "AppUpdatePublishedDate" } date) date.Text = state.Release is null ? "" : "Published " + Helpers.DateTimeDisplay.FormatDate(state.Release.PublishedAt);
            if (child is HyperlinkButton { Name: "AppUpdateFullRelease" } link)
            {
                link.Content = manualInstaller ? "Download full installer on GitHub" : "Full release notes on GitHub";
                link.Visibility = state.Release is null ? Visibility.Collapsed : Visibility.Visible;
                link.NavigateUri = state.Release is null ? null : new Uri("https://github.com/ellermw/SiloDesktopApp/releases/tag/" + Uri.EscapeDataString(state.Release.Tag));
            }
        }
        _refreshingUpdatePreferences = true;
        try { _updateModeCombo!.SelectedIndex = (int)_appUpdates.Mode; if (_updateChannelCombo is not null) _updateChannelCombo.SelectedIndex = (int)_appUpdates.Channel; }
        finally { _refreshingUpdatePreferences = false; }
    }

    private async void InstallAppUpdate_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_appUpdates is null || _updateDialogBusy) return;
        var fullMigrationConfirmed = _appUpdates.State.RequiresFullInstaller && _appUpdates.State.ReadyInstaller is not null;
        if (App.Services.GetService<IAppUpdateInstaller>()?.IsSupported != true)
        {
            App.Services.GetRequiredService<ToastService>().Error("Install Silo using its Windows installer before using in-app updates."); return;
        }
        if (RequiresManualFullInstaller(_appUpdates.State))
        {
            App.Services.GetRequiredService<ToastService>().Error(UpdateInstallation.ManualFullInstallerMessage); return;
        }
        _updateDialogBusy = true; RefreshUpdateDialog();
        using var consent = CancellationTokenSource.CreateLinkedTokenSource(_appUpdateLifetime!.Token);
        _manualUpdateConsent = consent;
        try
        {
            if (App.Services.GetService<IAppUpdateInstaller>() is IWindowedAppUpdateInstaller windowed
                && _appUpdates.PreferIncrementalUpdates && !_appUpdates.State.RequiresFullInstaller
                && _appUpdates.State.Release is { Incremental: not null } windowedRelease)
            {
                await RunWindowedUpdateAsync(sender, windowed, windowedRelease, confirmRestart: true, consent.Token);
                return;
            }
            await _appUpdates.DownloadAsync(ct: consent.Token).WaitAsync(consent.Token);
            if (RequiresManualFullInstaller(_appUpdates.State))
            {
                App.Services.GetRequiredService<ToastService>().Error(UpdateInstallation.ManualFullInstallerMessage);
                return;
            }
            if (!await _appUpdates.ReverifyReadyAsync(consent.Token)) return;
            consent.Token.ThrowIfCancellationRequested();
            // Discovering a full migration is not consent to close. Show the
            // verified package and close/save explanation, then require its action.
            if (_appUpdates.State.RequiresFullInstaller && !fullMigrationConfirmed) return;
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

    // From Update now on, the small update panel owns the update: this dialog closes, the panel
    // shows the download (only the files actually fetched), asks Restart now or Later, then
    // installs and restarts. A resumed deferral passes confirmRestart false and has no dialog.
    private async Task RunWindowedUpdateAsync(ContentDialog? dialog, IWindowedAppUpdateInstaller windowed, AppRelease release, bool confirmRestart, CancellationToken ct)
    {
        _updateWindowOwnsUpdate = true;
        try
        {
            var updates = _appUpdates!;
            var packageDirectory = IncrementalUpdateClient.PreparedDirectory(updates.CacheDirectory, release);
            dialog?.Hide();
            var outcome = await windowed.PrepareWithDownloadAsync(release, packageDirectory, true, confirmRestart, _updateHwnd.ToInt64(), async (report, token) =>
            {
                void Forward(AppUpdateState state) { if (state.PreparationProgress is { } progress) report(progress); }
                updates.Changed += Forward;
                try { await updates.DownloadAsync(ct: token).WaitAsync(token); }
                finally { updates.Changed -= Forward; }
                if (updates.State.RequiresFullInstaller) return new(null, null);
                if (updates.State.PreparedPackage is null || !await updates.ReverifyReadyAsync(token))
                    return new(null, updates.State.Error ?? "The update could not be downloaded. You can keep using Silo and try again.");
                if (confirmRestart) _windowedDownloadReady = release;
                return new(updates.State.PreparedPackage, null);
            }, ct);
            if (outcome == WindowedUpdateOutcome.Deferred)
            {
                // Later: Silo keeps running; the download stays ready and the next start finishes it.
                DeferAppUpdate(release);
                return;
            }
            if (outcome != WindowedUpdateOutcome.Staged)
            {
                // A promoted automatic transfer is not owned by the window's cancellation token.
                await updates.StopAsync();
                updates.ApplyAuthorityCooldown((windowed as IStagedAppUpdateInstaller)?.AuthorityCooldownUntil);
                // A verified migration still needs the full installer and its own consent in the dialog.
                if (updates.State.RequiresFullInstaller && !ct.IsCancellationRequested)
                    DispatcherQueue.TryEnqueue(() => _ = ShowAppUpdateDialogAsync());
                else if (!ct.IsCancellationRequested)
                    App.Services.GetRequiredService<ToastService>().Error(updates.State.Error ?? "The update could not start. You can retry or use the full installer from the release page.");
                return;
            }
            // Staged: any earlier Later is now carried out, so the next start must not resume it again.
            DeferredAppUpdate.Clear(updates.CacheDirectory);
            _manualUpdateRequested = true;
            await CloseWithAppUpdatesAsync();
        }
        finally { _updateWindowOwnsUpdate = false; _windowedDownloadReady = null; }
    }

    private async Task<bool> PrepareAppUpdateExitAsync()
    {
        if (_appUpdates is null) return true;
        var closeArmed=false;
        var placementSaved=false;
        var allowClose=!_manualUpdateRequested || _updateSessionEnding;
        LocalLog.AppendLine("updates.txt", $"exit_requested manual={_manualUpdateRequested} automaticEligible={_appUpdates.ShouldInstallOnExit} sessionEnding={_updateSessionEnding} closeGuard={_updateCloseGuardAvailable}");
        try
        {
            if (_updateWindowOwnsUpdate && !_manualUpdateRequested)
            {
                // A user close abandons the window's handoff, never starts a second one. Closing
                // while the panel asks Restart now or Later counts as Later: the next start finishes it.
                // A resumed deferral keeps its record on disk until it is staged, so it survives this close too.
                if (_windowedDownloadReady is { } downloaded) DeferAppUpdate(downloaded);
                _manualUpdateConsent?.Cancel();
                await _appUpdates.StopAsync();
                return allowClose;
            }
            await _appUpdates.StopAsync();
            if (!_manualUpdateRequested && !_updateCloseGuardAvailable) return allowClose;
            if (_updateSessionEnding || (!_manualUpdateRequested && !_appUpdates.ShouldInstallOnExit)) return allowClose;
            var installer = App.Services.GetService<IAppUpdateInstaller>();
            if (installer?.IsSupported != true || !await _appUpdates.ReverifyReadyAsync()) return allowClose;
            if (RequiresManualFullInstaller(_appUpdates.State)) return allowClose;
            if(_appUpdates.State is {PreparedPackage:{ } package,Release:{ } release} && installer is IStagedAppUpdateInstaller staged)
            {
                if(!staged.HasPreparedHandoff && !await staged.PrepareAsync(release,package,_manualUpdateRequested,_updateHwnd.ToInt64(),CancellationToken.None))
                {_appUpdates.ApplyAuthorityCooldown(staged.AuthorityCooldownUntil);return allowClose;}
            }
            using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _playerService.CloseAsync().WaitAsync(drain.Token);
            await _playerService.AllPendingProgressSaves.WaitAsync(drain.Token);
            // A manual update restarts Silo; the new client reopens where this window is now.
            // Saved before the handoff so the file exists before this process can exit.
            if(_manualUpdateRequested){SaveRelaunchPlacement();placementSaved=true;}
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
                // Silo stays open, so no restart will use the saved placement.
                if(placementSaved)DiscardRelaunchPlacement();
                _manualUpdateRequested=false;
                App.Services.GetRequiredService<ToastService>().Error(RequiresManualFullInstaller(_appUpdates.State)
                    ? UpdateInstallation.ManualFullInstallerMessage
                    : "The update was deferred because playback could not be saved or the update could not be verified. Silo is still open; try again when ready.");
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
            // Consume once before interpreting identity or presenting feedback.
            File.Delete(path);
            var state = json.RootElement.GetProperty("state").GetString();
            var message = json.RootElement.GetProperty("message").GetString() ?? "Open update details to try again.";
            if (state == "installed")
            {
                ReleaseVersion? installed = null;
                try { installed = ReleaseVersion.FromAssembly(typeof(App).Assembly); }
                catch (InvalidDataException ex) { LocalLog.AppendException("updates.txt", ex, "update_result_identity"); }
                if (installed is not null && json.RootElement.TryGetProperty("version", out var resultVersion)
                    && resultVersion.ValueKind == JsonValueKind.String
                    && Version.TryParse(resultVersion.GetString(), out var numeric) && numeric == installed.GetWindowsVersion())
                    App.Services.GetRequiredService<ToastService>().Success("Silo was updated successfully to " + installed.Text + (installed.IsPrerelease ? " (Beta)" : "") + ".");
                else App.Services.GetRequiredService<ToastService>().Warning("Previous update: " + message);
            }
            else App.Services.GetRequiredService<ToastService>().Warning("Previous update: " + message);
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
