namespace ContinuumPlayer.Core.Models.Admin;

public class LibraryProviderChainEntry
{
    public int ProviderId { get; set; }
    public string ProviderSlug { get; set; } = "";
    public int Priority { get; set; }
    public bool Enabled { get; set; }
}

public class LibraryProviderChainResponse
{
    public Dictionary<string, List<LibraryProviderChainEntry>> Levels { get; set; } = new();
}

public class SetLibraryChainEntry
{
    public int ProviderId { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; }
}

public class SetLibraryChainRequest
{
    public Dictionary<string, List<SetLibraryChainEntry>> Levels { get; set; } = new();
}
