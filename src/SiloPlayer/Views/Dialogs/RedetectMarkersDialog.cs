using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using System.Text.Json;

namespace SiloPlayer.Views.Dialogs;

public sealed class RedetectMarkersDialog : ItemScopedDialog
{
    private readonly List<(string Kind, Button Button, TextBlock Description)> _choices = [];
    private readonly bool _honorsSettings;
    private bool _intro = true, _credits = true;
    public RedetectMarkersDialog(string id, bool honorsSettings) : base(id, "Re-detect Markers", "Run local detection again on this server. Manual markers and markers from higher-priority sources stay as they are.", 512, "", AuthorizationPolicy.IsActingAdmin)
    {
        _honorsSettings = honorsSettings; var choices = new StackPanel { Spacing = 12 }; Body.Children.Add(choices);
        foreach (var (kind, label, description, icon) in new[] { ("intro", "Intro", "Find the opening again from chapters and the season's shared audio.", "skip-forward"), ("credits", "Credits", "Find the end credits again from chapters, audio, and the picture.", "list-end"), ("all", "Intro and credits", "Run both.", "refresh-cw") })
        {
            var copy = new StackPanel { Spacing = 4 }; var title = Text(label); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; var help = Text(description, 14, true); copy.Children.Add(title); copy.Children.Add(help);
            var content = new Grid { ColumnSpacing = 12 }; content.ColumnDefinitions.Add(new() { Width = new GridLength(20) }); content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var glyph = WebUiIcon.Create(icon, 20, Brush("SecondaryTextBrush")); glyph.VerticalAlignment = VerticalAlignment.Top; content.Children.Add(glyph); Grid.SetColumn(copy, 1); content.Children.Add(copy);
            var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16), BorderThickness = new Thickness(1), BorderBrush = Brush("BorderBrush"), Background = Brush("SurfaceBrush"), CornerRadius = new CornerRadius(12), IsEnabled = false };
            AutomationProperties.SetName(button, label); button.Click += async (_, _) => await RunActionAsync(async () => { await new MediaMarkerApi(Client).RedetectAsync(Context, ItemId, "episode", kind, Lifetime.Token); RequireAuthority(); HasSaved = true; DispatcherQueue.TryEnqueue(Hide); });
            choices.Children.Add(button); _choices.Add((kind, button, help));
        }
    }
    protected override async Task LoadAsync()
    {
        if (!_honorsSettings) return;
        try
        {
            var settings = await Client.SendRequestAsync<JsonElement>(Context, HttpMethod.Get, "/api/v2/admin/settings/effective", null, Lifetime.Token); RequireAuthority();
            bool On(string key) => !settings.TryGetProperty(key, out var value) || !string.Equals(value.ToString().Trim(), "false", StringComparison.OrdinalIgnoreCase);
            _intro = On("markers.detect_intros"); _credits = On("markers.detect_credits");
        }
        catch (OperationCanceledException) { throw; }
        catch { /* Unknown settings leave the decision to the server, matching WebUI. */ }
        foreach (var choice in _choices)
        {
            var introOff = !_intro && choice.Kind != "credits"; var creditsOff = !_credits && choice.Kind != "intro";
            if (introOff || creditsOff) choice.Description.Text = introOff && creditsOff ? "Intro and credits detection are turned off in marker settings." : introOff ? "Intro detection is turned off in marker settings." : "Credits detection is turned off in marker settings.";
        }
        if (!_intro || !_credits)
        {
            var link = new HyperlinkButton { Content = "Change marker settings", HorizontalAlignment = HorizontalAlignment.Left };
            link.Click += async (_, _) => { if (!CanAct || Busy) return; try { await Windows.System.Launcher.LaunchUriAsync(new Uri(new Uri(Client.BaseUrl.TrimEnd('/') + "/"), "admin/settings/library")); } catch (Exception error) { Status.Text = error.Message; } };
            Body.Children.Add(link);
        }
    }
    protected override void UpdateCommands() { IsPrimaryButtonEnabled = false; SetEditing(Ready && CanAct && !Busy); }
    protected override void SetEditing(bool enabled) { foreach (var choice in _choices) choice.Button.IsEnabled = enabled && (choice.Kind == "credits" || _intro) && (choice.Kind == "intro" || _credits); }
    protected override Task SubmitAsync() => Task.CompletedTask;
}
