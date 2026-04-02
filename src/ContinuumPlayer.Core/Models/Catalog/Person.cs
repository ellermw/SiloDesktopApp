namespace ContinuumPlayer.Core.Models.Catalog;

public class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Bio { get; set; }
    public string? BirthDate { get; set; }
    public string? DeathDate { get; set; }
    public string? Birthplace { get; set; }
    public string? Homepage { get; set; }
    public string? PhotoUrl { get; set; }
    public string? PhotoThumbhash { get; set; }
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? TvdbId { get; set; }
    public string? PlexGuid { get; set; }
}

public class PeopleResponse
{
    public List<Person> Items { get; set; } = [];
    public int Total { get; set; }
}
