namespace ContinuumPlayer.Core.Models.Admin;

public class UnmatchedFile
{
    public int Id { get; set; }
    public int MediaFolderId { get; set; }
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
    public string Container { get; set; } = "";
}

public class UnmatchedFilesResponse
{
    public List<UnmatchedFile> Files { get; set; } = [];
}
