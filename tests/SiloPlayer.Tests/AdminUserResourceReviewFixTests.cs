using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Review fixes for the account-resource pages (official a84b2551b2): the autosaved draft's view between two saves, the editor's patches, a draft
/// that no longer saves, the dialog slot, the contrast selection look, invisible characters in names, and a write that never left. Each test computes
/// behaviour; none searches source text. Synthetic values only.
/// </summary>
public sealed class AdminUserResourceReviewFixTests
{
    private static SubtitleAppearance Appearance(string size = "large", string color = "#ffffff", int opacity = 75) =>
        new() { FontSize = size, FontColor = color, BackgroundOpacity = opacity };

    /// <summary>A draft whose saves are answered by the test, one at a time, with every notification's view recorded.</summary>
    private sealed class Saves
    {
        public readonly List<SubtitleAppearance> Sent = [];
        public readonly List<TaskCompletionSource<DraftSaveResult>> Answers = [];
        public readonly List<(SubtitleAppearance View, bool Saving, bool Locked)> Notifications = [];
        public AutosavedDraft<SubtitleAppearance> Draft { get; }
        public Saves(SubtitleAppearance? initial = null)
        {
            Draft = AdminSubtitleDraft.Create(initial ?? Appearance(), json =>
            {
                Sent.Add(SubtitleAppearance.Parse(json));
                var answer = new TaskCompletionSource<DraftSaveResult>(); Answers.Add(answer); return answer.Task;
            });
            Draft.Changed += () => Notifications.Add((Draft.Value, Draft.Saving, Draft.Locked));
        }
        public void Edit(Action<SubtitleAppearance> change)
        {
            var after = Draft.Value; change(after); Draft.Edit(AdminSubtitleDraft.Between(Draft.Value, after));
        }
    }

    // ---- finding 1: the view between two saves ----

    [Fact]
    public void TheViewNeverLosesAQueuedEditInsideAnyNotification()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge"); saves.Edit(s => s.FontColor = "#3b82f6"); saves.Edit(s => s.BackgroundOpacity = 40);
        saves.Notifications.Clear();
        saves.Answers[0].SetResult(DraftSaveResult.Saved);                      // the merged save of the two queued edits goes out now
        Assert.Equal(2, saves.Sent.Count);
        Assert.NotEmpty(saves.Notifications);
        foreach (var (view, saving, _) in saves.Notifications)
        {
            Assert.Equal(("xlarge", "#3b82f6", 40), (view.FontSize, view.FontColor, view.BackgroundOpacity));   // the queued edits are still shown
            Assert.True(saving);                                                                                   // and the merged save already counts as in flight
        }
        saves.Notifications.Clear();
        saves.Answers[1].SetResult(DraftSaveResult.Saved);
        Assert.All(saves.Notifications, n => Assert.Equal(("xlarge", "#3b82f6", 40), (n.View.FontSize, n.View.FontColor, n.View.BackgroundOpacity)));
        Assert.False(saves.Draft.Saving);
    }

    [Fact]
    public void AfterARefusalTheEditsQueuedLaterStayShownWhileTheyAreSent()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge"); saves.Edit(s => s.FontColor = "#3b82f6");
        saves.Notifications.Clear();
        saves.Answers[0].SetResult(DraftSaveResult.Refused("No."));
        Assert.Equal(2, saves.Sent.Count); Assert.Equal(("large", "#3b82f6"), (saves.Sent[1].FontSize, saves.Sent[1].FontColor));   // built from the acknowledged value
        Assert.All(saves.Notifications, n => { Assert.Equal(("large", "#3b82f6"), (n.View.FontSize, n.View.FontColor)); Assert.True(n.Saving); });
        Assert.Equal("No.", saves.Draft.Failure);
    }

    [Fact]
    public void AfterAnUnknownOutcomeTheViewIsTheAcknowledgedValueAndNothingIsSentAgain()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge"); saves.Edit(s => s.FontColor = "#3b82f6");
        saves.Notifications.Clear();
        saves.Answers[0].SetResult(DraftSaveResult.Unknown("Reload."));
        Assert.Single(saves.Sent);
        Assert.All(saves.Notifications, n => { Assert.Equal(("large", "#ffffff"), (n.View.FontSize, n.View.FontColor)); Assert.True(n.Locked); Assert.False(n.Saving); });
    }

    [Fact]
    public void AfterCloseTheAnswerOfTheSaveInFlightSendsNothingMore()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge"); saves.Edit(s => s.FontColor = "#3b82f6");
        saves.Draft.Close(); saves.Notifications.Clear();
        saves.Answers[0].SetResult(DraftSaveResult.Saved);
        Assert.Single(saves.Sent); Assert.Equal(("xlarge", "#ffffff"), (saves.Draft.Value.FontSize, saves.Draft.Value.FontColor));
        Assert.All(saves.Notifications, n => Assert.False(n.Saving));
    }

    [Fact]
    public void AResetOfTheServerValueKeepsTheSaveInFlightAndQueuedEditsOnTop()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge"); saves.Edit(s => s.FontColor = "#3b82f6");
        saves.Notifications.Clear(); saves.Draft.Reset(Appearance(opacity: 10));
        var view = Assert.Single(saves.Notifications).View;
        Assert.Equal(("xlarge", "#3b82f6", 10), (view.FontSize, view.FontColor, view.BackgroundOpacity));
    }

    [Fact]
    public void DiscardingTheQueuedEditsLeavesTheSaveInFlightShown()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge"); saves.Edit(s => s.FontColor = "#3b82f6");
        saves.Notifications.Clear(); saves.Draft.DiscardQueued();
        var view = Assert.Single(saves.Notifications).View;
        Assert.Equal(("xlarge", "#ffffff"), (view.FontSize, view.FontColor));
        saves.Answers[0].SetResult(DraftSaveResult.Saved); Assert.Single(saves.Sent);
    }

    /// <summary>The controls of the editor: a change writes into <see cref="AdminSubtitleDraftEditor.State"/>, then tells the editor.</summary>
    private sealed class Controls
    {
        public readonly Saves Saves = new(); public readonly AdminSubtitleDraftEditor Editor; public int Redraws;
        public readonly List<string> ColorsSeenByRedraw = [];
        public Controls() { Editor = new(Saves.Draft, Saves.Draft.Value); Editor.Redraw += () => { Redraws++; ColorsSeenByRedraw.Add(Editor.State.FontColor); }; }
        public void Change(Action<SubtitleAppearance> control) { control(Editor.State); Editor.Edited(); }
    }

    [Fact]
    public void AnEditMadeDuringTheMergedSaveSendsOnlyTheNewEditAndTheControlsNeverRevert()
    {
        var c = new Controls();
        c.Change(s => s.FontSize = "xlarge");                       // save 1 goes out
        c.Change(s => s.FontColor = "#3b82f6");                     // queued
        c.Change(s => s.BackgroundOpacity = 40);                    // queued, merged
        c.Saves.Answers[0].SetResult(DraftSaveResult.Saved);        // save 2 (both queued edits) goes out
        Assert.Equal(2, c.Saves.Sent.Count);
        Assert.Equal(("xlarge", "#3b82f6", 40), (c.Editor.State.FontSize, c.Editor.State.FontColor, c.Editor.State.BackgroundOpacity));   // the controls kept them
        c.Change(s => s.BackgroundOpacity = 41);                    // a slider drag during the merged save
        c.Saves.Answers[1].SetResult(DraftSaveResult.Saved);        // save 3
        Assert.Equal(3, c.Saves.Sent.Count);
        var third = c.Saves.Sent[2];
        Assert.Equal(("xlarge", "#3b82f6", 41), (third.FontSize, third.FontColor, third.BackgroundOpacity));
        Assert.Equal(c.Saves.Sent[1].ToJson().Replace("40", "41"), third.ToJson());   // it differs from the save before it by the new edit alone
        Assert.Equal(0, c.Redraws);                                 // the controls were never asked to show an older value
        c.Saves.Answers[2].SetResult(DraftSaveResult.Saved);
        Assert.Equal(("xlarge", "#3b82f6", 41), (c.Saves.Draft.Confirmed.FontSize, c.Saves.Draft.Confirmed.FontColor, c.Saves.Draft.Confirmed.BackgroundOpacity));
        Assert.Equal(0, c.Redraws);
    }

    [Fact]
    public void AnEditPatchIsWhatTheControlChangedNotWhatTheDraftHappenedToShow()
    {
        var c = new Controls();
        c.Change(s => s.FontSize = "xlarge"); c.Change(s => s.FontColor = "#3b82f6");     // one in flight, one queued
        // The server value is read again underneath (the draft's view changes in a field the admin did not touch).
        c.Saves.Draft.Reset(Appearance(opacity: 10));
        Assert.Equal(10, c.Editor.State.BackgroundOpacity);                                // the controls follow the notification
        c.Change(s => s.TextOutline = true);
        c.Saves.Answers[0].SetResult(DraftSaveResult.Saved);
        var merged = c.Saves.Sent[1];
        Assert.Equal(("xlarge", "#3b82f6", true), (merged.FontSize, merged.FontColor, merged.TextOutline));
    }

    [Fact]
    public void ARefusedEditLeavesTheControlsAtTheAcknowledgedValueAndTheNextEditCarriesOnlyItself()
    {
        var c = new Controls();
        c.Change(s => s.FontColor = "#3b82f6");
        c.Saves.Answers[0].SetResult(DraftSaveResult.Refused("No."));
        Assert.Equal(1, c.Redraws); Assert.Equal("#ffffff", c.Editor.State.FontColor);
        c.Change(s => s.FontSize = "small");
        Assert.Equal(("small", "#ffffff"), (c.Saves.Sent[1].FontSize, c.Saves.Sent[1].FontColor));
    }

    // ---- finding 4: a retired draft ----

    [Fact]
    public void ARetiredDraftSaysSoAndIgnoresFurtherEdits()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "small");
        Assert.False(saves.Draft.Retired);
        saves.Answers[0].SetResult(DraftSaveResult.Retired);
        Assert.True(saves.Draft.Retired); Assert.False(saves.Draft.Locked);
        saves.Edit(s => s.FontColor = "#000000");
        Assert.Single(saves.Sent); Assert.Equal("large", saves.Draft.Value.FontSize);
    }

    [Fact]
    public void ControlsOfARetiredDraftAreAskedToShowTheAcknowledgedValue()
    {
        var c = new Controls(); c.Change(s => s.FontSize = "small");
        c.Saves.Answers[0].SetResult(DraftSaveResult.Retired);
        Assert.Equal("large", c.Editor.State.FontSize);
        var redraws = c.Redraws; c.Change(s => s.FontColor = "#000000");
        Assert.Single(c.Saves.Sent); Assert.Equal("#ffffff", c.Editor.State.FontColor); Assert.True(c.Redraws > redraws);
    }

    // ---- finding 5: a reset whose outcome is unknown ----

    [Fact]
    public void LockingTheDraftStopsEverySaveAndDropsQueuedEdits()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge"); saves.Edit(s => s.FontColor = "#3b82f6");
        saves.Draft.Lock("The change may have reached the server.");
        Assert.True(saves.Draft.Locked); Assert.Equal("The change may have reached the server.", saves.Draft.Failure);
        saves.Answers[0].SetResult(DraftSaveResult.Saved);
        Assert.Single(saves.Sent);                                                  // the queued edit was dropped, not sent
        saves.Edit(s => s.FontColor = "#000000"); Assert.Single(saves.Sent);       // and the next edit sends nothing
        Assert.Equal(("xlarge", "#ffffff"), (saves.Draft.Value.FontSize, saves.Draft.Value.FontColor));
    }

    [Fact]
    public void LockingAnIdleDraftMakesTheNextEditSendNothing()
    {
        var saves = new Saves(); saves.Draft.Lock(null);
        Assert.True(saves.Draft.Locked); Assert.Equal("", saves.Draft.Failure);
        saves.Edit(s => s.FontSize = "small"); Assert.Empty(saves.Sent);
    }

    // ---- finding 8: a write that never left ----

    [Fact]
    public async Task AWriteThatIsRefusedBecauseAnotherIsRunningSaysItNeverStarted()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminUserResourceController(authority); controller.Open();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = controller.TryWriteAsync(_ => gate.Task);
        Assert.True(controller.Busy);
        var second = await controller.TryWriteAsync(_ => throw new InvalidOperationException("must not be called"));
        Assert.Equal(AdminWriteResult.NotStarted, second);
        gate.SetResult();
        Assert.Equal(AdminWriteResult.Done, await first);
    }

    [Fact]
    public async Task AWriteThatThrowsAfterItStartedIsFailedNotNotStarted()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminUserResourceController(authority); controller.Open();
        Assert.Equal(AdminWriteResult.Failed, await controller.TryWriteAsync(_ => throw new InvalidOperationException("sent, then refused")));
        Assert.False(await controller.WriteAsync(_ => throw new InvalidOperationException("again")));
    }

    [Fact]
    public void TheWordingForAStateTheOfficialPagesLackIsKeptApartFromTheOfficialCatalog()
    {
        foreach (var key in AdminUserResourceText.NativeOnly.Keys)
        {
            Assert.StartsWith("admin.native.userResources.", key);
            Assert.False(AdminUserResourceText.Has(key));                          // never mixed into the exact copy of the official messages
            Assert.Equal(AdminUserResourceText.NativeOnly[key], AdminUserResourceText.T(key));
        }
        Assert.Contains("another change is being saved", AdminUserResourceText.T("admin.native.userResources.subtitleEditor.writeBusy"));
        foreach (var official in new[] { "web.admin.common.subtitleAppearanceDialog.failed", "web.admin.common.subtitleAppearanceDialog.resetFailed", "web.settings.subtitlePanel.resetting" })
            Assert.False(AdminUserResourceText.Has(official));
    }

    // ---- finding 3: the dialog slot ----

    private static AdminDialogSlot Slot(int patienceMs = 1000) => new() { Patience = TimeSpan.FromMilliseconds(patienceMs) };

    [Fact]
    public async Task AFreeSlotIsTakenAtOnce()
    {
        using var lease = await Slot().AcquireAsync(() => true);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task ASlotWithAShowingDialogRefusesAnotherAtOnce()
    {
        var slot = Slot(); using var first = await slot.AcquireAsync(() => true);
        Assert.True(slot.Showing);
        Assert.Null(await slot.AcquireAsync(() => true));      // never two dialogs
    }

    [Fact]
    public async Task ARequestMadeWhileTheDialogIsClosingWaitsForItAndThenOpens()
    {
        var slot = Slot(); var first = await slot.AcquireAsync(() => true); Assert.NotNull(first);
        first!.MarkClosed();                                    // the popup is gone; its owner has not finished yet
        Assert.False(slot.Showing); Assert.True(slot.Occupied);
        var second = slot.AcquireAsync(() => true);
        Assert.False(second.IsCompleted);                       // not dropped, not opened yet
        first.Dispose();                                        // ShowAsync returned
        using var lease = await second;
        Assert.NotNull(lease); Assert.True(slot.Showing);
    }

    [Fact]
    public async Task ARequestThatBecameStaleWhileWaitingIsDropped()
    {
        var slot = Slot(); var first = (await slot.AcquireAsync(() => true))!; first.MarkClosed();
        var rowStillThere = true;
        var second = slot.AcquireAsync(() => rowStillThere);
        rowStillThere = false;                                  // the list the request was built from was replaced
        first.Dispose();
        Assert.Null(await second);
        Assert.False(slot.Occupied);
    }

    [Fact]
    public async Task ARequestWaitingWhenThePageIsLeftIsCancelled()
    {
        var slot = Slot(); var first = (await slot.AcquireAsync(() => true))!; first.MarkClosed();
        using var leave = new CancellationTokenSource();
        var second = slot.AcquireAsync(() => true, leave.Token);
        leave.Cancel();
        Assert.Null(await second);
        first.Dispose(); Assert.False(slot.Occupied);
    }

    [Fact]
    public async Task AClosingDialogThatNeverFinishesDropsTheRequestAfterThePatience()
    {
        var slot = Slot(patienceMs: 50); var first = (await slot.AcquireAsync(() => true))!; first.MarkClosed();
        Assert.Null(await slot.AcquireAsync(() => true));
        first.Dispose();
    }

    [Fact]
    public async Task OnlyOneOfTwoWaitingRequestsGetsTheSlot()
    {
        var slot = Slot(); var first = (await slot.AcquireAsync(() => true))!; first.MarkClosed();
        var a = slot.AcquireAsync(() => true); var b = slot.AcquireAsync(() => true);
        first.Dispose();
        var results = await Task.WhenAll(a, b);
        Assert.Equal(1, results.Count(x => x != null));
        foreach (var lease in results) lease?.Dispose();
    }

    [Fact]
    public async Task AStaleRequestIsDroppedEvenWhenTheSlotIsFree()
    {
        var slot = Slot(); Assert.Null(await slot.AcquireAsync(() => false)); Assert.False(slot.Occupied);
    }

    // ---- finding 2: the selected look in a contrast theme ----

    [Fact]
    public void ACheckedOptionKeepsThePlatformHighlightInAContrastTheme()
    {
        Assert.Empty(AdminSelectionLook.KeysReplaced(systemContrast: true));
        Assert.False(AdminSelectionLook.ReplacesCheckedLook(true));
        var normal = AdminSelectionLook.KeysReplaced(systemContrast: false);
        Assert.Equal(9, normal.Count); Assert.Equal(9, normal.Distinct().Count());
        Assert.All(normal, key => Assert.StartsWith("ToggleButton", key));
    }

    // ---- finding 7: invisible characters in third-party text ----

    private static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

    [Theory]
    [InlineData(0x200B)] [InlineData(0x2060)] [InlineData(0x2061)] [InlineData(0x2062)] [InlineData(0x2063)] [InlineData(0x2064)] [InlineData(0xFEFF)]
    public void InvisibleCharactersBecomeASpaceInAName(int invisible)
    {
        Assert.Equal("Ann Lee", AdminNameText.Label("Ann" + C(invisible) + "Lee"));
        Assert.Equal("", AdminNameText.Label(C(invisible) + C(invisible)));                // nothing is left of a name made of them
        Assert.Equal("Ann", AdminNameText.Label(C(invisible) + "Ann" + C(invisible)));
    }

    [Fact]
    public void JoinersThatShapeTextStayInAName()
    {
        var zwj = C(0x200D); var zwnj = C(0x200C);
        var family = "👨" + zwj + "👩" + zwj + "👧";
        Assert.Equal(family, AdminNameText.Label(family));
        var persian = "می" + zwnj + "خواهم";                                               // "I want": the non-joiner is part of the spelling
        Assert.Equal(persian, AdminNameText.Label(persian));
    }
}
