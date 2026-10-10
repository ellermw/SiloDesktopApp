using Microsoft.Extensions.DependencyInjection;

namespace SiloPlayer;

public sealed partial class MainWindow
{
    private bool _shufflePickPending;
    private void OnShuffleChanged() => DispatcherQueue.TryEnqueue(UpdateShufflePostRoll);
    private void OnShuffleFailed(string message) => DispatcherQueue.TryEnqueue(() =>
        App.Services.GetRequiredService<Services.ToastService>().Error(message));

    private void UpdateShufflePostRoll()
    {
        var active = _playerService.IsShufflePlayback;
        PlayingNextShuffleScope.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        PlayingNextShuffleActions.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        PlayingNextFinishedDescription.Text = active ? "There are no more playable titles in this shuffle." : "There are no more episodes available. Pick something else from On Deck below.";
        if (!active || PlayingNextOverlay.Visibility != Visibility.Visible) return;
        PlayingNextShuffleScope.Text = "Shuffling " + _playerService.ShuffleScopeLabel;
        var hasNext = _playerService.HasNextEpisodeForCurrentPlayback;
        PlayingNextLabelText.Text = hasNext ? "PLAYING NEXT" : "FINISHED";
        PlayingNextEpisodePanel.Visibility = hasNext ? Visibility.Visible : Visibility.Collapsed;
        PlayingNextFinishedPanel.Visibility = hasNext ? Visibility.Collapsed : Visibility.Visible;
        PlayingNextTitleText.Text = _playerService.NextEpisodeTitle ?? "";
        PlayingNextSeriesText.Text = _playerService.NextEpisodeSeriesTitle ?? "";
        PlayingNextOverviewText.Text = _playerService.NextEpisodeOverview ?? "";
        PlayingNextOverviewText.Visibility = string.IsNullOrWhiteSpace(PlayingNextOverviewText.Text) ? Visibility.Collapsed : Visibility.Visible;
        PlayingNextMetaText.Text = FormatPlayingNextMeta(_playerService.NextEpisodeAirDate, _playerService.NextEpisodeRuntime);
        PlayingNextMetaText.Visibility = string.IsNullOrWhiteSpace(PlayingNextMetaText.Text) ? Visibility.Collapsed : Visibility.Visible;
        PlayingNextPlayNowButton.IsEnabled = PlayingNextPickAnother.IsEnabled = hasNext && !_shufflePickPending;
        PlayingNextStopShuffle.IsEnabled = !_shufflePickPending;
        if (hasNext) _ = LoadPlayingNextPosterAsync();
        else StopPlayingNextCountdown();
        UpdatePlayingNextAutoPlayVisuals();
    }

    private async void PlayingNextPickAnother_Click(object sender, RoutedEventArgs e)
    {
        if (_shufflePickPending || !_playerService.IsShufflePlayback) return;
        var owner = _playerService.ActiveShuffleId;
        _shufflePickPending = true; StopPlayingNextCountdown(); UpdateShufflePostRoll();
        try { await _playerService.PickAnotherShuffleItemAsync(); }
        finally
        {
            _shufflePickPending = false;
            if (owner == _playerService.ActiveShuffleId && PlayingNextOverlay.Visibility == Visibility.Visible)
            {
                _playingNextRemaining = PlayingNextCountdownSeconds;
                PlayingNextCountdownText.Text = $"{_playingNextRemaining}s";
                PlayingNextCountdownRing.Value = _playingNextRemaining;
                UpdateShufflePostRoll();
                if (_playingNextAutoPlay && _playerService.HasNextEpisodeForCurrentPlayback && _playerService.IsPostRollVideoEnded) StartPlayingNextCountdown();
            }
        }
    }

    private async void PlayingNextStopShuffle_Click(object sender, RoutedEventArgs e)
    {
        if (_shufflePickPending || !_playerService.IsShufflePlayback) return;
        StopPlayingNextCountdown(); PlayingNextOverlay.Visibility = Visibility.Collapsed;
        await _playerService.StopShuffleAsync();
    }
}
