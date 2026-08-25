namespace SiloPlayer.Core.Services;

public sealed record AdminNodeToggleResult(
    bool UpdateSucceeded,
    bool Enabled,
    string? UpdateError,
    string? ReloadError);

public static class AdminNodeToggleOperation
{
    public static async Task<AdminNodeToggleResult> ExecuteAsync(
        bool currentEnabled,
        Func<bool, Task> updateAsync,
        Func<Task> reloadAsync)
    {
        ArgumentNullException.ThrowIfNull(updateAsync);
        ArgumentNullException.ThrowIfNull(reloadAsync);

        var requestedEnabled = !currentEnabled;
        try
        {
            await updateAsync(requestedEnabled);
        }
        catch (Exception ex)
        {
            return new AdminNodeToggleResult(
                UpdateSucceeded: false,
                Enabled: currentEnabled,
                UpdateError: ex.Message,
                ReloadError: null);
        }

        try
        {
            await reloadAsync();
            return new AdminNodeToggleResult(
                UpdateSucceeded: true,
                Enabled: requestedEnabled,
                UpdateError: null,
                ReloadError: null);
        }
        catch (Exception ex)
        {
            return new AdminNodeToggleResult(
                UpdateSucceeded: true,
                Enabled: requestedEnabled,
                UpdateError: null,
                ReloadError: ex.Message);
        }
    }
}
