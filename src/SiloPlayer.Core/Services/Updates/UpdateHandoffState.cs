namespace SiloPlayer.Core.Services.Updates;

/// <summary>The final close is a separate consent boundary from staging files.</summary>
public sealed class UpdateHandoffState
{
    private bool visible, staged, closing, activated;
    public void Show(){if(activated)throw new InvalidOperationException(); visible=true;}
    public void Staged(){if(!visible || activated)throw new InvalidOperationException();staged=true;}
    public void ConfirmClose(){if(!staged || activated)throw new InvalidOperationException();closing=true;}
    public bool CanActivate(bool parentExited)=>visible && staged && closing && !activated && parentExited;
    public void Activated(bool parentExited){if(!CanActivate(parentExited))throw new InvalidOperationException();activated=true;}
}
