using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

public static class HistoryImportScope
{
    public static bool CanTargetOthers(UserInfo? user, Profile? actor)
        => user?.Role == "admin" || actor?.IsPrimary == true;
    public static string Target(UserInfo? user, Profile? actor, string selected)
        => CanTargetOthers(user, actor) ? string.IsNullOrWhiteSpace(selected) ? actor?.Id ?? "" : selected : actor?.Id ?? "";
    public static string Label(string id, IEnumerable<Profile> profiles)
        => profiles.FirstOrDefault(p => p.Id == id)?.Name ?? (string.IsNullOrEmpty(id) ? "Unknown profile" : $"Profile {id}");
}
