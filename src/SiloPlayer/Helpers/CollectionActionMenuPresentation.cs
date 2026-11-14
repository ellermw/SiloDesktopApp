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

/// <summary>
/// Display text for the collections, query-rule and Home-customization surfaces: explicit catalog keys with the
/// existing English as fallback. Never used for names people typed, server data, wire values or routing.
/// </summary>
internal static class CollectionText
{
    internal static string Get(string key, string fallback) => SiloPlayer.Core.Localization.AppStrings.Get(key, null, fallback);

    internal static string Get(string key, string fallback, params (string Name, object? Value)[] arguments)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in arguments) values[name] = value;
        return SiloPlayer.Core.Localization.AppStrings.Get(key, values, fallback);
    }

    /// <summary>A failed request's display text: Problem Details through <see cref="SiloPlayer.Core.Localization.ApiProblemText"/>, other errors as raised.</summary>
    internal static string Problem(Exception exception)
        => exception is SiloPlayer.Core.Api.ApiException api ? SiloPlayer.Core.Localization.ApiProblemText.Display(api) : exception.Message;

    /// <summary>A count printed in the display culture (for <c>count_text</c>-style arguments).</summary>
    internal static string Number(long value) => value.ToString("N0", SiloPlayer.Core.Localization.AppStrings.FormatCulture);

    /// <summary>"N titles" under a collection poster (catalog plural, display-culture number).</summary>
    internal static string Titles(long count)
        => Get("web.collections.metaLine.titleCount", $"{count} title{(count == 1 ? "" : "s")}", ("count", count), ("shown", Number(count)));

    /// <summary>
    /// The accessible name of a poster's open button, also used to find it again after a rebuild. The catalog has no
    /// "Open {name}" message (the WebUI link is named by the title alone), so this stays English rather than composing words.
    /// </summary>
    internal static string Open(string name) => "Open " + name;

    /// <summary>
    /// Keys a programmatically built menu entry before it is ever opened: its literal English text stays as the
    /// fallback, and the entry follows later language and direction changes while it is shown.
    /// </summary>
    internal static T Key<T>(T item, string key) where T : MenuFlyoutItemBase
    {
        switch (item)
        {
            case MenuFlyoutItem entry: SiloPlayer.Services.Localized.SetText(entry, key); break;
            case MenuFlyoutSubItem sub: SiloPlayer.Services.Localized.SetText(sub, key); break;
        }
        SiloPlayer.Services.Localized.SetDirection(item, true);
        return item;
    }

    /// <summary>A code-built label whose English text never changes: keyed so it follows later language changes.</summary>
    internal static TextBlock Keyed(TextBlock block, string key)
    {
        SiloPlayer.Services.Localized.SetText(block, key);
        return block;
    }

    /// <summary>A code-built button, check box or list item whose literal English content is the fallback for <paramref name="key"/>.</summary>
    internal static T KeyedContent<T>(T control, string key) where T : ContentControl
    {
        SiloPlayer.Services.Localized.SetContent(control, key);
        return control;
    }

    /// <summary>A code-built input whose literal English header is the fallback for <paramref name="key"/>.</summary>
    internal static T KeyedHeader<T>(T control, string key) where T : FrameworkElement
    {
        SiloPlayer.Services.Localized.SetHeader(control, key);
        return control;
    }

    /// <summary>A code-built input whose literal English placeholder is the fallback for <paramref name="key"/>.</summary>
    internal static T KeyedPlaceholder<T>(T control, string key) where T : FrameworkElement
    {
        SiloPlayer.Services.Localized.SetPlaceholder(control, key);
        return control;
    }

    /// <summary>True while a combo box is being relabelled; value handlers ignore the selection events it causes.</summary>
    [ThreadStatic] internal static bool Relabeling;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBoxItem, ItemText> ItemKeys = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, object> KeyedCombos = new();
    private sealed record ItemText(string Key, string English);

    /// <summary>
    /// Sets a combo row's text, keeping the closed box current: WinUI copies the selected row's content into it, so a
    /// changed selected row is reselected under <see cref="Relabeling"/>.
    /// </summary>
    internal static void Relabel(ComboBox combo, ComboBoxItem item, string label)
    {
        if (Equals(item.Content, label)) return;
        if (!combo.IsLoaded || !ReferenceEquals(combo.SelectedItem, item)) { item.Content = label; return; }
        Relabeling = true;
        try { combo.SelectedItem = null; item.Content = label; combo.SelectedItem = item; }
        finally { Relabeling = false; }
    }

    /// <summary>
    /// A combo row whose literal English content is the fallback for <paramref name="key"/>. Rows of a closed combo box
    /// never load, so the combo box itself carries the refresh for all its keyed rows.
    /// </summary>
    internal static ComboBoxItem KeyedItem(ComboBox combo, ComboBoxItem item, string key)
    {
        // The English fallback is captured once, from the row's original literal content.
        var english = ItemKeys.TryGetValue(item, out var known) ? known.English : item.Content as string ?? "";
        ItemKeys.AddOrUpdate(item, new ItemText(key, english));
        item.Content = Get(key, english);
        if (!KeyedCombos.TryGetValue(combo, out _))
        {
            KeyedCombos.Add(combo, new object());
            SiloPlayer.Services.AppLocalization.Register(combo, () =>
            {
                foreach (var row in combo.Items.OfType<ComboBoxItem>().ToArray())
                    if (ItemKeys.TryGetValue(row, out var text)) Relabel(combo, row, Get(text.Key, text.English));
            });
        }
        return item;
    }

    /// <summary>Keys every existing row of a XAML combo box by its tag.</summary>
    internal static void KeyItems(ComboBox combo, Func<string, string?> keyOfTag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>().ToArray())
            if (keyOfTag(item.Tag as string ?? "") is { } key) KeyedItem(combo, item, key);
    }

    /// <summary>Applies the interface direction to every entry of a code-built menu each time it opens.</summary>
    internal static MenuFlyout Direct(MenuFlyout menu)
    {
        menu.Opening += (_, _) => DirectItems(menu.Items);
        return menu;
    }

    private static void DirectItems(IEnumerable<MenuFlyoutItemBase> items)
    {
        foreach (var item in items)
        {
            SiloPlayer.Services.AppLocalization.ApplyDirection(item);
            if (item is MenuFlyoutSubItem sub) DirectItems(sub.Items);
        }
    }

    /// <summary>
    /// A code-built dialog: <paramref name="refresh"/> sets its text now, again when it opens in a newer language and
    /// whenever the language changes while it is open; its direction follows the interface.
    /// </summary>
    internal static ContentDialog Dialog(ContentDialog dialog, Action refresh)
    {
        refresh();
        SiloPlayer.Services.AppLocalization.ApplyDirection(dialog);
        SiloPlayer.Services.AppLocalization.Register(dialog, () =>
        {
            refresh();
            SiloPlayer.Services.AppLocalization.ApplyDirection(dialog);
        });
        return dialog;
    }
}
