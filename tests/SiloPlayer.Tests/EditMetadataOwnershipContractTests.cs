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
