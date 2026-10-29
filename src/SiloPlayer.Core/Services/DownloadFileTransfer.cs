using System.Net;
using System.Net.Http.Headers;

namespace SiloPlayer.Core.Services;

public static class DownloadFileTransfer
{
    /// <summary>
    /// A Windows-safe basename for a save dialog. Servers can return names and
    /// paths from either OS (a Linux file may be "Mission: Impossible.mkv");
    /// never use a server-supplied directory as a destination.
    /// </summary>
    public static string SafeFileName(string? name, string fallback)
    {
        name = SanitizeBaseName(name);
        if (string.IsNullOrWhiteSpace(name)) name = SanitizeBaseName(fallback);
        if (string.IsNullOrWhiteSpace(name)) name = "download";

        var extension = Path.GetExtension(name);
        if (!IsSafeExtension(extension)) extension = "";
        return BoundFileName(name, extension);
    }

    private static bool IsReservedBaseName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd(' ');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3]));
    }

    private static string SanitizeBaseName(string? name)
    {
        name = (name ?? "").Trim().Trim('"').Replace('\\', '/').Split('/').Last();
        var characters = name.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            var c = characters[i];
            if (char.IsHighSurrogate(c) && i + 1 < characters.Length && char.IsLowSurrogate(characters[i + 1]))
            { i++; continue; }
            if (c < 32 || "<>:\"/\\|?*".Contains(c) || char.IsSurrogate(c)) characters[i] = '_';
        }
        return new string(characters).Trim(' ', '.');
    }

    private static bool IsSafeExtension(string extension)
        => extension.Length is >= 2 and <= 12 && extension[0] == '.' && extension[1..].All(char.IsAsciiLetterOrDigit);

    private static string BoundFileName(string name, string extension)
    {
        var stem = extension.Length == 0 ? name : name[..^extension.Length];
        var length = Math.Min(stem.Length, 180 - extension.Length);
        if (length > 0 && char.IsHighSurrogate(stem[length - 1])) length--;
        stem = stem[..length].TrimEnd(' ', '.');
        // Truncation and trimming can themselves expose a reserved device stem.
        if (IsReservedBaseName(stem)) return BoundFileName("_" + stem + extension, extension);
        return (stem.Length == 0 ? "download" : stem) + extension;
    }

    /// <summary>
    /// Resolve the name and FileTypeChoices extension as one safe save-dialog suggestion.
    /// Set fallbackIsStem when fallback dots are title text rather than a file extension.
    /// </summary>
    public static (string FileName, string Extension) GetSaveFileSuggestion(string? name, string fallback,
        string? fallbackExtension = null, string? mediaType = null, bool fallbackIsStem = false)
    {
        name = SanitizeBaseName(name);
        var hasOriginalName = !string.IsNullOrWhiteSpace(name);
        name = SafeFileName(name, fallback);
        var extension = fallbackIsStem && !hasOriginalName ? "" : Path.GetExtension(name);
        if (!IsSafeExtension(extension))
        {
            extension = "." + (fallbackExtension ?? "").Trim().TrimStart('.');
            if (!IsSafeExtension(extension)) extension = mediaType?.ToLowerInvariant() switch
            {
                "video/mp4" or "audio/mp4" => ".mp4",
                "video/x-matroska" or "audio/x-matroska" => ".mkv",
                "video/webm" or "audio/webm" => ".webm",
                "video/x-msvideo" => ".avi",
                "video/quicktime" => ".mov",
                "video/mp2t" => ".ts",
                "video/mpeg" => ".mpeg",
                "video/x-ms-wmv" => ".wmv",
                // Unknown bytes must not be mislabeled as a known container.
                _ => ".bin"
            };
            name += extension;
        }
        return (BoundFileName(name, extension), extension);
    }

    public static string GetSuggestedFileName(HttpContentHeaders headers)
    {
        var disposition = headers.ContentDisposition;
        var name = disposition?.FileNameStar;
        if (string.IsNullOrWhiteSpace(name)) name = disposition?.FileName;
        return GetSaveFileSuggestion(name, "download", mediaType: headers.ContentType?.MediaType).FileName;
    }

    public static void EnsureSuccessfulResponse(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        // This path requests the whole file, never a range. A 206/204 is not
        // proof of a complete download even if its own content length matches.
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException("The server did not return a complete download.", null, response.StatusCode);
    }

    public static async Task SaveAsync(HttpResponseMessage response, string destinationPath,
        CancellationToken cancellationToken = default, Action? beforeCommit = null)
    {
        EnsureSuccessfulResponse(response);
        cancellationToken.ThrowIfCancellationRequested();
        var target = Path.GetFullPath(destinationPath);
        var partial = Path.Combine(Path.GetDirectoryName(target)!, $".silo-download-{Guid.NewGuid():N}.partial");
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                if (response.Content.Headers.ContentLength is { } length && destination.Length != length)
                    throw new IOException("The transfer ended before the complete file was received.");
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            beforeCommit?.Invoke();
            // Staging in the same directory permits a single rename after a
            // complete transfer and keeps existing destination bytes intact
            // on transfer, flush, cancellation or replacement failures.
            File.Move(partial, target, overwrite: true);
        }
        catch (Exception failure)
        {
            try { File.Delete(partial); }
            catch (DirectoryNotFoundException) { /* No incomplete file remains. */ }
            catch (Exception cleanupFailure) when (cleanupFailure is IOException or UnauthorizedAccessException)
            {
                throw new DownloadCleanupException(partial, failure is OperationCanceledException, failure);
            }
            throw;
        }
    }

    public static string GetSaveErrorMessage(Exception error) => error switch
    {
        DownloadCleanupException cleanup => $"Save failed. Remove the incomplete file at {cleanup.PartialPath}, then try again.",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => "Save failed: you no longer have permission to download this file. Check your profile permissions and try again.",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => "Save failed: your session expired. Sign in again and retry.",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "Save failed: this download is no longer available. Refresh Downloads or request it again.",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "Save failed: the download limit was reached. Try again later.",
        HttpRequestException or OperationCanceledException => "Save failed: the server connection was interrupted or returned an incomplete response. Check your connection and try again.",
        UnauthorizedAccessException => "Save failed: the destination is not writable. Choose a folder you can write to and try again.",
        IOException => "Save failed: the transfer or disk write could not finish. Check free space, close any app using the destination file, and try again.",
        _ => "Save failed. Choose a writable destination and try again."
    };

    public static string GetEmptyDestinationNotice(string? destinationPath)
    {
        if (string.IsNullOrEmpty(destinationPath)) return "";
        try
        {
            var file = new FileInfo(destinationPath);
            // Windows' picker may create an empty placeholder. Do not delete
            // it: it could also be an empty file the user already owned.
            return file.Exists && file.Length == 0
                ? $" The selected file is empty and contains no media: {destinationPath}"
                : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ""; // Keep the original transfer failure as the visible error.
        }
    }
}

public sealed class DownloadCleanupException(string partialPath, bool wasCanceled, Exception innerException)
    : IOException("The incomplete download could not be removed.", innerException)
{
    public string PartialPath { get; } = partialPath;
    public bool WasCanceled { get; } = wasCanceled;
}
