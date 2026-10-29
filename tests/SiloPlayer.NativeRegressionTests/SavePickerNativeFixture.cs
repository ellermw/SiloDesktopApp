using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Views;
using Windows.Storage.Pickers;

internal static class SavePickerNativeFixture
{
    // Builds each page's actual picker configuration. No dialog is opened and
    // no StorageFile is selected, created, modified or saved.
    internal static Task RunAsync(StackPanel unused, Window unusedWindow)
    {
        var receipts = new List<object>();
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { FileName = "...", Container = "mkv" }, "Spider.Man"], "Spider.Man.mkv", ".mkv");
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { FileName = "/srv/media/...", Container = "mkv" }, "Spider.Man"], "Spider.Man.mkv", ".mkv");
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { FileName = @"D:\Media\...", Container = "mkv" }, "Spider.Man"], "Spider.Man.mkv", ".mkv");
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { Container = "mkv" }, "Spider.Man"], "Spider.Man.mkv", ".mkv");
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { FileName = " ", Container = "mkv" }, "Avatar.2"], "Avatar.2.mkv", ".mkv");
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { Container = "mk?v" }, "Avatar.2"], "Avatar.2.bin", ".bin");
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { FileName = "Avatar.2.mkv", Container = "mkv" }, "Avatar.2"], "Avatar.2.mkv", ".mkv");
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { FileName = "movie.mk?v", Container = "mkv" }, "Movie"],
            "movie.mk_v.mkv", ".mkv");
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { Container = "../mp?4" }, "../CON: bad?"], "CON_ bad_.bin", ".bin");
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { FileName = "COM¹.mkv" }, "Movie"], "_COM¹.mkv", ".mkv");
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { FileName = new string('x', 175) + "😀" + new string('y', 20) + ".mp4" }, "Movie"],
            new string('x', 175) + ".mp4", ".mp4");
        Check(typeof(ItemDetailPage), "CreateDirectDownloadPicker",
            [new FileVersion { Container = "mp4" }, new string('x', 300)], new string('x', 176) + ".mp4", ".mp4");
        Check(typeof(EbookReaderPage), "CreateReaderDownloadPicker",
            [new FileVersion { FileName = "Book.Vol.1.epub", Container = "epub" }, "Book"], "Book.Vol.1.epub", ".epub");
        Check(typeof(EbookReaderPage), "CreateReaderDownloadPicker",
            [new FileVersion { FilePath = "/library/LPT².epub", Container = "epub" }, "Book"], "_LPT².epub", ".epub");
        Check(typeof(EbookReaderPage), "CreateReaderDownloadPicker",
            [new FileVersion { FileName = "Book.ep?ub", Container = "ep?ub" }, "Book"], "Book.bin", ".bin");
        Check(typeof(EbookReaderPage), "CreateReaderDownloadPicker",
            [new FileVersion { Container = "epub" }, new string('x', 300)], new string('x', 175) + ".epub", ".epub");

        using var content = new ByteArrayContent([]);
        var headers = content.Headers;
        headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = "../Movie.mk?v" };
        headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        Check(typeof(DownloadsPage), "CreateDownloadPicker", [DownloadFileTransfer.GetSuggestedFileName(headers)],
            "Movie.mk_v.mp4", ".mp4");
        headers.ContentDisposition.FileNameStar = new string('x', 175) + " " + new string('y', 20) + ".mp4";
        Check(typeof(DownloadsPage), "CreateDownloadPicker", [DownloadFileTransfer.GetSuggestedFileName(headers)],
            new string('x', 175) + ".mp4", ".mp4");
        headers.ContentDisposition.FileNameStar = null;
        headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        Check(typeof(DownloadsPage), "CreateDownloadPicker", [DownloadFileTransfer.GetSuggestedFileName(headers)],
            "download.bin", ".bin");

        File.WriteAllText(Path.Combine(Program.ResultDirectory, "save-picker-configurations.json"),
            JsonSerializer.Serialize(receipts, new JsonSerializerOptions { WriteIndented = true }));
        Program.Log($"PASS: {receipts.Count} actual page FileSavePicker configurations; no dialogs or user files.");
        return Task.CompletedTask;

        void Check(Type page, string method, object?[] arguments, string expectedName, string expectedExtension)
        {
            var factory = page.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(page.FullName, method);
            var picker = (FileSavePicker)factory.Invoke(null, arguments)!;
            // WinRT's map-values projection does not expose the generic
            // IEnumerable interface used by LINQ on this Windows runtime.
            // Read the actual registered choice directly instead.
            var label = page == typeof(ItemDetailPage) ? "Media file" : page == typeof(EbookReaderPage) ? "Book file" : "Media File";
            Require(picker.FileTypeChoices.Count == 1, $"{page.Name}: unexpected file type choices.");
            var choice = picker.FileTypeChoices[label];
            Require(choice.Count == 1, $"{page.Name}: unexpected extension count.");
            var extensions = new[] { choice[0] };
            Require(picker.SuggestedFileName == expectedName, $"{page.Name}: name does not match expected safe suggestion.");
            Require(extensions.Length == 1 && extensions[0] == expectedExtension, $"{page.Name}: unsafe or mismatched file type.");
            Require(picker.SuggestedFileName.Length <= 180, $"{page.Name}: unbounded filename.");
            receipts.Add(new { page = page.Name, name = picker.SuggestedFileName, extensions });
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
