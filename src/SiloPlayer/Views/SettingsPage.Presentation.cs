using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private sealed record SettingsSectionPresentation(Border Section, Grid Layout, FrameworkElement Heading, StackPanel Body);
    private readonly List<SettingsSectionPresentation> _settingsSections = [];

    private void InitializeSettingsPresentation()
    {
        SettingsContentPanel.SizeChanged += (_, _) => ReflowSettingsSections();
        InitializeSettingsRadioPalette();
        // On compact windows the root is a directory: heading, profile, search,
        // then the grouped list. Desktop opens Playback in the replacement rail.
        SettingsHeaderGrid.Children.Remove(SettingsSearchPanel);
        SettingsOverviewPanel.Children.Insert(1, SettingsSearchPanel);
        if (PlaybackPanel.Children.OfType<Border>().FirstOrDefault()?.Child is StackPanel qualityRows)
            PlaybackPanel.Children.OfType<Controls.PlaybackConnectionSettingsControl>().Single().PlaceBufferingSettingsIn(qualityRows);
        InitializeSettingsToggleRows();
        // Keep the existing controls and bindings alive. Only the presentation
        // container changes to the current shared WebUI SettingsGroup geometry.
        foreach (var border in LogicalSettingsDescendants(SettingsContentPanel).OfType<Border>().ToArray())
        {
            if (!ReferenceEquals(border.Style, Resources["SettingsGroupStyle"]) || border.Child is not StackPanel body || body.Children.Count < 2)
                continue;
            FrameworkElement? heading = null;
            if (body.Children[0] is StackPanel candidate && candidate.Children.All(child => child is TextBlock)
                && candidate.Children.FirstOrDefault() is TextBlock { FontSize: <= 18 } nestedTitle)
            {
                nestedTitle.FontSize = 15;
                heading = candidate;
                body.Children.RemoveAt(0);
            }
            else if (body.Children[0] is TextBlock { FontSize: <= 18 } title)
            {
                var text = new StackPanel { Spacing = 6 };
                body.Children.RemoveAt(0);
                title.FontSize = 15;
                title.Margin = new Thickness(0);
                text.Children.Add(title);
                if (body.Children.FirstOrDefault() is TextBlock { FontSize: <= 14 } description)
                {
                    body.Children.RemoveAt(0);
                    description.Margin = new Thickness(0);
                    description.FontSize = 13;
                    text.Children.Add(description);
                }
                heading = text;
            }
            if (heading is null) continue;
            foreach (var text in LogicalSettingsDescendants(heading).Prepend(heading).OfType<TextBlock>())
            {
                text.LineHeight = text.FontSize == 15 ? 22.5 : 21.125;
                text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                if (text.FontSize == 15) text.CharacterSpacing = -25;
            }
            // Empty historical placeholders must not produce stacked rules.
            Border? precedingSeparator = null;
            foreach (var child in body.Children.ToArray())
            {
                if (child is Border separator && ReferenceEquals(separator.Style, Resources["SettingsSeparator"]))
                {
                    if (precedingSeparator != null) body.Children.Remove(separator);
                    else precedingSeparator = separator;
                }
                else precedingSeparator = null;
            }
            if (precedingSeparator != null) body.Children.Remove(precedingSeparator);
            border.Child = null;
            var layout = new Grid { ColumnSpacing = 48, RowSpacing = 16, Tag = "settings-section-layout" };
            layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new() { Width = new GridLength(0) });
            layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            layout.Children.Add(heading);
            layout.Children.Add(body);
            Grid.SetRow(body, 1);
            border.Child = layout;
            _settingsSections.Add(new(border, layout, heading, body));
        }
        NormalizeSettingsPageHeaders();
    }

    private void InitializeSettingsToggleRows()
    {
        foreach (var section in LogicalSettingsDescendants(SettingsContentPanel).OfType<Border>()
            .Where(border => ReferenceEquals(border.Style, Resources["SettingsGroupStyle"])).ToArray())
        foreach (var parent in LogicalSettingsDescendants(section).OfType<StackPanel>().ToArray())
        foreach (var toggle in parent.Children.OfType<ToggleSwitch>().Where(toggle => toggle.Header is string).ToArray())
        {
            var label = (string)toggle.Header!;
            var index = parent.Children.IndexOf(toggle);
            parent.Children.RemoveAt(index);
            toggle.Header = null;
            toggle.OnContent = toggle.OffContent = "";
            toggle.MinWidth = 0;
            toggle.HorizontalAlignment = HorizontalAlignment.Right;
            toggle.VerticalAlignment = VerticalAlignment.Center;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, label);
            var ruled = parent.Children.Take(index).OfType<Grid>().Any(previous => Equals(previous.Tag, "settings-value-row"));
            parent.Spacing = 16;
            var row = new Grid { ColumnSpacing = 12, RowSpacing = 12, Tag = "settings-value-row",
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(0, ruled ? 1 : 0, 0, 0),
                Padding = new Thickness(0, ruled ? 16 : 0, 0, 0) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.RowDefinitions.Add(new() { Height = GridLength.Auto });
            row.RowDefinitions.Add(new() { Height = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Medium,
                LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            row.SetBinding(VisibilityProperty, new Microsoft.UI.Xaml.Data.Binding { Source = toggle,
                Path = new PropertyPath(nameof(Visibility)), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
            parent.Children.Insert(index, row);
        }
    }

    private void SynchronizeSettingsStatusPresentation()
        => SettingsStatusPanel.Visibility = ViewModel.IsLoading || !string.IsNullOrWhiteSpace(ViewModel.ErrorMessage)
            || !string.IsNullOrWhiteSpace(ViewModel.StatusMessage) ? Visibility.Visible : Visibility.Collapsed;

    private void InitializeSettingsRadioPalette()
    {
        // WinUI1.8 generic.xaml's native RadioButton template uses these keys.
        // Scope the current WebUI foreground ring/dot to Settings, retaining
        // the native control's focus, state transitions and keyboard behavior.
        var foreground = Application.Current.Resources["PrimaryTextBrush"];
        var background = Application.Current.Resources["AppBackgroundBrush"];
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            Resources["RadioButtonForeground" + state] = foreground;
            Resources["RadioButtonOuterEllipseCheckedStroke" + state] = foreground;
            Resources["RadioButtonOuterEllipseCheckedFill" + state] = background;
            Resources["RadioButtonCheckGlyphFill" + state] = foreground;
            Resources["RadioButtonCheckGlyphStroke" + state] = foreground;
        }
        Resources["RadioButtonCheckGlyphStrokeChecked"] = foreground;
        Resources["RadioButtonCheckGlyphSize"] = 8d;
    }

    private void SynchronizeQualityDescription()
        => PreferredQualityDescription.Text = ViewModel.QualityPreference is "" or "auto" or "original"
            ? "Plays the original file whenever your device and connection can handle it."
            : "The resolution your profile should request when playback begins.";

    private void NormalizeSettingsPageHeaders()
    {
        foreach (var page in SettingsContentPanel.Children.Select(child => child is ContentControl content ? content.Content : child).OfType<StackPanel>())
        {
            var header = page.Children.SelectMany(child => child switch
            {
                StackPanel stack => new[] { stack },
                Grid grid => grid.Children.OfType<StackPanel>(),
                _ => Enumerable.Empty<StackPanel>(),
            }).FirstOrDefault(panel =>
                panel.Children.FirstOrDefault() is TextBlock { FontSize: >= 20 });
            if (header is null) continue;
            var title = (TextBlock)header.Children[0];
            title.FontSize = SettingsViewportWidth < 640 ? 28 : 32;
            title.FontWeight = FontWeights.Bold;
            title.LineHeight = title.FontSize * 1.15;
            title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            title.CharacterSpacing = -35;
            header.Spacing = 8;
            foreach (var description in header.Children.OfType<TextBlock>().Skip(1))
            {
                description.FontSize = 14;
                description.LineHeight = 22.75;
                description.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                var measure = new TextBlock { Text = new string('0', 72), FontSize = 14, FontFamily = description.FontFamily };
                measure.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                description.MaxWidth = measure.DesiredSize.Width;
                description.HorizontalAlignment = HorizontalAlignment.Left;
            }
            if (page.Children.OfType<Border>().Any(border => ReferenceEquals(border.Style, Resources["SettingsGroupStyle"])))
            {
                page.Spacing = 0;
                header.Margin = new Thickness(0, 0, 0, 32);
            }
        }
    }

    private void ReflowSettingsSections()
    {
        foreach (var section in _settingsSections)
        {
            var desktop = SettingsViewportWidth >= 1024;
            var split = desktop && SettingsContentPanel.ActualWidth >= 832;
            section.Layout.ColumnSpacing = split ? 48 : 0;
            section.Section.Padding = new Thickness(0, desktop ? 28 : 24, 0, desktop ? 28 : 24);
            section.Layout.ColumnDefinitions[0].Width = split ? new GridLength(256) : new GridLength(1, GridUnitType.Star);
            section.Layout.ColumnDefinitions[1].Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(section.Body, split ? 1 : 0);
            Grid.SetRow(section.Body, split ? 0 : 1);
        }
    }

    private static IEnumerable<DependencyObject> LogicalSettingsDescendants(DependencyObject parent)
    {
        IEnumerable<DependencyObject> children = parent switch
        {
            Panel panel => panel.Children,
            Border { Child: { } child } => [child],
            ContentControl { Content: DependencyObject child } => [child],
            _ => [],
        };
        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in LogicalSettingsDescendants(child)) yield return descendant;
        }
    }
}
