using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class EditMetadataOwnershipContractTests
{
    [Fact]
    public void DetailEditorFetchesCurrentItemUnderNavigationAndAuthorityBeforeOpening()
    {
        var code = Source("src/SiloPlayer/Views/ItemDetailPage.xaml.cs");
        var start = code.IndexOf("edit.Click += async", StringComparison.Ordinal);
        var end = code.IndexOf("MoreFlyout.Items.Add(edit)", start, StringComparison.Ordinal);
        var opening = code[start..end];
        var fetch = opening.IndexOf("GetItemDetailAsync(item.ContentId, loading.Token)", StringComparison.Ordinal);
        var guard = opening.IndexOf("if (!Current()) return;", fetch, StringComparison.Ordinal);
        var dialog = opening.IndexOf("EditMetadataDialog(detail, writeRevision)", StringComparison.Ordinal);
        Assert.True(fetch >= 0 && guard > fetch && dialog > guard);
        Assert.DoesNotContain("EditMetadataDialog(item)", opening);
        Assert.Contains("client.IsCurrentContext(authority)", opening);
        Assert.Contains("AuthorizationPolicy.CanCurateMetadata(auth)", opening);
        Assert.Contains("ReferenceEquals(_navigationCts, navigation)", opening);
        Assert.Contains("CreateLinkedTokenSource(navigation.Token)", opening);
        Assert.Contains("edit.IsEnabled = false", opening);
        Assert.Contains("MediaItemCardActions.Problem(ex)", opening);
        Assert.Contains("PendingWrites.Revision", opening);
    }

    [Fact]
    public void DetailReloadAfterEditingSurvivesTransportReconnectButNotIdentityOrNavigationRetirement()
    {
        var code = Source("src/SiloPlayer/Views/ItemDetailPage.xaml.cs");
        var start = code.IndexOf("edit.Click += async", StringComparison.Ordinal);
        var end = code.IndexOf("MoreFlyout.Items.Add(edit)", start, StringComparison.Ordinal);
        var opening = code[start..end];
        var show = opening.IndexOf("await dialog.ShowAsync()", StringComparison.Ordinal);
        Assert.True(opening.IndexOf("DetachRead();", StringComparison.Ordinal) < show);
        Assert.Contains("if (dialog.HasSaved && IdentityCurrent())", opening[show..]);
        Assert.Contains("if (IdentityCurrent()) UpdateUI()", opening[show..]);
        Assert.DoesNotContain("&& Current()", opening[show..]);
        var identityStart = opening.IndexOf("bool IdentityCurrent()", StringComparison.Ordinal);
        var identityEnd = opening.IndexOf("bool OwnerCurrent()", identityStart, StringComparison.Ordinal);
        var identityGuard = opening[identityStart..identityEnd];
        Assert.Contains("client.IsCurrentIdentityContext(identity)", identityGuard);
        Assert.Contains("ReferenceEquals(_navigationCts, navigation)", identityGuard);
        Assert.Contains("AccessGeneration == authority.AccessGeneration", identityGuard);
        Assert.Contains("AuthorizationPolicy.CanCurateMetadata(auth)", identityGuard);
        Assert.DoesNotContain("client.IsCurrentContext(authority)", identityGuard);
        Assert.DoesNotContain("loading.IsCancellationRequested", identityGuard);
    }

    [Fact]
    public void AcceptedWritePublishesBeforeReconciliationAndResetReportsUnknownCompletion()
    {
        var ownership = Source("src/SiloPlayer/Views/Dialogs/EditMetadataDialog.Ownership.cs");
        var write = ownership.IndexOf("await write(ct);", StringComparison.Ordinal);
        var publish = ownership.IndexOf("Changed();", write, StringComparison.Ordinal);
        var refetch = ownership.IndexOf("await catalog.GetItemDetailAsync", write, StringComparison.Ordinal);
        Assert.True(write >= 0 && publish > write && publish < refetch);
        var dialog = Source("src/SiloPlayer/Views/Dialogs/EditMetadataDialog.cs");
        Assert.Contains("completed = await _api.ResetToProviderAndWaitAsync", dialog);
        Assert.Contains("Metadata reset queued. Provider changes will appear when the job completes.", dialog);
        Assert.Contains("catch (OperationCanceledException) when (!CurrentOwner)", dialog);
    }

    [Fact]
    public void ReopenedFormAdoptsRefetchedValuesAndLocksWithoutRevertingProviderChanges()
    {
        var state = new MetadataEditState(new MediaItemDetail { Type = "movie", Title = "Old page", Overview = "Old overview", LockedFields = [0, 3] });
        var fresh = new MediaItemDetail { Type = "movie", Title = "Provider title", Overview = "Provider overview", LockedFields = [10, 99] };
        state.Rebase(fresh);
        Assert.Equal("Provider title", state.Fields["title"]);
        Assert.Equal("Provider overview", state.Fields["overview"]);
        Assert.Empty(state.Changes(fresh.LockedFields));
        state.Set("overview", "This opening's edit");
        var changes = state.Changes(fresh.LockedFields);
        Assert.DoesNotContain("title", changes.Keys);
        Assert.Equal(new[] { 1, 10, 99 }, (int[])changes["locked_fields"]!);
    }

    [Fact]
    public void RefetchPreservesEditsAndLockOverridesMadeInThisOpening()
    {
        var state = new MetadataEditState(new MediaItemDetail { Type = "movie", Title = "Old", Genres = ["Drama"], LockedFields = [3] });
        state.Set("title", "Draft"); state.SetLock(3, false);
        var fresh = new MediaItemDetail { Type = "movie", Title = "New", Genres = ["Comedy"], LockedFields = [3, 10] };
        state.Rebase(fresh);
        Assert.Equal("Draft", state.Fields["title"]);
        Assert.Equal(new[] { "Comedy" }, (string[])state.Fields["genres"]!);
        Assert.Equal(new[] { 0, 10 }, (int[])state.Changes(fresh.LockedFields)["locked_fields"]!);
    }

    [Fact]
    public void DialogUsesSingletonRegistryAndLifetimeOnlyForAdmissionAndObservation()
    {
        var dialog = Source("src/SiloPlayer/Views/Dialogs/EditMetadataDialog.cs");
        var ownership = Source("src/SiloPlayer/Views/Dialogs/EditMetadataDialog.Ownership.cs");
        Assert.Contains("if (WritePending || !CurrentOwner) return", dialog);
        Assert.Contains("CurrentOwner && !WritePending", dialog);
        Assert.Contains("_api.PendingWrites.WriteStarted += MetadataWriteStarted", dialog);
        Assert.Contains("_api.PendingWrites.WriteStarted -= MetadataWriteStarted", dialog);
        Assert.Contains("WaitForIdleAsync(_identityOwner, _item.ContentId, _lifetime.Token)", ownership);
        Assert.Contains("}, _lifetime.Token, authority.Token)", ownership);
        Assert.Contains("RebaseItem(refreshed)", ownership);
        Assert.Contains("revision != _api.PendingWrites.Revision", dialog);
        Assert.Contains("ResetToProviderAndWaitAsync", dialog);
        Assert.Contains("ApplyImageAsync(_item.ContentId, String(selected, \"original_url\"), selectedType, String(selected, \"provider_id\"), ct)", dialog);
    }

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine((directory ?? throw new InvalidOperationException("Repository root not found")).FullName, relative));
    }
}
