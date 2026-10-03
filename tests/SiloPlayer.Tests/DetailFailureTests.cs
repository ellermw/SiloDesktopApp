using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class DetailFailureTests
{
    [Theory]
    [InlineData(404, "not_found", DetailFailureKind.NotFound)]
    [InlineData(403, "forbidden", DetailFailureKind.AccessDenied)]
    [InlineData(503, "service_unavailable", DetailFailureKind.Transient)]
    public async Task DetailDistinguishesMissingDeniedAndTemporaryFailureAndRetryGetsFreshContent(int status, string code, DetailFailureKind expected)
    {
        int calls = 0;
        var cache = new ItemDetailPrefetchCache((id, _) => ++calls == 1
            ? Task.FromException<MediaItemDetail>(new ApiException(code, "Sanitized fixture", status))
            : Task.FromResult(new MediaItemDetail { ContentId = id, Type = "movie", UserState = new(), UserData = new() }));
        var vm = new ItemDetailViewModel(null!, cache);
        await vm.LoadCommand.ExecuteAsync("fixture");
        Assert.Equal(expected, vm.FailureKind);
        Assert.Null(vm.Item);
        if (status != 404) Assert.DoesNotContain("not found", vm.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        await vm.ReloadAsync("fixture");
        Assert.Equal(DetailFailureKind.None, vm.FailureKind);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal("fixture", vm.Item!.ContentId);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AnOlderFailedReadCannotReplaceNewerSuccessfulDetailState()
    {
        var pending = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ItemDetailPrefetchCache((id, _) => id == "old" ? pending.Task : Task.FromResult(new MediaItemDetail { ContentId = id, UserState = new() }));
        var vm = new ItemDetailViewModel(null!, cache);
        var oldLoad = vm.LoadCommand.ExecuteAsync("old");
        await vm.LoadCommand.ExecuteAsync("new");
        pending.SetException(new ApiException("unavailable", "Fixture", 503));
        await oldLoad;
        Assert.Null(vm.ErrorMessage);
        Assert.Equal("new", vm.Item!.ContentId);
    }
}
