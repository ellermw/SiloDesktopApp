using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using Windows.Storage.Pickers;
using U = SiloPlayer.Controls.AdminSettingsUi;
using A = SiloPlayer.Controls.AdminMonitorUi;
using T = SiloPlayer.Core.Services.Admin.AdminAppearanceText;
using OverlayText = SiloPlayer.Core.Services.Admin.AdminAppearanceOverlayText;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// A file the admin picked: its declared type and length are known before any byte is
/// read, and <see cref="Open"/> streams it only when an upload is sent. The page keeps
/// no path and logs nothing about the file.
/// </summary>
public sealed record AdminAppearancePickedImage(string Name, string MediaType, long Length, Func<CancellationToken, Task<Stream>> Open);

/// <summary>BrandingAssetField (official 66b836f82) and the badge-image upload and delete flows.</summary>
public sealed partial class AdminAppearanceSettingsPage
{
    private sealed record AssetControls(string Kind, Image Preview, FontIcon Placeholder, TextBlock Caption, Button Upload, Button Remove, string? DefaultAsset, long MaximumBytes);
    private readonly Dictionary<string, AssetControls> _assets = new(StringComparer.Ordinal);
    private AdminAppearancePickedImage? _pendingBadge;
    private bool _picking;

    /// <summary>
    /// Test seam for the OS file picker: (purpose, cancellation) → the picked file, or null
    /// when the admin cancels. Purpose is a branding slot kind or "overlay".
    /// </summary>
    internal static Func<string, CancellationToken, Task<AdminAppearancePickedImage?>>? ImagePickerOverride;

    private void AddAsset(string kind, string labelKey, string guidanceKey, int pixels, int megabytes, string? defaultAsset)
    {
        var label = T.T("web.admin.settings.appearance.logos." + labelKey + ".label");
        var wide = kind is "wordmark" or "login_bg";
        var preview = new Image { Width = wide ? 112 : 56, Height = 56, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(preview, T.T("web.admin.common.brandingAssetField.previewAlt", ("label", label)));
        AutomationProperties.SetAutomationId(preview, "AppearancePreview" + kind);
        var placeholder = new FontIcon { Glyph = "", FontSize = 20, Opacity = .4, Visibility = Visibility.Collapsed };
        var frame = new Grid { Width = wide ? 112 : 56, Height = 56 };
        frame.Children.Add(preview); frame.Children.Add(placeholder);
        var caption = U.Text("", 12, true);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetAutomationId(caption, "AppearanceCaption" + kind);
        var previewHost = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        previewHost.Children.Add(new Border { Child = frame, BorderBrush = U.Brush("BorderBrush"), BorderThickness = new(1), CornerRadius = new(8), Background = A.Solid(A.Muted, .12) });
        previewHost.Children.Add(caption);
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(U.Text(label));
        labels.Children.Add(U.Text(T.T("web.admin.settings.appearance.logos." + labelKey + ".description"), 12, true));
        var size = T.T("web.common.format.size.mb", ("size", megabytes));
        labels.Children.Add(U.Text(T.T("web.admin.common.brandingAssets." + guidanceKey, ("px", pixels), ("size", size)), 12, true));
        var upload = U.Action(T.T("web.admin.common.brandingAssetField.upload"), "AppearanceUpload" + kind);
        var remove = new Button { Content = A.Icon("trash-2", 14, A.Muted), Padding = new(6), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) };
        AutomationProperties.SetName(remove, T.T("web.admin.common.brandingAssetField.remove", ("label", label)));
        AutomationProperties.SetAutomationId(remove, "AppearanceRemove" + kind);
        upload.Click += async (_, _) => await UploadAssetAsync(kind);
        remove.Click += async (_, _) =>
        {
            if (_controller is { } controller) await controller.DeleteAssetAsync(kind);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(upload);
        actions.Children.Add(remove);
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(previewHost);
        Grid.SetColumn(labels, 1); row.Children.Add(labels);
        Grid.SetColumn(actions, 2); row.Children.Add(actions);
        _logos.Add(new Border { Child = row, Padding = new(12), CornerRadius = new(12), BorderBrush = U.Brush("BorderBrush"), BorderThickness = new(1), Margin = new(0, 4, 0, 4) });
        _assets[kind] = new(kind, preview, placeholder, caption, upload, remove, defaultAsset, megabytes * 1024L * 1024L);
    }

    private void RenderAssets(AdminAppearanceSettingsState state)
    {
        var storage = state.Branding?.StorageAvailable == true;
        _storageWarning.Visibility = state.Branding != null && !storage ? Visibility.Visible : Visibility.Collapsed;
        if (_storageWarning.Child is Grid warning && warning.Children.OfType<TextBlock>().FirstOrDefault() is { } warningText)
            U.UpdateLiveText(warningText, T.T(state.S3Configured ? "web.admin.settings.appearance.logos.restartForUploads" : "web.admin.settings.appearance.logos.storageNeeded"));
        _logos.RefreshRules();
        var current = state.Owner?.IsCurrent == true;
        foreach (var controls in _assets.Values)
        {
            var url = current ? state.Branding?.AssetUrl(controls.Kind) : null;
            // An empty slot shows the bundled default, dimmed and captioned (official BrandingAssetField).
            ShowPreview(controls, url);
            controls.Preview.Opacity = url == null ? .4 : 1;
            controls.Caption.Text = url == null ? T.T(controls.Kind == "login_bg"
                ? "web.admin.common.brandingAssets.themeGradientCaption" : "web.admin.common.brandingAssets.defaultCaption") : "";
            controls.Caption.Visibility = url == null ? Visibility.Visible : Visibility.Collapsed;
            controls.Upload.Content = T.T(url == null ? "web.admin.common.brandingAssetField.upload" : "web.admin.common.brandingAssetField.replace");
            controls.Upload.IsEnabled = storage && state.CanWriteImages && !_picking;
            controls.Remove.Visibility = url == null ? Visibility.Collapsed : Visibility.Visible;
            controls.Remove.IsEnabled = state.CanWriteImages && state.Branding != null;
        }
    }

    private void ShowPreview(AssetControls controls, string? url)
    {
        string? source = null;
        var svg = false;
        if (url != null)
        {
            var resolved = App.Services.GetRequiredService<SiloApiClient>().ResolveServerUrl(url);
            if (Uri.TryCreate(resolved, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                // An SVG favicon is accepted and stored as uploaded, but never rendered here:
                // a vector document may reference other content. A neutral image glyph stands in.
                svg = AdminAppearanceAssetPolicy.IsSvgUrl(uri);
                source = svg ? null : uri.AbsoluteUri;
            }
        }
        else if (controls.DefaultAsset != null) source = "ms-appx:///Assets/" + controls.DefaultAsset;
        controls.Placeholder.Visibility = svg ? Visibility.Visible : Visibility.Collapsed;
        if (source == null) { controls.Preview.Source = null; controls.Preview.Tag = null; return; }
        if (controls.Preview.Tag as string == source) return;
        // Decoded at the preview's size, never at the upload's full resolution.
        controls.Preview.Source = new BitmapImage(new Uri(source)) { DecodePixelWidth = (int)controls.Preview.Width * 2, DecodePixelType = DecodePixelType.Logical };
        controls.Preview.Tag = source;
    }

    private async Task UploadAssetAsync(string kind)
    {
        var controller = _controller;
        if (controller == null || _picking || !_assets.TryGetValue(kind, out var controls) || !controller.State.CanWriteImages) return;
        try
        {
            using var action = controller.CaptureAction();
            controller.ShowAssetError(null);
            var picked = await PickAsync(kind, action.Token);
            if (picked == null || !_active || !action.IsCurrent || !ReferenceEquals(controller, _controller)) return;
            // Type and size are checked before a byte is read, with the server's own refusals.
            if (!AdminAppearanceAssetPolicy.IsAllowed(kind, picked.MediaType, Math.Max(1, Math.Min(picked.Length, controls.MaximumBytes))))
            {
                controller.ShowAssetError("Unsupported image type; use PNG, JPEG, WebP (or PNG/ICO/SVG for favicon)");
                return;
            }
            if (picked.Length > controls.MaximumBytes || picked.Length <= 0)
            {
                controller.ShowAssetError("The image exceeds the maximum size for this asset.");
                return;
            }
            var bytes = await ReadBoundedAsync(picked, controls.MaximumBytes, action.Token);
            if (bytes == null) { controller.ShowAssetError("The image exceeds the maximum size for this asset."); return; }
            if (!_active || !action.IsCurrent || !ReferenceEquals(controller, _controller)) return;
            await controller.UploadAssetAsync(kind, bytes, picked.MediaType);
        }
        catch (OperationCanceledException) { }
        catch { if (_active && ReferenceEquals(controller, _controller)) controller.ShowAssetError(T.T("web.admin.settings.hooks.branding.uploadFailed")); }
        finally { QueueRender(); }
    }

    private async Task PickBadgeImageAsync()
    {
        var controller = _controller;
        if (controller == null || _picking || !controller.State.CanWriteImages || controller.State.OverlayConfig?.ImageUploadsAvailable != true) return;
        try
        {
            using var action = controller.CaptureAction();
            _overlayEditor.ShowLibraryError(null);
            var picked = await PickAsync("overlay", action.Token);
            if (picked == null || !_active || !action.IsCurrent || !ReferenceEquals(controller, _controller)) return;
            if (picked.Length > AdminAppearanceApi.MaxOverlayUploadBytes)
            {
                _overlayEditor.ShowLibraryError(OverlayText.T("web.settings.overlays.library.tooLarge"));
                return;
            }
            if (picked.MediaType is not ("image/png" or "image/jpeg" or "image/webp") || picked.Length <= 0)
            {
                _overlayEditor.ShowLibraryError(OverlayText.T("web.admin.settings.hooks.overlayImages.uploadFailed"));
                return;
            }
            // Only the handle is kept until Upload; the bytes are read when it is sent.
            _pendingBadge = picked;
            // "4K Dolby Vision.png" → "4K Dolby Vision".
            var dot = picked.Name.LastIndexOf('.');
            _overlayEditor.ShowPendingUpload(dot > 0 ? picked.Name[..dot] : dot == 0 ? "" : picked.Name);
        }
        catch (OperationCanceledException) { }
        catch { if (_active) _overlayEditor.ShowLibraryError(OverlayText.T("web.admin.settings.hooks.overlayImages.uploadFailed")); }
    }

    private async Task UploadBadgeImageAsync(string name)
    {
        var controller = _controller;
        var picked = _pendingBadge;
        if (controller == null || picked == null || !controller.State.CanWriteImages) return;
        try
        {
            using var action = controller.CaptureAction();
            _overlayEditor.ShowLibraryError(null);
            var bytes = await ReadBoundedAsync(picked, AdminAppearanceApi.MaxOverlayUploadBytes, action.Token);
            if (bytes == null) { _overlayEditor.ShowLibraryError(OverlayText.T("web.settings.overlays.library.tooLarge")); return; }
            if (!_active || !action.IsCurrent || !ReferenceEquals(controller, _controller)) return;
            var acknowledged = await controller.UploadOverlayImageAsync(bytes, picked.MediaType, name);
            // The picked file and name stay after a refusal so it can be retried as it was
            // (official OverlayImageLibrary); an acknowledged or unknown upload releases them.
            if (acknowledged || controller.State.AssetsUnknown) DropPendingBadge();
        }
        catch (OperationCanceledException) { }
        catch { if (_active) _overlayEditor.ShowLibraryError(OverlayText.T("web.admin.settings.hooks.overlayImages.uploadFailed")); }
        finally { QueueRender(); }
    }

    private void DropPendingBadge()
    {
        _pendingBadge = null;
        _overlayEditor.ClearPendingUpload();
    }

    private async Task DeleteBadgeImageAsync(string reference)
    {
        var controller = _controller;
        if (controller == null || _dialog != null || XamlRoot == null) return;
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
            AutomationProperties.SetAutomationId(dialog, "AppearanceDeleteImageDialog");
            _dialog = dialog;
            var confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;
            if (ReferenceEquals(_dialog, dialog)) _dialog = null;
            // Badges that name it fall back to text for every profile, as the server does;
            // stored references stay and show as "Removed image" so they can be cleared.
            if (confirmed && _active && action.IsCurrent && ReferenceEquals(controller, _controller))
                await controller.DeleteOverlayImageAsync(reference);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; QueueRender(); }
    }

    private async Task<AdminAppearancePickedImage?> PickAsync(string purpose, CancellationToken cancellation)
    {
        _picking = true;
        try
        {
            if (ImagePickerOverride is { } pickerOverride) return await pickerOverride(purpose, cancellation);
            if (App.MainWindowInstance is not { } window) return null;
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
            foreach (var extension in purpose == "favicon" ? new[] { ".png", ".ico", ".svg", ".webp" } : new[] { ".png", ".jpg", ".jpeg", ".webp" })
                picker.FileTypeFilter.Add(extension);
            var file = await picker.PickSingleFileAsync();
            cancellation.ThrowIfCancellationRequested();
            if (file == null) return null;
            var properties = await file.GetBasicPropertiesAsync();
            var mediaType = file.FileType.ToLowerInvariant() switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".ico" => "image/x-icon", ".svg" => "image/svg+xml", _ => "",
            };
            return new(file.Name, mediaType, (long)properties.Size, async token => await file.OpenStreamForReadAsync().WaitAsync(token));
        }
        finally { _picking = false; }
    }

    /// <summary>Reads at most <paramref name="maximum"/> bytes; null when the file is (or grew) larger.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(AdminAppearancePickedImage picked, long maximum, CancellationToken cancellation)
    {
        if (picked.Length <= 0 || picked.Length > maximum) return null;
        await using var source = await picked.Open(cancellation);
        using var target = new MemoryStream((int)Math.Min(picked.Length, maximum));
        var buffer = new byte[81920];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellation);
            if (count == 0) break;
            if (target.Length + count > maximum) return null;
            await target.WriteAsync(buffer.AsMemory(0, count), cancellation);
        }
        return target.Length == 0 ? null : target.ToArray();
    }
}
