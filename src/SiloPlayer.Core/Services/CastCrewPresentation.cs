using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Services;

public sealed record CreditGroup(string Label, IReadOnlyList<CastMember> Members);

public static class CastCrewPresentation
{
    // Only displayed lead credits exclude writers; series creators have no cap.
    public static IReadOnlyList<CreditGroup> BuildGroups(string type, IEnumerable<CrewMember> crew, IEnumerable<CastMember> cast)
    {
        var credits = crew.ToList();
        var creators = credits.Where(c => c.Job == "Creator").ToList();
        var role = type == "series" && creators.Count > 0 ? "Creator" : "Director";
        var leads = Unique(credits.Where(c => c.Job == role)).Take(role == "Creator" ? int.MaxValue : 2).ToList();
        var shown = leads.Select(Key).ToHashSet(StringComparer.Ordinal);
        var writers = Unique(credits.Where(c => (c.Job is "Writer" or "Screenplay") && !shown.Contains(Key(c)))).Take(3).ToList();
        var groups = new List<CreditGroup>();
        Add(role, leads, role);
        Add("Writers", writers, "Writer");
        var actors = cast.OrderBy(c => c.Order).Take(12).ToList();
        if (actors.Count > 0) groups.Add(new("Cast", actors));
        return groups;

        void Add(string label, List<CrewMember> members, string caption)
        {
            if (members.Count > 0) groups.Add(new(label, members.Select(m => new CastMember {
                PersonId = m.PersonId, Name = m.Name, Character = caption, PhotoUrl = m.PhotoUrl, PhotoThumbhash = m.PhotoThumbhash
            }).ToList()));
        }
    }
    private static string Key(CrewMember c) => string.IsNullOrEmpty(c.PersonId) ? c.Name : c.PersonId;
    private static IEnumerable<CrewMember> Unique(IEnumerable<CrewMember> members) => members.DistinctBy(Key, StringComparer.Ordinal);
}
