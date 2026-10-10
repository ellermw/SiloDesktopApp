namespace SiloPlayer.Tests;

public sealed class CodeRabbitFollowUpRegressionTests
{
    [Fact]
    public void CollectionCreationBecomesRetrySafeBeforePostCreateItemWork()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "ViewModels", "CollectionEditorViewModel.cs");
        var create = source.IndexOf("var created =", StringComparison.Ordinal);
        var manualItems = source.IndexOf("if (CollectionType == \"manual\"", create, StringComparison.Ordinal);
        var collectionId = source.IndexOf("CollectionId = created.Id;", create, StringComparison.Ordinal);
        var editMode = source.IndexOf("IsEditing = true;", create, StringComparison.Ordinal);
        var baselineReset = source.IndexOf("_originalManualItemIds.Clear();", create, StringComparison.Ordinal);

        Assert.True(create >= 0 && manualItems > create);
        Assert.InRange(collectionId, create + 1, manualItems - 1);
        Assert.InRange(editMode, create + 1, manualItems - 1);
        Assert.InRange(baselineReset, create + 1, manualItems - 1);

        var addItem = source.IndexOf("await _collectionsApi.AddCollectionItemAsync(created.Id, item.MediaItemId);", manualItems, StringComparison.Ordinal);
        var trackPersistedItem = source.IndexOf("_originalManualItemIds.Add(item.MediaItemId);", addItem, StringComparison.Ordinal);
        var reorder = source.IndexOf("await _collectionsApi.ReorderCollectionItemsAsync(created.Id, addedIds);", addItem, StringComparison.Ordinal);
        Assert.True(addItem >= 0 && trackPersistedItem > addItem && reorder > trackPersistedItem);
    }

    [Fact]
    public void LetterJumpPreservesWhetherTheRetainedCatalogTotalIsExact()
    {
        var source = ExtractMethod(
            ReadRepoFile("src", "SiloPlayer", "ViewModels", "LibraryViewModel.cs"),
            "private async Task JumpToLetterAsync");

        Assert.Contains("var knownTotalIsExact = _hasExactTotal;", source, StringComparison.Ordinal);
        Assert.Contains("_hasExactTotal = knownTotalIsExact;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_hasExactTotal = true;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UserChangedDoesNotReconnectAnAlreadyRunningEventChannelAfterTokenRefresh()
    {
        var source = ExtractMethod(
            ReadRepoFile("src", "SiloPlayer.Core", "Services", "EventChannelClient.cs"),
            "private void OnUserChanged");

        Assert.Contains("var wasReconnectSuppressed = _reconnectSuppressed;", source, StringComparison.Ordinal);
        Assert.Contains("wasReconnectSuppressed || _runTask == null || _runTask.IsCompleted", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRunning_NoLock(forceReconnect: false);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureRunning_NoLock(forceReconnect: true);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditScriptsShareAnExactOfficialGithubOriginPolicy()
    {
        var common = ReadRepoFile("scripts", "audit-common.ps1");
        Assert.Contains("^https://github\\.com/Silo-Server/silo-server(?:\\.git)?$", common, StringComparison.Ordinal);
        Assert.Contains("^git@github\\.com:Silo-Server/silo-server(?:\\.git)?$", common, StringComparison.Ordinal);
        Assert.Contains("^ssh://git@github\\.com/Silo-Server/silo-server(?:\\.git)?$", common, StringComparison.Ordinal);

        foreach (var scriptName in new[] { "audit-scan.ps1", "audit-delta.ps1", "audit-bump.ps1" })
        {
            var script = ReadRepoFile("scripts", scriptName);
            Assert.Contains(". \"$PSScriptRoot\\audit-common.ps1\"", script, StringComparison.Ordinal);
            Assert.Contains("Assert-OfficialSiloOrigin", script, StringComparison.Ordinal);
            Assert.DoesNotContain("github\\.com[/:]Silo-Server/silo-server", script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AuditScriptsValidateAndSynchronizeBeforeUsingOfficialMain()
    {
        var scan = ReadRepoFile("scripts", "audit-scan.ps1");
        var scanValidate = scan.IndexOf("Assert-OfficialSiloOrigin", StringComparison.Ordinal);
        var scanFetch = scan.IndexOf("Invoke-GitChecked @(\"fetch\", \"origin\", \"main\")", StringComparison.Ordinal);
        Assert.True(scanValidate >= 0 && scanFetch > scanValidate);
        Assert.Contains("rev-parse\", \"origin/main", scan, StringComparison.Ordinal);
        Assert.Contains("log\", \"-1\", \"--format=%ci\", \"origin/main", scan, StringComparison.Ordinal);

        var delta = ReadRepoFile("scripts", "audit-delta.ps1");
        var deltaValidate = delta.IndexOf("Assert-OfficialSiloOrigin", StringComparison.Ordinal);
        var deltaFetch = delta.IndexOf("Invoke-GitChecked @(\"fetch\", \"origin\", \"main\")", StringComparison.Ordinal);
        Assert.True(deltaValidate >= 0 && deltaFetch > deltaValidate);

        var bump = ReadRepoFile("scripts", "audit-bump.ps1");
        var bumpValidate = bump.IndexOf("Assert-OfficialSiloOrigin", StringComparison.Ordinal);
        var bumpFetch = bump.IndexOf("Invoke-GitChecked @(\"fetch\", \"origin\", \"main\")", StringComparison.Ordinal);
        var bumpClean = bump.IndexOf("status\", \"--porcelain", StringComparison.Ordinal);
        var bumpMerge = bump.IndexOf("merge\", \"--ff-only\", \"origin/main", StringComparison.Ordinal);
        Assert.True(bumpValidate >= 0 && bumpClean > bumpValidate && bumpFetch > bumpClean && bumpMerge > bumpFetch);
    }

    [Fact]
    public void ImageSourceCacheRemovesDeadKeysAndPeriodicallyPrunes()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Converters", "UrlToImageSourceConverter.cs");

        Assert.Contains("Interlocked.Increment(ref _lookupCount)", source, StringComparison.Ordinal);
        Assert.Contains("PruneDeadSources();", source, StringComparison.Ordinal);
        Assert.Contains("private static void PruneDeadSources()", source, StringComparison.Ordinal);
        Assert.Contains("RemoveDeadEntry(sourceKey, existing);", source, StringComparison.Ordinal);
        Assert.Contains("var sourceKey = dimRequestPoster ? url + \"|request-dim\" : url;", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(current, bitmap)", source, StringComparison.Ordinal);
        Assert.Contains(".Remove(new(sourceKey, saved))", source, StringComparison.Ordinal);
        Assert.Contains("GetImageAsync(cacheKey, \"converted\", url, httpClient)", source, StringComparison.Ordinal);
        Assert.Contains("ICollection<KeyValuePair<string, WeakReference<BitmapImage>>>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionImdbBadgeUsesTheSameValidatedValueAsTheAppliedRule()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionBrowsePage.xaml.cs");
        var rules = ExtractMethod(source, "private List<QueryRule> BuildExtraRules");
        var badges = ExtractMethod(source, "private List<FilterBadge> BuildActiveFilterBadges");

        Assert.Contains("TryReadMinimumRating(out var minimumRating)", rules, StringComparison.Ordinal);
        Assert.Contains("TryReadMinimumRating(out var minimumRating)", badges, StringComparison.Ordinal);
        Assert.DoesNotContain("AddText(\"IMDb\", \"rating_imdb\", MinimumRatingBox.Text)", badges, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryPercentageUsesTheSameResolvedDurationAsItsDisplay()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "ViewModels", "HistoryViewModel.cs");
        var historyBranch = source[source.IndexOf("foreach (var entry in response.Items)", StringComparison.Ordinal)..];

        Assert.Contains("var resolvedDuration = entry.DurationSeconds ?? Math.Max(0, entry.Runtime * 60);", historyBranch, StringComparison.Ordinal);
        Assert.Contains("DurationSeconds = resolvedDuration", historyBranch, StringComparison.Ordinal);
        Assert.Contains("ProgressPercent = resolvedDuration > 0", historyBranch, StringComparison.Ordinal);
        Assert.Contains("/ resolvedDuration * 100", historyBranch, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Could not find {signature}.");
        var openingBrace = source.IndexOf('{', signatureIndex);
        Assert.True(openingBrace >= 0, $"Could not find the opening brace for {signature}.");

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[openingBrace..(index + 1)];
        }

        throw new InvalidOperationException($"Could not find the closing brace for {signature}.");
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(Path.Combine(parts));
    }
}
