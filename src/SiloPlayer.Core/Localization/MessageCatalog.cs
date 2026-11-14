using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace SiloPlayer.Core.Localization;

/// <summary>A language the desktop ships an interface catalog for.</summary>
public sealed record AppLanguageInfo(string Tag, string Endonym, bool IsRightToLeft);

/// <summary>
/// The offline interface catalogs generated from silo-translations (see
/// Generated/catalog-manifest.json). Each language loads once, on first use,
/// into an immutable message table.
/// </summary>
public sealed class MessageCatalog
{
    public const string ResourcePrefix = "SiloPlayer.Core.Localization.Generated.";
    private static readonly ConcurrentDictionary<string, Lazy<MessageCatalog>> Loaded = new(StringComparer.Ordinal);
    private static readonly Lazy<CatalogManifest> ManifestValue = new(ReadManifest);
    private readonly FrozenDictionary<string, MessagePattern> _messages;

    private MessageCatalog(string language, FrozenDictionary<string, MessagePattern> messages)
    {
        Language = language;
        _messages = messages;
    }

    public string Language { get; }
    public int Count => _messages.Count;

    /// <summary>The release and source the generated catalogs came from, and their checksums.</summary>
    public static CatalogManifest Manifest => ManifestValue.Value;

    /// <summary>Shipped languages in catalog order; English first.</summary>
    public static IReadOnlyList<AppLanguageInfo> ShippedLanguages => Manifest.Languages;

    public bool TryGet(string key, out MessagePattern pattern) => _messages.TryGetValue(key, out pattern!);

    public bool Contains(string key) => _messages.ContainsKey(key);

    public static MessageCatalog English => Load("en");

    /// <summary>The catalog for a shipped language tag (exact, as listed in the manifest).</summary>
    public static MessageCatalog Load(string language)
    {
        if (!Manifest.Languages.Any(item => item.Tag == language))
            throw new ArgumentOutOfRangeException(nameof(language), language, "Not a shipped catalog language.");
        return Loaded.GetOrAdd(language, tag => new Lazy<MessageCatalog>(() => Read(tag), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static MessageCatalog Read(string language)
    {
        var bytes = ReadResource(language + ".json");
        var entry = Manifest.Entries.Single(item => item.Tag == language);
        // The catalog is generated, never edited: a resource whose bytes differ
        // from the generator's record is refused rather than shown.
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, entry.OutputSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"The {language} interface catalog does not match its generated checksum.");
        using var document = JsonDocument.Parse(bytes);
        var messages = new Dictionary<string, MessagePattern>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            messages[property.Name] = MessagePattern.Parse(property.Value);
        return new MessageCatalog(language, messages.ToFrozenDictionary(StringComparer.Ordinal));
    }

    internal static byte[] ReadResource(string name)
    {
        var assembly = typeof(MessageCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new FileNotFoundException($"Missing embedded interface catalog resource {name}.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static CatalogManifest ReadManifest()
    {
        using var document = JsonDocument.Parse(ReadResource("catalog-manifest.json"));
        var root = document.RootElement;
        var entries = root.GetProperty("languages").EnumerateArray().Select(item => new CatalogManifestEntry(
            item.GetProperty("tag").GetString()!,
            item.GetProperty("name").GetString()!,
            item.GetProperty("dir").GetString() == "rtl",
            item.GetProperty("messages").GetInt32(),
            item.GetProperty("output_sha256").GetString()!)).ToArray();
        return new CatalogManifest(
            root.GetProperty("catalog_release").GetString()!,
            root.GetProperty("source_commit").GetString()!,
            entries,
            entries.Select(entry => new AppLanguageInfo(entry.Tag, entry.Endonym, entry.IsRightToLeft)).ToArray());
    }
}

public sealed record CatalogManifestEntry(string Tag, string Endonym, bool IsRightToLeft, int Messages, string OutputSha256);

public sealed record CatalogManifest(string Release, string SourceCommit, IReadOnlyList<CatalogManifestEntry> Entries,
    IReadOnlyList<AppLanguageInfo> Languages);
