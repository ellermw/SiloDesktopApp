using System.Text.Json.Serialization;
using SiloPlayer.Core.Json;

namespace SiloPlayer.Core.Models.Catalog;

public class PersonRefreshResponse
{
    public string Status { get; set; } = "";
    [JsonConverter(typeof(StringOrNumberJsonConverter))]
    public string PersonId { get; set; } = "";
}
