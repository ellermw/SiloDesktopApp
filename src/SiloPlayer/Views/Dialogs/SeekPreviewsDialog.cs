using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using System.Text.Json;

namespace SiloPlayer.Views.Dialogs;

public sealed class SeekPreviewsDialog : ItemScopedDialog
{
    private readonly MediaMarkerApi _api;
    private readonly IReadOnlyDictionary<string, string> _fileNames;
    private readonly StackPanel _files = new() { Spacing = 8 };
    private readonly TextBlock _off;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _allOff, _reading;
    private string? _rendered;
    public SeekPreviewsDialog(string id, IReadOnlyDictionary<string, string> fileNames) : base(id, "Seek Previews", "The thumbnails players show while seeking, for each of this item's files.", 512, "Make Again", AuthorizationPolicy.IsActingAdmin)
    {
        _api = new(Client); _fileNames = fileNames; CloseButtonText = "Close";
        Body.Children.Add(new ScrollViewer { Content = _files, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _off = Text("Turn on Generate seek previews in the library's settings to make them.", 12, true); _off.Visibility = Visibility.Collapsed; Body.Children.Add(_off);
        _poll.Tick += async (_, _) => { if (_reading || Busy || !CanAct) return; _reading = true; try { await LoadAsync(); } catch (OperationCanceledException) { _poll.Stop(); } catch (Exception error) { Status.Text = error.Message; Ready = false; _poll.Stop(); } finally { _reading = false; UpdateCommands(); } };
        Closed += (_, _) => _poll.Stop();
    }
    protected override async Task LoadAsync()
    {
        var response = await _api.GetPreviewsAsync(Context, ItemId, Lifetime.Token); RequireAuthority();
        var files = response.GetProperty("files").EnumerateArray().ToArray(); _allOff = files.Length > 0 && files.All(file => Value(file, "state") == "off"); _off.Visibility = _allOff ? Visibility.Visible : Visibility.Collapsed;
        if (files.Any(file => Value(file, "state") is "pending" or "running")) _poll.Start(); else _poll.Stop();
        if (_rendered == response.ToString()) return; _rendered = response.ToString(); _files.Children.Clear();
        foreach (var file in files)
        {
            var state = Value(file, "state"); var failures = Number(file, "failures");
            var stateText = state == "pending" && failures > 0 ? $"Retrying after {failures:0} {(failures == 1 ? "failure" : "failures")}" : state switch { "off" => "Off for this library", "pending" => "Waiting", "running" => "In progress", "ready" => "Ready", "unusable" => "Failed", _ => state };
            var row = new StackPanel { Spacing = 4 }; var head = new Grid { ColumnSpacing = 12 };
            head.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var id = Value(file, "file_id"); var label = Text(_fileNames.GetValueOrDefault(id, "File " + id)); label.TextTrimming = TextTrimming.CharacterEllipsis; label.TextWrapping = TextWrapping.NoWrap;
            head.Children.Add(label); var status = Text(stateText, 14, true); if (state == "unusable") status.Foreground = Brush("ErrorBrush"); Grid.SetColumn(status, 1); head.Children.Add(status); row.Children.Add(head);
            var summary = new List<string>(); var count = Number(file, "thumbnail_count"); if (count > 0) { summary.Add($"{count:0} previews"); var width = Number(file, "thumbnail_width"); var interval = Number(file, "interval_ms"); if (width > 0 && interval > 0) summary.Add($"{width:0} px every {interval / 1000:0.###} s"); var bytes = Number(file, "sheet_bytes"); if (bytes > 0) summary.Add(Size(bytes)); }
            if (DateTimeOffset.TryParse(Value(file, "generated_at"), out var generated)) summary.Add("made " + DateTimeDisplay.FormatDateTime(generated));
            if (summary.Count > 0) row.Children.Add(Text(string.Join(" · ", summary), 12, true));
            if (file.TryGetProperty("servable", out var available) && available.ValueKind == JsonValueKind.True && state is "pending" or "running") row.Children.Add(Text("Players keep the current previews until the new ones are ready.", 12, true));
            if (Value(file, "last_error") is { Length: > 0 } error) { var text = Text(error, 12); text.Foreground = Brush("ErrorBrush"); row.Children.Add(text); }
            _files.Children.Add(new Border { Child = row, Padding = new Thickness(12, 8, 12, 8), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) });
        }
    }
    protected override async Task SubmitAsync()
    {
        RequireAuthority(); if (_allOff) return;
        var response = await _api.RegeneratePreviewsAsync(Context, ItemId, Lifetime.Token); RequireAuthority();
        await LoadAsync(); var count = Number(response, "requeued"); Status.Text = count == 0 ? "No seek previews were queued" : $"Seek previews queued for {count:0} {(count == 1 ? "file" : "files")}";
    }
    protected override void UpdateCommands() => IsPrimaryButtonEnabled = Ready && CanAct && !Busy && !_allOff;
    private static string Value(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";
    private static double Number(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : 0;
    private static string Size(double bytes) { var units = new[] { "B", "KB", "MB", "GB", "TB" }; var i = 0; while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; } return $"{bytes:0.#} {units[i]}"; }
}
