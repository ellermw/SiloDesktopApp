using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Controls;

internal static class CollectionPickRow
{
    internal static RadioButton Create(CollectionTemplate template, bool selected, string group)
    {
        var columns = new Grid { ColumnSpacing = 12 };
        columns.ColumnDefinitions.Add(new() { Width = new(30) }); columns.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var poster = new Border { Width = 30, Height = 45, CornerRadius = new(5), Background = (Brush)Application.Current.Resources["MutedBrush"] };
        var posterUrl = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>(App.Services).ResolveServerUrl(template.PosterPath);
        poster.Child = string.IsNullOrWhiteSpace(posterUrl) ? new TextBlock { Text = template.Icon, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : new Image { Stretch = Stretch.UniformToFill, Source = (ImageSource)new SiloPlayer.Converters.UrlToImageSourceConverter().Convert(posterUrl, typeof(ImageSource), null!, "") };
        poster.Loaded += (_, _) => ClipPoster(poster);
        var copy = new StackPanel { Spacing = 2 };
        var heading = new Grid { ColumnSpacing = 8 };
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new TextBlock { Text = template.Title, FontSize = 14.5, LineHeight = 21.75, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        heading.Children.Add(title);
        var tag = template.Source switch { "mdblist" => "MDBLIST", "tmdb" or "tmdb_list" => "TMDB", _ => null };
        if (tag != null)
        {
            var badge = new Border { Padding = new(6, 1, 6, 1), CornerRadius = new(4), Background = (Brush)Application.Current.Resources["MutedBrush"], VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = tag, FontSize = 10.5, FontWeight = FontWeights.SemiBold, CharacterSpacing = 25, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] } };
            Grid.SetColumn(badge, 1); heading.Children.Add(badge);
            heading.SizeChanged += (_, _) => title.MaxWidth = Math.Max(0, heading.ActualWidth - badge.ActualWidth - 8);
        }
        copy.Children.Add(heading);
        var media = template.MediaKind switch { "movie" => "Movies", "series" or "tv" => "TV", "mixed" => "Movies + TV", _ => "" };
        var meta = string.Join(" · ", new[] { media, template.DefaultLimit > 0 ? $"{template.DefaultLimit} titles" : "", string.IsNullOrWhiteSpace(template.DefaultSyncSchedule) ? "" : "syncs " + Schedule(template.DefaultSyncSchedule) }.Where(value => value.Length > 0));
        copy.Children.Add(new TextBlock { Text = meta, FontSize = 12.5, LineHeight = 18.75, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(copy, 1); columns.Children.Add(poster); columns.Children.Add(copy);
        var row = new RadioButton { Content = columns, IsChecked = selected, GroupName = group, Padding = new(14, 10, 14, 10), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        row.Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="RadioButton">
              <Grid x:Name="Root">
                <VisualStateManager.VisualStateGroups>
                  <VisualStateGroup x:Name="CommonStates">
                    <VisualState x:Name="Normal" />
                    <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="Hover.Opacity" Value="0.6" /></VisualState.Setters></VisualState>
                    <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="Hover.Opacity" Value="0.6" /></VisualState.Setters></VisualState>
                    <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Root.Opacity" Value="0.5" /></VisualState.Setters></VisualState>
                  </VisualStateGroup>
                  <VisualStateGroup x:Name="CheckStates">
                    <VisualState x:Name="Unchecked" />
                    <VisualState x:Name="Checked"><VisualState.Setters><Setter Target="CheckedSurface.Opacity" Value="1" /><Setter Target="Indicator.Opacity" Value="1" /><Setter Target="Dot.Opacity" Value="1" /><Setter Target="Dot.BorderBrush" Value="{StaticResource PrimaryTextBrush}" /></VisualState.Setters></VisualState>
                    <VisualState x:Name="Indeterminate" />
                  </VisualStateGroup>
                </VisualStateManager.VisualStateGroups>
                <Border Background="Transparent" />
                <Border x:Name="Hover" Background="{StaticResource AccentBackgroundBrush}" Opacity="0" />
                <Border x:Name="CheckedSurface" Background="{StaticResource AccentBackgroundBrush}" Opacity="0" />
                <Grid Padding="{TemplateBinding Padding}" ColumnSpacing="12">
                  <Grid.ColumnDefinitions><ColumnDefinition Width="18" /><ColumnDefinition Width="*" /></Grid.ColumnDefinitions>
                  <Border x:Name="Dot" Width="18" Height="18" CornerRadius="9" BorderThickness="1.5" BorderBrush="{StaticResource SecondaryTextBrush}" Opacity="0.7" VerticalAlignment="Center">
                    <Ellipse x:Name="Indicator" Width="8" Height="8" Fill="{StaticResource PrimaryTextBrush}" Opacity="0" />
                  </Border>
                  <ContentPresenter Grid.Column="1" Content="{TemplateBinding Content}" HorizontalContentAlignment="Stretch" VerticalContentAlignment="Center" />
                </Grid>
              </Grid>
            </ControlTemplate>
            """);
        AutomationProperties.SetName(row, template.Title); AutomationProperties.SetHelpText(row, string.Join(" · ", new[] { tag, meta }.Where(value => !string.IsNullOrEmpty(value)))); return row;
    }
    private static string Schedule(string cron)
    {
        var fields = cron.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5) return "on schedule";
        if (System.Text.RegularExpressions.Regex.IsMatch(fields[1], @"^\*/\d+$")) return $"every {fields[1][2..]} hours";
        if (fields[1] == "*") return "hourly";
        if (fields[2] == "1" && fields[3] == "*") return "monthly";
        if (fields[2] == "*" && fields[3] == "*" && fields[4] == "*") return "daily";
        return fields[2] == "*" && fields[3] == "*" ? "weekly" : "on schedule";
    }
    private static void ClipPoster(Border poster)
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(poster);
        var geometry = visual.Compositor.CreateRoundedRectangleGeometry(); geometry.Size = new(30, 45); geometry.CornerRadius = new(5);
        visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
    }
}
