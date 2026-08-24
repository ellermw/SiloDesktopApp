using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using Windows.System;

namespace SiloPlayer.Views.Dialogs;

/// <summary>
/// Native rendering of the server-driven WebUI feature tour. Unknown step
/// kinds are skipped so newer server manifests remain forward compatible.
/// </summary>
public sealed class FeatureTourDialog : ContentDialog
{
    private static readonly HashSet<string> KnownKinds =
        ["welcome", "feature_card", "setting_choice", "handoff"];

    private readonly OnboardingFlow _flow;
    private readonly List<OnboardingStep> _steps;
    private readonly SettingsApi _settingsApi;
    private readonly StackPanel _body = new() { Width = 460, Spacing = 14 };
    private int _index;
    private bool _finished;
    private string? _selectedSettingValue;

    public FeatureTourDialog(OnboardingFlow flow)
    {
        _flow = flow;
        _steps = flow.Steps.Where(step => KnownKinds.Contains(step.Kind)).ToList();
        _settingsApi = App.Services.GetRequiredService<SettingsApi>();
        Title = "Feature tour";
        Content = new ScrollViewer
        {
            MaxHeight = 570,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _body,
        };
        CloseButtonText = "Skip tour";
        PrimaryButtonText = "Show me";
        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += PrimaryButton_Click;
        SecondaryButtonClick += SecondaryButton_Click;
        CloseButtonClick += CloseButton_Click;
        Opened += (_, _) => RenderStep();
        AutomationProperties.SetName(this, "Feature tour");
    }

    private void RenderStep()
    {
        _body.Children.Clear();
        if (_steps.Count == 0)
        {
            _finished = true;
            Hide();
            _ = _settingsApi.ReportOnboardingProgressAsync(_flow.TourId, completed: true);
            return;
        }

        var step = _steps[_index];
        var illustration = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new FontIcon
            {
                Glyph = IllustrationGlyph(step.Illustration),
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        _body.Children.Add(illustration);
        if (!string.IsNullOrWhiteSpace(step.Title))
            _body.Children.Add(new TextBlock
            {
                Text = step.Title,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
            });
        if (!string.IsNullOrWhiteSpace(step.Body))
            _body.Children.Add(new TextBlock
            {
                Text = step.Body,
                FontSize = 14,
                LineHeight = 21,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
            });

        if (step.Kind == "setting_choice" && step.Setting != null)
            _body.Children.Add(BuildSettingChoice(step.Setting));
        if (step.Links.Count > 0)
        {
            var links = new StackPanel { Spacing = 8 };
            foreach (var link in step.Links)
            {
                var button = new Button
                {
                    Content = $"{link.Label}  ↗",
                    Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Tag = link.Url,
                };
                button.Click += async (_, _) =>
                {
                    if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri))
                        await Launcher.LaunchUriAsync(uri);
                };
                links.Children.Add(button);
            }
            _body.Children.Add(links);
        }

        _body.Children.Add(BuildProgressPips());
        SecondaryButtonText = _index > 0 ? "Back" : "";
        IsSecondaryButtonEnabled = _index > 0;
        PrimaryButtonText = step.Kind == "handoff"
            ? step.Title ?? "Finish"
            : _index == _steps.Count - 1 ? "Done" : _index == 0 ? "Show me" : "Next";
    }

    private FrameworkElement BuildSettingChoice(OnboardingSettingSpec spec)
    {
        _selectedSettingValue ??= spec.Default ?? "";
        var panel = new StackPanel { Spacing = 7 };
        if (!string.IsNullOrWhiteSpace(spec.Label))
            panel.Children.Add(new TextBlock { Text = spec.Label, FontWeight = FontWeights.SemiBold });
        foreach (var option in spec.Options)
        {
            var radio = new RadioButton
            {
                Content = option.Label,
                Tag = option,
                GroupName = $"TourSetting{_index}",
                IsChecked = string.Equals(_selectedSettingValue, option.Value, StringComparison.Ordinal),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 7, 10, 7),
            };
            radio.Click += async (_, _) =>
            {
                _selectedSettingValue = option.Value;
                radio.IsEnabled = false;
                try
                {
                    await SaveSettingChoiceAsync(spec, option.Value);
                }
                catch (Exception ex)
                {
                    App.Services.GetRequiredService<ToastService>().Error(ex.Message);
                }
                finally
                {
                    radio.IsEnabled = true;
                }
            };
            panel.Children.Add(radio);
        }
        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = panel,
        };
    }

    private async Task SaveSettingChoiceAsync(OnboardingSettingSpec spec, string value)
    {
        if (spec.Target == "profile_field")
        {
            var profileId = App.Services.GetRequiredService<AuthService>().SelectedProfileId;
            if (!string.IsNullOrWhiteSpace(profileId))
                await _settingsApi.UpdateProfileAsync(profileId, new Dictionary<string, object?> { [spec.Key] = value });
        }
        else if (spec.Target == "device_setting")
        {
            await _settingsApi.PutDeviceSettingAsync(spec.Key, value);
        }
        else if (spec.Target == "setting")
        {
            await _settingsApi.PutSettingAsync(spec.Key, value);
        }
    }

    private FrameworkElement BuildProgressPips()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };
        for (var i = 0; i < _steps.Count; i++)
            row.Children.Add(new Border
            {
                Width = i == _index ? 16 : 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = (Brush)Application.Current.Resources[
                    i == _index ? "AccentBrush" : "SurfaceBrush"],
            });
        return row;
    }

    private async void PrimaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var step = _steps[_index];
        if (step.Kind == "handoff" || _index == _steps.Count - 1)
        {
            _finished = true;
            await _settingsApi.ReportOnboardingProgressAsync(
                _flow.TourId, step.Id, completed: true, skipped: false);
            Hide();
            NavigateRoute(step.Route);
            return;
        }

        _index++;
        _selectedSettingValue = null;
        await _settingsApi.ReportOnboardingProgressAsync(_flow.TourId, _steps[_index].Id);
        RenderStep();
    }

    private void SecondaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_index <= 0) return;
        _index--;
        _selectedSettingValue = null;
        RenderStep();
    }

    private async void CloseButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_finished || _steps.Count == 0) return;
        var step = _steps[Math.Clamp(_index, 0, _steps.Count - 1)];
        await _settingsApi.ReportOnboardingProgressAsync(
            _flow.TourId, step.Id, completed: false, skipped: true);
    }

    private static void NavigateRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return;
        var nav = App.Services.GetRequiredService<NavigationService>();
        switch (route.TrimEnd('/').ToLowerInvariant())
        {
            case "/taste-seed": nav.Navigate<TasteSeedPage>(true); break;
            case "/requests": nav.Navigate<RequestsPage>(); break;
            case "/calendar": nav.Navigate<CalendarPage>(); break;
            case "/rooms/join": nav.Navigate<WatchTogetherJoinPage>(); break;
            case "/recommendations": nav.Navigate<RecommendationsPage>(); break;
            case "/notifications": nav.Navigate<NotificationsPage>(); break;
            case "/collections": nav.Navigate<CollectionsPage>(); break;
            default: nav.Navigate<HomePage>(); break;
        }
    }

    private static string IllustrationGlyph(string? illustration) => illustration switch
    {
        "watchlist" => "\uEB51",
        "watch-together" => "\uE716",
        "requests" => "\uE8F1",
        "recommendations" => "\uE734",
        "calendar" => "\uE787",
        "playback" => "\uE768",
        "subtitles" => "\uED1E",
        "notifications" => "\uEA8F",
        "apps" => "\uE7F4",
        "jellyfin" => "\uE839",
        _ => "\uE735",
    };
}
