using System.Text.Json.Serialization;
using SiloPlayer.Core.Json;

namespace SiloPlayer.Core.Models.Catalog;

public class Person
{
    // B33 + B59: Person IDs are strings end-to-end (cast/crew person_id can be
    // non-numeric for third-party-provider records). BUT — the /people/{id} server
    // route returns `id` as a JSON number (int64), not a string. Without the
    // StringOrNumber converter, GET /people/{id} deserialization throws. The
    // converter reads numeric id tokens, formats as decimal string, and the rest
    // of the pipeline stays string-uniform.
    [JsonConverter(typeof(StringOrNumberJsonConverter))]
    public string Id { get; set; } = "";
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

// NOTE: GET /people returns a bare JSON array of Person objects, not a wrapper object.
// Deserialized directly as List<Person> in PeopleApi.
