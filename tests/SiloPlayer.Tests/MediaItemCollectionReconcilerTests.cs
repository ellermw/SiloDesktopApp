using System.Collections.ObjectModel;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class MediaItemCollectionReconcilerTests
{
    [Fact]
    public void ChangedResultReplacesOnlyThatItemWithoutResettingTheCollection()
    {
        var unchanged = Item("movie-1", "Movie 1");
        var changed = Item("movie-2", "Movie 2");
        var current = new ObservableCollection<MediaItem>([unchanged, changed]);
        var incomingChanged = Item("movie-2", "Movie 2");
        incomingChanged.PositionSeconds = 120;

        var didChange = MediaItemCollectionReconciler.Apply(
            current,
            [Item("movie-1", "Movie 1"), incomingChanged]);

        Assert.True(didChange);
        Assert.Same(unchanged, current[0]);
        Assert.Same(incomingChanged, current[1]);
    }

    [Fact]
    public void IdenticalResultsKeepMountedItemInstances()
    {
        var first = Item("movie-1", "Movie 1");
        var second = Item("movie-2", "Movie 2");
        var current = new ObservableCollection<MediaItem>([first, second]);

        var didChange = MediaItemCollectionReconciler.Apply(
            current,
            [Item("movie-1", "Movie 1"), Item("movie-2", "Movie 2")]);

        Assert.False(didChange);
        Assert.Same(first, current[0]);
        Assert.Same(second, current[1]);
    }

    [Theory]
    [InlineData("pending-language")]
    [InlineData("translation-fields")]
    [InlineData("poster-provenance")]
    [InlineData("backdrop-provenance")]
    [InlineData("in-progress")]
    public void PresentationStateChangePublishesEvenWhenTextAndArtworkAreIdentical(string field)
    {
        var mounted = Item("episode", "Unchanged title"); mounted.Type = "episode";
        mounted.PosterUrl = "https://fixture.invalid/poster.jpg";
        mounted.BackdropUrl = "https://fixture.invalid/backdrop.jpg";
        mounted.UserState = new() { Played = false, IsInProgress = false };
        var incoming = Item("episode", "Unchanged title"); incoming.Type = "episode";
        incoming.PosterUrl = mounted.PosterUrl; incoming.BackdropUrl = mounted.BackdropUrl;
        incoming.UserState = new() { Played = false, IsInProgress = false };
        switch (field)
        {
            case "pending-language": incoming.PendingTranslationLanguage = "fr"; break;
            case "translation-fields": incoming.MachineTranslatedFields = ["overview"]; break;
            case "poster-provenance": incoming.PosterIsEpisodeStill = true; break;
            case "backdrop-provenance": incoming.BackdropIsEpisodeStill = false; break;
            case "in-progress": incoming.UserState.IsInProgress = true; break;
        }
        var current = new ObservableCollection<MediaItem>([mounted]);
        var notifications = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        current.CollectionChanged += (_, args) => notifications.Add(args.Action);
        Assert.True(MediaItemCollectionReconciler.Apply(current, [incoming]));
        Assert.Same(incoming, current[0]);
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Replace], notifications);
        Assert.False(MediaItemCollectionReconciler.Apply(current, [incoming]));
        Assert.Single(notifications);
    }

    [Fact]
    public void EquivalentNewPresentationFieldsKeepMountedReferenceForFreshResponses()
    {
        MediaItem Create() => new()
        {
            ContentId = "episode", Type = "episode", Title = "Unchanged title",
            PendingTranslationLanguage = "fr", MachineTranslatedFields = ["overview"],
            PosterIsEpisodeStill = true, BackdropIsEpisodeStill = false,
            UserState = new() { IsInProgress = true },
        };
        var mounted = Create(); var current = new ObservableCollection<MediaItem>([mounted]);
        Assert.False(MediaItemCollectionReconciler.Apply(current, [Create()]));
        Assert.Same(mounted, current[0]);
    }

    private static MediaItem Item(string id, string title) => new()
    {
        ContentId = id,
        Type = "movie",
        Title = title,
    };
}
