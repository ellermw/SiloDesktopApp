using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services;

/// <summary>A file chosen for artwork upload. Nothing is read until <see cref="Open"/> is called.</summary>
/// <param name="Name">The file's name only, never its path.</param>
/// <param name="ContentType">The type the system names for the file; empty when it cannot.</param>
public sealed record ArtworkUploadFile(string Name, string ContentType, long Length, Func<CancellationToken, Task<Stream>> Open);

public enum ArtworkUploadRefusal { None, WrongType, TooLarge }

/// <summary>
/// Official Silo WebUI rules for uploading a poster, backdrop or logo from Edit metadata > Images
/// (silo-server #3088, web/src/components/ImageSelectorTab.tsx): JPEG, PNG or WebP of at most 10 MiB,
/// refused before anything is sent when the type or size is wrong.
/// </summary>
public static class ItemArtworkUpload
{
    public const long MaxBytes = 10L * 1024 * 1024;
    public static readonly IReadOnlyList<string> AcceptedTypes = ["image/jpeg", "image/png", "image/webp"];
    public static readonly IReadOnlyList<string> PickerExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    public const string UnnamedType = "application/octet-stream";

    /// <summary>Movies and series take a poster, backdrop and logo; a season takes a poster and a backdrop.</summary>
    public static IReadOnlyList<string> SlotsFor(string itemType)
        => itemType == "season" ? ["poster", "backdrop"] : ["poster", "backdrop", "logo"];

    public static bool IsSlot(string slot) => slot is "poster" or "backdrop" or "logo";

    public static string UploadKey(string slot) => slot switch
    {
        "backdrop" => "web.item.images.uploadBackdrop",
        "logo" => "web.item.images.uploadLogo",
        _ => "web.item.images.uploadPoster",
    };

    public static string UploadName(string slot) => AppStrings.Get(UploadKey(slot), null, slot switch
    {
        "backdrop" => "Upload backdrop",
        "logo" => "Upload logo",
        _ => "Upload poster",
    });

    public static string ButtonText(bool uploading) => uploading
        ? AppStrings.Get("web.item.images.uploading", null, "Uploading...")
        : AppStrings.Get("web.item.images.upload", null, "Upload");

    /// <summary>
    /// A file whose type the system cannot name (empty) goes to the server, which checks the image's bytes.
    /// A named type must be one of the three; the type is checked before the size.
    /// </summary>
    public static ArtworkUploadRefusal Check(string? contentType, long length)
    {
        if (!string.IsNullOrEmpty(contentType) && !AcceptedTypes.Contains(contentType, StringComparer.Ordinal)) return ArtworkUploadRefusal.WrongType;
        return length > MaxBytes ? ArtworkUploadRefusal.TooLarge : ArtworkUploadRefusal.None;
    }

    public static string RefusalMessage(ArtworkUploadRefusal refusal) => refusal switch
    {
        ArtworkUploadRefusal.WrongType => AppStrings.Get("web.item.images.wrongType", null, "Choose a JPEG, PNG or WebP image"),
        ArtworkUploadRefusal.TooLarge => AppStrings.Get("web.item.images.tooLarge", null, "Images can be at most 10 MiB"),
        _ => "",
    };

    public static string SuccessMessage() => AppStrings.Get("web.item.toast.imageUploaded", null, "Image uploaded");

    /// <summary>
    /// A validation problem shows its first field's detail (why the file was refused); any other error shows its
    /// own text. The generic text covers an error with none.
    /// </summary>
    public static string FailureMessage(Exception error)
    {
        var text = error is ApiException api
            ? api.FieldErrors is { Count: > 0 } fields && ApiProblemText.FieldDetail(fields[0]) is { Length: > 0 } detail ? detail : ApiProblemText.Display(api)
            : error.Message;
        return string.IsNullOrWhiteSpace(text) ? GenericFailureMessage() : text;
    }

    public static string GenericFailureMessage() => AppStrings.Get("web.item.toast.imageUploadFailed", null, "Failed to upload image");

    /// <summary>The file's bytes, or null when it is, or turned out to be, over the limit.</summary>
    public static async Task<byte[]?> ReadAsync(ArtworkUploadFile file, CancellationToken ct)
    {
        if (file.Length > MaxBytes) return null;
        await using var source = await file.Open(ct).ConfigureAwait(false);
        using var target = new MemoryStream((int)Math.Clamp(file.Length, 0, MaxBytes));
        var buffer = new byte[81920]; int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (target.Length + read > MaxBytes) return null;
            target.Write(buffer, 0, read);
        }
        return target.ToArray();
    }
}
