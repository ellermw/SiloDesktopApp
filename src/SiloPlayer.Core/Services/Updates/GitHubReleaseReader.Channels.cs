using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

internal sealed partial class GitHubReleaseReader
{
    private const string CatalogUrl = "https://api.github.com/repos/ellermw/SiloDesktopApp/releases?per_page=100&page=";
    private const int MaximumCatalogPages = 10, MaximumCatalogPageBytes = 2 * 1024 * 1024;

    internal static bool TryReleaseIdentity(string tag, bool prerelease, out Version numeric, out ReleaseVersion? semantic)
    {
        numeric = new(0, 0, 0, 0); semantic = null;
        if (tag.Length is 0 or > 64) return false;
        var text = tag.StartsWith('v') ? tag[1..] : tag;
        if (ReleaseVersion.TryParse(text, out var parsed))
        {
            if (parsed.IsPrerelease != prerelease) return false;
            try { numeric = parsed.GetWindowsVersion(); semantic = parsed; return true; }
            catch (InvalidDataException) { return false; }
        }
        // Retain previously supported four-part stable identities. New releases
        // use the canonical semantic contract; a four-part tag cannot claim Beta.
        return !prerelease && Regex.IsMatch(text, @"\A\d+\.\d+\.\d+\.\d+\z", RegexOptions.CultureInvariant)
            && Version.TryParse(text, out numeric!) && numeric.Major <= 65534 && numeric.Minor <= 65534
            && numeric.Build <= 65534 && numeric.Revision <= 65534;
    }

    internal async Task<ReleaseCheck> ReadForChannelAsync(Version installed, ReleaseVersion? semanticInstalled,
        AppUpdateChannel channel, ReleaseCheck? previous, CancellationToken ct)
    {
        if (previous is not null && previous.Channel != channel) previous = null;
        if (channel != AppUpdateChannel.Beta) return await ReadAsync(installed, previous, ct).ConfigureAwait(false);
        // Only GitHub Latest is approved stable authority. A non-Latest stable
        // catalog entry cannot bypass the separate orchestrator's promotion gate.
        var stable = await ReadAsync(installed, previous?.Stable, ct).ConfigureAwait(false);
        var current = semanticInstalled ?? ReleaseVersion.Parse($"{installed.Major}.{installed.Minor}.{Math.Max(0, installed.Build)}");
        var pages = new List<ReleaseCatalogPage>();
        AppRelease? best = stable.Release;
        for (var page = 1; page <= MaximumCatalogPages; page++)
        {
            var cached = previous?.Pages.ElementAtOrDefault(page - 1);
            var catalog = await ReadCatalogPageAsync(page, cached, ct).ConfigureAwait(false);
            pages.Add(catalog);
            using var document = JsonDocument.Parse(catalog.Metadata, new JsonDocumentOptions { MaxDepth = 24 });
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 100)
                throw new InvalidDataException("Invalid release catalog.");
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
                    || !entry.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.True
                    || !entry.TryGetProperty("tag_name", out var tagName) || tagName.ValueKind != JsonValueKind.String) continue;
                var tag = tagName.GetString() ?? "";
                if (!TryReleaseIdentity(tag, true, out var numeric, out var semantic) || semantic is null
                    || semantic.Major != current.Major || semantic.CompareTo(current) <= 0 || numeric <= Normalize(installed)) continue;
                if (best is not null && semantic.CompareTo(best.SemanticVersion ?? ReleaseVersion.Parse($"{best.Version.Major}.{best.Version.Minor}.{best.Version.Build}")) <= 0) continue;
                // Validate the actual selected release, including the mandatory
                // installer and all authority bounds. Invalid new assets fail closed
                // for that release only: it is ineligible, and the already-validated
                // Stable or a lower valid Beta remains the offer.
                try { best = Parse(Encoding.UTF8.GetBytes(entry.GetRawText()), installed, allowPrerelease: true) ?? best; }
                catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException) { }
            }
            if (!catalog.HasNext) return new(best, null) { Channel = AppUpdateChannel.Beta, Stable = stable, Pages = pages };
        }
        throw new InvalidDataException("The release catalog exceeds the supported update-check limit.");
    }

    private async Task<ReleaseCatalogPage> ReadCatalogPageAsync(int page, ReleaseCatalogPage? previous, CancellationToken ct)
    {
        if (http.DefaultRequestHeaders.Authorization is not null) throw new InvalidDataException("Update authority must be unauthenticated.");
        var uri = new Uri(CatalogUrl + page.ToString(CultureInfo.InvariantCulture));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("SiloDesktopApp-Updater");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        if (previous?.ETag is { } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri is { } final && final != uri) throw new InvalidDataException("Release catalog redirected.");
        if (response.StatusCode == HttpStatusCode.NotModified && previous?.ETag is not null) return previous;
        ThrowIfRateLimited(response);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("Could not read the release catalog.", null, response.StatusCode);
        if (response.Content.Headers.ContentLength > MaximumCatalogPageBytes) throw new InvalidDataException("Release catalog is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var bytes = await UpdateManifestValidation.ReadBoundedAsync(stream, MaximumCatalogPageBytes, ct).ConfigureAwait(false);
        var hasNext = false;
        if (response.Headers.TryGetValues("Link", out var links))
        {
            foreach (var link in links.SelectMany(value => value.Split(',')))
            {
                if (!link.Contains("rel=\"next\"", StringComparison.Ordinal)) continue;
                if (!IsNextPageHint(link.Trim(), page + 1)) throw new InvalidDataException("Untrusted release pagination.");
                hasNext = true;
            }
        }
        return new(bytes, response.Headers.ETag?.ToString(), hasNext);
    }

    private static bool IsNextPageHint(string link, int next)
    {
        if (link.Length > 1024) return false;
        var match = Regex.Match(link, @"\A<([^>]+)>;\s*rel=""next""\z", RegexOptions.CultureInvariant);
        if (!match.Success || !Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Host != "api.github.com" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        if (uri.AbsolutePath != "/repos/ellermw/SiloDesktopApp/releases"
            && !Regex.IsMatch(uri.AbsolutePath, @"\A/repositories/[1-9][0-9]*/releases\z", RegexOptions.CultureInvariant)) return false;
        var parameters = uri.Query.TrimStart('?').Split('&');
        // Link is only a bounded pagination hint. Requests always use CatalogUrl;
        // even GitHub's numeric-repository links never become request authority.
        return parameters.Length == 2 && parameters.Contains("per_page=100", StringComparer.Ordinal)
            && parameters.Contains("page=" + next.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);
    }
}
