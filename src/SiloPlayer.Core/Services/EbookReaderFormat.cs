using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public static class EbookReaderFormat
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
        { "epub", "pdf", "mobi", "azw", "azw3", "cbz", "cbr", "fb2", "fbz" };

    public static string Detect(FileVersion? file)
    {
        if (file == null) return "";
        var fileName = file.FileName ?? file.FilePath ?? "";
        if (fileName.EndsWith(".fb2.zip", StringComparison.OrdinalIgnoreCase))
            return "fbz";

        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        var container = file.Container.Trim().TrimStart('.').ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(container) && container is not ("zip" or "rar"))
            return container;
        return !string.IsNullOrWhiteSpace(extension) ? extension : container;
    }

    public static bool IsSupported(FileVersion? file) => Supported.Contains(Detect(file));
}
