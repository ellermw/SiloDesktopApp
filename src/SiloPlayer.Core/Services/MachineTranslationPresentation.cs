namespace SiloPlayer.Core.Services;

public static class MachineTranslationPresentation
{
    public static bool HasField(IEnumerable<string>? fields, string field = "overview")
        => fields?.Contains(field, StringComparer.Ordinal) == true;
}
