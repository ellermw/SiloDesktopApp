namespace ContinuumPlayer.Core.Models.Admin;

public class ScanRequest
{
    public int? LibraryId { get; set; }
    public string? Path { get; set; }
}

public class ScanResponse
{
    public string Status { get; set; } = "";
    public string Mode { get; set; } = "";
    public int LibraryId { get; set; }
}

public class CreateLibraryRequest
{
    public List<string> Paths { get; set; } = [];
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public bool? Enabled { get; set; }
}

public class UpdateLibraryRequest
{
    public List<string>? Paths { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public bool? Enabled { get; set; }
}
