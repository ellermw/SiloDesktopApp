using SiloPlayer.Core.Api;
using SiloPlayer.Helpers;

namespace SiloPlayer.Tests;

/// <summary>
/// Collection and person pages are kept for Back. Back may show a kept page as it was only for the
/// same entry under the same account, profile and access, and rereads it in place once something
/// changed while it was away or it is older than the WebUI's ten-minute catalog staleTime.
/// </summary>
public sealed class KeptPageEntryTests
{
    private static readonly DateTime Start = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ApiRequestContext Viewer = new(1, 1, "https://kept.invalid", "profile-a");

    [Fact]
    public void BackToTheSameEntryUnderTheSameIdentityKeepsThePage()
    {
        var parameter = new object();
        var entry = new KeptPageEntry();
        entry.Keep(parameter, Viewer, Start);

        Assert.True(entry.Holds(parameter, Viewer));
        Assert.False(entry.NeedsRefresh(Start.AddMinutes(9)));
    }

    [Fact]
    public void ANewEntryIsNotHeldEvenWithEqualContents()
    {
        var entry = new KeptPageEntry();
        entry.Keep(new object(), Viewer, Start);

        Assert.False(entry.Holds(new object(), Viewer));
        Assert.False(entry.Holds(null, Viewer));
    }

    [Fact]
    public void AStringParameterMatchesByValue()
    {
        var entry = new KeptPageEntry();
        entry.Keep("person-7", Viewer, Start);

        Assert.True(entry.Holds(string.Concat("person-", "7"), Viewer));
        Assert.False(entry.Holds("person-8", Viewer));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("account")]
    [InlineData("access")]
    [InlineData("server")]
    public void AnotherIdentityNeverGetsTheKeptPage(string change)
    {
        var parameter = new object();
        var entry = new KeptPageEntry();
        entry.Keep(parameter, Viewer, Start);
        var current = change switch
        {
            "profile" => Viewer with { ProfileId = "profile-b" },
            "account" => Viewer with { AuthenticationGeneration = 2 },
            "access" => Viewer with { RequestContextGeneration = 2 },
            _ => Viewer with { BaseUrl = "https://other.invalid" },
        };

        Assert.False(entry.Holds(parameter, current));
    }

    [Fact]
    public void AChangeWhileAwayRereadsInPlace()
    {
        var entry = new KeptPageEntry();
        entry.Keep("person-7", Viewer, Start);
        entry.MarkStale();

        Assert.True(entry.Holds("person-7", Viewer));
        Assert.True(entry.NeedsRefresh(Start.AddSeconds(5)));

        entry.Keep("person-7", Viewer, Start.AddSeconds(6));
        Assert.False(entry.NeedsRefresh(Start.AddSeconds(7)));
    }

    [Fact]
    public void AKeptPageOlderThanTheCatalogStaleTimeRereadsInPlace()
    {
        var entry = new KeptPageEntry();
        entry.Keep("person-7", Viewer, Start);

        Assert.False(entry.NeedsRefresh(Start + KeptPageEntry.FreshFor - TimeSpan.FromSeconds(1)));
        Assert.True(entry.NeedsRefresh(Start + KeptPageEntry.FreshFor));
    }

    [Fact]
    public void AForgottenEntryIsNeverHeldAndCannotBeMarkedStale()
    {
        var entry = new KeptPageEntry();
        entry.MarkStale();
        Assert.False(entry.HasEntry);

        entry.Keep("person-7", Viewer, Start);
        entry.Forget();

        Assert.False(entry.HasEntry);
        Assert.False(entry.Holds("person-7", Viewer));
    }
}
