using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Models.Updates;

/// <summary>Semantic display/precedence, separate from the immutable bootstrap's numeric identity.</summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private static readonly Regex Syntax = new(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z", RegexOptions.CultureInvariant);
    private ReleaseVersion(string text, int major, int minor, int patch, string prerelease, string metadata)
        => (Text, Major, Minor, Patch, Prerelease, BuildMetadata) = (text, major, minor, patch, prerelease, metadata);
    public string Text { get; }
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string Prerelease { get; }
    public string BuildMetadata { get; }
    public bool IsPrerelease => Prerelease.Length != 0;
    public string Designation => IsPrerelease ? "Beta" : "Stable";

    public static bool TryParse(string? text, [NotNullWhen(true)] out ReleaseVersion? result)
    {
        result = null;
        if (text is null || text.Length is 0 or > 128) return false;
        var match = Syntax.Match(text);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch)) return false;
        var pre = match.Groups[4].Value;
        if (pre.Split('.').Any(id => id.Length > 1 && id[0] == '0' && id.All(char.IsAsciiDigit))) return false;
        result = new(text, major, minor, patch, pre, match.Groups[5].Value);
        return true;
    }

    public static ReleaseVersion Parse(string text) => TryParse(text, out var version)
        ? version : throw new InvalidDataException("Invalid semantic release version.");

    public static ReleaseVersion FromAssembly(Assembly assembly)
    {
        var version = FromBuild(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidDataException("The installed release identity is missing."), assembly.GetName().Version!);
        if (!Version.TryParse(assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version, out var file)
            || file != version.GetWindowsVersion()) throw new InvalidDataException("The installed file version disagrees with its release identity.");
        return version;
    }

    public static ReleaseVersion FromBuild(string informational, Version numeric)
    {
        _ = Parse(informational);
        var version = Parse(informational.Split('+')[0]);
        if (version.GetWindowsVersion() != numeric) throw new InvalidDataException("Semantic and numeric installed versions disagree.");
        return version;
    }

    public Version GetWindowsVersion()
    {
        if (Major > 65534 || Minor > 65534 || Patch > 65534 || BuildMetadata.Length != 0)
            throw new InvalidDataException("The release cannot use the supported Windows version contract.");
        if (!IsPrerelease) return new(Major, Minor, Patch, 0);
        var parts = Prerelease.Split('.');
        if (Patch < 1 || parts.Length != 2 || parts[0] != "beta" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence is < 1 or > 65534)
            throw new InvalidDataException("A package prerelease must use beta.1 through beta.65534 and a positive patch.");
        // Reserve the preceding patch's nonzero revisions, leaving stable P.0 greater
        // than every beta for P without changing the installed Protocol1 bootstrap.
        return new(Major, Minor, Patch - 1, sequence);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var core = new Version(Major, Minor, Patch).CompareTo(new Version(other.Major, other.Minor, other.Patch));
        if (core != 0) return core;
        if (!IsPrerelease || !other.IsPrerelease) return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;
        var left = Prerelease.Split('.'); var right = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var a = left[i]; var b = right[i];
            var an = a.All(char.IsAsciiDigit); var bn = b.All(char.IsAsciiDigit);
            var order = an && bn ? a.Length.CompareTo(b.Length) : an == bn ? 0 : an ? -1 : 1;
            if (order == 0) order = string.CompareOrdinal(a, b);
            if (order != 0) return order;
        }
        return left.Length.CompareTo(right.Length);
    }
    public override string ToString() => Text;
}
