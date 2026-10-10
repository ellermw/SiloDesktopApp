using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Helpers;

/// <summary>Current calm collection menu body, retaining native menu commands and UIA.</summary>
internal static class CollectionActionMenuPresentation
{
    internal static void Configure(MenuFlyout menu)
    {
        menu.MenuFlyoutPresenterStyle = new Style { TargetType = typeof(MenuFlyoutPresenter), Setters =
        {
            new Setter(FrameworkElement.MinWidthProperty, 248d), new Setter(Control.PaddingProperty, new Thickness(6)),
            new Setter(Control.CornerRadiusProperty, new CornerRadius(14)), new Setter(Control.BorderThicknessProperty, new Thickness(1)),
            new Setter(Control.BackgroundProperty, Application.Current.Resources["PopoverBrush"]), new Setter(Control.BorderBrushProperty, Application.Current.Resources["BorderBrush"])
        } };
    }

    internal static void Configure(MenuFlyoutItemBase item, string? help = null, bool destructive = false)
    {
        var toggle = item is ToggleMenuFlyoutItem; var type = toggle ? "ToggleMenuFlyoutItem" : "MenuFlyoutItem";
        var switchStates = toggle ? """
          <VisualStateGroup x:Name="CheckStates"><VisualState x:Name="Unchecked"/><VisualState x:Name="Checked"><VisualState.Setters>
          <Setter Target="SwitchTrack.Background" Value="{StaticResource AccentBrush}"/><Setter Target="SwitchKnob.Background" Value="{StaticResource AccentForegroundBrush}"/>
          <Setter Target="SwitchKnob.(UIElement.RenderTransform).(CompositeTransform.TranslateX)" Value="14"/>
          </VisualState.Setters></VisualState></VisualStateGroup>
          """ : "";
        var switchBody = toggle ? """
          <Border x:Name="SwitchTrack" Grid.Column="2" Width="32" Height="18.4" CornerRadius="10" Padding="1" Background="{StaticResource BorderBrush}" Margin="8,0,0,0" VerticalAlignment="Top">
            <Border x:Name="SwitchKnob" Width="16" Height="16" CornerRadius="8" Background="{StaticResource PrimaryTextBrush}" HorizontalAlignment="Left"><Border.RenderTransform><CompositeTransform/></Border.RenderTransform></Border>
          </Border>
          """ : "";
        item.Height = double.NaN; item.MinHeight = 36; item.Padding = new Thickness(10, 8, 10, 8); item.CornerRadius = new CornerRadius(9);
        item.FontSize = 14; item.Tag = help; item.Foreground = (Brush)Application.Current.Resources[destructive ? "ErrorBrush" : "PrimaryTextBrush"];
        item.Template = (ControlTemplate)XamlReader.Load($$"""
          <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{{type}}">
            <Grid x:Name="Root" Background="Transparent" CornerRadius="{TemplateBinding CornerRadius}" Padding="{TemplateBinding Padding}">
              <VisualStateManager.VisualStateGroups>
                <VisualStateGroup x:Name="CommonStates"><VisualState x:Name="Normal"/>
                  <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="Root.Background" Value="{StaticResource SurfaceHoverBrush}"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="Root.Background" Value="{StaticResource SurfaceRaisedBrush}"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Root.Opacity" Value="0.5"/></VisualState.Setters></VisualState>
                </VisualStateGroup>{{switchStates}}
              </VisualStateManager.VisualStateGroups>
              <Grid.ColumnDefinitions><ColumnDefinition Width="16"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <ContentPresenter Content="{TemplateBinding Icon}" Width="16" Height="16" VerticalAlignment="Top" Margin="0,2,0,0"/>
              <StackPanel Grid.Column="1" Spacing="2" Margin="10,0,0,0">
                <TextBlock Text="{TemplateBinding Text}" FontSize="{TemplateBinding FontSize}" FontWeight="Medium" Foreground="{TemplateBinding Foreground}" TextWrapping="Wrap"/>
                <TextBlock Text="{TemplateBinding Tag}" FontSize="12.5" LineHeight="17.1875" LineStackingStrategy="BlockLineHeight" TextWrapping="Wrap" Foreground="{StaticResource SecondaryTextBrush}" Visibility="{{(help is null ? "Collapsed" : "Visible")}}"/>
              </StackPanel>{{switchBody}}
            </Grid>
          </ControlTemplate>
          """);
    }
}
