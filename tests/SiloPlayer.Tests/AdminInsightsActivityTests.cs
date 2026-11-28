using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminInsightsActivityTests
{
    [Theory]
    [InlineData(null, null, AdminActivityLayout.Table)]
    [InlineData(null, 0, AdminActivityLayout.Cards)]
    [InlineData(null, 8, AdminActivityLayout.Cards)]
    [InlineData(null, 9, AdminActivityLayout.Table)]
    [InlineData("cards", 12, AdminActivityLayout.Cards)]
    [InlineData("table", 3, AdminActivityLayout.Table)]
    [InlineData("invalid", 3, AdminActivityLayout.Cards)]
    public void LayoutUsesSavedChoiceOrCount(string? saved, int? loaded, AdminActivityLayout expected) =>
        Assert.Equal(expected, AdminActivityLayoutRules.Resolve(AdminActivityLayoutRules.Parse(saved), loaded));

    [Theory]
    [InlineData("applied", "acknowledged", false, false, "Message sent")]
    [InlineData("applied", "dispatched", true, false, "The player didn’t confirm the message. It may show up late or not at all.")]
    [InlineData("replayed", "dispatched", true, false, "The player didn’t confirm the message. It may show up late or not at all.")]
    [InlineData("replayed", "acknowledged", false, true, "Your earlier message request was already accepted. Send again to send a new message.")]
    [InlineData("applied", "", true, false, "The player didn’t confirm the message. It may show up late or not at all.")]
    public void MessageReceiptDecidesToastAndDialog(string outcome, string delivery, bool unconfirmed, bool keepOpen, string text)
    {
        var result = AdminActivityCommandText.FromReceipt("message", new() { Outcome = outcome, Delivery = delivery });
        Assert.Equal((unconfirmed, keepOpen, text), (result.Unconfirmed, result.KeepMessageOpen, result.Message));
    }

    [Theory]
    [InlineData("ATSC A/52B (AC-3, E-AC-3)", "en", "English · AAC 2.0")]
    [InlineData("DCA (DTS Coherent Acoustics)", "en", "English · AAC 2.0")]
    [InlineData("MP2 (MPEG audio layer 2)", "eng", "English · AAC 2.0")]
    [InlineData("PCM 32-bit floating point little-endian", null, "AAC 2.0")]
    [InlineData("1", "en", "1 · AAC 2.0")]
    [InlineData("Director commentary AAC", "en", "Director commentary AAC · AAC 2.0")]
    [InlineData(null, "en", "English · AAC 2.0")]
    public void AudioSummaryLeadsWithTitleOrLanguage(string? title, string? language, string expected)
    {
        var session = new AdminActivitySession
        {
            SourceAudioTitle = title, SourceAudioLanguage = language, SourceAudioCodec = "aac", SourceAudioChannels = 2,
        };
        Assert.Equal(expected, AdminActivityPresentation.FormatAudioSummary(session));
    }

    [Theory]
    [InlineData(AdminInsightsTab.Activity, false, false, false, false)]
    [InlineData(AdminInsightsTab.Playback, true, true, true, true)]
    [InlineData(AdminInsightsTab.Titles, true, true, true, true)]
    [InlineData(AdminInsightsTab.Library, true, true, true, true)]
    [InlineData(AdminInsightsTab.People, true, true, false, true)]
    [InlineData(AdminInsightsTab.Devices, true, true, true, true)]
    [InlineData(AdminInsightsTab.Bandwidth, true, false, false, false)]
    public void SharedToolbarVisibility(AdminInsightsTab tab, bool toolbar, bool library, bool account, bool zone)
    {
        Assert.Equal((toolbar, library, account, zone), (tab.ShowsToolbar(), tab.ShowsToolbar() && tab.ShowsLibraryFilter(),
            tab.ShowsAccountFilter(), tab.ShowsToolbar() && tab.ShowsTimezoneFilter()));
    }

    [Fact]
    public void FullLocationRoundTripsAndResetKeepsTheTab()
    {
        var today = new DateOnly(2026, 11, 26);
        const string local = "America/Chicago";
        var request = AdminInsightsFilters.WithTab(new(AdminRoute.Insights), AdminInsightsTab.People);
        request = AdminInsightsFilters.WithRange(request, AdminInsightsRange.Custom, today);
        request = AdminInsightsFilters.WithTimezone(request, "UTC", local);
        request = AdminInsightsFilters.WithLibrary(request, 4);
        request = AdminInsightsFilters.WithAccount(request, 9);
        request = AdminInsightsFilters.WithPerson(request, new(9, "profile-2"));
        request = AdminInsightsFilters.WithTitle(request, "item-3");
        var state = AdminInsightsFilterState.Parse(request, local, today);
        var details = AdminInsightsFilters.ParseDetail(request);
        Assert.Equal("2026-10-28", state.FromDate);
        Assert.Equal("2026-11-26", state.ToDate);
        Assert.Equal(request, state.ToQuery(local, details).ToRequest());
        Assert.Null(state.ForDetail().UserId);
        Assert.True(state.HasNonDefaultFilters(local));
        Assert.Equal("/admin/insights?tab=people", AdminInsightsFilters.Reset(request, state.Tab).Href);
        var personRequest = AdminInsightsFilters.CloseForDetail(request);
        Assert.Equal(details.Person, AdminInsightsFilters.ParseDetail(personRequest).Person);
        Assert.False(AdminInsightsFilters.ParseDetail(AdminInsightsFilters.CloseForDetail(personRequest)).IsOpen);
    }
}
