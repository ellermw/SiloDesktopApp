namespace ContinuumPlayer.Core.Models.Admin;

public class UnmatchedFile
{
    public int Id { get; set; }
    public int MediaFolderId { get; set; }
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
    public string Container { get; set; } = "";
}

// NOTE: Server returns unmatched items as a bare array with unmatchedItemResponse fields,
// which differ from this model. See AdminApi.GetUnmatchedItemsAsync.
