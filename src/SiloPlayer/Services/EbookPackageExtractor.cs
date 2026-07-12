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
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".svg" };

    public static ExtractedEbook Extract(byte[] bytes, string targetDirectory, string declaredFormat)
    {
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
            ExtractArchive(bytes, targetDirectory);
            return ReadComicArchive(targetDirectory);
        }
        if (!IsZip(bytes))
            throw new InvalidDataException($"The {declaredFormat.ToUpperInvariant()} book file is not a recognized or supported package.");

        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            var root = Path.GetFullPath(targetDirectory) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var output = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The ebook archive contains an unsafe path.");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var input = entry.Open();
                using var destination = File.Create(output);
                input.CopyTo(destination);
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

    private static void ExtractArchive(byte[] bytes, string targetDirectory)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = ReaderFactory.OpenReader(stream);
        var root = Path.GetFullPath(targetDirectory) + Path.DirectorySeparatorChar;
        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry;
            if (entry.IsDirectory) continue;
            var relative = (entry.Key ?? "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var output = Path.GetFullPath(Path.Combine(targetDirectory, relative));
            if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The ebook archive contains an unsafe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var input = reader.OpenEntryStream();
            using var destination = File.Create(output);
            input.CopyTo(destination);
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
        return new ExtractedEbook(root, chapters, "epub");
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

    private static bool IsZip(byte[] bytes) => bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B;
    private static bool IsRar(byte[] bytes) => bytes.Length >= 7 && bytes[0] == 0x52 && bytes[1] == 0x61 && bytes[2] == 0x72 && bytes[3] == 0x21;
    private static bool IsPdf(byte[] bytes) => bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46;
}
