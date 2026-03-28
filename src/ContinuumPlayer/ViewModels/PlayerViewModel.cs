using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.ViewModels;

public partial class PlayerViewModel : ObservableObject
{
    public PlaybackManager Manager { get; }

    public PlayerViewModel(PlaybackManager manager)
    {
        Manager = manager;
    }

    // ── Observable properties ────────────────────────────────────────────

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private double _position;

    [ObservableProperty]
    private double _duration;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private double _volume = 100;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _showSkipIntro;

    [ObservableProperty]
    private bool _showSkipCredits;

    [ObservableProperty]
    private string _playMethod = "";

    [ObservableProperty]
    private string _resolution = "";

    // ── Collections ──────────────────────────────────────────────────────

    public ObservableCollection<SubtitleTrackInfo> SubtitleTracks { get; } = [];
    public ObservableCollection<FileVersion> Versions { get; } = [];

    // ── Derived display properties ───────────────────────────────────────

    public string PositionDisplay => FormatTime(Position);
    public string DurationDisplay => FormatTime(Duration);
    public double SeekMax => Duration > 0 ? Duration : 1;

    // ── Helpers ──────────────────────────────────────────────────────────

    public void UpdatePosition(double pos)
    {
        Position = pos;
        OnPropertyChanged(nameof(PositionDisplay));

        Manager.UpdatePosition(pos, !IsPlaying);
        CheckSkipMarkers(pos);
    }

    public void UpdateDuration(double dur)
    {
        Duration = dur;
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(SeekMax));
    }

    public static string FormatTime(double totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    private void CheckSkipMarkers(double pos)
    {
        var intro = Manager.WatchDetail?.Intro;
        ShowSkipIntro = intro != null && pos >= intro.Start && pos < intro.End;

        var credits = Manager.WatchDetail?.Credits;
        ShowSkipCredits = credits != null && pos >= credits.Start && pos < credits.End;
    }

    public double? IntroEnd => Manager.WatchDetail?.Intro?.End;
    public double? CreditsEnd => Manager.WatchDetail?.Credits?.End;
}
