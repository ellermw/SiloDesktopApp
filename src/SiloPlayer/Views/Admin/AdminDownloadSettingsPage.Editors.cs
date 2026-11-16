using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminDownloadSettingsPage
{
    /// <summary>Official LimitField: an Unlimited check ahead of a number box. The check sits before the box so the box ends on the row's shared control edge.</summary>
    private sealed class LimitEditor : Grid
    {
        internal CheckBox Unlimited { get; }
        internal TextBox Box { get; }
        /// <summary>The limit Unlimited replaced, restored when it is unchecked (never the unlimited sentinel itself).</summary>
        internal string LastLimit { get; set; } = "";

        internal LimitEditor(string label, string key)
        {
            ColumnSpacing = 12;
            ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            Unlimited = new CheckBox
            {
                Content = AdminText.T("web.settings.limitField.unlimited"), MinWidth = 0, MinHeight = 36, Padding = new(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, UseSystemFocusVisuals = true,
            };
            AutomationProperties.SetName(Unlimited, AdminText.T("web.settings.limitField.unlimited"));
            AutomationProperties.SetHelpText(Unlimited, label);
            AutomationProperties.SetAutomationId(Unlimited, "Downloads_" + key + "_Unlimited");
            Box = U.Input(label, "Downloads_" + key, "", number: true);
            Grid.SetColumn(Box, 1);
            Children.Add(Unlimited); Children.Add(Box);
            HorizontalAlignment = HorizontalAlignment.Right;
        }

        internal void SetWide(bool wide)
        {
            ColumnDefinitions[1].Width = wide ? GridLength.Auto : new(1, GridUnitType.Star);
            Box.Width = wide ? 160 : double.NaN;
            Box.HorizontalAlignment = wide ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        }
    }

    /// <summary>Official PathSettingField: a text box whose placeholder names the derived default, with Reset to default under it while overridden.</summary>
    private sealed class PathEditor : StackPanel
    {
        internal TextBox Box { get; }
        internal Button Reset { get; }

        internal PathEditor(string label, string key)
        {
            Spacing = 4;
            Box = U.Input(label, "Downloads_" + key);
            Box.HorizontalAlignment = HorizontalAlignment.Stretch;
            Reset = U.Action(AdminText.T("web.settings.pathSettingField.reset"), "Downloads_" + key + "_Reset");
            // ghost xs: muted text, no border, right aligned and tucked to the control edge.
            Reset.Height = 28; Reset.MinHeight = 28; Reset.FontSize = 12; Reset.BorderThickness = new(0);
            Reset.Padding = new(8, 0, 8, 0); Reset.HorizontalAlignment = HorizontalAlignment.Right; Reset.Margin = new(0, 0, -8, 0);
            Reset.Foreground = U.Brush("SecondaryTextBrush");
            Reset.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(Reset, AdminText.T("web.settings.pathSettingField.resetLabel", new { label }));
            Children.Add(Box); Children.Add(Reset);
            HorizontalAlignment = HorizontalAlignment.Right;
        }

        internal void SetWide(bool wide)
        {
            Box.Width = wide ? 240 : double.NaN;
            Box.HorizontalAlignment = wide ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            Width = wide ? 240 : double.NaN;
            HorizontalAlignment = wide ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        }
    }

    /// <summary>Official AdvancedSection disclosure: a quiet row with a chevron, the title and a trailing count.</summary>
    private Button NewDisclosure()
    {
        var template = (ControlTemplate)XamlReader.Load("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Border x:Name="DisclosureRoot" Background="Transparent" Padding="{TemplateBinding Padding}"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ContentPresenter Content="{TemplateBinding Content}" HorizontalContentAlignment="Stretch" VerticalAlignment="Center"/>
              </Border>
            </ControlTemplate>
            """);
        var button = new Button
        {
            Template = template, MinWidth = 0, MinHeight = 0, Padding = new(0, 12, 0, 12), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, UseSystemFocusVisuals = true,
            FocusVisualPrimaryBrush = AdminMonitorUi.Solid(AdminMonitorUi.ColorOf("FocusVisualPrimaryBrush", Microsoft.UI.Colors.White), .5),
            FocusVisualPrimaryThickness = new(3), FocusVisualSecondaryBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            CornerRadius = new(6),
        };
        AutomationProperties.SetAutomationId(button, "DownloadsAdvanced");
        button.PointerEntered += (_, _) => { _disclosureHover = true; RenderDisclosure(); };
        button.PointerExited += (_, _) => { _disclosureHover = false; RenderDisclosure(); };
        button.GotFocus += (_, _) => RenderDisclosure();
        return button;
    }

    private void RenderDisclosure()
    {
        var open = _advancedOpen;
        var count = AdminDownloadSettingsRules.AdvancedKeys.Count;
        var title = T("web.settings.advancedSection.title");
        var foreground = open || _disclosureHover ? U.Brush("PrimaryTextBrush") : U.Brush("SecondaryTextBrush");
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        // The chevron turns violet while open, as the official rotated icon does.
        var chevron = AdminMonitorUi.Icon(open ? "chevron-down" : "chevron-right", 14, open ? U.Accent : U.Brush("SecondaryTextBrush"));
        chevron.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(chevron);
        var label = U.Text(title, 13); label.FontWeight = FontWeights.Medium; label.Foreground = foreground; label.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(label, 1); content.Children.Add(label);
        var number = U.Text(AdminText.FormatNumber(count), 12, true); number.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(number, 2); content.Children.Add(number);
        _disclosure.Content = content;
        AutomationProperties.SetName(_disclosure, T("web.settings.advancedSection.accessibleLabel",
            new Dictionary<string, object?> { ["title"] = title, ["count"] = (long)count, ["count_text"] = AdminText.FormatNumber(count) }));
        AutomationProperties.SetItemStatus(_disclosure, open ? "expanded" : "collapsed");
        _rail.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Official SettingsSubheading: an uppercase quiet label with its own hairline, inside the advanced rail.</summary>
    private static Border Subheading(string text)
    {
        var heading = U.Text(text.ToUpper(System.Globalization.CultureInfo.CurrentCulture), 12, true);
        heading.FontWeight = FontWeights.SemiBold; heading.CharacterSpacing = 50;
        AutomationProperties.SetName(heading, text);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level3);
        return new Border
        {
            Child = heading, Padding = new(0, 16, 0, 4), BorderThickness = new(0, 0, 0, 1),
            BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6),
        };
    }
}
