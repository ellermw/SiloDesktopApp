using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using System.Text.Json;

namespace SiloPlayer.Views.Dialogs;

/// <summary>Filtered admin playback log reached from an ordinary media-card menu.</summary>
public sealed class PlayHistoryDialog : ContentDialog
{
    private readonly string _id;
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly Button _more = new() { Content = "Load more" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SiloApiClient _client;
    private readonly ApiRequestContext _context;
    private string? _cursor;
    private readonly HashSet<string> _seen = [];
    private bool _loading;
    public PlayHistoryDialog(string contentId)
    {
        _id = contentId; _client = App.Services.GetRequiredService<SiloApiClient>(); _context = _client.CaptureContext();
        Title = "Play History"; CloseButtonText = "Close";
        var body = new StackPanel { Spacing = 16 }; body.Children.Add(_status); body.Children.Add(_rows); body.Children.Add(_more);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = scroll; Opened += async (_, _) => { body.Width = Math.Max(220, Math.Min(850, (XamlRoot?.Size.Width ?? 1000) - 80)); scroll.MaxHeight = (XamlRoot?.Size.Height ?? 800) * .75; await Load(); };
        _more.Click += async (_, _) => await Load(); Closed += (_, _) => _lifetime.Cancel();
    }
    private async Task Load()
    {
        if (_loading || !AuthorizationPolicy.IsActingAdmin(App.Services.GetRequiredService<AuthService>())) return;
        _loading = true; _more.IsEnabled = false; _status.Text = "Loading…";
        try
        {
            if (!_client.IsCurrentContext(_context)) throw new OperationCanceledException();
            var result = await _client.GetAsync<JsonElement>("/api/v2/admin/playback-history?limit=50&media_item_id=" + Uri.EscapeDataString(_id) + (_cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(_cursor)), _lifetime.Token);
            if (!_client.IsCurrentContext(_context)) throw new OperationCanceledException();
            foreach (var row in result.GetProperty("items").EnumerateArray())
            {
                string Value(string key) => row.TryGetProperty(key, out var value) ? value.ToString() : "";
                var content = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 16 };
                content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var text = new TextBlock { Text = Value("username") + " · " + Value("profile_name") + "\n" + Value("media_title"), TextWrapping = TextWrapping.Wrap, FontSize = 14 };
                var info = new TextBlock { Text = Value("started_at") + "\n" + Value("play_method") + " · " + Value("watched_seconds") + " seconds", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
                content.Children.Add(text); Grid.SetColumn(info, 1); content.Children.Add(info);
                _rows.Children.Add(new Border { Child = content, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = (Brush)Application.Current.Resources["BorderBrush"] });
            }
            var page = result.GetProperty("page"); var hasMore = page.GetProperty("has_more").GetBoolean();
            _cursor = page.TryGetProperty("next_cursor", out var cursor) ? cursor.GetString() : null;
            if (hasMore && (string.IsNullOrEmpty(_cursor) || !_seen.Add(_cursor))) throw new InvalidOperationException("Playback history changed. Close and reopen to try again.");
            _more.Visibility = hasMore ? Visibility.Visible : Visibility.Collapsed;
            _status.Text = _rows.Children.Count == 0 ? "No playback history." : "";
        }
        catch (OperationCanceledException) { _more.Visibility = Visibility.Collapsed; }
        catch (Exception ex) { _status.Text = ex.Message; _more.Content = "Retry"; _more.Visibility = Visibility.Visible; }
        finally { _loading = false; _more.IsEnabled = true; }
    }
}
