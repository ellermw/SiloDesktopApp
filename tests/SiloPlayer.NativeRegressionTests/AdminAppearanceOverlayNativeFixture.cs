using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Views.Admin;
using F = AdminAppearanceFixture;

/// <summary>
/// The Appearance card-overlay editor and badge-image library through the real page and
/// shell (official AppearanceSettings.tsx at 66b836f82): style presets and look, every
/// offered category with its switch and position, the live movie/show/requested preview,
/// per-value images, the library upload and delete, the badges-off state and an older
/// manifest. Every edit is staged in the page batch; nothing writes a viewer profile.
/// </summary>
internal static class AdminAppearanceOverlayNativeFixture
{
    internal static async Task RunAsync(Window window)
    {
        await using var host = new AdminAppearanceFixtureHost(window);
        var server = host.Server;
        server.Images.Add((AdminAppearanceFixtureServer.ImageRef(0), "Synthetic logo"));
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000));
        var page = await host.OpenAsync();
        var controller = page.Controller!;
        await F.Until(() => F.Has(page, "AdminAppearanceOverlayEditor") && F.Find<FrameworkElement>(page, "AdminAppearanceOverlayEditor").IsLoaded, "overlay editor mounted");
        await F.Until(() => controller.State.SupportsStyles && F.Shown(page, "AppearanceOverlayLibrary"), "revision 29 shows the badge-image library");
        var editor = F.Find<FrameworkElement>(page, "AdminAppearanceOverlayEditor");
        await Task.Delay(150);
        F.Check(controller.State.DirtyKeys.Count == 0 && server.Writes.Count == 0, "binding the editor stages and sends nothing");
        string Document() => controller.State.Values["defaults.card_overlays"];
        JsonElement Root() => JsonDocument.Parse(Document()).RootElement;
        JsonElement Item(string id) => Root().GetProperty("items").GetProperty(id);

        // Rating sources: the capability shows rt_critic but not rt_audience.
        F.Check(F.Shown(editor, "AppearanceOverlayToggle-rating_rt") && !F.Shown(editor, "AppearanceOverlayToggle-rating_rt_audience"),
            "rating badges are offered only for the sources the server shows");
        F.Check(F.Shown(editor, "AppearanceOverlayToggle-availability") && F.Shown(editor, "AppearanceOverlayToggle-resolution_hdr_audio"), "every other registry overlay is offered");
        F.Check(new[] { "MEDIA INFO", "RATINGS & CERTIFICATIONS", "CONTENT METADATA", "STATUS & AWARDS" }.All(F.Texts(editor).Contains), "the four official categories render");

        // A switch, then a position from the five official choices.
        var year = F.Find<ToggleSwitch>(editor, "AppearanceOverlayToggle-year");
        var position = F.Find<ComboBox>(editor, "AppearanceOverlayPosition-year");
        F.Check(!position.IsEnabled, "a disabled badge's position is disabled");
        year.IsOn = true;
        await F.Until(() => controller.State.DirtyKeys.Contains("defaults.card_overlays") && Item("year").GetProperty("enabled").GetBoolean(), "switch stages the badge");
        await F.Until(() => position.IsEnabled, "position enabled with the badge");
        F.Check(position.Items.Cast<ComboBoxItem>().Select(item => item.Content?.ToString()).SequenceEqual(["Top Left", "Left Edge", "Right Edge", "Bottom Left", "Bottom Right"]),
            "the five official positions are offered");
        position.SelectedIndex = 1;
        await F.Until(() => Item("year").GetProperty("position").GetString() == "left-edge", "position stages");

        // Style presets and the custom look.
        await F.Click(editor, "AppearanceOverlayPreset-plate");
        await F.Until(() => Root().GetProperty("preset").GetString() == "plate", "plate preset stages");
        F.Find<Slider>(editor, "AppearanceOverlaySize").Value = 1.5;
        await F.Until(() => Root().TryGetProperty("style", out var style) && style.GetProperty("size").GetDouble() == 1.5, "size stages");
        await F.Click(editor, "AppearanceOverlayLook-shape-rounded");
        await F.Until(() => Root().GetProperty("style").GetProperty("shape").GetString() == "rounded", "shape stages");
        await F.Click(editor, "AppearanceOverlaySwatch-fill-slate");
        await F.Until(() => Root().GetProperty("style").GetProperty("background").GetString() == "#0F172ACC", "fill swatch stages");
        await F.Until(() => F.Shown(editor, "AppearanceOverlayResetLook"), "reset look offered once a style exists");
        await F.Click(editor, "AppearanceOverlayResetLook");
        await F.Until(() => !Root().TryGetProperty("style", out _), "reset removes the custom style");
        F.Check(server.Writes.Count == 0, "look and badge edits remain staged");

        // Live preview with the official samples.
        var preview = F.Find<FrameworkElement>(editor, "AppearanceOverlayPreview");
        await F.Until(() => F.Texts(preview).Contains("4K"), "movie sample shows its resolution badge");
        F.Find<ToggleSwitch>(editor, "AppearanceOverlayToggle-network").IsOn = true;
        await F.Click(editor, "AppearancePreviewVariant-show");
        await F.Until(() => F.Texts(preview).Contains("HBO"), "show sample shows a show-only badge");
        F.Check(!F.Texts(preview).Contains("4K"), "show sample uses its own 1080p data");
        await F.Click(editor, "AppearancePreviewVariant-requested");
        await F.Until(() => F.Texts(preview).Any(text => text.StartsWith("Downloading 43", StringComparison.Ordinal)), "requested sample shows the request status");
        await F.Click(editor, "AppearancePreviewVariant-movie");
        F.Check(F.Find<ToggleButton>(editor, "AppearancePreviewVariant-movie").IsChecked == true, "the sample choice is local view state");

        // Per-badge images: the icon image and a per-value image, with the flyout retained across a re-render.
        var reference = AdminAppearanceFixtureServer.ImageRef(0);
        var imageButton = F.Find<Button>(editor, "AppearanceOverlayImages-resolution");
        F.Check(imageButton.Visibility == Visibility.Visible, "badges offer images while the library has one");
        var flyout = (Flyout)imageButton.Flyout;
        flyout.ShowAt(imageButton);
        await F.Until(() => flyout.Content is ScrollViewer { IsLoaded: true }, "image flyout opened");
        var content = (FrameworkElement)flyout.Content;
        var icon = F.Find<ComboBox>(content, "AppearanceOverlayIcon-resolution");
        icon.SelectedItem = icon.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == reference);
        await F.Until(() => Item("resolution").TryGetProperty("image", out var image) && image.GetString() == reference, "icon image stages");
        var key = F.Find<AutoSuggestBox>(content, "AppearanceOverlayValueKey-resolution");
        key.Text = "2160p";
        var valueImage = F.Find<ComboBox>(content, "AppearanceOverlayNewValueImage-resolution");
        valueImage.SelectedItem = valueImage.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == reference);
        var staged = Document();
        // A re-render (any page state change) rebinds the editor without staging or rebuilding the open flyout.
        F.Find<ToggleSwitch>(page, "AppearanceTitleArt").IsOn = false;
        await F.Until(() => controller.State.DirtyKeys.Contains("defaults.ui.title_art"), "unrelated edit re-renders the page");
        await Task.Delay(100);
        F.Check(Document() == staged && ReferenceEquals(key, F.Find<AutoSuggestBox>(content, "AppearanceOverlayValueKey-resolution")) && key.Text == "2160p",
            "rebinding keeps the open image controls and the unfinished value without staging");
        await F.Click(content, "AppearanceOverlayAddValue-resolution");
        await F.Until(() => Item("resolution").TryGetProperty("valueImages", out var map) && map.TryGetProperty("2160p", out var assigned) && assigned.GetString() == reference, "per-value image stages");
        flyout.Hide();
        await F.Until(() => !content.IsLoaded, "image flyout closed");

        // The badge-image library: choose, name, upload, refuse an oversized pick, delete.
        var writes = server.Writes.Count;
        host.Pick = _ => F.File("huge.png", "image/png", 5L * 1024 * 1024);
        await F.Click(page, "AppearanceOverlayChoose");
        await F.Until(() => F.Shown(page, "AppearanceOverlayLibraryError") && F.Find<TextBlock>(page, "AppearanceOverlayLibraryError").Text == "Pick an image of 4 MB or less.", "oversized badge image refused");
        F.Check(server.Writes.Count == writes, "an oversized badge image is never sent");
        host.Pick = _ => F.File("4K Dolby Vision.png", "image/png");
        await F.Click(page, "AppearanceOverlayChoose");
        await F.Until(() => F.Shown(page, "AppearanceOverlayImageName") && F.Find<TextBox>(page, "AppearanceOverlayImageName").Text == "4K Dolby Vision", "picked file offers its name");
        F.Check(F.Find<Button>(page, "AppearanceOverlayChoose").Content?.ToString() == "Pick another image", "Choose becomes Pick another image");
        F.Find<TextBox>(page, "AppearanceOverlayImageName").Text = "  Synthetic 4K  ";
        await F.Click(page, "AppearanceOverlayUpload");
        await F.Until(() => server.Images.Count == 2 && controller.State.OverlayConfig?.Images.Count == 2 && controller.State.AssetBusy == null, "badge image uploaded and listed");
        F.Check(server.LastImageName == "Synthetic 4K" && server.Writes.Count == writes + 1, "one upload with the trimmed name");
        await F.Until(() => !F.Shown(page, "AppearanceOverlayImageName"), "an acknowledged upload clears the picked file");
        var uploaded = AdminAppearanceFixtureServer.ImageRef(2);
        await F.Until(() => F.Shown(page, "AppearanceOverlayDelete-" + uploaded), "uploaded image row");
        await F.Click(page, "AppearanceOverlayDelete-" + reference);
        await F.Until(() => F.Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "delete confirmation");
        var dialog = (ContentDialog)F.Field(page, "_dialog")!;
        F.Check(dialog.Title?.ToString() == "Delete this image?", "delete uses the official confirmation");
        F.Invoke(F.DialogButton(dialog, "Delete"));
        await F.Until(() => server.Images.All(image => image.Ref != reference) && controller.State.OverlayConfig?.Images.Count == 1 && controller.State.AssetBusy == null, "image deleted and library re-read");
        F.Check(Item("resolution").GetProperty("image").GetString() == reference, "deleting an image leaves stored references for the admin to clear");
        await F.Until(() => !F.Has(page, "AppearanceOverlayDelete-" + reference), "deleted row removed");
        F.Check(!server.Writes.Any(write => write.Contains("/settings/values", StringComparison.Ordinal)), "the editor never writes a viewer profile");

        // Badges off: style, preview and rows are inert; the library stays usable.
        var enabled = F.Find<ToggleSwitch>(page, "AppearanceOverlaysEnabled");
        enabled.IsOn = false;
        await F.Until(() => controller.State.Values["overlays.enabled"] == "false", "badges-off staged");
        await F.Until(() => !F.Find<ToggleSwitch>(editor, "AppearanceOverlayToggle-resolution").IsEnabled, "rows are inert while badges are off");
        F.Check(F.Find<Button>(page, "AppearanceOverlayChoose").IsEnabled, "the badge-image library stays usable while badges are off");
        await F.Click(page, "AppearanceSave");
        await F.Until(() => controller.State.DirtyKeys.Count == 0 && !controller.State.Saving, "overlay defaults saved in one batch");
        F.Check(server.LastSavedKeys.OrderBy(name => name).SequenceEqual(["defaults.card_overlays", "defaults.ui.title_art", "overlays.enabled"]), "one save carries the staged appearance keys");
        using (var saved = JsonDocument.Parse(server.Values["defaults.card_overlays"]))
            F.Check(saved.RootElement.GetProperty("items").GetProperty("year").GetProperty("position").GetString() == "left-edge"
                && saved.RootElement.GetProperty("preset").GetString() == "plate", "the saved document carries the staged edits");

        // An older manifest: no style controls, edge-free presets, image editors or library.
        server.ManifestRevision = 24;
        await F.Click(page, "AppearanceReload");
        await F.Until(() => controller.State is { Loading: false, SupportsStyles: false }, "revision 24 re-read");
        await F.Until(() => !F.Shown(page, "AppearanceOverlayLibrary") && !F.Shown(editor, "AppearanceOverlayPreset-band"), "advanced presets and the library are hidden");
        F.Check(!F.Shown(editor, "AppearanceOverlaySize") && !F.Shown(editor, "AppearanceOverlayImages-resolution"), "custom look and badge images are hidden");
        F.Check(F.Shown(editor, "AppearanceOverlayPreset-classic"), "legacy presets remain");
        await MediaParityNativeFixture.CaptureAsync(host.Outer, "appearance-overlays-1440.png");
        host.Auth.SetCurrentUser(new UserInfo { Id = "1", Username = "Synthetic admin", Role = "user" });
        await F.Until(() => controller.State.Owner == null, "authority retirement");
    }
}
