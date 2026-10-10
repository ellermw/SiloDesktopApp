using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using System.Text.Json;

namespace SiloPlayer.Views.Dialogs;

public sealed class MarkerEditorDialog : ItemScopedDialog
{
    private readonly Dictionary<string, (TextBox Start, TextBox End, Button Clear)> _fields = [];
    private JsonElement _original;
    private readonly MediaMarkerApi _api;
    private readonly StackPanel _history = new() { Spacing = 0 };
    private readonly StackPanel _historySection = new() { Spacing = 8 };
    public MarkerEditorDialog(string id) : base(id, "Edit markers", "Set intro, recap, credits, and preview times. Use m:ss or h:mm:ss; leave both fields empty to remove a marker. Removed markers stay absent through detection and rescans until you set them again.", 672, "Save", AuthorizationPolicy.CanEditMarkers)
    {
        _api = new(Client); var rows = new StackPanel { Spacing = 12, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var kind in MarkerEditPlan.Kinds)
        {
            var row = new Grid { ColumnSpacing = 8 };
            foreach (var width in new[] { new GridLength(88), new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(36) }) row.ColumnDefinitions.Add(new() { Width = width });
            var label = Text(MarkerEditPlan.Label(kind)); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
            var start = Input(kind, "start"); var end = Input(kind, "end");
            var clear = new Button { Content = WebUiIcon.Create("trash-2", 16), Width = 36, Height = 36, MinWidth = 0, MinHeight = 0, Padding = new Thickness(10), Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
            AutomationProperties.SetName(clear, "Clear " + MarkerEditPlan.Label(kind)); clear.Click += (_, _) => { start.Text = end.Text = ""; Status.Text = ""; };
            Grid.SetColumn(start, 1); Grid.SetColumn(end, 2); Grid.SetColumn(clear, 3); row.Children.Add(start); row.Children.Add(end); row.Children.Add(clear); rows.Children.Add(row); _fields.Add(kind, (start, end, clear));
        }
        Body.Children.Add(rows);
        _historySection.Children.Add(Text("Recent changes")); _historySection.Children.Add(new ScrollViewer { Content = _history, MaxHeight = 192, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Body.Children.Add(new Border { Child = _historySection, BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0), Visibility = AuthorizationPolicy.IsActingAdmin(Auth) ? Visibility.Visible : Visibility.Collapsed });
    }
    private TextBox Input(string kind, string edge)
    {
        var input = new TextBox { PlaceholderText = edge, Height = 36, MinHeight = 0, MinWidth = 0, FontSize = 14, Padding = new Thickness(12, 6, 12, 6) };
        AutomationProperties.SetName(input, MarkerEditPlan.Label(kind) + " " + edge); input.TextChanged += (_, _) => Status.Text = ""; return input;
    }
    protected override async Task LoadAsync()
    {
        _original = await _api.GetMarkersAsync(Context, ItemId, Lifetime.Token); RequireAuthority();
        foreach (var (kind, field) in _fields)
        {
            var segment = _original.TryGetProperty(kind, out var marker) ? marker : default;
            field.Start.Text = MarkerEditPlan.Format(MarkerEditPlan.Edge(segment, "start")); field.End.Text = MarkerEditPlan.Format(MarkerEditPlan.Edge(segment, "end"));
        }
        if (AuthorizationPolicy.IsActingAdmin(Auth)) _ = LoadHistoryAsync();
    }
    private async Task LoadHistoryAsync()
    {
        _history.Children.Clear(); _history.Children.Add(Text("Loading…", 13, true));
        try
        {
            var response = await _api.GetHistoryAsync(Context, ItemId, Lifetime.Token); RequireAuthority(); if (!AuthorizationPolicy.IsActingAdmin(Auth)) return;
            _history.Children.Clear(); foreach (var row in response.GetProperty("history").EnumerateArray())
            {
                string Value(string key) => row.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";
                var created = DateTimeOffset.TryParse(Value("created_at"), out var time) ? DateTimeDisplay.FormatShortDateTime(time) : Value("created_at");
                string Range(string key) { var marker = row.TryGetProperty(key, out var value) ? value : default; var start = MarkerEditPlan.Edge(marker, "start"); var end = MarkerEditPlan.Edge(marker, "end"); return start == null || end == null ? "none" : MarkerEditPlan.Format(start) + "-" + MarkerEditPlan.Format(end); }
                var segment = Value("segment"); var label = MarkerEditPlan.Kinds.Contains(segment) ? MarkerEditPlan.Label(segment) : segment;
                var content = new Grid { ColumnSpacing = 4, RowSpacing = 4, Padding = new Thickness(0, 8, 0, 8) };
                var narrow = (XamlRoot?.Size.Width ?? 900) < 640;
                content.ColumnDefinitions.Add(new() { Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(128) });
                content.ColumnDefinitions.Add(new() { Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star) });
                content.RowDefinitions.Add(new() { Height = GridLength.Auto }); if (narrow) content.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var identity = new StackPanel(); identity.Children.Add(Text(created, 12, true)); identity.Children.Add(Text(Value("username") is { Length: > 0 } user ? user : "Unknown user", 12, true)); content.Children.Add(identity);
                var detail = new StackPanel(); var action = Text((Value("action") == "clear" ? "Cleared " : "Set ") + label); action.FontWeight = Microsoft.UI.Text.FontWeights.Medium; detail.Children.Add(action);
                var range = Text(Range("before") + " -> " + Range("after"), 12, true); range.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"); detail.Children.Add(range); Grid.SetColumn(detail, narrow ? 0 : 1); Grid.SetRow(detail, narrow ? 1 : 0); content.Children.Add(detail);
                _history.Children.Add(new Border { Child = content, BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1) });
            }
            if (_history.Children.Count == 0) _history.Children.Add(Text("No marker edits recorded.", 14, true));
        }
        catch (OperationCanceledException) { }
        catch { if (CanAct) { _history.Children.Clear(); _history.Children.Add(Text("Could not load recent changes.", 13, true)); } }
    }
    protected override async Task SubmitAsync()
    {
        RequireAuthority(); var plan = MarkerEditPlan.Build(_original, _fields.ToDictionary(pair => pair.Key, pair => (pair.Value.Start.Text, pair.Value.End.Text)));
        if (plan.Count > 0) { await _api.SetMarkersAsync(Context, ItemId, plan, Lifetime.Token); RequireAuthority(); HasSaved = true; }
        // Hide after the command deferral releases the pending-close guard.
        DispatcherQueue.TryEnqueue(Hide);
    }
    protected override void SetEditing(bool enabled) { foreach (var field in _fields.Values) { field.Start.IsEnabled = field.End.IsEnabled = field.Clear.IsEnabled = enabled; } }
}
