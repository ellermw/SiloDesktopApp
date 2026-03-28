namespace ContinuumPlayer.Core.Models.Admin;

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
}

public class CreateNodeRequest
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Type { get; set; } = "";
}

public class CheckNodeResponse
{
    public bool Healthy { get; set; }
    public string? Message { get; set; }
}
