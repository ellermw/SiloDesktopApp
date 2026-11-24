using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Views.Dialogs;

// Official silo-server #3088: upload a poster, backdrop or logo from Edit metadata > Images.
internal static partial class ConditionalEditorsNativeFixture
{
    private static ToastContainer? _toastHost;
    private static MediaItemDetail Season() => new() { ContentId = "fixture-season", Type = "season", Title = "Fixture Season" };

    // The system file picker cannot be driven; the dialog takes its chosen file from this seam.
    private static void ChooseFile(EditMetadataDialog dialog, ArtworkUploadFile? file)
        => typeof(EditMetadataDialog).GetField("_artworkPicker", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(dialog, new Func<Task<ArtworkUploadFile?>>(() => Task.FromResult(file)));
    private static ArtworkUploadFile Chosen(string name, string type, long length, Action? opened = null)
        => new(name, type, length, _ => { opened?.Invoke(); return Task.FromResult<Stream>(new MemoryStream(new byte[Math.Min(length, 1 << 16)])); });
    private static StackPanel ImagesOf(EditMetadataDialog dialog) => Field<Dictionary<string, StackPanel>>(dialog, "_sections")["images"];
    private static Button UploadOf(StackPanel images) => Descendants<Button>(images).Single(button => Equals(button.Tag, "upload"));
    private static Button TabOf(StackPanel images, string slot) => Descendants<Button>(images).Single(button => Equals(button.Tag, slot));
    private static Button[] CardsOf(StackPanel images) => images.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
    private static string UploadText(Button upload) => string.Concat(Descendants<TextBlock>(upload).Select(text => text.Text));
    private static string[] ToastTexts() => Descendants<TextBlock>(_toastHost!).Select(text => text.Text).ToArray();
    private static string Name(Button button) => AutomationProperties.GetName(button);

    private static async Task<(EditMetadataDialog Dialog, Windows.Foundation.IAsyncOperation<ContentDialogResult> Showing, StackPanel Images)> OpenImagesAsync(FrameworkElement owner, MediaItemDetail item, int firstTabCards)
    {
        var dialog = new EditMetadataDialog(item) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync(); await Task.Delay(150);
        Section(dialog, "Images"); var images = ImagesOf(dialog);
        await UntilAsync(() => CardsOf(images).Length == firstTabCards, () => "image cards did not load: " + CardsOf(images).Length);
        await Task.Delay(120); dialog.UpdateLayout();
        return (dialog, showing, images);
    }
    private static Windows.Foundation.Rect BoundsIn(FrameworkElement element, FrameworkElement root)
    {
        var origin = element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        return new(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }
    private static bool Overlaps(Windows.Foundation.Rect a, Windows.Foundation.Rect b) => a.Left < b.Right - 0.5 && b.Left < a.Right - 0.5 && a.Top < b.Bottom - 0.5 && b.Top < a.Bottom - 0.5;
    private static async Task ShowToastAsync(string name)
    {
        await Task.Delay(420);
        // Capture the newest toast itself: the toast layer is transparent and sits outside the dialog.
        var text = Descendants<TextBlock>(_toastHost!).FirstOrDefault(); if (text == null) throw new InvalidOperationException("No toast to capture for " + name);
        DependencyObject node = text; while (VisualTreeHelper.GetParent(node) is { } parent && !(parent is StackPanel stack && stack.Name == "ToastStack")) node = parent;
        await CaptureAsync((FrameworkElement)node, name);
    }

    private static async Task UploadControlsAsync(FrameworkElement owner, Wire wire, AuthService auth, int width)
    {
        wire.Reset(); wire.RichImages = true;
        var (dialog, showing, images) = await OpenImagesAsync(owner, Movie(), 4);
        try
        {
            foreach (var (slot, cards, label) in new[] { ("poster", 4, "Upload poster"), ("backdrop", 3, "Upload backdrop"), ("logo", 2, "Upload logo") })
            {
                Invoke(TabOf(images, slot)); await UntilAsync(() => CardsOf(images).Length == cards, () => slot + " cards " + CardsOf(images).Length); await Task.Delay(120); dialog.UpdateLayout();
                var upload = UploadOf(images);
                Check(upload.Visibility == Visibility.Visible && upload.IsEnabled && UploadText(upload) == "Upload" && Name(upload) == label, $"{slot}: upload control missing, disabled or mislabelled ({UploadText(upload)}/{Name(upload)})");
                var tabs = Descendants<StackPanel>(images).Single(panel => panel.Children.OfType<Button>().Any(button => Equals(button.Tag, "poster")));
                var tabBounds = BoundsIn(tabs, dialog); var uploadBounds = BoundsIn(upload, dialog);
                Check(!Overlaps(tabBounds, uploadBounds), $"{slot}: upload control overlaps the tabs at {width}: tabs={tabBounds} upload={uploadBounds}");
                var textless = Descendants<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>(images).Single();
                Check(slot == "logo" ? textless.Visibility == Visibility.Collapsed : !Overlaps(BoundsIn(textless, dialog), uploadBounds), $"{slot}: textless filter and upload overlap");
                var body = Field<ScrollViewer>(dialog, "_scroll");
                Check(uploadBounds.Right <= BoundsIn(body, dialog).Right, $"{slot}: upload control is clipped on the right at {width}");
                Program.Log($"TRACE artwork upload {width} {slot} tabs={tabBounds} upload={uploadBounds}");
                await CaptureAsync(dialog, $"artwork-upload-movie-{slot}-{width}.png");
            }
            Check(wire.Uploads.Count == 0 && wire.Writes.Count == 0, "opening the picker or switching tabs sent a request");
            Close(dialog); await showing;
        }
        finally { dialog.Hide(); await showing; }

        // A season takes a poster only in this picker; its Upload control is named for the poster.
        wire.Reset(); wire.RichImages = true;
        (dialog, showing, images) = await OpenImagesAsync(owner, Season(), 4);
        try
        {
            var buttons = Descendants<Button>(images).Where(button => button.Tag is "poster" or "backdrop" or "logo").Select(button => (string)button.Tag).ToArray();
            Check(buttons.SequenceEqual(["poster"]), "season picker tabs: " + string.Join(",", buttons));
            var upload = UploadOf(images);
            Check(upload.IsEnabled && Name(upload) == "Upload poster" && UploadText(upload) == "Upload", "season upload control is wrong: " + Name(upload));
            await CaptureAsync(dialog, $"artwork-upload-season-poster-{width}.png");
        }
        finally { dialog.Hide(); await showing; }

        // Only an administrator is offered Images at all, so only they are offered an upload.
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        try
        {
            var plain = new EditMetadataDialog(Movie()) { XamlRoot = owner.XamlRoot }; var plainShowing = plain.ShowAsync(); await Task.Delay(150);
            try { Check(!Field<Dictionary<string, StackPanel>>(plain, "_sections").ContainsKey("images") && !Descendants<Button>(plain).Any(button => Equals(button.Tag, "upload")), "a non-administrator was offered the image upload"); }
            finally { plain.Hide(); await plainShowing; }
        }
        finally { Admin(auth); }
    }

    private static async Task UploadRefusedAsync(FrameworkElement owner, Wire wire, int width)
    {
        wire.Reset(); wire.RichImages = true;
        var (dialog, showing, images) = await OpenImagesAsync(owner, Movie(), 4);
        try
        {
            var upload = UploadOf(images); var opened = false;
            ChooseFile(dialog, Chosen("animation.gif", "image/gif", 1024, () => opened = true)); Invoke(upload); await Task.Delay(250);
            Check(ToastTexts().Contains("Choose a JPEG, PNG or WebP image"), "a wrong file type was not refused with the official text: " + string.Join("|", ToastTexts()));
            Check(wire.Uploads.Count == 0 && !opened && upload.IsEnabled && UploadText(upload) == "Upload", "a wrong file type was sent, read or left the control busy");
            await CaptureAsync(dialog, $"artwork-upload-refused-type-{width}.png"); await ShowToastAsync($"artwork-upload-refused-type-toast-{width}.png");

            ChooseFile(dialog, Chosen("huge.png", "image/png", 10L * 1024 * 1024 + 1, () => opened = true)); Invoke(upload); await Task.Delay(250);
            Check(ToastTexts().Contains("Images can be at most 10 MiB"), "a too-large file was not refused with the official text: " + string.Join("|", ToastTexts()));
            Check(wire.Uploads.Count == 0 && !opened && upload.IsEnabled, "a too-large file was sent or read");
            await CaptureAsync(dialog, $"artwork-upload-refused-size-{width}.png"); await ShowToastAsync($"artwork-upload-refused-size-toast-{width}.png");

            // A file the system cannot name is sent for the server to check, exactly 10 MiB is allowed, and cancelling the picker sends nothing.
            ChooseFile(dialog, null); Invoke(upload); await Task.Delay(200);
            Check(wire.Uploads.Count == 0 && upload.IsEnabled, "cancelling the picker sent a request or left the control busy");
            ChooseFile(dialog, Chosen("no-extension", "", 5)); Invoke(upload); await UntilAsync(() => wire.Uploads.Count == 1, () => "unnamed-type file was not sent");
            Check(wire.Uploads[0].ContentType == "application/octet-stream", "an unnamed type was not sent as octet-stream: " + wire.Uploads[0].ContentType);
            await UntilAsync(() => !Field<bool>(dialog, "_uploadPending"));
        }
        finally { dialog.Hide(); await showing; }
    }

    private static async Task UploadBusyThenCurrentAsync(FrameworkElement owner, Wire wire, int width)
    {
        wire.Reset(); wire.RichImages = true; wire.UploadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var (dialog, showing, images) = await OpenImagesAsync(owner, Movie(), 4);
        try
        {
            Invoke(TabOf(images, "backdrop")); await UntilAsync(() => CardsOf(images).Length == 3); await Task.Delay(100);
            var upload = UploadOf(images); var before = CardsOf(images);
            Check(before.Count(card => Descendants<TextBlock>(card).Any(text => text.Text == "Current")) == 1 && Descendants<TextBlock>(before[0]).Any(text => text.Text == "Current"), "the provider backdrop was not Current before the upload");
            Invoke(before[1]); var applyButton = images.Children.OfType<Button>().Single();
            Check(applyButton.Visibility == Visibility.Visible && applyButton.IsEnabled && Equals(applyButton.Content, "Apply Backdrop"), "a selected backdrop did not offer Apply Backdrop");
            ChooseFile(dialog, Chosen("backdrop.png", "image/png", 2048)); Invoke(upload);
            await UntilAsync(() => wire.Uploads.Count == 1 && Field<bool>(dialog, "_uploadPending"), () => "upload did not start");
            await Task.Delay(150);
            Check(!upload.IsEnabled && UploadText(upload) == "Uploading...", "busy state: label/disabled wrong: " + UploadText(upload) + "/" + upload.IsEnabled);
            Check(Descendants<ProgressRing>(upload).Any(), "busy state shows no spinner");
            Check(!dialog.IsPrimaryButtonEnabled, "busy state left Save enabled");
            Check(TabOf(images, "poster").IsEnabled, "tabs were disabled during an upload");
            Check(!applyButton.IsEnabled && Equals(applyButton.Content, "Apply Backdrop"), "an upload in flight enabled Apply or labelled it as applying: " + applyButton.Content);
            await CaptureAsync(dialog, $"artwork-upload-busy-{width}.png");
            var request = wire.Uploads[0];
            Check(request.Path == "/api/v2/admin/items/fixture-movie/images/upload" && request.Slot == "backdrop" && request.PartOrder == "type,image" && request.FileName == "backdrop.png" && request.ContentType == "image/png" && request.Length == 2048 && request.Authorized,
                "upload request is wrong: " + request);

            wire.UploadGate.SetResult(Response(HttpStatusCode.OK, """{"content_id":"fixture-movie","stored_path":"local/backdrop","thumbhash":"AAAA"}"""));
            await UntilAsync(() => !Field<bool>(dialog, "_uploadPending") && dialog.IsPrimaryButtonEnabled, () => "upload did not finish");
            await Task.Delay(200);
            var after = CardsOf(images);
            Check(wire.Uploads.Count == 1, "an upload was sent more than once");
            Check(wire.ImageListReads == 2, "the choices were not reloaded exactly once after the upload: " + wire.ImageListReads);
            Check(after.Length == 4 && Equals(after[0].Tag, "upload://sha256:0123abcd"), "the uploaded image is not the first choice: " + after.Length);
            var current = after.Where(card => Descendants<TextBlock>(card).Any(text => text.Text == "Current")).Select(card => (string)card.Tag).ToArray();
            Check(current.SequenceEqual(["upload://sha256:0123abcd"]), "only the upload should be Current afterwards: " + string.Join(",", current));
            Check(upload.IsEnabled && UploadText(upload) == "Upload" && !Descendants<ProgressRing>(upload).Any(), "the control stayed busy after the upload");
            Check(dialog.HasSaved && Field<MetadataEditState>(dialog, "_state").EffectiveLocks(Movie().LockedFields).Contains(10), "the upload did not record the images lock");
            Check(ToastTexts().Contains("Image uploaded"), "no success toast: " + string.Join("|", ToastTexts()));
            await CaptureAsync(dialog, $"artwork-upload-after-{width}.png"); await ShowToastAsync($"artwork-upload-after-toast-{width}.png");
            // Another tab still shows its own choices; the poster list is unchanged.
            Invoke(TabOf(images, "poster")); await UntilAsync(() => CardsOf(images).Length == 4);
            Check(!CardsOf(images).Any(card => Equals(card.Tag, "upload://sha256:0123abcd")), "the backdrop upload leaked into the poster list");
            await Task.Delay(300); Check(wire.Uploads.Count == 1 && wire.ImageListReads == 2, "work was repeated after the upload settled");
        }
        finally { wire.UploadGate?.TrySetResult(Response(HttpStatusCode.OK, "{}")); dialog.Hide(); await showing; }
    }

    private static async Task UploadFailureAsync(FrameworkElement owner, Wire wire)
    {
        wire.Reset(); wire.RichImages = true;
        var problem = new HttpResponseMessage((HttpStatusCode)422) { Content = new StringContent("""{"type":"https://silo.invalid/problems/validation","title":"Validation failed","status":422,"detail":"The request did not pass validation","errors":[{"location":"body.image","code":"image_unsupported","detail":"The file is not a JPEG, PNG or WebP image"}]}""", Encoding.UTF8, "application/problem+json") };
        wire.UploadFailure = problem;
        var (dialog, showing, images) = await OpenImagesAsync(owner, Movie(), 4);
        try
        {
            var upload = UploadOf(images); var before = CardsOf(images); var earlierSuccess = ToastTexts().Count(text => text == "Image uploaded");
            ChooseFile(dialog, Chosen("broken.png", "image/png", 99)); Invoke(upload);
            await UntilAsync(() => wire.Uploads.Count == 1 && !Field<bool>(dialog, "_uploadPending"), () => "failed upload did not settle");
            await Task.Delay(300);
            Check(ToastTexts().Contains("The file is not a JPEG, PNG or WebP image"), "the refusal detail was not shown: " + string.Join("|", ToastTexts()));
            Check(wire.Uploads.Count == 1, "a failed upload was sent again");
            Check(wire.ImageListReads == 1 && CardsOf(images).SequenceEqual(before), "a failed upload reloaded or changed the choices");
            Check(upload.IsEnabled && UploadText(upload) == "Upload" && dialog.IsPrimaryButtonEnabled && !dialog.HasSaved, "a failed upload left the dialog busy or marked saved");
            Check(ToastTexts().Count(text => text == "Image uploaded") <= earlierSuccess, "a failed upload claimed success");
            // The person decides to try again: that is a new request.
            wire.UploadFailure = null; Invoke(upload); await UntilAsync(() => wire.Uploads.Count == 2 && !Field<bool>(dialog, "_uploadPending"));
            Check(wire.Uploads.Count == 2, "the second attempt was not a single new request");
        }
        finally { dialog.Hide(); await showing; }
    }

    private static async Task UploadReplyAfterSwitchAsync(FrameworkElement owner, Wire wire, AuthService auth)
    {
        wire.Reset(); wire.RichImages = true; wire.UploadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var (dialog, showing, images) = await OpenImagesAsync(owner, Movie(), 4);
        var successToasts = ToastTexts().Count(text => text == "Image uploaded");
        try
        {
            var upload = UploadOf(images);
            ChooseFile(dialog, Chosen("late.png", "image/png", 64)); Invoke(upload);
            await UntilAsync(() => wire.Uploads.Count == 1 && Field<bool>(dialog, "_uploadPending"));
            // The account loses its right to curate (or is replaced) while the upload is on its way.
            auth.SetCurrentUser(new() { Id = "other", Role = "user" });
            wire.UploadGate.SetResult(Response(HttpStatusCode.OK, """{"content_id":"fixture-movie","stored_path":"local/poster","thumbhash":"AAAA"}"""));
            await UntilAsync(() => !Field<bool>(dialog, "_uploadPending"), () => "reply of a retired owner kept the dialog busy");
            await Task.Delay(400);
            Check(wire.ImageListReads == 1, "a reply after an account switch reloaded the choices");
            Check(ToastTexts().Count(text => text == "Image uploaded") <= successToasts, "a reply after an account switch was announced");
            Check(wire.Uploads.Count == 1, "a retired upload was sent again");
        }
        finally { wire.UploadGate?.TrySetResult(Response(HttpStatusCode.OK, "{}")); dialog.Hide(); await showing; Admin(auth); }
    }
}
