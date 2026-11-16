using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SiloPlayer.Core.Services.Admin.Users;

public sealed class AdminUserAvatarImages : IDisposable
{
    public const int MaximumBytes = 256 * 1024;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public AdminUserAvatarImages() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }), true) { }
    public AdminUserAvatarImages(HttpClient http) : this(http, false) { }
    private AdminUserAvatarImages(HttpClient http, bool ownsClient) { _http = http; _ownsClient = ownsClient; }

    public static Uri PreviewUri(string reference, Uri server)
    {
        if (!server.IsAbsoluteUri || server.Scheme is not ("http" or "https") || server.UserInfo.Length != 0)
            throw new ArgumentException("A server origin is required for preset previews.");
        var preset = reference.StartsWith("preset:", StringComparison.Ordinal) ? reference[7..] : reference;
        if (Enumerable.Range(1, 8).Any(index => preset == "avatar-" + index))
            return new Uri(server, "/profile-avatars/" + preset + ".svg");
        var parts = preset.Split(':');
        if (parts.Length != 3 || parts[0] != "dicebear" || !AdminUserAvatars.Styles.Contains(parts[1]) || string.IsNullOrWhiteSpace(parts[2]) || parts[2].Length > 256)
            throw new ArgumentException("Only a known profile preset can be previewed.");
        return new Uri("https://api.dicebear.com/9.x/" + parts[1] + "/svg?seed=" + Uri.EscapeDataString(parts[2]) + "&size=128&radius=24&backgroundType=gradientLinear");
    }

    public async Task<byte[]> ReadAsync(string reference, Uri server, CancellationToken ct = default)
    {
        var uri = PreviewUri(reference, server);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(12));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("image/svg+xml");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumBytes) throw new InvalidDataException("Preset preview exceeded its byte bound.");
        await using var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(), linked.Token).ConfigureAwait(false);
            if (count == 0) break;
            if (bytes.Length + count > MaximumBytes) throw new InvalidDataException("Preset preview exceeded its byte bound.");
            bytes.Write(buffer, 0, count);
        }
        linked.Token.ThrowIfCancellationRequested();
        var result = bytes.ToArray();
        ValidateSvg(result);
        return result;
    }

    private static void ValidateSvg(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes });
            var document = XDocument.Load(reader);
            if (document.Root?.Name != XName.Get("svg", "http://www.w3.org/2000/svg")) throw new InvalidDataException("Invalid preset SVG.");
            foreach (var element in document.Root.DescendantsAndSelf())
            {
                if (element.Name.LocalName is "script" or "foreignObject" or "image" or "style") throw new InvalidDataException("External preset resources are not supported.");
                foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
                {
                    if (attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) || attribute.Name.LocalName == "src" ||
                        attribute.Name.LocalName == "href" && !attribute.Value.StartsWith('#')) throw new InvalidDataException("External preset resources are not supported.");
                    var localOnly = Regex.Replace(attribute.Value, @"url\(\s*['"" ]?#[a-zA-Z_][\w:.-]*['"" ]?\s*\)", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (localOnly.Contains("url(", StringComparison.OrdinalIgnoreCase) || localOnly.Contains("@import", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("External preset resources are not supported.");
                }
            }
        }
        catch (XmlException exception) { throw new InvalidDataException("Invalid preset SVG.", exception); }
    }
    public void Dispose() { if (_ownsClient) _http.Dispose(); }
}
