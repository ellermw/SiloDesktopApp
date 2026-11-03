using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SettingsApi
{
    internal LibraryPageStateStore.Session LibraryStateSession { get; } = new();
    public LibraryPageStateStore LibraryPageState => new(this);
    public LocalizedMediaReads LocalizedReads => client.LocalizedReads;
    public SettingsRequestContext CaptureSettingsContext() => client.CaptureSettingsContext();
    public bool IsCurrentSettingsContext(SettingsRequestContext owner) => CaptureSettingsContext() == owner;

    internal Task WriteLibraryStateDocumentAsync(SettingsRequestContext owner, object value, CancellationToken ct)
    {
        if (!IsCurrentSettingsContext(owner)) throw new OperationCanceledException("Library settings owner changed.", ct);
        return client.SendRequestAsync<ContractEffectiveSettingEntry>(owner.Request, HttpMethod.Put,
            BuildContractSettingPath("ui.library_page_state", "profile_device", owner.DeviceId, owner.Request.ProfileId),
            new Dictionary<string, object?> { ["value"] = value }, ct);
    }

    private async Task NotifyLibrarySettingWriteAsync(Func<Task> write, string? key)
    {
        var owner = CaptureSettingsContext();
        await write();
        if (IsCurrentSettingsContext(owner) && (key == null || LibraryPageStateStore.IsRelevantKey(key)))
            LibraryPageState.Invalidate();
    }
}
