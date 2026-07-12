namespace SiloPlayer.Core.Models.Admin;

public class StreamNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Healthy { get; set; }
    public string? LastHealthCheck { get; set; }
    public int ActiveJobs { get; set; }
    public string? Group { get; set; }
    public int? MaxJobs { get; set; }
    public int? MaxBandwidthKbps { get; set; }
    public int EgressKbps { get; set; }
    public string CreatedAt { get; set; } = "";
}

public class CreateNodeRequest
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Group { get; set; }
    public int? MaxJobs { get; set; }
    public int? MaxBandwidthKbps { get; set; }
}

public class UpdateNodeRequest
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public bool? Enabled { get; set; }
    public string? Group { get; set; }
    public int? MaxJobs { get; set; }
    public int? MaxBandwidthKbps { get; set; }
}

public class CheckNodeResponse
{
    public bool Healthy { get; set; }
    public string? Message { get; set; }
    public int ActiveJobs { get; set; }
    public int EgressKbps { get; set; }
}
