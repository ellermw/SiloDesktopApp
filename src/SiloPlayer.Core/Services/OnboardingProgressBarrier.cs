namespace SiloPlayer.Core.Services;

/// <summary>A local tour transition is committed only after the server accepted it.</summary>
public sealed class OnboardingProgressBarrier
{
    public bool IsSaving { get; private set; }
    public string? Error { get; private set; }
    public async Task<bool> SaveAsync(Func<Task> save)
    {
        if (IsSaving) return false;
        IsSaving = true;
        Error = null;
        try { await save(); return true; }
        catch (Exception ex) { Error = ex.Message; return false; }
        finally { IsSaving = false; }
    }
}
