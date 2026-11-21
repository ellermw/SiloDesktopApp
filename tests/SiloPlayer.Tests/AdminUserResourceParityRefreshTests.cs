using System.Globalization;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Official a84b2551b2 behavior for the account-resource pages: the autosaved draft behind the device subtitle override editor (#4543), the
/// avatar initial (#4998, #5027) and the plain bounded text names take in confirmations. Synthetic values only.
/// </summary>
public sealed class AdminUserResourceParityRefreshTests
{
    private static SubtitleAppearance Appearance(string size = "large", string color = "#ffffff", int opacity = 75) =>
        new() { FontSize = size, FontColor = color, BackgroundOpacity = opacity };

    /// <summary>A draft whose saves are answered by the test, one at a time.</summary>
    private sealed class Saves
    {
        public readonly List<SubtitleAppearance> Sent = [];
        public readonly List<TaskCompletionSource<DraftSaveResult>> Answers = [];
        public AutosavedDraft<SubtitleAppearance> Draft { get; }
        public Saves(SubtitleAppearance? initial = null) =>
            Draft = AdminSubtitleDraft.Create(initial ?? Appearance(), json =>
            {
                Sent.Add(SubtitleAppearance.Parse(json));
                var answer = new TaskCompletionSource<DraftSaveResult>(); Answers.Add(answer); return answer.Task;
            });
        public void Edit(Action<SubtitleAppearance> change)
        {
            var after = Draft.Value; change(after); Draft.Edit(AdminSubtitleDraft.Between(Draft.Value, after));
        }
    }

    [Fact]
    public void AnEditShowsAtOnceAndIsSavedFromTheAcknowledgedValue()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge");
        Assert.True(saves.Draft.Saving);
        Assert.Equal("xlarge", saves.Draft.Value.FontSize);
        Assert.Equal("large", saves.Draft.Confirmed.FontSize);
        Assert.Single(saves.Sent); Assert.Equal("xlarge", saves.Sent[0].FontSize); Assert.Equal(75, saves.Sent[0].BackgroundOpacity);
        saves.Answers[0].SetResult(DraftSaveResult.Saved);
        Assert.False(saves.Draft.Saving); Assert.Equal("xlarge", saves.Draft.Confirmed.FontSize); Assert.Null(saves.Draft.Failure);
    }

    [Fact]
    public void ARefusedSaveDropsItsEditFromTheViewAndKeepsTheServersMessage()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xxlarge");
        saves.Answers[0].SetResult(DraftSaveResult.Refused("The server rejected these values."));
        Assert.Equal("large", saves.Draft.Value.FontSize);
        Assert.Equal("The server rejected these values.", saves.Draft.Failure);
        Assert.False(saves.Draft.Saving); Assert.False(saves.Draft.Locked);
        // Nothing was replayed.
        Assert.Single(saves.Sent);
    }

    [Fact]
    public void ARefusedEditDoesNotRideAlongInTheNextSave()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xxlarge");
        saves.Answers[0].SetResult(DraftSaveResult.Refused("No."));
        saves.Edit(s => s.FontColor = "#facc15");
        Assert.Equal(2, saves.Sent.Count);
        Assert.Equal("large", saves.Sent[1].FontSize);          // the rejected size is not in the new request
        Assert.Equal("#facc15", saves.Sent[1].FontColor);
        Assert.Null(saves.Draft.Failure);                        // a new edit clears the old message
    }

    [Fact]
    public void EditsMadeWhileASaveIsInFlightMergeIntoOneQueuedSaveSentAfterIt()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "small");
        saves.Edit(s => s.FontColor = "#22c55e"); saves.Edit(s => s.BackgroundOpacity = 40); saves.Edit(s => s.FontColor = "#06b6d4");
        Assert.Single(saves.Sent);                               // one save at a time
        Assert.Equal(("small", "#06b6d4", 40), (saves.Draft.Value.FontSize, saves.Draft.Value.FontColor, saves.Draft.Value.BackgroundOpacity));
        saves.Answers[0].SetResult(DraftSaveResult.Saved);
        Assert.Equal(2, saves.Sent.Count);
        Assert.Equal(("small", "#06b6d4", 40), (saves.Sent[1].FontSize, saves.Sent[1].FontColor, saves.Sent[1].BackgroundOpacity));
        saves.Answers[1].SetResult(DraftSaveResult.Saved);
        Assert.False(saves.Draft.Saving); Assert.Equal("#06b6d4", saves.Draft.Confirmed.FontColor);
    }

    [Fact]
    public void ARefusedSaveDropsOnlyItsOwnFieldsAndANewerEditStillGoesOutBuiltFromTheAcknowledgedValue()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xxlarge");
        saves.Edit(s => s.FontColor = "#3b82f6");                // made while the first is in flight
        saves.Answers[0].SetResult(DraftSaveResult.Refused("No."));
        Assert.Equal(("large", "#3b82f6"), (saves.Draft.Value.FontSize, saves.Draft.Value.FontColor));
        Assert.Equal(2, saves.Sent.Count);
        Assert.Equal(("large", "#3b82f6"), (saves.Sent[1].FontSize, saves.Sent[1].FontColor));
    }

    [Fact]
    public void AnUnknownOutcomeIsNeverReplayedDropsQueuedEditsAndLocksTheDraft()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "xlarge"); saves.Edit(s => s.FontColor = "#ef4444");
        saves.Answers[0].SetResult(DraftSaveResult.Unknown("The change may have reached the server."));
        Assert.True(saves.Draft.Locked); Assert.Equal("The change may have reached the server.", saves.Draft.Failure);
        Assert.Single(saves.Sent);                               // neither the unknown write nor the queued edit was sent again
        Assert.Equal(("large", "#ffffff"), (saves.Draft.Value.FontSize, saves.Draft.Value.FontColor));
        saves.Edit(s => s.FontSize = "small");
        Assert.Single(saves.Sent); Assert.Equal("large", saves.Draft.Value.FontSize);   // locked: further edits send nothing
    }

    [Fact]
    public void ASaveThatThrowsIsAnUnknownOutcomeAndIsNotRepeated()
    {
        var calls = 0;
        var draft = AdminSubtitleDraft.Create(Appearance(), _ => { calls++; throw new HttpRequestException("lost"); });
        draft.Edit(s => s.FontSize = "small");
        Assert.True(draft.Locked); Assert.Equal(1, calls); Assert.Equal("large", draft.Value.FontSize);
    }

    [Fact]
    public void DiscardQueuedKeepsTheSaveInFlightButSendsNothingElse()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "small"); saves.Edit(s => s.FontColor = "#000000");
        saves.Draft.DiscardQueued();
        Assert.Equal("#ffffff", saves.Draft.Value.FontColor);
        saves.Answers[0].SetResult(DraftSaveResult.Saved);
        Assert.Single(saves.Sent); Assert.Equal("small", saves.Draft.Confirmed.FontSize);
    }

    [Fact]
    public async Task ClosingDropsUnsentEditsWhileTheSaveInFlightStillFinishes()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "small"); saves.Edit(s => s.FontColor = "#000000");
        saves.Draft.Close();
        var settled = saves.Draft.Settled; Assert.False(settled.IsCompleted);
        saves.Answers[0].SetResult(DraftSaveResult.Saved);
        await settled;
        Assert.Single(saves.Sent); Assert.Equal("small", saves.Draft.Confirmed.FontSize);
        saves.Edit(s => s.FontSize = "xlarge"); Assert.Single(saves.Sent);   // an editor that is gone sends nothing
    }

    [Fact]
    public void ARetiredSaveStopsTheChainWithoutAFailure()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "small"); saves.Edit(s => s.FontColor = "#000000");
        saves.Answers[0].SetResult(DraftSaveResult.Retired);
        Assert.Single(saves.Sent); Assert.Null(saves.Draft.Failure); Assert.False(saves.Draft.Saving);
    }

    [Fact]
    public void ResetStartsFromAFreshServerValueWithPendingEditsOnTop()
    {
        var saves = new Saves(); saves.Edit(s => s.FontSize = "small");
        saves.Draft.Reset(Appearance(color: "#9ca3af"));
        Assert.Equal(("small", "#9ca3af"), (saves.Draft.Value.FontSize, saves.Draft.Value.FontColor));
    }

    [Fact]
    public void ADraftPatchCarriesOnlyTheFieldsThatDiffer()
    {
        var before = Appearance(); var after = before.Clone(); after.Position = "top"; after.TextOutline = true;
        var target = Appearance("small", "#000000", 10); AdminSubtitleDraft.Between(before, after)(target);
        Assert.Equal(("top", true, "small", "#000000", 10), (target.Position, target.TextOutline, target.FontSize, target.FontColor, target.BackgroundOpacity));
    }

    [Fact]
    public void ChangedEventFollowsEverySettledStep()
    {
        var saves = new Saves(); var changes = 0; saves.Draft.Changed += () => changes++;
        saves.Edit(s => s.FontSize = "small"); var afterEdit = changes;
        saves.Answers[0].SetResult(DraftSaveResult.Refused("No."));
        Assert.True(afterEdit >= 1); Assert.True(changes > afterEdit);
    }

    // ---- avatar initials ----

    [Theory]
    [InlineData("alex", "A")] [InlineData("  bob", "B")] [InlineData("Élodie", "É")] [InlineData("山田", "山")]
    public void InitialIsTheFirstCharacterUpperCased(string name, string expected) =>
        Assert.Equal(expected, AdminNameText.Initial(name, CultureInfo.InvariantCulture));


    // Invisible and control characters are built from their code points so the source stays readable and exact.
    private static string C(int codePoint) => char.ConvertFromUtf32(codePoint);
    private static readonly string Zwj = C(0x200D);
    private static readonly string Family = "👨" + Zwj + "👩" + Zwj + "👧";

    [Fact]
    public void InitialKeepsAWholeSurrogatePairInsteadOfSplittingIt()
    {
        var initial = AdminNameText.Initial("😀 Party", CultureInfo.InvariantCulture);
        Assert.Equal("😀", initial); Assert.Equal(2, initial.Length); Assert.False(char.IsLowSurrogate(initial[0]));
        Assert.Equal("𝒜", AdminNameText.Initial("𝒜lice", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void InitialKeepsACombinedEmojiAndAFlagWhole()
    {
        Assert.Equal(Family, AdminNameText.Initial(Family + " Home", CultureInfo.InvariantCulture));
        Assert.Equal("🇫🇷", AdminNameText.Initial("🇫🇷 Paris", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void InitialKeepsACombiningMarkWithItsLetter() =>
        Assert.Equal("E" + C(0x301), AdminNameText.Initial("e" + C(0x301) + "cole", CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")] [InlineData("\t\n")]
    public void InitialOfNothingIsEmptySoTheCallerShowsAQuestionMark(string? name) =>
        Assert.Equal("", AdminNameText.Initial(name, CultureInfo.InvariantCulture));

    [Fact]
    public void InitialFollowsTheInterfaceLanguageSpecialCasing()
    {
        Assert.Equal("İ", AdminNameText.Initial("istanbul", new CultureInfo("tr-TR")));
        Assert.Equal("I", AdminNameText.Initial("istanbul", new CultureInfo("en-US")));
        Assert.Equal("SS", AdminNameText.Initial("ßeta", new CultureInfo("de-DE")));
    }

    [Fact]
    public void InitialReadsTheInterfaceLanguageWhenNoCultureIsGiven()
    {
        var turkish = new LocaleService().Create(AppLocaleResolution.Resolve("tr", [], "tr-TR"));
        using (AppStrings.UseForCurrentFlow(turkish)) Assert.Equal("İ", AdminNameText.Initial("irem"));
        var english = new LocaleService().Create(AppLocaleResolution.Resolve("en", [], "en-US"));
        using (AppStrings.UseForCurrentFlow(english)) Assert.Equal("I", AdminNameText.Initial("irem"));
    }

    [Fact]
    public void LeadingGraphemesCountsReadableCharactersNotCodeUnits()
    {
        Assert.Equal("a😀", AdminNameText.LeadingGraphemes("a😀b", 2));
        Assert.Equal("e" + C(0x301) + "x", AdminNameText.LeadingGraphemes("e" + C(0x301) + "xy", 2));
        Assert.Equal("", AdminNameText.LeadingGraphemes("abc", 0));
        Assert.Equal("abc", AdminNameText.LeadingGraphemes("abc", 10));
    }

    // ---- bounded plain text for confirmations ----

    [Fact]
    public void LabelTurnsControlLineAndDirectionCharactersIntoOneSpace()
    {
        Assert.Equal("Family room", AdminNameText.Label("Family\r\nroom"));
        Assert.Equal("a b", AdminNameText.Label("a" + C(0x2028) + C(0x2029) + C(0x85) + "\t" + "b"));
        Assert.Equal("Anna bob", AdminNameText.Label("Anna" + C(0x202E) + C(0x2066) + " bob" + C(0x2069) + C(0x202C)));
        Assert.Equal("a b", AdminNameText.Label("a" + C(0x200E) + "b"));
        Assert.Equal("a b", AdminNameText.Label("  a    b  "));
    }

    [Fact]
    public void SingleLineReadsEachLineBreakAsOneSpaceAndChangesNothingElse()
    {
        Assert.Equal("a b c d e", AdminNameText.SingleLine("a
b
c" + C(0x2028) + "d" + C(0x2029) + "e"));
        Assert.Equal("a  b", AdminNameText.SingleLine("a

b"));
        var directed = "x" + C(0x202E) + "y";
        Assert.Equal(directed, AdminNameText.SingleLine(directed));      // direction characters are shown as they are, as a page shows them
        Assert.Equal("", AdminNameText.SingleLine(null)); Assert.Equal("plain", AdminNameText.SingleLine("plain"));
    }

    [Fact]
    public void LabelKeepsEmojiJoinersAndLeavesNoDirectionCharactersBehind()
    {
        Assert.Equal(Family + " x", AdminNameText.Label(Family + C(0x202E) + " x"));
        var forbidden = new[] { 0x200E, 0x200F, 0x061C, 0x202A, 0x202B, 0x202C, 0x202D, 0x202E, 0x2066, 0x2067, 0x2068, 0x2069, 0x2028, 0x2029, 0x0A, 0x0D, 0x00, 0x85 }.Select(C).ToArray();
        var text = AdminNameText.Label(string.Concat(forbidden.Select(c => "x" + c)) + "end");
        Assert.DoesNotContain(text, c => forbidden.Contains(c.ToString()));
        Assert.StartsWith("x x x", text); Assert.EndsWith("end", text);
    }

    [Fact]
    public void LabelBoundsLengthAtAWholeCharacterAndEndsWithAnEllipsis()
    {
        var long80 = new string('a', AdminNameText.MaxLabelElements);
        Assert.Equal(long80, AdminNameText.Label(long80));
        Assert.Equal(long80 + "…", AdminNameText.Label(long80 + "b"));
        Assert.Equal("😀😀😀😀😀…", AdminNameText.Label(string.Concat(Enumerable.Repeat("😀", 100)), 5));
        Assert.Equal(Family + Family + "…", AdminNameText.Label(string.Concat(Enumerable.Repeat(Family, 9)), 2));
        Assert.Equal("", AdminNameText.Label(null)); Assert.Equal("", AdminNameText.Label(C(0x202E) + "\n"));
    }

    // ---- an email cut short keeps its start ----

    [Theory]
    [InlineData("alex@example.com", true)]
    [InlineData("אלכס@example.com", false)]
    [InlineData("علي@example.com", false)]
    [InlineData("1234@example.com", true)]
    [InlineData("٠١٢٣@مثال.com", false)]
    [InlineData("٠١٢٣@example.com", true)]
    [InlineData("", true)]
    public void EmailKeepsLeftToRightUnlessItStartsInARightToLeftScript(string email, bool leftToRight) =>
        Assert.Equal(leftToRight, AdminNameText.EmailIsLeftToRight(email));
}
