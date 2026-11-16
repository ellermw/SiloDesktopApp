using Microsoft.UI.Xaml.Input;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer;

/// <summary>
/// Official Silo Server #4296 (385d90755): the expanded audiobook player
/// (Now Listening) covers the shell, so the covered navigation, page content
/// and shell controls are inert while it is open, focus starts on its Back
/// control, and collapsing it returns focus to the mini-player cover tile that
/// opened it. Every player state change resynchronises the scope, so stop,
/// picture-in-picture and a video takeover restore the shell too.
/// </summary>
public sealed partial class MainWindow
{
    private readonly NowListeningFocusScope _nowListeningFocus = new();

    /// <summary>
    /// The focused element before the mini player collapses for Now Listening.
    /// Null while Now Listening is already open, so a repeated expand keeps the
    /// original opener.
    /// </summary>
    private DependencyObject? CaptureNowListeningOpener() =>
        _nowListeningFocus.IsOpen || Content?.XamlRoot is not { } root
            ? null
            : FocusManager.GetFocusedElement(root) as DependencyObject;

    private void SyncNowListeningFocusScope(PlayerState state, DependencyObject? opener)
    {
        if (AudiobookNowListeningControl.Visibility == Visibility.Visible)
        {
            _nowListeningFocus.Open(RootGrid, AudiobookNowListeningControl, opener,
                AudiobookNowListeningControl.FocusCollapseButton);
            return;
        }

        // Collapse returns focus to the mini player's cover tile (upstream
        // autoFocusExpand). Stop has no mini player: fall back to the opener,
        // then the current page. A video takeover or PiP moves focus itself.
        _nowListeningFocus.Close(AudiobookNowListeningControl,
            returnFocus: state is PlayerState.Minimized or PlayerState.Idle,
            preferred: MiniPlayerBarControl.FocusNowListeningOpener,
            fallbackScope: ContentFrame);
    }
}
