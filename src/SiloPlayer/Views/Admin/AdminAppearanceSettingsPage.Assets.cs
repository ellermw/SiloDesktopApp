using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using Windows.Storage.Pickers;
using U = SiloPlayer.Controls.AdminSettingsUi;
using A = SiloPlayer.Controls.AdminMonitorUi;
using T = SiloPlayer.Core.Services.Admin.AdminAppearanceText;
using OverlayText = SiloPlayer.Core.Services.Admin.AdminAppearanceOverlayText;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminAppearanceSettingsPage
{
    private sealed record AssetControls(string Kind, Image Preview, TextBlock Caption, TextBlock Guidance,
        Button Upload, Button Remove, string? DefaultAsset, long MaximumBytes);
    private readonly Dictionary<string, AssetControls> _assets = new(StringComparer.Ordinal);
    internal static Func<string, CancellationToken, Task<(byte[] Bytes, string MediaType, string Name)?>>? ImagePickerOverride;

    private void AddAsset(string kind, string labelKey, string guidanceKey, int pixels, int megabytes, string? defaultAsset)
    {
        var label = T.T("web.admin.settings.appearance.logos." + labelKey + ".label");
        var preview = new Image { Width = kind is "wordmark" or "login_bg" ? 112 : 56, Height = 56, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(preview, T.T("web.admin.common.brandingAssetField.previewAlt", ("label", label)));
        var caption = U.Text("", 12, true);
        var previewHost = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
        previewHost.Children.Add(new Border { Child = preview, BorderBrush = U.Brush("BorderBrush"), BorderThickness = new(1), CornerRadius = new(8) });
        previewHost.Children.Add(caption);
        var labels = new StackPanel { Spacing = 4 };
        labels.Children.Add(U.Text(label));
        labels.Children.Add(U.Text(T.T("web.admin.settings.appearance.logos." + labelKey + ".description"), 12, true));
        var guidance = U.Text(T.T("web.admin.common.brandingAssets." + guidanceKey, ("px", pixels), ("size", megabytes + " MB")), 12, true);
        labels.Children.Add(guidance);
        var upload = U.Action(T.T("web.admin.common.brandingAssetField.upload"), "AppearanceUpload" + kind);
        var remove = U.Action("×", "AppearanceRemove" + kind);
        AutomationProperties.SetName(remove, T.T("web.admin.common.brandingAssetField.remove", ("label", label)));
        upload.Click += async (_, _) => await UploadAssetAsync(kind);
        remove.Click += async (_, _) =>
        {
            if (_controller is { } controller) await controller.DeleteAssetAsync(kind);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(upload);
        actions.Children.Add(remove);
        var row = new LogsWrapPanel { Spacing = 16 };
        row.Children.Add(previewHost);
        labels.MaxWidth = 560;
        row.Children.Add(labels);
        row.Children.Add(actions);
        _logos.Add(new Border { Child = row, Padding = new(12), CornerRadius = new(12), BorderBrush = U.Brush("BorderBrush"), BorderThickness = new(1), Margin = new(0, 4, 0, 4) });
        _assets[kind] = new(kind, preview, caption, guidance, upload, remove, defaultAsset, megabytes * 1024L * 1024L);
    }

    private void RenderAssets(AdminAppearanceSettingsState state)
    {
        var storage = state.Branding?.StorageAvailable == true;
        _storageWarning.Visibility = storage ? Visibility.Collapsed : Visibility.Visible;
        var storageMessage = T.T(state.S3Configured ? "web.admin.settings.appearance.logos.restartForUploads" : "web.admin.settings.appearance.logos.storageNeeded");
        if (_storageWarning.Child is Grid warning && warning.Children.OfType<TextBlock>().FirstOrDefault() is { } warningText)
            U.UpdateLiveText(warningText, storageMessage);
        foreach (var controls in _assets.Values)
        {
            var current = state.Owner?.IsCurrent == true ? state.Branding?.AssetUrl(controls.Kind) : null;
            var source = current;
            if (source == null && controls.DefaultAsset != null) source = "ms-appx:///Assets/" + controls.DefaultAsset;
            controls.Preview.Opacity = current == null ? .4 : 1;
            if (source != null)
            {
                var resolved = source.StartsWith("ms-appx:", StringComparison.Ordinal) ? source
                    : App.Services.GetRequiredService<SiloApiClient>().ResolveServerUrl(source);
                if (Uri.TryCreate(resolved, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "ms-appx")
                {
                    var existing = controls.Preview.Tag as string;
                    if (existing != uri.AbsoluteUri)
                    {
                        controls.Preview.Source = AdminAppearanceAssetPolicy.IsSvgUrl(uri)
                            ? new SvgImageSource(uri) : new BitmapImage(uri);
                        controls.Preview.Tag = uri.AbsoluteUri;
                    }
                }
                else { controls.Preview.Source = null; controls.Preview.Tag = null; }
            }
            else { controls.Preview.Source = null; controls.Preview.Tag = null; }
            controls.Caption.Text = current == null ? T.T(controls.Kind == "login_bg"
                ? "web.admin.common.brandingAssets.themeGradientCaption" : "web.admin.common.brandingAssets.defaultCaption") : "";
            controls.Caption.Visibility = current == null ? Visibility.Visible : Visibility.Collapsed;
            controls.Upload.Content = T.T(current == null ? "web.admin.common.brandingAssetField.upload" : "web.admin.common.brandingAssetField.replace");
            controls.Upload.IsEnabled = storage && state.Owner?.IsCurrent == true && !state.Loading && !state.Saving && state.AssetBusy == null && !state.OutcomeUnknown;
            controls.Remove.Visibility = current == null ? Visibility.Collapsed : Visibility.Visible;
            controls.Remove.IsEnabled = state.Owner?.IsCurrent == true && !state.Loading && !state.Saving && state.AssetBusy == null && !state.OutcomeUnknown;
        }
    }

    private async Task UploadAssetAsync(string kind)
    {
        var controller = _controller;
        if (controller == null || !_assets.TryGetValue(kind, out var controls)) return;
        try
        {
            using var action = controller.CaptureAction();
            var file = await PickImageAsync(kind, controls.MaximumBytes, action.Token);
            if (file == null || !_active || !action.IsCurrent || !ReferenceEquals(controller, _controller)) return;
            if (!AdminAppearanceAssetPolicy.IsAllowed(kind, file.Value.MediaType, file.Value.Bytes.LongLength))
                throw new InvalidDataException("Unsupported image.");
            await controller.UploadAssetAsync(kind, file.Value.Bytes, file.Value.MediaType);
        }
        catch (OperationCanceledException) { }
        catch { ShowPickerError(); }
    }

    private async Task UploadBadgeImageAsync()
    {
        var controller = _controller;
        if (controller == null || _dialog != null || !_overlays.AdvancedAvailable || controller.State.OverlayConfig?.ImageUploadsAvailable != true) return;
        ContentDialog? dialog = null;
        try
        {
            using var action = controller.CaptureAction();
            var file = await PickImageAsync("overlay", AdminAppearanceApi.MaxOverlayUploadBytes, action.Token);
            if (file == null || !_active || !action.IsCurrent || !ReferenceEquals(controller, _controller)) return;
            var name = U.Input(OverlayText.T("web.settings.overlays.library.name"), "AppearanceBadgeImageName");
            name.Text = Path.GetFileNameWithoutExtension(file.Value.Name);
            var status = U.Text("", 13);
            var contents = new StackPanel { Spacing = 12 };
            contents.Children.Add(name);
            contents.Children.Add(status);
            dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = OverlayText.T("web.settings.overlays.library.upload"), Content = contents,
                PrimaryButtonText = OverlayText.T("web.settings.overlays.library.upload"), CloseButtonText = AdminText.T("common.cancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            var uploading = false;
            void UpdateUpload() => dialog.IsPrimaryButtonEnabled = !uploading && action.IsCurrent &&
                controller.State.OverlayConfig?.ImageUploadsAvailable == true && !controller.State.OutcomeUnknown &&
                name.Text.Trim().EnumerateRunes().Count() is > 0 and <= AdminAppearanceApi.MaxOverlayImageNameLength;
            name.TextChanged += (_, _) => UpdateUpload();
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                args.Cancel = true;
                if (uploading || !_active || !action.IsCurrent || !ReferenceEquals(controller, _controller)) return;
                uploading = true;
                UpdateUpload();
                try
                {
                    await controller.ChangeOverlayImagesAsync(lease => _api.UploadOverlayImageAsync(lease,
                        file.Value.Bytes, file.Value.MediaType, name.Text.Trim()), forUpload: true);
                    if (!_active || !action.IsCurrent || !ReferenceEquals(controller, _controller)) return;
                    if (controller.State.Error == null) { uploading = false; dialog.Hide(); }
                    else U.UpdateLiveText(status, controller.State.Error);
                }
                catch (OperationCanceledException) { }
                catch { if (_active && action.IsCurrent) U.UpdateLiveText(status, "Could not upload this image. Review the name, format and size."); }
                finally { uploading = false; if (_active && action.IsCurrent) UpdateUpload(); }
            };
            dialog.Closing += (_, args) => { if (_active && uploading && action.IsCurrent) args.Cancel = true; };
            _dialog = dialog;
            UpdateUpload();
            await dialog.ShowAsync();
            name.Text = "";
        }
        catch (OperationCanceledException) { }
        catch { ShowPickerError(); }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    private async Task DeleteBadgeImageAsync(string reference)
    {
        var controller = _controller;
        if (controller == null || _dialog != null) return;
        ContentDialog? dialog = null;
        try
        {
            using var action = controller.CaptureAction();
            dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = OverlayText.T("web.settings.overlays.library.deleteTitle"),
                Content = U.Text(OverlayText.T("web.settings.overlays.library.deleteDescription")),
                PrimaryButtonText = OverlayText.T("web.settings.overlays.library.delete"), CloseButtonText = AdminText.T("common.cancel"), DefaultButton = ContentDialogButton.Close,
            };
            _dialog = dialog;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && _active && action.IsCurrent && ReferenceEquals(controller, _controller))
                await controller.ChangeOverlayImagesAsync(lease => _api.DeleteOverlayImageAsync(lease, reference));
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    private async Task<(byte[] Bytes, string MediaType, string Name)?> PickImageAsync(string kind, long maximumBytes, CancellationToken cancellation)
    {
        if (ImagePickerOverride is { } pickerOverride) return await pickerOverride(kind, cancellation);
        if (App.MainWindowInstance is not { } window) return null;
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        foreach (var extension in kind == "favicon" ? new[] { ".png", ".webp", ".ico", ".svg" } : new[] { ".png", ".jpg", ".jpeg", ".webp" })
            picker.FileTypeFilter.Add(extension);
        var file = await picker.PickSingleFileAsync();
        cancellation.ThrowIfCancellationRequested();
        if (file == null) return null;
        using var source = await file.OpenStreamForReadAsync();
        using var target = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellation);
            if (count == 0) break;
            if (target.Length + count > maximumBytes) throw new InvalidDataException("The image is too large.");
            await target.WriteAsync(buffer.AsMemory(0, count), cancellation);
        }
        cancellation.ThrowIfCancellationRequested();
        var mediaType = file.FileType.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".ico" => "image/x-icon", ".svg" => "image/svg+xml", _ => "image/png",
        };
        return (target.ToArray(), mediaType, file.Name);
    }

    private void ShowPickerError()
    {
        if (!_active) return;
        U.UpdateLiveText(_error, "Could not use that image. Check the supported format and size, then try again.");
        _alert.Visibility = Visibility.Visible;
    }
}
