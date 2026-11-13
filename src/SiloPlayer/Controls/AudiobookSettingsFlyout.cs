using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

internal static class AudiobookSettingsFlyout
{
    public static Flyout Create(PlayerService player, SettingsService settings)
    {
        var panel = new StackPanel { Width = 228, Spacing = 16 };
        var buttons = new List<(Button Button, bool Back)>();
        // Labels re-render in place while the flyout is open (interface language change).
        var texts = new List<Action>();
        void Text(Action apply) { apply(); texts.Add(apply); }
        var busy = false;
        void Paint()
        {
            foreach (var (button, back) in buttons)
            {
                var selected = (int)button.Tag == (back ? player.SeekIntervals.AudiobookBack : player.SeekIntervals.AudiobookForward);
                button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(selected ? (byte)38 : (byte)0, 255, 255, 255));
                button.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(selected ? (byte)255 : (byte)153, 255, 255, 255));
                button.IsEnabled = !busy;
                AutomationProperties.SetHelpText(button, selected ? AppStrings.Get("web.player.quality.selected", fallback: "Selected") : "");
            }
        }
        void AddInterval(string key, string english, bool back)
        {
            string Label() => AppStrings.Get(key, fallback: english);
            var row = new StackPanel { Spacing = 6 };
            var heading = new TextBlock { FontSize = 11, CharacterSpacing = 140, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(102, 255, 255, 255)) };
            Text(() => heading.Text = AppStrings.Uppercase(Label()));
            row.Children.Add(heading);
            var choices = new WrapPanel { HorizontalSpacing = 4, VerticalSpacing = 4 };
            foreach (var seconds in SeekPreferences.Choices)
            {
                var choice = new Button { Tag = seconds, Height = 22, MinHeight = 0, MinWidth = 0, Padding = new Thickness(8, 0, 8, 0), FontSize = 12, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(0) };
                var args = new Dictionary<string, object?> { ["seconds"] = seconds };
                Text(() =>
                {
                    choice.Content = AppStrings.Get("web.audiobooks.player.settingsSeconds", args, fallback: $"{seconds}s");
                    AutomationProperties.SetName(choice, $"{Label()} {AppStrings.Get("web.player.seekBar.positionSeconds", args, fallback: $"{seconds} seconds")}");
                });
                choice.Click += async (_, _) =>
                {
                    if (busy) return;
                    busy = true; Paint();
                    try { await player.SaveSeekIntervalAsync(true, back, seconds); }
                    finally { busy = false; Paint(); }
                };
                buttons.Add((choice, back)); choices.Children.Add(choice);
            }
            row.Children.Add(choices); panel.Children.Add(row);
        }
        AddInterval("web.audiobooks.player.settingsSkipBack", "Skip back", true);
        AddInterval("web.audiobooks.player.settingsSkipForward", "Skip forward", false);
        var shared = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(102, 255, 255, 255)) };
        Text(() => shared.Text = AppStrings.Get("web.audiobooks.player.intervalsShared", fallback: "Saved to your profile for every Silo app"));
        panel.Children.Add(shared);
        var smartRow = new Grid { ColumnSpacing = 12 };
        smartRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        smartRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var smartLabel = new StackPanel { Spacing = 2 };
        var smartTitle = new TextBlock { FontSize = 14, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(217, 255, 255, 255)) };
        var smartHint = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(102, 255, 255, 255)) };
        smartLabel.Children.Add(smartTitle); smartLabel.Children.Add(smartHint);
        smartRow.Children.Add(smartLabel);
        var smart = new ToggleSwitch { IsOn = settings.Load().AudiobookSmartRewind, OnContent = "", OffContent = "", MinWidth = 32, VerticalAlignment = VerticalAlignment.Top };
        Text(() =>
        {
            smartTitle.Text = AppStrings.Get("web.audiobooks.player.smartRewind", fallback: "Smart rewind");
            smartHint.Text = AppStrings.Get("web.audiobooks.player.smartRewindHint", fallback: "Backs up a little after a pause");
            AutomationProperties.SetName(smart, smartTitle.Text);
        });
        smart.Toggled += (_, _) => { var config = settings.Load(); config.AudiobookSmartRewind = smart.IsOn; settings.Save(config); };
        Grid.SetColumn(smart, 1); smartRow.Children.Add(smart); panel.Children.Add(smartRow);
        Paint();
        AppLocalization.Register(panel, () => { AppLocalization.ApplyDirection(panel); foreach (var text in texts) text(); Paint(); });
        var presenter = new Style { TargetType = typeof(FlyoutPresenter) };
        presenter.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Windows.UI.Color.FromArgb(230, 0, 0, 0))));
        presenter.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 14, 16, 14)));
        presenter.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        presenter.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        return new Flyout { Content = panel, FlyoutPresenterStyle = presenter };
    }
}
