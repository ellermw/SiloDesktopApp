namespace SiloPlayer.Core.Services;

/// <summary>Separates route presentation from the saved browsing preference.</summary>
public sealed class SidebarPresentationState
{
    public bool BrowsingOpen { get; set; } = true;
    public bool IsDetail { get; private set; }
    private bool? _detailOverride;
    public bool IsOpen => IsDetail ? _detailOverride ?? false : BrowsingOpen;
    public void Navigate(bool isDetail)
    {
        if (IsDetail != isDetail) _detailOverride = null;
        IsDetail = isDetail;
    }
    public bool Toggle(bool open)
    {
        if (IsDetail) { _detailOverride = open; return false; }
        BrowsingOpen = open;
        return true;
    }
}
