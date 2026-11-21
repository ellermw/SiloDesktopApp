using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.UI;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The official solid red of a delete confirmation: <c>--destructive-fill</c> (#dc2626) under white <c>--destructive-foreground</c>,
/// 90% on hover (official #4700 and #4704). Page-local: the shared destructive style and the theme are not touched. In a Windows
/// contrast theme the dialog keeps its system colors.
/// </summary>
public static class AdminCollectionsDestructiveStyle
{
    public static readonly Color Fill = Color.FromArgb(255, 0xDC, 0x26, 0x26);
    public static readonly Color Label = Colors.White;
    /// <summary>The fill at 90% (official <c>hover:bg-destructive-fill/90</c>): alpha 230 of 255.</summary>
    public static readonly Color HoverFill = Color.FromArgb(230, 0xDC, 0x26, 0x26);

    public static bool HighContrast
    {
        get { try { return new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast; } catch (Exception) { return false; } }
    }

    // The app's own button templates take their hover brush from the theme, so a dialog resource cannot change it: this page carries its own.
    private const string TemplateXaml = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
            <Border x:Name="Root" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="{TemplateBinding CornerRadius}" Padding="{TemplateBinding Padding}">
                <ContentPresenter x:Name="Label" Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" Foreground="{TemplateBinding Foreground}" FontFamily="{TemplateBinding FontFamily}" FontSize="{TemplateBinding FontSize}" FontWeight="{TemplateBinding FontWeight}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}" />
                <VisualStateManager.VisualStateGroups>
                    <VisualStateGroup x:Name="CommonStates">
                        <VisualState x:Name="Normal" />
                        <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="Root.Background" Value="#E6DC2626" /></VisualState.Setters></VisualState>
                        <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="Root.Background" Value="#E6DC2626" /></VisualState.Setters></VisualState>
                        <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Root.Opacity" Value="0.5" /></VisualState.Setters></VisualState>
                    </VisualStateGroup>
                </VisualStateManager.VisualStateGroups>
            </Border>
        </ControlTemplate>
        """;
    private static Style? _primary;

    /// <summary>The primary button of a delete confirmation: the destructive button's size and type, the official fill and label, and the fill at 90% on hover and press.</summary>
    public static Style PrimaryStyle => _primary ??= Build();
    private static Style Build()
    {
        var style = new Style(typeof(Button));
        if (Application.Current.Resources.TryGetValue("DestructiveButtonStyle", out var shared) && shared is Style basis) style.BasedOn = basis;
        style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(TemplateXaml)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Fill)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Label)));
        return style;
    }

    /// <summary>Draws the dialog's primary button as the official destructive button. Returns false, changing nothing, when a contrast theme keeps system colors.</summary>
    public static bool Apply(ContentDialog dialog, bool highContrast)
    {
        if (highContrast) return false;
        dialog.PrimaryButtonStyle = PrimaryStyle;
        return true;
    }
}

public sealed partial class AdminCollectionsPage
{
    /// <summary>
    /// A collection, shelf or title name on one line. It is cut at the end by the control (trimming), so a long name keeps its start;
    /// the string is never sliced. Content that reads right to left is laid out by its own content, and the full name is the tooltip.
    /// </summary>
    private static TextBlock NameBlock(string name, double size = 14, double maxWidth = 420, bool muted = false, bool strong = false)
    {
        var block = new TextBlock
        {
            Text = name, FontSize = size, MaxWidth = maxWidth, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
            TextReadingOrder = TextReadingOrder.DetectFromContent, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = strong || size >= 20 ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = AdminMonitorUi.Solid(muted ? AdminMonitorUi.Muted : AdminMonitorUi.Foreground)
        };
        if (AdminCollectionNames.Plain(name, 200) is { Length: > 0 } full) ToolTipService.SetToolTip(block, full);
        return block;
    }

    /// <summary>A button that is a name: the same command as <see cref="ActionButton"/>, its label cut by trimming.</summary>
    private Button NameButton(string name, string id, Func<Task> action, double maxWidth = 420)
    {
        var button = ActionButton(name, id, action);
        var block = NameBlock(name, 14, maxWidth - 24, strong: false);
        block.ClearValue(TextBlock.ForegroundProperty); ToolTipService.SetToolTip(block, null); // the label takes the button's own text color in every state
        button.Content = block; button.MaxWidth = maxWidth;
        if (AdminCollectionNames.Plain(name, 200) is { Length: > 0 } full) ToolTipService.SetToolTip(button, full);
        return button;
    }

    /// <summary>A status badge. Failures read as the official destructive text color with medium weight.</summary>
    private static TextBlock StatusText(string text, bool failed, double size = 13)
    {
        var block = Text(text, size, !failed);
        if (failed) { block.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive); block.FontWeight = FontWeights.Medium; }
        return block;
    }

    /// <summary>The names of the libraries a collection is in, in the page's library order.</summary>
    private List<string> LibraryNameList(AdminCollectionEntry c) => Controller.Libraries.Where(l => c.InLibrary(l.Id)).Select(l => l.Name).ToList();
}
