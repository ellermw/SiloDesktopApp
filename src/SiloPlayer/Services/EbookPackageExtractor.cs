using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;
using SharpCompress.Readers;

namespace SiloPlayer.Services;

internal sealed record EbookReaderChapter(string Title, string RelativePath);
internal sealed record ExtractedEbook(string RootDirectory, IReadOnlyList<EbookReaderChapter> Chapters, string Format);

internal static class EbookPackageExtractor
{
    // Book packages come from library files; bound what one archive may expand
    // to on disk so a malformed or hostile package cannot fill the profile drive.
    internal const long MaxExtractedBytes = 2L * 1024 * 1024 * 1024;
    internal const int MaxEntries = 50_000;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".svg" };

    public static ExtractedEbook Extract(byte[] bytes, string targetDirectory, string declaredFormat)
        => Extract(bytes, targetDirectory, declaredFormat, MaxExtractedBytes, MaxEntries);

    internal static ExtractedEbook Extract(
        byte[] bytes, string targetDirectory, string declaredFormat, long maxBytes, int maxEntries)
    {
        // Start from an empty directory: leftovers from an earlier extraction would
        // otherwise be listed as comic pages or served as book resources.
        if (Directory.Exists(targetDirectory))
            Directory.Delete(targetDirectory, recursive: true);
        Directory.CreateDirectory(targetDirectory);
        if (IsPdf(bytes))
        {
            var path = Path.Combine(targetDirectory, "book.pdf");
            File.WriteAllBytes(path, bytes);
            return new ExtractedEbook(targetDirectory, [new EbookReaderChapter("Document", "book.pdf")], "pdf");
        }
        if (declaredFormat.Equals("fb2", StringComparison.OrdinalIgnoreCase) && !IsZip(bytes))
            return ReadFb2(bytes, targetDirectory);
        if (declaredFormat.Equals("cbr", StringComparison.OrdinalIgnoreCase) || IsRar(bytes))
        {
            ExtractArchive(bytes, targetDirectory, new ExtractionBudget(maxBytes, maxEntries));
            return ReadComicArchive(targetDirectory);
        }
        if (!IsZip(bytes))
            throw new InvalidDataException($"The {declaredFormat.ToUpperInvariant()} book file is not a recognized or supported package.");

        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            var root = Path.GetFullPath(targetDirectory) + Path.DirectorySeparatorChar;
            var budget = new ExtractionBudget(maxBytes, maxEntries);
            foreach (var entry in archive.Entries)
            {
                budget.CountEntry();
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var output = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The ebook archive contains an unsafe path.");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var input = entry.Open();
                using var destination = File.Create(output);
                budget.Copy(input, destination);
            }
        }

        var containerPath = Path.Combine(targetDirectory, "META-INF", "container.xml");
        if (File.Exists(containerPath))
            return ReadEpub(targetDirectory, containerPath);
        var fb2 = Directory.EnumerateFiles(targetDirectory, "*.fb2", SearchOption.AllDirectories).FirstOrDefault();
        if (fb2 != null)
            return ReadFb2(File.ReadAllBytes(fb2), targetDirectory);
        return ReadComicArchive(targetDirectory);
    }

    private static void ExtractArchive(byte[] bytes, string targetDirectory, ExtractionBudget budget)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = ReaderFactory.OpenReader(stream);
        var root = Path.GetFullPath(targetDirectory) + Path.DirectorySeparatorChar;
        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry;
            budget.CountEntry();
            if (entry.IsDirectory) continue;
            var relative = (entry.Key ?? "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var output = Path.GetFullPath(Path.Combine(targetDirectory, relative));
            if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The ebook archive contains an unsafe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var input = reader.OpenEntryStream();
            using var destination = File.Create(output);
            budget.Copy(input, destination);
        }
    }

    private static ExtractedEbook ReadFb2(byte[] bytes, string root)
    {
        using var stream = new MemoryStream(bytes);
        var document = XDocument.Load(stream);
        var imageDirectory = Path.Combine(root, "fb2-images");
        Directory.CreateDirectory(imageDirectory);
        var imagePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binary in document.Descendants().Where(e => e.Name.LocalName == "binary"))
        {
            var id = binary.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
            if (string.IsNullOrWhiteSpace(id)) continue;
            try
            {
                var data = Convert.FromBase64String(string.Concat(binary.Value.Where(ch => !char.IsWhiteSpace(ch))));
                var contentType = binary.Attributes().FirstOrDefault(a => a.Name.LocalName == "content-type")?.Value ?? "image/jpeg";
                var extension = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png" : contentType.Contains("gif", StringComparison.OrdinalIgnoreCase) ? ".gif" : ".jpg";
                var file = Path.Combine(imageDirectory, SafeFileComponent(id) + extension);
                File.WriteAllBytes(file, data);
                imagePaths[id] = Path.GetRelativePath(root, file).Replace('\\', '/');
            }
            catch (FormatException) { }
        }

        var bodies = document.Descendants().Where(e => e.Name.LocalName == "body" &&
            !string.Equals(e.Attributes().FirstOrDefault(a => a.Name.LocalName == "name")?.Value, "notes", StringComparison.OrdinalIgnoreCase)).ToList();
        var sections = bodies.SelectMany(body => body.Elements().Where(e => e.Name.LocalName == "section")).ToList();
        if (sections.Count == 0) sections = bodies.Cast<XElement>().ToList();
        var chapters = new List<EbookReaderChapter>();
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            var title = string.Join(" ", section.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.DescendantsAndSelf().Where(e => e.Name.LocalName == "p").Select(e => e.Value.Trim()) ?? []);
            if (string.IsNullOrWhiteSpace(title)) title = $"Chapter {index + 1}";
            var html = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"></head><body>");
            RenderFb2Element(section, html, imagePaths);
            html.Append("</body></html>");
            var relative = $"fb2-chapter-{index + 1:D4}.html";
            File.WriteAllText(Path.Combine(root, relative), html.ToString(), Encoding.UTF8);
            chapters.Add(new EbookReaderChapter(title, relative));
        }
        if (chapters.Count == 0) throw new InvalidDataException("The FB2 document contains no readable sections.");
        return new ExtractedEbook(root, chapters, "fb2");
    }

    private static void RenderFb2Element(XElement element, StringBuilder html, IReadOnlyDictionary<string, string> images)
    {
        var name = element.Name.LocalName;
        if (name == "binary") return;
        if (name == "image")
        {
            var href = element.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value?.TrimStart('#');
            if (href != null && images.TryGetValue(href, out var path)) html.Append("<img src=\"").Append(SecurityElement.Escape(path)).Append("\" alt=\"\">");
            return;
        }
        var tag = name switch { "title" => "h2", "subtitle" => "h3", "p" => "p", "emphasis" => "em", "strong" => "strong", "epigraph" => "blockquote", "poem" => "blockquote", "stanza" => "div", "text-author" => "cite", "empty-line" => "br", _ => "div" };
        if (name == "empty-line") { html.Append("<br>"); return; }
        html.Append('<').Append(tag).Append('>');
        foreach (var node in element.Nodes())
        {
            if (node is XText text) html.Append(SecurityElement.Escape(text.Value));
            else if (node is XElement child) RenderFb2Element(child, html, images);
        }
        html.Append("</").Append(tag).Append('>');
    }

    private static string SafeFileComponent(string value) => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private static ExtractedEbook ReadEpub(string root, string containerPath)
    {
        var container = XDocument.Load(containerPath);
        var packageRelative = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value
            ?? throw new InvalidDataException("The EPUB package manifest is missing.");
        var packagePath = SafeCombine(root, packageRelative);
        var package = XDocument.Load(packagePath);
        var packageDirectory = Path.GetDirectoryName(packagePath)!;
        var manifest = package.Descendants().Where(e => e.Name.LocalName == "item")
            .Select(e => new
            {
                Id = e.Attribute("id")?.Value,
                Href = e.Attribute("href")?.Value,
                Type = e.Attribute("media-type")?.Value,
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Href))
            .ToDictionary(x => x.Id!, x => x, StringComparer.Ordinal);
        var chapters = new List<EbookReaderChapter>();
        foreach (var itemRef in package.Descendants().Where(e => e.Name.LocalName == "itemref"))
        {
            var id = itemRef.Attribute("idref")?.Value;
            if (id == null || !manifest.TryGetValue(id, out var item)) continue;
            if (item.Type is not ("application/xhtml+xml" or "text/html")) continue;
            var decoded = Uri.UnescapeDataString(item.Href!.Split('#')[0]);
            var full = SafeCombine(packageDirectory, decoded);
            var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
            var title = Path.GetFileNameWithoutExtension(decoded).Replace('_', ' ').Replace('-', ' ');
            chapters.Add(new EbookReaderChapter(string.IsNullOrWhiteSpace(title) ? $"Chapter {chapters.Count + 1}" : title, relative));
        }
        if (chapters.Count == 0)
            throw new InvalidDataException("The EPUB contains no readable spine entries.");
        return new ExtractedEbook(root, chapters, "epub");
    }

    private static ExtractedEbook ReadComicArchive(string root)
    {
        var chapters = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select((path, index) => new EbookReaderChapter($"Page {index + 1}", Path.GetRelativePath(root, path).Replace('\\', '/')))
            .ToList();
        if (chapters.Count == 0)
            throw new InvalidDataException("The comic archive contains no readable images.");
        return new ExtractedEbook(root, chapters, "cbz");
    }

    private static string SafeCombine(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The ebook references an unsafe path.");
        return path;
    }

    private sealed class ExtractionBudget(long maxBytes, int maxEntries)
    {
        private readonly byte[] _buffer = new byte[81920];
        private long _bytes;
        private int _entries;

        public void CountEntry()
        {
            if (++_entries > maxEntries)
                throw new InvalidDataException("The ebook archive contains too many files.");
        }

        // Count actual decompressed bytes; archive headers declare sizes the
        // package author controls.
        public void Copy(Stream input, Stream destination)
        {
            int read;
            while ((read = input.Read(_buffer, 0, _buffer.Length)) > 0)
            {
                _bytes += read;
                if (_bytes > maxBytes)
                    throw new InvalidDataException("The ebook archive expands beyond the supported size.");
                destination.Write(_buffer, 0, read);
            }
        }
    }

    private static bool IsZip(byte[] bytes) => bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B;
    private static bool IsRar(byte[] bytes) => bytes.Length >= 7 && bytes[0] == 0x52 && bytes[1] == 0x61 && bytes[2] == 0x72 && bytes[3] == 0x21;
    private static bool IsPdf(byte[] bytes) => bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46;
}

/// <summary>
/// Per-open scratch directories with OS-held ownership leases. Only marked,
/// unlocked directories can be pruned; legacy directories have no ownership
/// proof and are retained rather than risking another reader's files.
/// </summary>
internal static class EbookReaderCache
{
    internal const string RootEnvironmentVariable = "SILOPLAYER_READER_CACHE_DIRECTORY";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, FileStream> Active = new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultRoot => Environment.GetEnvironmentVariable(RootEnvironmentVariable) is { Length: > 0 } isolated
        ? Path.GetFullPath(isolated)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "reader-cache");

    public static string Allocate(string root)
    {
        root = Path.GetFullPath(root);
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(root, id);
        var leaseDirectory = Directory.CreateDirectory(Path.Combine(root, ".leases")).FullName;
        // The lease lives outside the extraction directory, which Extract clears.
        var leasePath = Path.Combine(leaseDirectory, id + ".lease");
        var lease = new FileStream(leasePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try
        {
            Directory.CreateDirectory(directory);
            lock (Gate) Active.Add(directory, lease);
            return directory;
        }
        catch
        {
            lease.Dispose();
            TryPruneLease(root, leasePath);
            throw;
        }
    }

    public static void Release(string directory)
    {
        directory = Path.GetFullPath(directory);
        lock (Gate)
        {
            if (!Active.Remove(directory, out var lease)) return;
            lease.Dispose();
        }
        var root = Path.GetDirectoryName(directory)!;
        TryPruneLease(root, Path.Combine(root, ".leases", Path.GetFileName(directory) + ".lease"));
    }

    public static async Task ReleaseAfterAsync(string directory, Task writer)
    {
        try { await writer.ConfigureAwait(false); }
        catch { /* Failed or canceled materialization still owns files until it ends. */ }
        finally { Release(directory); }
    }

    public static void PruneInactive(string root)
    {
        root = Path.GetFullPath(root);
        string[] leases;
        try { leases = Directory.GetFiles(Path.Combine(root, ".leases"), "*.lease"); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        foreach (var lease in leases) TryPruneLease(root, lease);
    }

    private static void TryPruneLease(string root, string leasePath)
    {
        var id = Path.GetFileNameWithoutExtension(leasePath);
        if (!Guid.TryParseExact(id, "N", out _)) return;
        var directory = Path.GetFullPath(Path.Combine(root, id));
        var rootPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            // Exclusive open fails while any process still owns this reader.
            using (var lease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (Directory.Exists(directory))
                {
                    var isLink = (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;
                    Directory.Delete(directory, recursive: !isLink);
                }
            }
            File.Delete(leasePath);
        }
        catch (IOException) { /* Owner active or WebView still holds a file; retry later. */ }
        catch (UnauthorizedAccessException) { }
    }
}
