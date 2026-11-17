namespace SiloPlayer.Tests;

/// <summary>
/// Official #4287 (9eb3df6f3): each custom text-field key handler consults the
/// shared IME composition tracker before it submits, navigates, selects or
/// commits a tag.
/// </summary>
public sealed class ImeCompositionParitySourceTests
{
    [Theory]
    [InlineData("src/SiloPlayer/Controls/GlobalSearchDialog.xaml.cs", "private void SearchBox_KeyDown(", "_imeComposition.ShouldIgnore(e)")]
    [InlineData("src/SiloPlayer/Views/SearchPage.xaml.cs", "private async void SearchBox_KeyDown(", "ImeCompositionGuard.Attach(SearchBox).ShouldIgnore(e)")]
    [InlineData("src/SiloPlayer/Views/CollectionEditorPage.xaml.cs", "private async void SearchBox_KeyDown(", "ImeCompositionGuard.Attach(SearchBox).ShouldIgnore(e)")]
    [InlineData("src/SiloPlayer/Controls/MetadataTagsInput.cs", "_input.KeyDown += (_, e) =>", "ImeCompositionGuard.ShouldIgnore(composition, e)) return;")]
    // Follow-up surfaces: every custom handler that submits, searches, joins or commits.
    [InlineData("src/SiloPlayer/Views/RequestsPage.xaml.cs", "private async void SearchBox_KeyDown(", "ImeCompositionGuard.Attach(SearchBox).ShouldIgnore(e)) return;")]
    [InlineData("src/SiloPlayer/Views/WatchTogetherJoinPage.xaml.cs", "private async void RoomCodeBox_KeyDown(", "SiloPlayer.Helpers.ImeCompositionGuard.ShouldIgnore(SiloPlayer.Helpers.ImeCompositionGuard.Attach(RoomCodeBox), e)) return;")]
    [InlineData("src/SiloPlayer/Views/WatchTogetherRoomPage.xaml.cs", "private async void HostSearchBox_KeyDown(", "ImeCompositionGuard.Attach(HostSearchBox).ShouldIgnore(e)) return;")]
    [InlineData("src/SiloPlayer/Views/LoginPage.xaml.cs", "private void InputBox_KeyDown(", "sender is TextBox box && ImeCompositionGuard.Attach(box).ShouldIgnore(e)) return;")]
    [InlineData("src/SiloPlayer/Views/SignupPage.xaml.cs", "private void InputBox_KeyDown(", "sender is TextBox box && ImeCompositionGuard.Attach(box).ShouldIgnore(e)) return;")]
    [InlineData("src/SiloPlayer/Views/ActivateDevicePage.xaml.cs", "private void CodeBox_KeyDown(", "SiloPlayer.Helpers.ImeCompositionGuard.ShouldIgnore(SiloPlayer.Helpers.ImeCompositionGuard.Attach(CodeBox), e)) return;")]
    [InlineData("src/SiloPlayer/Views/EbookReaderPage.xaml.cs", "private async void SearchBox_KeyDown(", "ImeCompositionGuard.Attach(SearchBox).ShouldIgnore(e)) return;")]
    [InlineData("src/SiloPlayer/Views/CollectionsPage.xaml.cs", "private async void AddGroupNameBox_KeyDown(", "ImeCompositionGuard.Attach(AddGroupNameBox).ShouldIgnore(e)) return;")]
    [InlineData("src/SiloPlayer/Views/CollectionsPage.xaml.cs", "renameBox.KeyDown += async (_, args) =>", "renameComposition.ShouldIgnore(args)) return;")]
    [InlineData("src/SiloPlayer/Controls/MatchItemDialog.xaml.cs", "private void SearchField_KeyDown(", "sender is TextBox box && ImeCompositionGuard.Attach(box).ShouldIgnore(e)) return;")]
    [InlineData("src/SiloPlayer/Controls/NotificationSettingsControl.xaml.cs", "private async void EmailAddressInput_KeyDown(", "ImeCompositionGuard.Attach(EmailAddressInput).ShouldIgnore(e)) return;")]
    public void KeyHandlerConsultsTheTrackerBeforeActing(string path, string handler, string guard)
    {
        var source = Read(path);
        var start = source.IndexOf(handler, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{path} no longer has {handler}");
        var guardAt = source.IndexOf(guard, start, StringComparison.Ordinal);
        Assert.True(guardAt > start, $"{path} {handler} does not consult the IME tracker");
        // The guard is the first thing the handler does: no key acts before it.
        var beforeGuard = source[start..guardAt];
        Assert.DoesNotContain("VirtualKey.", beforeGuard);
        Assert.DoesNotContain("e.Handled", beforeGuard);
    }

    [Theory]
    [InlineData("src/SiloPlayer/Controls/GlobalSearchDialog.xaml.cs", "_imeComposition = ImeCompositionGuard.Attach(SearchBox);")]
    [InlineData("src/SiloPlayer/Views/SearchPage.xaml.cs", "ImeCompositionGuard.Attach(SearchBox);")]
    [InlineData("src/SiloPlayer/Views/CollectionEditorPage.xaml.cs", "ImeCompositionGuard.Attach(SearchBox);")]
    [InlineData("src/SiloPlayer/Controls/MetadataTagsInput.cs", "var composition = SiloPlayer.Helpers.ImeCompositionGuard.Attach(_input);")]
    [InlineData("src/SiloPlayer/Views/RequestsPage.xaml.cs", "ImeCompositionGuard.Attach(SearchBox);")]
    [InlineData("src/SiloPlayer/Views/WatchTogetherJoinPage.xaml.cs", "SiloPlayer.Helpers.ImeCompositionGuard.Attach(RoomCodeBox);")]
    [InlineData("src/SiloPlayer/Views/WatchTogetherRoomPage.xaml.cs", "ImeCompositionGuard.Attach(HostSearchBox);")]
    [InlineData("src/SiloPlayer/Views/LoginPage.xaml.cs", "ImeCompositionGuard.Attach(UsernameBox);")]
    [InlineData("src/SiloPlayer/Views/SignupPage.xaml.cs", "foreach (var input in new[] { UsernameBox, EmailBox, InviteCodeBox }) ImeCompositionGuard.Attach(input);")]
    [InlineData("src/SiloPlayer/Views/ActivateDevicePage.xaml.cs", "SiloPlayer.Helpers.ImeCompositionGuard.Attach(CodeBox);")]
    [InlineData("src/SiloPlayer/Views/EbookReaderPage.xaml.cs", "ImeCompositionGuard.Attach(SearchBox);")]
    [InlineData("src/SiloPlayer/Views/CollectionsPage.xaml.cs", "ImeCompositionGuard.Attach(AddGroupNameBox);")]
    [InlineData("src/SiloPlayer/Controls/MatchItemDialog.xaml.cs", "ImeCompositionGuard.Attach(box);")]
    [InlineData("src/SiloPlayer/Controls/NotificationSettingsControl.xaml.cs", "ImeCompositionGuard.Attach(EmailAddressInput);")]
    public void TrackerIsAttachedInTheConstructorBeforeAnyCompositionCanStart(string path, string attach)
    {
        var source = Read(path);
        var initialize = source.IndexOf("InitializeComponent();", StringComparison.Ordinal);
        var attachAt = source.IndexOf(attach, StringComparison.Ordinal);
        Assert.True(attachAt >= 0, $"{path} does not attach the IME tracker");
        var firstHandler = source.IndexOf("KeyDown(object sender", StringComparison.Ordinal);
        if (firstHandler < 0) firstHandler = source.IndexOf("_input.KeyDown +=", StringComparison.Ordinal);
        Assert.True(attachAt < firstHandler, $"{path} attaches the IME tracker only inside its key handler, after composition began");
        if (initialize >= 0) Assert.True(attachAt > initialize, $"{path} attaches before its TextBox exists");
    }

    [Fact]
    public void GroupRenameBoxIsTrackedBeforeItsKeyHandlerIsRegistered()
    {
        var source = Read("src/SiloPlayer/Views/CollectionsPage.xaml.cs");
        var attachAt = source.IndexOf("var renameComposition = ImeCompositionGuard.Attach(renameBox);", StringComparison.Ordinal);
        var handlerAt = source.IndexOf("renameBox.KeyDown += async (_, args) =>", StringComparison.Ordinal);
        Assert.True(attachAt >= 0, "the group rename box no longer attaches the IME tracker");
        Assert.True(attachAt < handlerAt, "the group rename box registers its key handler before it is tracked");
    }

    [Fact]
    public void MatchDialogTracksTheDynamicProviderBoxesBeforeTheirHandlers()
    {
        var source = Read("src/SiloPlayer/Controls/MatchItemDialog.xaml.cs");
        foreach (var name in new[] { "providerBox", "valueBox" })
        {
            var attachAt = source.IndexOf($"ImeCompositionGuard.Attach({name});", StringComparison.Ordinal);
            var handlerAt = source.IndexOf($"{name}.KeyDown += SearchField_KeyDown;", StringComparison.Ordinal);
            Assert.True(attachAt >= 0, $"{name} no longer attaches the IME tracker");
            Assert.True(attachAt < handlerAt, $"{name} registers SearchField_KeyDown before it is tracked");
        }
    }

    [Fact]
    public void TagCommaCommitWaitsForTheCompositionToEnd()
    {
        var source = Read("src/SiloPlayer/Controls/MetadataTagsInput.cs");
        Assert.Contains("_input.TextChanged += (_, _) => { if (!composition.IsComposing && _input.Text.Contains(',')) Commit(); };", source);
    }

    [Fact]
    public void GuardBindsTextBoxCompositionEventsAndProcessKey()
    {
        var guard = Read("src/SiloPlayer/Helpers/ImeCompositionGuard.cs");
        Assert.Contains("box.TextCompositionStarted += (_, _) => state.Start();", guard);
        Assert.Contains("box.TextCompositionEnded += (_, _) => state.End();", guard);
        Assert.Contains("box.LostFocus += (_, _) => state.Reset();", guard);
        Assert.Contains("state.ShouldIgnoreKey((int)e.Key, (int)e.OriginalKey)", guard);
        // An IME Enter/Escape must not reach a parent dialog's default or close action.
        Assert.Contains("if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Escape) e.Handled = true;", guard);
        var state = Read("src/SiloPlayer.Core/Services/ImeCompositionState.cs");
        Assert.Contains("public const int ProcessKey = 229;", state);
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
