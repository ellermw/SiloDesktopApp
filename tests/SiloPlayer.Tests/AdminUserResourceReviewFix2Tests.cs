using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Second review fixes for the account-resource pages (official a84b2551b2): why a dialog request was refused, a locked draft's text, a throwing
/// subscriber, how a write that never left is reported, the contrast signal, and the invisible characters of a name. Each test computes behaviour; none
/// searches source text. Synthetic values only.
/// </summary>
public sealed class AdminUserResourceReviewFix2Tests
{
    private static SubtitleAppearance Appearance() => new() { FontSize = "large", FontColor = "#ffffff", BackgroundOpacity = 75 };

    private sealed class Saves
    {
        public readonly List<TaskCompletionSource<DraftSaveResult>> Answers = [];
        public AutosavedDraft<SubtitleAppearance> Draft { get; }
        public Saves()
        {
            Draft = AdminSubtitleDraft.Create(Appearance(), _ => { var answer = new TaskCompletionSource<DraftSaveResult>(); Answers.Add(answer); return answer.Task; });
        }
        public void Edit(Action<SubtitleAppearance> change) { var after = Draft.Value; change(after); Draft.Edit(AdminSubtitleDraft.Between(Draft.Value, after)); }
    }

    // ---- the dialog slot: why a request got nothing ----

    /// <summary>Runs on one thread in order, as the page's UI thread does, so which waiting request resumes first does not depend on a race.</summary>
    private static Task OnOneThread(Func<Task> body) =>
        Task.Factory.StartNew(body, CancellationToken.None, TaskCreationOptions.None, new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler).Unwrap();

    private static AdminDialogSlot Slot(int patienceMs = 1000) => new() { Patience = TimeSpan.FromMilliseconds(patienceMs) };

    [Fact]
    public Task ARequestThatWaitedAndFoundTheSlotTakenByAnotherPressIsToldSo() => OnOneThread(async () =>
    {
        var slot = Slot(); var first = (await slot.TryAcquireAsync(() => true)).Lease!; first.MarkClosed();
        var reset = slot.TryAcquireAsync(() => true);                          // a reset button pressed while the dialog is closing
        var editor = slot.TryAcquireAsync(() => true);                         // "Edit subtitles" pressed in the same moment
        Assert.False(reset.IsCompleted); Assert.False(editor.IsCompleted);
        first.Dispose();                                                       // ShowAsync returned
        var results = await Task.WhenAll(reset, editor);
        Assert.Equal(1, results.Count(x => x.Lease != null));                   // exactly one dialog opens
        var loser = results.Single(x => x.Lease == null);
        Assert.Equal(AdminDialogSlot.Refusal.Taken, loser.Refusal);             // and the other is refused, not thrown at the page
        Assert.True(slot.Showing);
        results.Single(x => x.Lease != null).Lease!.Dispose();
        Assert.False(slot.Occupied);
    });

    [Fact]
    public Task AWaitingRequestIsTakenByARequestThatArrivesTheMomentTheDialogFinished() => OnOneThread(async () =>
    {
        var slot = Slot(); var first = (await slot.TryAcquireAsync(() => true)).Lease!; first.MarkClosed();
        var waiting = slot.TryAcquireAsync(() => true);
        first.Dispose();                                                       // the slot is free now, before the waiting request has resumed
        var arriving = await slot.TryAcquireAsync(() => true);                 // a press that finds it free takes it at once
        Assert.NotNull(arriving.Lease); Assert.Equal(AdminDialogSlot.Refusal.None, arriving.Refusal);
        var late = await waiting;
        Assert.Null(late.Lease); Assert.Equal(AdminDialogSlot.Refusal.Taken, late.Refusal);
        arriving.Lease!.Dispose();
    });

    [Fact]
    public Task EveryRefusalSaysWhy() => OnOneThread(async () =>
    {
        var slot = Slot(patienceMs: 40);
        Assert.Equal(AdminDialogSlot.Refusal.Stale, (await slot.TryAcquireAsync(() => false)).Refusal);
        var first = (await slot.TryAcquireAsync(() => true)).Lease!;
        Assert.Equal(AdminDialogSlot.Refusal.Showing, (await slot.TryAcquireAsync(() => true)).Refusal);
        first.MarkClosed();
        Assert.Equal(AdminDialogSlot.Refusal.TimedOut, (await slot.TryAcquireAsync(() => true)).Refusal);   // the closing dialog never finished
        using var leave = new CancellationTokenSource(); var waiting = slot.TryAcquireAsync(() => true, leave.Token); leave.Cancel();
        Assert.Equal(AdminDialogSlot.Refusal.Cancelled, (await waiting).Refusal);
        var stale = true; var changing = slot.TryAcquireAsync(() => stale); stale = false; first.Dispose();
        Assert.Equal(AdminDialogSlot.Refusal.Stale, (await changing).Refusal);
        using var free = (await slot.TryAcquireAsync(() => true)).Lease; Assert.NotNull(free);
    });

    // ---- a locked draft keeps its text ----

    [Fact]
    public void ALateSavedAnswerCannotClearTheTextOfALockedDraft()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge");
        saves.Draft.Lock("The change may have reached the server.");
        saves.Answers[0].SetResult(DraftSaveResult.Saved);                     // the save already in flight was accepted after the lock
        Assert.True(saves.Draft.Locked);
        Assert.Equal("The change may have reached the server.", saves.Draft.Failure);   // never "Changes saved automatically"
        Assert.Equal("xlarge", saves.Draft.Confirmed.FontSize);                // the value the server holds is still tracked
    }

    [Fact]
    public void ALateRefusedAnswerCannotReplaceTheTextOfALockedDraft()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge");
        saves.Draft.Lock("The change may have reached the server.");
        saves.Answers[0].SetResult(DraftSaveResult.Refused("The server refused this."));
        Assert.Equal("The change may have reached the server.", saves.Draft.Failure);
        Assert.Equal("large", saves.Draft.Confirmed.FontSize);
    }

    [Fact]
    public void ALateUnknownAnswerKeepsTheTextOfADraftLockedBefore()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge");
        saves.Draft.Lock("The reset may have reached the server.");
        saves.Answers[0].SetResult(DraftSaveResult.Unknown("The save may have reached the server."));
        Assert.True(saves.Draft.Locked); Assert.Equal("The reset may have reached the server.", saves.Draft.Failure);
    }

    [Fact]
    public void AnUnlockedDraftStillShowsTheLatestRefusalAndClearsItOnTheNextAcceptedSave()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge");
        saves.Answers[0].SetResult(DraftSaveResult.Refused("No."));
        Assert.Equal("No.", saves.Draft.Failure);
        saves.Edit(s => s.FontSize = "small"); saves.Answers[1].SetResult(DraftSaveResult.Saved);
        Assert.Null(saves.Draft.Failure);
    }

    // ---- a subscriber that throws cannot corrupt the draft ----

    [Fact]
    public async Task AThrowingSubscriberWhileQueuedEditsGoOutDoesNotLeaveTheDraftSavingOrFaultSettled()
    {
        var saves = new Saves(); var told = 0;
        saves.Edit(s => s.FontSize = "xlarge");
        saves.Edit(s => s.FontColor = "#3b82f6");                              // queued behind the save in flight
        saves.Draft.Changed += () => throw new InvalidOperationException("a subscriber failed");
        saves.Draft.Changed += () => told++;                                   // a later subscriber is still told
        saves.Answers[0].SetResult(DraftSaveResult.Saved);                     // the queued edit is sent next, inside the notification
        Assert.Equal(2, saves.Answers.Count);
        Assert.True(saves.Draft.Saving);
        saves.Answers[1].SetResult(DraftSaveResult.Saved);
        await saves.Draft.Settled.WaitAsync(TimeSpan.FromSeconds(5));          // completes, never faulted
        Assert.False(saves.Draft.Saving);
        Assert.Equal(("xlarge", "#3b82f6"), (saves.Draft.Confirmed.FontSize, saves.Draft.Confirmed.FontColor));
        Assert.IsType<InvalidOperationException>(saves.Draft.NotificationFailure);
        Assert.True(told > 0);
    }

    [Fact]
    public async Task AThrowingSubscriberAfterARefusalStillSettlesAndKeepsTheFailureText()
    {
        var saves = new Saves();
        saves.Edit(s => s.FontSize = "xlarge");
        saves.Draft.Changed += () => throw new InvalidOperationException("a subscriber failed");
        saves.Answers[0].SetResult(DraftSaveResult.Refused("No."));
        await saves.Draft.Settled.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(saves.Draft.Saving); Assert.Equal("No.", saves.Draft.Failure);
    }

    // ---- a write that never left ----

    [Fact]
    public void AWriteRefusedBecauseThePageNeedsAReviewIsNotAnUnknownOutcome()
    {
        var result = AdminSubtitleDraft.ResultOf(AdminWriteResult.NotStarted, ownerCurrent: true, needsReview: true, message: "An unrelated earlier error.");
        Assert.Equal(DraftSaveOutcome.Refused, result.Outcome);                // nothing was sent, so the editor is not locked as for an unknown outcome
        Assert.Equal(AdminUserResourceText.T("admin.native.userResources.subtitleEditor.needsReload"), result.Message);
        Assert.StartsWith("Nothing was sent", result.Message);
    }

    [Fact]
    public void AWriteRefusedBecauseAnotherIsRunningSaysThat()
    {
        var result = AdminSubtitleDraft.ResultOf(AdminWriteResult.NotStarted, ownerCurrent: true, needsReview: false, message: null);
        Assert.Equal(DraftSaveOutcome.Refused, result.Outcome);
        Assert.Equal(AdminUserResourceText.T("admin.native.userResources.subtitleEditor.writeBusy"), result.Message);
    }

    [Fact]
    public void OnlyAWriteThatLeftAndNeedsAReviewIsUnknown()
    {
        var unknown = AdminSubtitleDraft.ResultOf(AdminWriteResult.Failed, ownerCurrent: true, needsReview: true, message: "The change may have reached the server.");
        Assert.Equal((DraftSaveOutcome.Unknown, "The change may have reached the server."), (unknown.Outcome, unknown.Message));
        var refused = AdminSubtitleDraft.ResultOf(AdminWriteResult.Failed, ownerCurrent: true, needsReview: false, message: "The server refused this.");
        Assert.Equal((DraftSaveOutcome.Refused, "The server refused this."), (refused.Outcome, refused.Message));
        Assert.Equal(AdminUserResourceText.T("admin.native.userResources.subtitleEditor.saveFailed"), AdminSubtitleDraft.ResultOf(AdminWriteResult.Failed, true, false, " ").Message);
    }

    [Fact]
    public void ADoneWriteIsSavedAndARetiredOwnerIsRetiredWhateverElseIsTrue()
    {
        Assert.Equal(DraftSaveOutcome.Saved, AdminSubtitleDraft.ResultOf(AdminWriteResult.Done, true, false, null).Outcome);
        foreach (var written in new[] { AdminWriteResult.NotStarted, AdminWriteResult.Failed })
            Assert.Equal(DraftSaveOutcome.Retired, AdminSubtitleDraft.ResultOf(written, ownerCurrent: false, needsReview: true, message: "x").Outcome);
    }

    [Fact]
    public void TheNewWordingStaysApartFromTheOfficialCatalog()
    {
        foreach (var key in new[] { "admin.native.userResources.dialogStillClosing", "admin.native.userResources.dialogOpenFailed", "admin.native.userResources.subtitleEditor.finishing", "admin.native.userResources.subtitleEditor.needsReload" })
        {
            Assert.True(AdminUserResourceText.NativeOnly.ContainsKey(key)); Assert.False(AdminUserResourceText.Has(key));
            Assert.False(string.IsNullOrWhiteSpace(AdminUserResourceText.T(key)));
        }
        Assert.Contains("still closing", AdminUserResourceText.T("admin.native.userResources.dialogStillClosing"));
        Assert.Contains("Press again", AdminUserResourceText.T("admin.native.userResources.dialogStillClosing"));
    }

    // ---- the selected look follows the contrast setting ----

    [Fact]
    public void TheCheckedLookIsRemovedInAContrastThemeAndPutBackOutsideOne()
    {
        Assert.Equal(9, AdminSelectionLook.KeysRemoved(systemContrast: true).Count);       // a look applied earlier comes off when contrast turns on
        Assert.Empty(AdminSelectionLook.KeysReplaced(systemContrast: true));
        Assert.Empty(AdminSelectionLook.KeysRemoved(systemContrast: false));               // and nothing is removed when it is off
        Assert.Equal(AdminSelectionLook.KeysReplaced(false).OrderBy(x => x), AdminSelectionLook.KeysRemoved(true).OrderBy(x => x));
    }

    private sealed class FakeSetting
    {
        public bool HighContrast; public int Attached, Detached; private Action? _handler;
        public AdminContrastSignal Signal { get; }
        public FakeSetting() { Signal = new(() => HighContrast, h => { Attached++; _handler = h; }, h => { Detached++; _handler = null; }); }
        public void Change(bool value) { HighContrast = value; _handler?.Invoke(); }
    }

    [Fact]
    public void AControlIsToldWhenTheSettingChangesWhileItIsListening()
    {
        var setting = new FakeSetting(); var seen = new List<bool>();
        using var subscription = setting.Signal.Subscribe(seen.Add);
        setting.Change(true); setting.Change(false); setting.Change(true);
        Assert.Equal(new[] { true, false, true }, seen);
    }

    [Fact]
    public void AControlThatLeftIsNotToldAndTheSourceIsReleasedWithTheLastOne()
    {
        var setting = new FakeSetting(); var a = new List<bool>(); var b = new List<bool>();
        var first = setting.Signal.Subscribe(a.Add); var second = setting.Signal.Subscribe(b.Add);
        Assert.Equal(1, setting.Attached); Assert.Equal(2, setting.Signal.Listeners);   // one listener on the source however many controls
        setting.Change(true);
        first.Dispose(); first.Dispose();                                               // leaving twice is harmless
        setting.Change(false);
        Assert.Equal(new[] { true }, a); Assert.Equal(new[] { true, false }, b);
        Assert.Equal(0, setting.Detached);
        second.Dispose();
        Assert.Equal(1, setting.Detached); Assert.Equal(0, setting.Signal.Listeners);
        setting.Change(true); Assert.Equal(new[] { true, false }, b);                           // nobody is told once everyone left
        using var again = setting.Signal.Subscribe(_ => { }); Assert.Equal(2, setting.Attached);   // a later page listens again
    }

    [Fact]
    public void AControlThatThrowsDoesNotStopTheOthersFromBeingTold()
    {
        var setting = new FakeSetting(); var seen = new List<bool>();
        using var bad = setting.Signal.Subscribe(_ => throw new InvalidOperationException("one control failed")); using var good = setting.Signal.Subscribe(seen.Add);
        setting.Change(true);
        Assert.Equal(new[] { true }, seen);
    }

    // ---- invisible characters in third-party text ----

    private static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

    [Theory]
    [InlineData(0x3164)] [InlineData(0x115F)] [InlineData(0x1160)] [InlineData(0xFFA0)]                       // Hangul fillers
    public void HangulFillersAreNotPartOfAName(int filler)
    {
        Assert.Equal("Ann Lee", AdminNameText.Label("Ann" + C(filler) + "Lee"));
        Assert.Equal("Ann", AdminNameText.Label(C(filler) + "Ann" + C(filler)));
        Assert.Equal("", AdminNameText.Label(C(filler) + C(filler)));
    }

    [Fact]
    public void TheBlankBrailleCellIsNotPartOfAName()
    {
        Assert.Equal("Ann Lee", AdminNameText.Label("Ann" + C(0x2800) + "Lee"));
        Assert.Equal("", AdminNameText.Label(C(0x2800) + C(0x2800) + C(0x2800)));
        Assert.Equal("⠃", AdminNameText.Label("⠃"));                                                          // a braille letter with dots still shows
    }

    [Theory]
    [InlineData(0x180E)] [InlineData(0x00AD)] [InlineData(0x034F)]                                           // vowel separator, soft hyphen, grapheme joiner
    public void FormatCharactersThatDrawNothingAreNotPartOfAName(int format)
    {
        Assert.Equal("Ann Lee", AdminNameText.Label("Ann" + C(format) + "Lee"));
        Assert.Equal("", AdminNameText.Label(C(format) + C(format)));
        Assert.Equal("Ann", AdminNameText.Label("Ann" + C(format)));
    }

    [Fact]
    public void TagCharactersCannotCarryAHiddenMessageInAName()
    {
        var hidden = string.Concat("secret".Select(ch => C(0xE0000 + ch)));                                    // tag letters spell a message that no one sees
        Assert.Equal("Ann", AdminNameText.Label("Ann" + hidden));
        Assert.Equal("", AdminNameText.Label(hidden + C(0xE007F) + C(0xE0001) + C(0xE0020)));
        Assert.Equal("Ann Lee", AdminNameText.Label("Ann" + C(0xE0041) + "Lee"));
        foreach (var edge in new[] { 0xE0000, 0xE0001, 0xE0020, 0xE007E, 0xE007F }) Assert.Equal("", AdminNameText.Label(C(edge)));
        Assert.Equal("𐐀", AdminNameText.Label("𐐀"));                                                           // a neighbouring astral letter is not touched
    }

    [Fact]
    public void ALetterWithAnInvisibleMarkAttachedKeepsTheLetter()
    {
        Assert.Equal("a b", AdminNameText.Label("a" + C(0x034F) + "b"));
        Assert.Equal("🏴", AdminNameText.Label("🏴" + C(0xE0067) + C(0xE0062) + C(0xE007F)));                    // a flag drawn with tag letters keeps its base
    }

    [Fact]
    public void ANameMadeOnlyOfJoinersIsEmptyButJoinersBetweenCharactersStay()
    {
        Assert.Equal("", AdminNameText.Label(C(0x200D) + C(0x200C)));
        Assert.Equal("", AdminNameText.Label(C(0x200D)));
        var family = "👨" + C(0x200D) + "👩" + C(0x200D) + "👧";
        Assert.Equal(family, AdminNameText.Label(family));
        var persian = "می" + C(0x200C) + "خواهم";
        Assert.Equal(persian, AdminNameText.Label(persian));
    }

    [Fact]
    public void LoneSurrogatesNeverThrowAndTheTextAroundThemIsKept()
    {
        foreach (var text in new[] { "\uD800", "\uDC00", "Ann\uD800Lee", "Ann\uDC00" + C(0x3164) + "Lee", "\uD800" + C(0xE0041), C(0xE0041) + "\uDFFF", "\uD83D" })
        {
            var label = AdminNameText.Label(text);
            Assert.NotNull(label);
        }
        Assert.Contains("Ann", AdminNameText.Label("Ann\uD800Lee"));
        Assert.Contains("Lee", AdminNameText.Label("Ann\uDC00" + C(0x3164) + "Lee"));
        Assert.DoesNotContain(C(0x3164), AdminNameText.Label("Ann\uDC00" + C(0x3164) + "Lee"));
    }

    [Fact]
    public void AnUnnamedNameFallsBackWhereverTheLabelIsEmpty()
    {
        // The pages show their "unnamed" wording whenever the cleaned label is empty, so one made only of invisible characters must be empty, as an empty name is.
        foreach (var text in new[] { "", "   ", C(0x3164), C(0x2800) + " " + C(0x00AD), C(0x034F) + C(0xE0041) + C(0x180E) })
            Assert.Equal("", AdminNameText.Label(text));
    }
}
