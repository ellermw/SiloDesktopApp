using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

/// <summary>The minimized/native-shell surface for the same actions as the full-screen OSC.</summary>
public sealed class BufferingQualityPillControl : UserControl
{
    private readonly PlayerService _player = App.Services.GetRequiredService<PlayerService>();
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _action = new();
    private readonly Button _keep = new() { Content = "Keep quality" };
    public BufferingQualityPillControl()
    {
        MaxWidth = 380;
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(12) };
        panel.Children.Add(_text); var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(_action); row.Children.Add(_keep); panel.Children.Add(row);
        Content = new Border { Child = panel, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardOverlayBorderBrush"] };
        _action.Click += async (_, _) => { if (_player.BufferingQualityPill == "undo") await _player.RestoreBufferingQualityAsync(); else await _player.LowerBufferingQualityAsync(); };
        _keep.Click += (_, _) => _player.KeepBufferingQuality();
        KeyDown += (_, e) => { if (e.Key is Windows.System.VirtualKey.Escape or Windows.System.VirtualKey.GoBack or Windows.System.VirtualKey.GamepadB) { _player.KeepBufferingQuality(); e.Handled = true; } };
        Loaded += (_, _) => { _player.BufferingQualityChanged += Render; _player.StateChanged += StateChanged; Render(); };
        Unloaded += (_, _) => { _player.BufferingQualityChanged -= Render; _player.StateChanged -= StateChanged; };
    }
    private void StateChanged(PlayerState state) => Render();
    private void Render()
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(Render); return; }
        var pill = _player.BufferingQualityPill;
        Visibility = pill == null || _player.State != PlayerState.Minimized ? Visibility.Collapsed : Visibility.Visible;
        _text.Text = pill == "undo" ? "Quality lowered to stop buffering" : pill == "cant_lower" ? "Can't lower quality for this video" : pill == "lowered" ? "Quality lowered to stop buffering" : "Playback keeps buffering";
        _action.Content = pill == "undo" ? "Restore quality" : "Lower quality";
        _action.Visibility = pill is "prompt" or "undo" ? Visibility.Visible : Visibility.Collapsed;
        _keep.Visibility = pill == "prompt" ? Visibility.Visible : Visibility.Collapsed;
    }
}
