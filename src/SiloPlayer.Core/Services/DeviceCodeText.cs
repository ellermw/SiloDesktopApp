namespace SiloPlayer.Core.Services;

public static class DeviceCodeText
{
    private static string Compact(string? value) => new((value ?? "").ToUpperInvariant().Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9').Take(8).ToArray());
    public static string Format(string? value) { var clean = Compact(value); return clean.Length <= 4 ? clean : clean[..4] + " " + clean[4..]; }
    public static string Spoken(string? value) => string.Join(" ", Compact(value).ToCharArray());
}
