using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;

/// <summary>
/// A subtitle file rejected in Add Subtitles (official #5291) leaves nothing of the earlier file: its name and error, the language
/// detection still running for it, and a language that detection found. Real dialog with a synthetic server. Selector: subtitle-upload-reject.
/// </summary>
internal static class SubtitleUploadRejectNativeFixture
{
    internal static async Task RunAsync(FrameworkElement owner)
    {
        var originalWidth = owner.Width; var originalHeight = owner.Height;
        owner.Width = 1440; owner.Height = 900;
        owner.Measure(new Windows.Foundation.Size(1440, 900)); owner.Arrange(new Windows.Foundation.Rect(0, 0, 1440, 900)); owner.UpdateLayout();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var originalServices = servicesField.GetValue(null);
        var differences = new List<string>();
        var folder = Path.Combine(Program.ResultDirectory, "subtitle-upload-files");
        Directory.CreateDirectory(folder);
        var valid = Path.Combine(folder, "c-valid.vtt"); File.WriteAllText(valid, "WEBVTT\n\n00:00.000 --> 00:01.000\nBonjour\n");
        var manual = Path.Combine(folder, "episode.srt"); File.WriteAllText(manual, "1\n00:00:00,000 --> 00:00:01,000\nHallo\n");
        var notes = Path.Combine(folder, "notes.txt"); File.WriteAllText(notes, "notes");
        try
        {
            foreach (var scenario in new[] { "detected", "chosen", "late" })
            {
                using var handler = new Handler(scenario);
                using var http = new HttpClient(handler);
                var client = new SiloApiClient(http); client.SetBaseUrl("https://subtitle-upload.invalid");
                using var services = new ServiceCollection().AddSingleton(client).AddSingleton(new PlaybackApi(client)).BuildServiceProvider();
                servicesField.SetValue(null, services);
                var dialog = new SubtitleSearchDialog(1, playerMode: true) { XamlRoot = owner.XamlRoot };
                var showing = dialog.ShowAsync();
                try
                {
                    await UntilAsync(() => (bool)typeof(SubtitleSearchDialog).GetField("_providerStatusLoaded", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!);
                    var language = (ComboBox)dialog.FindName("UploadLanguageComboBox");
                    var browse = (Button)dialog.FindName("BrowseUploadButton");
                    var upload = (Button)dialog.FindName("UploadButton");
                    var file = (TextBlock)dialog.FindName("UploadFileText");
                    var status = (TextBlock)dialog.FindName("UploadStatusText");
                    var root = (FrameworkElement)dialog.FindName("DialogRoot");
                    string Tag() => (language.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                    if (Tag() != "en") differences.Add($"{scenario}: the dialog opens on English, not '{Tag()}'.");
                    const string Unsupported = "Unsupported file type. Use SRT, VTT, ASS, SSA, or SUB.";

                    if (scenario == "detected")
                    {
                        await LoadAsync(dialog, valid);
                        await UntilAsync(() => Tag() == "fr" && upload.IsEnabled);
                        if (!file.Text.Contains("c-valid.vtt") || !status.Text.StartsWith("Detected", StringComparison.Ordinal)) differences.Add($"{scenario}: the chosen file and the detected note must show first ('{file.Text}' / '{status.Text}').");
                        await MediaParityNativeFixture.CaptureAsync(root, "subtitle-upload-detected-1440.png");
                        await LoadAsync(dialog, notes);
                        if (file.Text != "No file selected" || status.Text != Unsupported || upload.IsEnabled || !browse.IsEnabled || Tag() != "en" || Bytes(dialog) != null)
                            differences.Add($"{scenario}: a rejected file must clear the earlier one and its language (file '{file.Text}', note '{status.Text}', upload {upload.IsEnabled}, browse {browse.IsEnabled}, language {Tag()}, kept bytes {Bytes(dialog)?.Length}).");
                        await MediaParityNativeFixture.CaptureAsync(root, "subtitle-upload-rejected-1440.png");
                        // The same file chosen again is read again.
                        await LoadAsync(dialog, valid);
                        await UntilAsync(() => Tag() == "fr" && upload.IsEnabled);
                        if (!file.Text.Contains("c-valid.vtt")) differences.Add($"{scenario}: the earlier file can be chosen again after the rejection.");
                    }
                    else if (scenario == "chosen")
                    {
                        // With no language cues the detector echoes the person's own pick back as a manual fallback; a rejection keeps that pick.
                        language.SelectedItem = language.Items.OfType<ComboBoxItem>().First(item => item.Tag as string == "de");
                        await LoadAsync(dialog, manual);
                        await UntilAsync(() => upload.IsEnabled && browse.IsEnabled);
                        await LoadAsync(dialog, notes);
                        if (Tag() != "de" || status.Text != Unsupported || file.Text != "No file selected" || upload.IsEnabled)
                            differences.Add($"{scenario}: the language the person picked stays after a rejection (language {Tag()}, note '{status.Text}').");
                        await MediaParityNativeFixture.CaptureAsync(root, "subtitle-upload-rejected-kept-language-1440.png");
                    }
                    else
                    {
                        // A rejection while detection for the earlier file is still running drops that detection and lets Browse work.
                        var pending = LoadAsync(dialog, valid);
                        await UntilAsync(() => handler.DetectionStarted && !browse.IsEnabled);
                        await LoadAsync(dialog, notes);
                        if (!browse.IsEnabled) differences.Add($"{scenario}: Browse must be usable again after the rejection.");
                        handler.FinishDetection();
                        await pending; await Task.Delay(150);
                        if (Tag() != "en" || status.Text != Unsupported || file.Text != "No file selected" || upload.IsEnabled || !browse.IsEnabled)
                            differences.Add($"{scenario}: a detection finishing after the rejection must change nothing (language {Tag()}, note '{status.Text}', file '{file.Text}', upload {upload.IsEnabled}, browse {browse.IsEnabled}).");
                        await MediaParityNativeFixture.CaptureAsync(root, "subtitle-upload-late-detection-1440.png");
                    }
                    Program.Log($"Subtitle upload {scenario}: language {Tag()}, file '{file.Text}', note '{status.Text}', upload {upload.IsEnabled}, browse {browse.IsEnabled}.");
                }
                finally { dialog.Hide(); await showing; }
            }
        }
        finally
        {
            servicesField.SetValue(null, originalServices); owner.Width = originalWidth; owner.Height = originalHeight;
        }
        if (differences.Count > 0) throw new InvalidOperationException(string.Join("\n", differences));
        Program.Log("PASS: SUBTITLE_UPLOAD_REJECT a rejected subtitle file clears the earlier selection, its detection and its detected language.");
    }

    private static async Task LoadAsync(SubtitleSearchDialog dialog, string path)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        await (Task)typeof(SubtitleSearchDialog).GetMethod("LoadUploadFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [file])!;
    }

    private static byte[]? Bytes(SubtitleSearchDialog dialog)
        => (byte[]?)typeof(SubtitleSearchDialog).GetField("_uploadFileBytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog);

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 250; i++) { if (condition()) return; await Task.Delay(20); }
        throw new TimeoutException("Subtitle upload native state timed out.");
    }

    private sealed class Handler(string scenario) : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _detection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool DetectionStarted;
        internal void FinishDetection() => _detection.TrySetResult(Json("{\"language\":\"fr\",\"source\":\"content\"}"));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/subtitles/providers/status") return Task.FromResult(Json("{\"enabled\":true}"));
            if (path == "/api/v2/subtitles/detect-language")
            {
                DetectionStarted = true;
                if (scenario == "late") return _detection.Task.WaitAsync(ct);
                return Task.FromResult(Json(scenario == "chosen" ? "{\"language\":\"de\",\"source\":\"manual\"}" : "{\"language\":\"fr\",\"source\":\"content\"}"));
            }
            throw new InvalidOperationException("Unexpected subtitle upload fixture request: " + path);
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
