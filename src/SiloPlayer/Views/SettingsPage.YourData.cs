using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using SiloPlayer.Core.Localization;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private int _yourDataGeneration;
    private ProfileExportCapability? _yourDataCapability;
    private int _yourDataBusyOwner;
    private ProfileImportResult? _yourDataImportResult;
    private ContentDialog? _yourDataPinDialog;
    private Action? _yourDataContextHandler;

    public interface IProfileTransferFileSource
    {
        long FileSize { get; }
        Task<byte[]> ReadBoundedAsync(long maxBytes, CancellationToken ct);
    }

    public interface IProfileTransferFilePicker
    {
        Task<StorageFile?> PickSaveFileAsync(nint windowHandle, string suggestedFileName);
        Task<IProfileTransferFileSource?> PickOpenFileAsync(nint windowHandle);
    }

    private sealed class NativeFileSource(StorageFile file, long size) : IProfileTransferFileSource
    {
        public long FileSize => size;

        public async Task<byte[]> ReadBoundedAsync(long maxBytes, CancellationToken ct)
        {
            using var stream = await file.OpenStreamForReadAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var buffer = new MemoryStream();
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (maxBytes > 0 && total > maxBytes)
                    throw new ApiException("validation_failed",
                        $"The file is too large. The limit is {FormatFileSize(maxBytes)}.", 422);
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
    }

    private sealed class NativeProfileTransferFilePicker : IProfileTransferFilePicker
    {
        public async Task<StorageFile?> PickSaveFileAsync(nint windowHandle, string suggestedFileName)
        {
            var picker = new FileSavePicker { SuggestedFileName = suggestedFileName };
            picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            return await picker.PickSaveFileAsync();
        }

        public async Task<IProfileTransferFileSource?> PickOpenFileAsync(nint windowHandle)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            var file = await picker.PickSingleFileAsync();
            if (file == null) return null;
            var props = await file.GetBasicPropertiesAsync();
            return new NativeFileSource(file, (long)props.Size);
        }
    }

    internal static IProfileTransferFilePicker ProfileTransferPicker { get; set; } = new NativeProfileTransferFilePicker();

    private void DeactivateYourData()
    {
        ++_yourDataGeneration;
        _yourDataCapability = null;
        _yourDataImportResult = null;
        if (_yourDataPinDialog != null)
        {
            _yourDataPinDialog.Hide();
            _yourDataPinDialog = null;
        }
        if (_yourDataContextHandler != null)
        {
            try { App.Services.GetRequiredService<SiloApiClient>().ContextInvalidated -= _yourDataContextHandler; } catch { }
            _yourDataContextHandler = null;
        }
    }

    private const string YourDataSessionChanged = "Your session changed. Try again.";
    private const string YourDataImportFinished = "Your import finished. Its summary is not available because this section reloaded.";

    // The request context retired under a section that is still showing; reload it instead of leaving dead controls.
    private void ReloadStaleYourData(int generation, string? notice)
    {
        if (generation == _yourDataGeneration) _ = LoadYourDataAsync(notice);
    }

    private async Task LoadYourDataAsync(string? notice = null, ProfileImportResult? summary = null)
    {
        DeactivateYourData();
        var generation = ++_yourDataGeneration;
        _yourDataImportResult = summary;
        _yourDataBusyOwner = 0;
        var api = new ProfileTransferApi(App.Services.GetRequiredService<SiloApiClient>());
        var context = api.CaptureContext();
        var identity = api.Client.CaptureIdentityContext();
        bool Current() => generation == _yourDataGeneration && api.IsCurrentContext(context);
        YourDataContentHost.Children.Clear();
        YourDataLoadingRing.IsActive = true;
        Action? handler = null;
        handler = () =>
        {
            var dq = DispatcherQueue;
            if (dq == null) return;
            void Reload()
            {
                // A queued callback can outlive the subscription it was raised for.
                if (!ReferenceEquals(_yourDataContextHandler, handler)) return;
                // An access refresh keeps the same profile, so its finished import summary stays on screen.
                var finished = api.Client.IsCurrentIdentityContext(identity) ? _yourDataImportResult : null;
                _ = LoadYourDataAsync(_yourDataBusyOwner != 0 ? YourDataSessionChanged : null, finished);
            }
            if (dq.HasThreadAccess) Reload();
            else dq.TryEnqueue(Reload);
        };
        _yourDataContextHandler = handler;
        api.Client.ContextInvalidated += handler;
        try
        {
            var capability = await api.GetCapabilityAsync(context);
            if (!Current()) { ReloadStaleYourData(generation, notice); return; }
            _yourDataCapability = capability;
            YourDataLoadingRing.IsActive = false;
            if (notice != null) YourDataContentHost.Children.Add(SecondaryText(notice));

            if (capability.State != "available")
            {
                YourDataContentHost.Children.Add(SecondaryText("Profile export and import are not available on this server."));
                return;
            }

            BuildYourDataUI(api, context, identity, generation);
        }
        catch (OperationCanceledException) when (!Current()) { ReloadStaleYourData(generation, notice); }
        catch (Exception ex)
        {
            if (!Current()) { ReloadStaleYourData(generation, notice); return; }
            YourDataLoadingRing.IsActive = false;
            YourDataContentHost.Children.Add(ErrorText($"Could not load: {ex.DisplayMessage()}"));
        }
    }

    private void BuildYourDataUI(ProfileTransferApi api, ApiRequestContext context, ApiIdentityContext identity, int generation)
    {
        bool Current() => generation == _yourDataGeneration && api.IsCurrentContext(context);

        var exportGroup = CreateSettingsGroup("Export your data",
            "Download this profile's watch history, ratings, favorites, collections, dismissals, and preferences as a file.");
        var exportStack = (StackPanel)exportGroup.Child;
        var exportButton = new Button
        {
            Content = "Export profile data",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinHeight = 42,
        };
        var exportStatus = SecondaryText("");
        exportStatus.Visibility = Visibility.Collapsed;
        exportStack.Children.Add(exportButton);
        exportStack.Children.Add(exportStatus);

        exportButton.Click += async (_, _) =>
        {
            if (_yourDataBusyOwner != 0) return;
            if (!Current()) { ReloadStaleYourData(generation, YourDataSessionChanged); return; }
            _yourDataBusyOwner = generation;
            exportButton.IsEnabled = false;
            exportStatus.Visibility = Visibility.Collapsed;
            var saved = false;
            try
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        var data = await api.ExportAsync(context);
                        if (!Current()) return;
                        var target = await ProfileTransferPicker.PickSaveFileAsync(
                            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!),
                            "silo-profile.json");
                        // The user has chosen where this profile's export goes, so only another account or profile drops it.
                        if (target == null || !api.Client.IsCurrentIdentityContext(identity)) return;
                        await FileIO.WriteBytesAsync(target, data);
                        saved = true;
                        App.Services.GetRequiredService<ToastService>().Success("Profile data exported");
                        return;
                    }
                    catch (ApiException ex) when (
                        ex.StatusCode == 403 &&
                        string.Equals(ex.ErrorCode, "profile_verification_required", StringComparison.Ordinal) &&
                        attempt == 0)
                    {
                        if (!Current()) return;
                        var (proof, failure) = await TryVerifyPinForYourDataAsync(api, context, generation, ex.DisplayMessage());
                        if (!Current()) return;
                        if (proof == null)
                        {
                            if (failure != null)
                            {
                                exportStatus.Text = failure;
                                exportStatus.Visibility = Visibility.Visible;
                            }
                            return;
                        }
                        context = api.Client.AdoptProfileProof(context, proof);
                        generation = ++_yourDataGeneration;
                        _yourDataBusyOwner = generation;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!Current()) return;
                exportStatus.Text = ex.DisplayMessage();
                exportStatus.Visibility = Visibility.Visible;
            }
            finally
            {
                if (_yourDataBusyOwner == generation) _yourDataBusyOwner = 0;
                if (Current()) exportButton.IsEnabled = true;
                else if (!saved) ReloadStaleYourData(generation, YourDataSessionChanged);
                // The file was written, so a section reloaded underneath it must not keep asking for a retry.
                else if (_yourDataContextHandler != null && _yourDataBusyOwner == 0) _ = LoadYourDataAsync();
            }
        };

        var importGroup = CreateSettingsGroup("Import profile data",
            "Merge a previously exported profile file into this profile. Newer progress and history win; nothing is deleted.");
        var importStack = (StackPanel)importGroup.Child;
        var importButton = new Button
        {
            Content = "Choose file",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinHeight = 42,
        };
        var importHint = SecondaryText("");
        if (_yourDataCapability is { MaxImportBytes: > 0 })
            importHint.Text = $"Maximum file size: {FormatFileSize(_yourDataCapability.MaxImportBytes)}";
        else
            importHint.Visibility = Visibility.Collapsed;

        var importStatus = SecondaryText("");
        importStatus.Visibility = Visibility.Collapsed;
        var importResultHost = new StackPanel { Spacing = 8 };

        importStack.Children.Add(importButton);
        importStack.Children.Add(importHint);
        importStack.Children.Add(importStatus);
        importStack.Children.Add(importResultHost);
        if (_yourDataImportResult != null) RenderImportResult(importResultHost, _yourDataImportResult);

        importButton.Click += async (_, _) =>
        {
            if (_yourDataBusyOwner != 0) return;
            if (!Current()) { ReloadStaleYourData(generation, YourDataSessionChanged); return; }
            _yourDataBusyOwner = generation;
            importButton.IsEnabled = false;
            importStatus.Visibility = Visibility.Collapsed;
            importResultHost.Children.Clear();
            _yourDataImportResult = null;
            var detached = false;
            try
            {
                var maxBytes = _yourDataCapability?.MaxImportBytes ?? 0;
                var source = await ProfileTransferPicker.PickOpenFileAsync(
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
                if (source == null || !Current()) return;
                if (maxBytes > 0 && source.FileSize > maxBytes)
                {
                    importStatus.Text = $"The file is too large ({FormatFileSize(source.FileSize)}). The limit is {FormatFileSize(maxBytes)}.";
                    importStatus.Visibility = Visibility.Visible;
                    return;
                }
                var bytes = await source.ReadBoundedAsync(maxBytes, CancellationToken.None);
                if (!Current()) return;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        var result = await api.ImportAsync(context, bytes, maxBytes);
                        // The server has merged the file even if this section or its request context retired during the upload.
                        var refreshed = await RefreshImportedDataCachesAsync(api.Client, identity);
                        if (!Current())
                        {
                            detached = refreshed;
                            return;
                        }
                        _yourDataImportResult = result;
                        RenderImportResult(importResultHost, result);
                        return;
                    }
                    catch (ApiException ex) when (
                        ex.StatusCode == 403 &&
                        string.Equals(ex.ErrorCode, "profile_verification_required", StringComparison.Ordinal) &&
                        attempt == 0)
                    {
                        if (!Current()) return;
                        var (proof, failure) = await TryVerifyPinForYourDataAsync(api, context, generation, ex.DisplayMessage());
                        if (!Current()) return;
                        if (proof == null)
                        {
                            if (failure != null)
                            {
                                importStatus.Text = failure;
                                importStatus.Visibility = Visibility.Visible;
                            }
                            return;
                        }
                        context = api.Client.AdoptProfileProof(context, proof);
                        generation = ++_yourDataGeneration;
                        _yourDataBusyOwner = generation;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!Current()) return;
                importStatus.Text = ex.DisplayMessage();
                importStatus.Visibility = Visibility.Visible;
            }
            finally
            {
                if (_yourDataBusyOwner == generation) _yourDataBusyOwner = 0;
                if (Current()) importButton.IsEnabled = true;
                else if (!detached) ReloadStaleYourData(generation, YourDataSessionChanged);
                // The import landed with no section left to show its summary; a reload must not drop a successor's operation.
                else if (_yourDataContextHandler != null && _yourDataBusyOwner == 0) _ = LoadYourDataAsync(YourDataImportFinished);
                else App.Services.GetRequiredService<ToastService>().Success("Profile data imported");
            }
        };

        YourDataContentHost.Children.Add(exportGroup);
        YourDataContentHost.Children.Add(importGroup);
    }

    /// <summary>False once the importing account or profile is gone; its caches retired with it.</summary>
    private async Task<bool> RefreshImportedDataCachesAsync(SiloApiClient client, ApiIdentityContext owner)
    {
        // An access refresh or reconnect catch-up replaces the request context without changing whose data was imported.
        var current = client.CaptureContext();
        if (!client.IsCurrentIdentityContext(owner)) return false;
        var customization = App.Services.GetRequiredService<UICustomizationService>();
        customization.Invalidate();
        try { App.Services.GetRequiredService<SettingsApi>().LibraryPageState.Invalidate(); } catch { }
        try { App.Services.GetRequiredService<CatalogApi>().InvalidateLibraryCache(); } catch { }
        try { App.Services.GetRequiredService<CatalogApi>().InvalidateFilterCache(); } catch { }
        await customization.RefreshAsync(current);
        return true;
    }

    private static void RenderImportResult(StackPanel host, ProfileImportResult result)
    {
        host.Children.Clear();
        var header = new TextBlock
        {
            Text = "Import complete",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = PrimaryBrush(),
        };
        host.Children.Add(header);

        foreach (var data in result.Data)
        {
            var counts = new List<string>();
            if (data.Added > 0) counts.Add(L("web.profiles.dataTypes.counts.added", $"{data.Added} added", Args(("count", Count(data.Added)))));
            if (data.Updated > 0) counts.Add(L("web.profiles.dataTypes.counts.updated", $"{data.Updated} updated", Args(("count", Count(data.Updated)))));
            if (data.Unchanged > 0) counts.Add($"{data.Unchanged} unchanged");
            if (data.LeftOut > 0) counts.Add(L("web.profiles.dataTypes.counts.leftOut", $"{data.LeftOut} left out", Args(("count", Count(data.LeftOut)))));
            var label = DataTypeLabel(data.Type);
            var line = counts.Count > 0 ? $"{label}: {string.Join(", ", counts)}" : $"{label}: no changes";
            host.Children.Add(SecondaryText(line));
            foreach (var note in data.Notes)
                host.Children.Add(new TextBlock
                {
                    Text = note,
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(12, 0, 0, 0),
                });
        }

        if (result.Unmatched.Count > 0)
        {
            var titles = result.Unmatched.Count <= 10
                ? string.Join(", ", result.Unmatched)
                : string.Join(", ", result.Unmatched.Take(10)) + $" and {result.Unmatched.Count - 10} more";
            host.Children.Add(SecondaryText($"Titles not in this catalog or not available: {titles}"));
        }
    }

    private static string DataTypeLabel(string type) => type switch
    {
        "watch_history" => L("web.profiles.dataTypes.watchHistory", "Watch history"),
        "ratings" => L("web.profiles.dataTypes.ratings", "Ratings"),
        "favorites" => L("web.profiles.dataTypes.favorites", "Favorites & watchlist"),
        "collections" => L("web.profiles.dataTypes.collections", "Collections"),
        "dismissals" => L("web.profiles.dataTypes.dismissals", "Dismissals"),
        "preferences" => L("web.profiles.dataTypes.preferences", "Preferences"),
        "notifications" => L("web.profiles.dataTypes.notifications", "Notifications"),
        _ => type
    };

    /// <summary>Proof once verified; otherwise Failure says why verification could not start, and is null when the user cancelled or the owner retired.</summary>
    private async Task<(string? Proof, string? Failure)> TryVerifyPinForYourDataAsync(ProfileTransferApi api, ApiRequestContext context, int generation, string requirement)
    {
        const string unverifiable = "Could not verify your PIN. Try again.";
        bool Current() => generation == _yourDataGeneration && api.IsCurrentContext(context);
        if (!Current()) return default;
        var authApi = App.Services.GetRequiredService<AuthApi>();
        var authService = App.Services.GetRequiredService<AuthService>();
        var profileId = authService.SelectedProfileId;
        if (string.IsNullOrWhiteSpace(profileId) || profileId != context.ProfileId) return (null, unverifiable);
        Profile? profile = null;
        try
        {
            var response = await authApi.GetProfilesAsync();
            if (!Current()) return default;
            profile = response.Profiles.FirstOrDefault(p => p.Id == profileId);
        }
        catch { return (null, unverifiable); }
        if (profile == null) return (null, unverifiable);
        // The server demands verification for a profile with no PIN to enter; pass its reason on.
        if (!profile.HasPin) return (null, requirement);
        if (!Current()) return default;

        string? verifiedToken = null;
        var pin = new PasswordBox
        {
            PlaceholderText = L("web.profiles.pin.placeholder", "Enter 4-digit PIN"),
            MaxLength = 4,
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var error = new TextBlock { Foreground = (Brush)Application.Current.Resources["ErrorBrush"], TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            Title = L("web.profiles.pin.title", $"Enter PIN for {profile.Name}", Args(("name", profile.Name))),
            Content = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = "Your data requires your PIN.", TextWrapping = TextWrapping.Wrap }, pin, error } },
            PrimaryButtonText = "Verify",
            CloseButtonText = L("common.cancel", "Cancel"),
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Primary,
        };
        _yourDataPinDialog = dialog;
        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                if (!Current()) return;
                if (string.IsNullOrWhiteSpace(pin.Password)) { error.Text = "Enter your PIN."; return; }
                sender.IsPrimaryButtonEnabled = false;
                error.Text = "";
                var result = await authApi.VerifyPinAsync(context, profile.Id, pin.Password);
                if (!Current()) return;
                if (result.Valid && !string.IsNullOrWhiteSpace(result.ProfileToken))
                {
                    verifiedToken = result.ProfileToken;
                    args.Cancel = false;
                    return;
                }
                error.Text = L("web.profiles.pin.incorrect", "Incorrect PIN");
                pin.Password = "";
                pin.Focus(FocusState.Programmatic);
            }
            catch (Exception ex)
            {
                error.Text = ex is ApiException { StatusCode: 429 } ? "Too many attempts. Try again later." : "Could not verify PIN.";
                pin.Password = "";
                pin.Focus(FocusState.Programmatic);
            }
            finally
            {
                sender.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        var dialogGuard = DispatcherQueue.CreateTimer();
        dialogGuard.Interval = TimeSpan.FromMilliseconds(100);
        dialogGuard.Tick += (_, _) =>
        {
            if (!Current() && ReferenceEquals(_yourDataPinDialog, dialog))
            {
                dialogGuard.Stop();
                _yourDataPinDialog = null;
                dialog.Hide();
            }
        };
        dialogGuard.Start();
        var dialogResult = await dialog.ShowAsync();
        dialogGuard.Stop();
        if (ReferenceEquals(_yourDataPinDialog, dialog)) _yourDataPinDialog = null;
        if (dialogResult != ContentDialogResult.Primary || !Current()) return default;
        return (verifiedToken, null);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return L("web.common.format.size.bytes", $"{bytes} B", Args(("size", Count(bytes))));
        if (bytes < 1024 * 1024) return L("web.common.format.size.kb", $"{bytes / 1024.0:F0} KB", Args(("size", (bytes / 1024.0).ToString("N0", AppStrings.FormatCulture))));
        return L("web.common.format.size.mb", $"{bytes / (1024.0 * 1024.0):F0} MB", Args(("size", (bytes / (1024.0 * 1024.0)).ToString("N0", AppStrings.FormatCulture))));
    }
}
