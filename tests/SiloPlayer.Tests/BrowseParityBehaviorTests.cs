using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class BrowseParityBehaviorTests
{
    [Fact]
    public void NewCollectionStartsWithSmartRules()
    {
        using var wire = new Wire(); var client = wire.Client();
        using var auth = new AuthService(client, new AuthApi(client));
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        Assert.Equal("smart", vm.CollectionType);
    }

    [Fact]
    public async Task WizardPreviewLoadsLaterWindowsAndKeepsQueryLimitAndScope()
    {
        using var wire = new Wire(); var client = wire.Client();
        using var auth = new AuthService(client, new AuthApi(client));
        var vm = new SmartCollectionWizardViewModel(new CatalogApi(client), new CollectionsApi(client), new AuthApi(client), auth);
        vm.LimitText = "150"; vm.MediaScope = "video"; vm.SelectedLibraryIds.Add(11); vm.SelectedLibraryIds.Add(22);
        vm.AddRule("genre", "contains", "Drama"); // a preview needs a complete rule
        await vm.PreviewAsync();
        await vm.LoadMorePreviewAsync();
        Assert.Equal(150, vm.PreviewMediaItems.Count);
        Assert.False(vm.PreviewHasMore);
        Assert.Equal(2, wire.CatalogPaths.Count);
        Assert.All(wire.CatalogPaths, path => Assert.Contains("library_id=11", path));
        Assert.Contains(wire.CatalogPaths, path => path.Contains("seek=100"));
        Assert.All(wire.CatalogPaths, path => Assert.Contains("query_limit=150", path));
        Assert.All(wire.CatalogPaths, path => Assert.Contains("type=video", path));
    }

    [Fact]
    public async Task WizardWithoutACompleteRuleSendsNoPreviewAndCannotSave()
    {
        using var wire = new Wire(); var client = wire.Client();
        using var auth = new AuthService(client, new AuthApi(client));
        var vm = new SmartCollectionWizardViewModel(new CatalogApi(client), new CollectionsApi(client), new AuthApi(client), auth) { Title = "Wizard" };
        Assert.True(vm.NeedsRule);
        await vm.PreviewAsync();
        await vm.SaveAsync();
        Assert.Empty(wire.CatalogPaths); Assert.Equal(0, vm.PreviewTotal);
        Assert.Equal(SmartCollectionWizardViewModel.RuleNeededMessage, vm.ErrorMessage);
        vm.AddRule("year", "between", new object[] { 1990, "" });
        Assert.True(vm.NeedsRule);
        vm.Rules[0].Value = 0; // zero is a value
        Assert.False(vm.NeedsRule);
        await vm.PreviewAsync();
        Assert.Single(wire.CatalogPaths);
    }

    [Fact]
    public async Task WizardPreviewOfSeveralLibrariesSaysItCoversOnlyTheFirst()
    {
        using var wire = new Wire(); var client = wire.Client();
        using var auth = new AuthService(client, new AuthApi(client));
        var vm = new SmartCollectionWizardViewModel(new CatalogApi(client), new CollectionsApi(client), new AuthApi(client), auth);
        vm.Libraries.Add(new() { Id = 11, Name = "Films" }); vm.Libraries.Add(new() { Id = 22, Name = "Shows" });
        vm.SelectedLibraryIds.Add(11);
        vm.AddRule("genre", "contains", "Drama");
        await vm.PreviewAsync();
        Assert.Null(vm.PreviewScopeNote); // one library: the preview is the whole collection
        vm.SelectedLibraryIds.Add(22);
        await vm.PreviewAsync();
        Assert.Contains("library_id=11", wire.CatalogPaths[^1]);
        Assert.Equal("Preview and count cover Films only. The saved collection also includes matches from the other selected library.", vm.PreviewScopeNote);
        Assert.Equal(new[] { 11, 22 }, vm.BuildQueryDefinition().LibraryIds);
        vm.Rules[0].Value = ""; // nothing is previewed, so there is no scope to describe
        await vm.PreviewAsync();
        Assert.Null(vm.PreviewScopeNote);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public List<string> CatalogPaths { get; } = [];
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://browse-fixture.invalid"); return client; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            var json = "{\"items\":[]}";
            if (path.StartsWith("/api/v2/catalog?"))
            {
                CatalogPaths.Add(path);
                var offset = path.Contains("seek=100") ? 100 : 0;
                json = JsonSerializer.Serialize(new { items = Enumerable.Range(offset, offset == 0 ? 100 : 50).Select(i => new { content_id = $"book-{i}", title = $"Book {i}" }), total = 150, total_exact = true, window_cursor = "preview", page = new { has_more = offset == 0 } });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
