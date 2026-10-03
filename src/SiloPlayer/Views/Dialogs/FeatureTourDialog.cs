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
    private readonly StackPanel _body = new() { Spacing = 0 };
    private TextBlock? _stepHeading;
    private FrameworkElement? _pips;
    private int _index;
    private bool _finished;
    private bool _settingBusy;
    private readonly OnboardingProgressBarrier _progress = new();
    private readonly TextBlock _saveError = new() { TextWrapping = TextWrapping.Wrap };
    private string? _selectedSettingValue;

    public FeatureTourDialog(OnboardingFlow flow)
    {
        _flow = flow;
        _steps = flow.Steps.Where(step => KnownKinds.Contains(step.Kind)).ToList();
        _settingsApi = App.Services.GetRequiredService<SettingsApi>();
        EditorDialogPresentation.Configure(this, 512, new Thickness(24, 24, 24, 0));
        CornerRadius = new CornerRadius(20); Resources["OverlayCornerRadius"] = new CornerRadius(20);
        Background = (Brush)Application.Current.Resources["CardBackgroundBrush"];
        Resources["ContentDialogTitleMargin"] = new Thickness(0);
        Resources["ContentDialogMinHeight"] = 0d;
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
        void Reflow(XamlRoot sender, XamlRootChangedEventArgs args) => ReflowPresentation();
        Opened += (_, _) => { if (XamlRoot != null) XamlRoot.Changed += Reflow; RenderStep(); };
        SizeChanged += (_, _) => ReflowPresentation();
        Closed += (_, _) => { if (XamlRoot != null) XamlRoot.Changed -= Reflow; };
        Closing += (_, args) => { if (!_finished && (_progress.IsSaving || _settingBusy)) args.Cancel = true; };
        AutomationProperties.SetName(this, "Feature tour");
    }

    private void RenderStep()
    {
        _body.Children.Clear();
        _stepHeading = null;
        if (_steps.Count == 0)
        {
            PrimaryButtonText = "Done";
            _body.Children.Add(_saveError);
            return;
        }

        var step = _steps[_index];
        var illustration = new Border
        {
            Name = "TourIllustration",
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(16),
            Background = (Brush)Application.Current.Resources["SecondaryBackgroundBrush"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 20),
            Child = step.Illustration is null or "welcome" or "recommendations"
                ? SiloPlayer.Controls.WebUiIcon.Create("sparkles", 24, (Brush)Application.Current.Resources["PrimaryTextBrush"])
                : step.Illustration == "watchlist"
                ? SiloPlayer.Controls.WebUiIcon.Create("heart", 24, (Brush)Application.Current.Resources["PrimaryTextBrush"])
                : new FontIcon
            {
                Glyph = IllustrationGlyph(step.Illustration),
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        _body.Children.Add(illustration);
        if (!string.IsNullOrWhiteSpace(step.Title))
            _body.Children.Add(_stepHeading = new TextBlock
            {
                Text = step.Title,
                FontSize = (XamlRoot?.Size.Width ?? 900) < 640 ? 20 : 24,
                LineHeight = (XamlRoot?.Size.Width ?? 900) < 640 ? 28 : 32,
                CharacterSpacing = -25,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
            });
        if (!string.IsNullOrWhiteSpace(step.Body))
            _body.Children.Add(new TextBlock
            {
                Text = step.Body,
                FontSize = 14,
                LineHeight = 22.75,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
            });

        if (step.Kind == "setting_choice" && step.Setting != null)
        { var choices = BuildSettingChoice(step.Setting); choices.Margin = new Thickness(0, 20, 0, 0); _body.Children.Add(choices); }
        if (step.Links.Count > 0)
        {
            var links = new StackPanel { Spacing = 8, Margin = new Thickness(0, 20, 0, 0) };
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

        _body.Children.Add(_saveError);
        _saveError.Visibility = string.IsNullOrEmpty(_saveError.Text) ? Visibility.Collapsed : Visibility.Visible;
        SecondaryButtonText = _index > 0 ? "Back" : "";
        IsSecondaryButtonEnabled = _index > 0;
        PrimaryButtonText = step.Kind == "handoff"
            ? step.Title ?? "Finish"
            : _index == _steps.Count - 1 ? "Done" : _index == 0 ? "Show me" : "Next";
        ReflowPresentation();
    }

    private void ReflowPresentation()
    {
        if (XamlRoot == null) return;
        var narrow = XamlRoot.Size.Width < 640;
        var padding = narrow ? 24d : 28d;
        var width = Math.Min(512, Math.Max(0, XamlRoot.Size.Width - 32));
        _body.Width = Math.Max(0, width - 2 - padding * 2);
        if (_stepHeading != null) { _stepHeading.FontSize = narrow ? 20 : 24; _stepHeading.LineHeight = narrow ? 28 : 32; }
        var background = EditorDialogPresentation.Descendants<Border>(this).FirstOrDefault(element => element.Name == "BackgroundElement");
        if (background != null) { background.Width = width; background.MaxHeight = XamlRoot.Size.Height * .85; }
        var title = EditorDialogPresentation.Descendants<ContentControl>(this).FirstOrDefault(element => element.Name == "Title");
        if (title != null) title.Visibility = Visibility.Collapsed;
        var contentScroll = EditorDialogPresentation.Descendants<ScrollViewer>(this).FirstOrDefault(element => element.Name == "ContentScrollViewer");
        if (contentScroll?.Content is Grid content) content.Padding = new Thickness(padding, padding, padding, 0);
        var commands = EditorDialogPresentation.Descendants<Grid>(this).FirstOrDefault(element => element.Name == "CommandSpace");
        if (commands == null) return;
        commands.Padding = new Thickness(padding, 28, padding, padding);
        commands.Background = Background;
        var primary = commands.Children.OfType<Button>().Single(button => button.Name == "PrimaryButton");
        var secondary = commands.Children.OfType<Button>().Single(button => button.Name == "SecondaryButton");
        var skip = commands.Children.OfType<Button>().Single(button => button.Name == "CloseButton");
        foreach (var button in new[] { primary, secondary, skip })
        {
            button.Style = (Style)Application.Current.Resources[button == primary ? "AccentButtonStyle" : "GhostButtonStyle"];
            button.MinWidth = 0; button.MinHeight = 32; button.Height = 32; button.FontSize = 14;
            button.Padding = new Thickness(12, 0, 12, 0); button.CornerRadius = new CornerRadius(10);
            button.HorizontalAlignment = HorizontalAlignment.Left;
        }
        primary.Background = (Brush)Application.Current.Resources["AccentBrush"];
        primary.Foreground = (Brush)Application.Current.Resources["AccentForegroundBrush"];
        skip.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        secondary.Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        commands.ColumnDefinitions[0].Width = GridLength.Auto;
        commands.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        commands.ColumnDefinitions[2].Width = GridLength.Auto;
        commands.ColumnDefinitions[3].Width = GridLength.Auto;
        commands.ColumnDefinitions[4].Width = GridLength.Auto;
        Grid.SetColumn(skip, 0); Grid.SetColumn(secondary, 3); Grid.SetColumn(primary, 4);
        secondary.Margin = new Thickness(0, 0, 8, 0);
        if (_pips != null) commands.Children.Remove(_pips);
        _pips = BuildProgressPips(); _pips.Margin = new Thickness(0, 0, 16, 0);
        _pips.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(_pips, 2); commands.Children.Add(_pips);
        skip.IsEnabled = !_progress.IsSaving && !_settingBusy;
    }

    private FrameworkElement BuildSettingChoice(OnboardingSettingSpec spec)
    {
        if (_selectedSettingValue == null)
        {
            var profile = App.Services.GetRequiredService<AuthService>().SelectedProfile;
            var data = System.Text.Json.JsonSerializer.SerializeToElement(profile, V2Json.Options);
            _selectedSettingValue = spec.Target == "profile_field" && data.ValueKind == System.Text.Json.JsonValueKind.Object && data.TryGetProperty(spec.Key, out var value)
                ? DeviceSettingDefinition.Scalar(value) : spec.Default ?? "";
        }
        var panel = new StackPanel { Spacing = 7 };
        if (!string.IsNullOrWhiteSpace(spec.Label))
            panel.Children.Add(new TextBlock { Text = spec.Label, FontWeight = FontWeights.SemiBold });
        var choices = new SiloPlayer.Controls.WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        panel.Children.Add(choices);
        foreach (var option in spec.Options)
        {
            var radio = new RadioButton
            {
                Content = option.Label,
                Tag = option,
                GroupName = $"TourSetting{_index}",
                IsChecked = string.Equals(_selectedSettingValue, option.Value, StringComparison.Ordinal),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                Padding = new Thickness(12, 8, 12, 8),
            };
            radio.Click += async (_, _) =>
            {
                if (_settingBusy || _progress.IsSaving) return;
                _settingBusy = true;
                ReflowPresentation();
                var prior = _selectedSettingValue;
                _selectedSettingValue = option.Value;
                foreach (var choice in choices.Children.OfType<Control>()) choice.IsEnabled = false;
                IsPrimaryButtonEnabled = IsSecondaryButtonEnabled = false;
                try
                {
                    await SaveSettingChoiceAsync(spec, option.Value);
                }
                catch (Exception ex)
                {
                    _selectedSettingValue = prior;
                    RenderStep();
                    App.Services.GetRequiredService<ToastService>().Error(ex.Message);
                }
                finally
                {
                    _settingBusy = false;
                    foreach (var choice in choices.Children.OfType<Control>()) choice.IsEnabled = true;
                    IsPrimaryButtonEnabled = true; IsSecondaryButtonEnabled = _index > 0;
                    ReflowPresentation();
                }
            };
            choices.Children.Add(radio);
        }
        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12),
            Child = panel,
        };
    }

    private async Task SaveSettingChoiceAsync(OnboardingSettingSpec spec, string value)
    {
        if (spec.Target == "profile_field")
        {
            var auth = App.Services.GetRequiredService<AuthService>();
            var profileId = auth.SelectedProfileId;
            if (!string.IsNullOrWhiteSpace(profileId))
            {
                var context = _settingsApi.CaptureContext();
                var profile = await _settingsApi.UpdateProfileAsync(profileId, new Dictionary<string, object?> { [spec.Key] = value });
                if (_settingsApi.IsCurrentContext(context))
                    auth.RefreshSelectedProfile(profile);
            }
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
        if (_progress.IsSaving || _settingBusy) return;
        if (_steps.Count == 0)
        {
            if (await TryReportProgressAsync(_flow.TourId, completed: true)) { _finished = true; Hide(); }
            return;
        }
        var step = _steps[_index];
        if (step.Kind == "handoff" || _index == _steps.Count - 1)
        {
            if (!await TryReportProgressAsync(_flow.TourId, step.Id, completed: true, skipped: false)) return;
            _finished = true;
            Hide();
            NavigateRoute(step.Route);
            return;
        }

        if (!await TryReportProgressAsync(_flow.TourId, _steps[_index + 1].Id)) return;
        _index++;
        _selectedSettingValue = null;
        RenderStep();
    }

    private void SecondaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_index <= 0 || _progress.IsSaving || _settingBusy) return;
        _index--;
        _selectedSettingValue = null;
        RenderStep();
    }

    private async void CloseButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_finished) return;
        args.Cancel = true;
        if (_progress.IsSaving || _settingBusy) return;
        var stepId = _steps.Count == 0 ? null : _steps[Math.Clamp(_index, 0, _steps.Count - 1)].Id;
        if (await TryReportProgressAsync(_flow.TourId, stepId, completed: false, skipped: true)) { _finished = true; Hide(); }
    }

    private async Task<bool> TryReportProgressAsync(
        string tourId,
        string? stepId = null,
        bool completed = false,
        bool skipped = false)
    {
        IsPrimaryButtonEnabled = IsSecondaryButtonEnabled = false;
        var skip = EditorDialogPresentation.Descendants<Button>(this).FirstOrDefault(button => button.Name == "CloseButton");
        if (skip != null) skip.IsEnabled = false;
        var saved = await _progress.SaveAsync(() => _settingsApi.ReportOnboardingProgressAsync(tourId, stepId, completed, skipped));
        _saveError.Text = saved ? "" : $"Could not save tour progress. Try again. {_progress.Error}";
        _saveError.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        IsPrimaryButtonEnabled = true;
        IsSecondaryButtonEnabled = _index > 0;
        if (skip != null) skip.IsEnabled = true;
        return saved;
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
