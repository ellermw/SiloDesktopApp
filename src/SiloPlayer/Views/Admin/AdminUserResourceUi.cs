using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

internal static class AdminUserResourceUi
{
    internal static string T(string key, params (string Name, object? Value)[] args) => AdminUserResourceText.T(key, args);
    internal static StackPanel Stack(double spacing = 12) => new() { Spacing = spacing };
    internal static StackPanel Inline(double spacing = 8) => new() { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
    internal static TextBlock Text(string text, double size = 14, bool muted = false) => LogsNativeUi.Text(text, size, muted);
    internal static TextBlock Mono(string text, double size = 12, bool muted = true) => LogsNativeUi.Text(text, size, muted, mono: true);
    /// <summary>
    /// One line cut with an ellipsis at its end (the official <c>truncate</c>), keeping the start of a long name. <paramref name="auto"/> reads the
    /// direction from the text itself (<c>dir="auto"</c>), so a name in another script keeps its own start; the element stays at the page's start side.
    /// </summary>
    internal static TextBlock Truncated(string text, double size = 14, bool muted = false, bool auto = false, bool mono = false)
    {
        text = AdminNameText.SingleLine(text);
        var block = mono ? Mono(text, size, muted) : Text(text, size, muted);
        block.TextWrapping = TextWrapping.NoWrap; block.TextTrimming = TextTrimming.CharacterEllipsis; block.HorizontalAlignment = HorizontalAlignment.Left;
        if (auto) block.TextReadingOrder = TextReadingOrder.DetectFromContent;
        return block;
    }
    /// <summary>At most <paramref name="lines"/> lines, cut with an ellipsis (the official <c>line-clamp-N</c>), the direction read from the text.</summary>
    internal static TextBlock Clamped(string text, int lines, double size = 14, bool muted = false)
    {
        var block = Text(AdminNameText.SingleLine(text), size, muted); block.MaxLines = lines; block.TextTrimming = TextTrimming.CharacterEllipsis; block.TextReadingOrder = TextReadingOrder.DetectFromContent;
        return block;
    }
    /// <summary>Another person's name or text as one bounded plain line for a confirmation, a title or an accessible name.</summary>
    internal static string Safe(string? text, int max = AdminNameText.MaxLabelElements) => AdminNameText.Label(text, max);
    /// <summary>An email address cut short: left to right unless it starts in a right-to-left script, so its start stays readable on either page direction.</summary>
    internal static TextBlock EmailLine(string email, double size = 12)
    {
        var block = Truncated(email, size, true);
        if (AdminNameText.EmailIsLeftToRight(email)) block.FlowDirection = FlowDirection.LeftToRight;
        return block;
    }
    internal static T Id<T>(T element, string id) where T : DependencyObject { AutomationProperties.SetAutomationId(element, id); return element; }
    internal static Brush Brush(string key) => LogsNativeUi.Brush(key);
    /// <summary>Releases an element that is reused across renders from whatever holds it, so it can be placed in a new tree.</summary>
    internal static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Border border: border.Child = null; break;
            case ContentControl content: content.Content = null; break;
        }
    }
    internal static Button Button(string label, string id, Action action, bool accent = false)
    {
        var button = LogsNativeUi.Button(label, id, accent ? "" : "OutlineButtonStyle");
        button.Click += (_, _) => action(); return button;
    }
    internal static Button Link(string label, string id, Action action)
    {
        var button = Id(new Button { Content = label, Padding = new(8, 4, 8, 4), MinHeight = 32, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0), Foreground = Brush("PrimaryTextBrush") }, id);
        AutomationProperties.SetName(button, label); button.Click += (_, _) => action(); return button;
    }
    internal static TextBox Input(string label, string id, string value, Action? changed = null)
    {
        var input = Id(new TextBox { Header = label, Text = value, MinWidth = 160 }, id);
        AutomationProperties.SetName(input, label); if (changed != null) input.TextChanged += (_, _) => changed(); return input;
    }
    /// <summary>A checkbox for facets and selections. Reacts to Checked/Unchecked, so a keyboard or pointer change both reach the owner.</summary>
    internal static CheckBox Toggle(string label, bool value, Action<bool> changed, bool enabled = true)
    {
        var box = new CheckBox { Content = label, IsChecked = value, IsEnabled = enabled, MinHeight = 36 };
        AutomationProperties.SetName(box, label);
        box.Checked += (_, _) => changed(true); box.Unchecked += (_, _) => changed(false); return box;
    }
    /// <summary>The official ToggleRow: a bordered row with a label, its description and a switch. The switch carries the label for assistive technology.</summary>
    internal static Border ToggleRow(string label, string description, bool value, Action<bool> changed, bool enabled = true, string? id = null)
    {
        var toggle = new ToggleSwitch { IsOn = value, IsEnabled = enabled, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, label); if (id != null) AutomationProperties.SetAutomationId(toggle, id);
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        var copy = Stack(2); copy.Children.Add(Text(label, 14)); copy.Children.Add(Text(description, 12, true));
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.Children.Add(copy); Grid.SetColumn(toggle, 1); grid.Children.Add(toggle);
        return new Border { Child = grid, Padding = new(12, 10, 12, 10), CornerRadius = new(8), BorderThickness = new(1), BorderBrush = Brush("BorderBrush") };
    }
    internal static ComboBox Select(string label, IEnumerable<(string Value, string Label)> options, string value, Action<string>? changed = null)
    {
        var select = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 160 };
        foreach (var option in options) select.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
        select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == value);
        AutomationProperties.SetName(select, label);
        if (changed != null) select.SelectionChanged += (_, _) => { if (select.SelectedItem is ComboBoxItem { Tag: string chosen }) changed(chosen); };
        return select;
    }
    internal static Border Panel(UIElement content) => LogsNativeUi.Panel(content, 16, 20);

    /// <summary>The official solid danger fill (<c>--destructive-fill</c>) under white text: 4.83:1, which the shared destructive colour does not reach.</summary>
    internal const string DestructiveFill = "#FFDC2626";
    internal static bool SystemContrast { get { try { return new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast; } catch { return false; } } }
    // The system setting's change event, listened to only while a control that looks at it is in a page's tree (see WatchContrast).
    private static Windows.UI.ViewManagement.AccessibilitySettings? _accessibility;
    private static Action? _contrastRaised;
    private static void OnHighContrastChanged(Windows.UI.ViewManagement.AccessibilitySettings sender, object args) => _contrastRaised?.Invoke();
    private static readonly AdminContrastSignal ContrastChanges = new(() => SystemContrast,
        raise => { try { _contrastRaised = raise; _accessibility ??= new(); _accessibility.HighContrastChanged += OnHighContrastChanged; } catch { /* without the setting the look stays as it was built */ } },
        raise => { try { if (_accessibility != null) _accessibility.HighContrastChanged -= OnHighContrastChanged; } catch { } });
    /// <summary>
    /// Calls <paramref name="reapply"/> with the system contrast setting when the element enters a page (in case it changed meanwhile) and every time it changes
    /// while the element is there; the call is made on the element's own thread. Nothing is listened to once the element has left the tree.
    /// </summary>
    internal static void WatchContrast(FrameworkElement element, Action<bool> reapply)
    {
        IDisposable? subscription = null;
        element.Loaded += (_, _) =>
        {
            if (subscription != null) return;
            var queue = element.DispatcherQueue;
            subscription = ContrastChanges.Subscribe(value => queue.TryEnqueue(() => { if (element.IsLoaded) reapply(value); }));
            reapply(ContrastChanges.Current);
        };
        element.Unloaded += (_, _) => { subscription?.Dispose(); subscription = null; };
    }
    private static string Argb(string brushKey)
    {
        var color = Brush(brushKey) is SolidColorBrush solid ? solid.Color : Microsoft.UI.Colors.Black;
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
    /// <summary>
    /// A destructive button, page-local so the shared style stays as it is: the official fill under white text, 90% of it on hover and press;
    /// in a Windows contrast theme the system colours (window, text and edge in the text colour; highlight on hover) as forced colours does.
    /// Built when asked, so it follows the theme now in effect.
    /// </summary>
    internal static Style DestructiveButtonStyle(bool? contrast = null)
    {
        var system = contrast ?? SystemContrast;
        var fill = system ? Argb("SurfaceBrush") : DestructiveFill; var text = system ? Argb("PrimaryTextBrush") : "#FFFFFFFF"; var edge = system ? Argb("PrimaryTextBrush") : "#00000000";
        var hover = system ? $"<SolidColorBrush Color=\"{Argb("AccentBackgroundBrush")}\"/>" : $"<SolidColorBrush Color=\"{DestructiveFill}\" Opacity=\"0.9\"/>";
        var hoverText = system ? Argb("ThemeAccentForegroundBrush") : "#FFFFFFFF";
        var xaml = $@"<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""Button"">
  <Setter Property=""Background"" Value=""{fill}""/><Setter Property=""Foreground"" Value=""{text}""/><Setter Property=""BorderBrush"" Value=""{edge}""/>
  <Setter Property=""BorderThickness"" Value=""{(system ? "1" : "0")}""/><Setter Property=""MinHeight"" Value=""36""/><Setter Property=""Padding"" Value=""16,8""/>
  <Setter Property=""FontSize"" Value=""14""/><Setter Property=""FontWeight"" Value=""Medium""/><Setter Property=""CornerRadius"" Value=""10""/>
  <Setter Property=""HorizontalContentAlignment"" Value=""Center""/><Setter Property=""VerticalContentAlignment"" Value=""Center""/><Setter Property=""UseSystemFocusVisuals"" Value=""True""/>
  <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
    <Border x:Name=""Root"" Background=""{{TemplateBinding Background}}"" BorderBrush=""{{TemplateBinding BorderBrush}}"" BorderThickness=""{{TemplateBinding BorderThickness}}"" CornerRadius=""{{TemplateBinding CornerRadius}}"" Padding=""{{TemplateBinding Padding}}"">
      <ContentPresenter x:Name=""Content"" Content=""{{TemplateBinding Content}}"" ContentTemplate=""{{TemplateBinding ContentTemplate}}"" Foreground=""{{TemplateBinding Foreground}}"" FontFamily=""{{TemplateBinding FontFamily}}"" FontSize=""{{TemplateBinding FontSize}}"" FontWeight=""{{TemplateBinding FontWeight}}"" HorizontalAlignment=""{{TemplateBinding HorizontalContentAlignment}}"" VerticalAlignment=""{{TemplateBinding VerticalContentAlignment}}""/>
      <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name=""CommonStates"">
        <VisualState x:Name=""Normal""/>
        <VisualState x:Name=""PointerOver""><VisualState.Setters><Setter Target=""Root.Background""><Setter.Value>{hover}</Setter.Value></Setter><Setter Target=""Content.Foreground"" Value=""{hoverText}""/></VisualState.Setters></VisualState>
        <VisualState x:Name=""Pressed""><VisualState.Setters><Setter Target=""Root.Background""><Setter.Value>{hover}</Setter.Value></Setter><Setter Target=""Content.Foreground"" Value=""{hoverText}""/></VisualState.Setters></VisualState>
        <VisualState x:Name=""Disabled""><VisualState.Setters><Setter Target=""Root.Opacity"" Value=""0.5""/></VisualState.Setters></VisualState>
      </VisualStateGroup></VisualStateManager.VisualStateGroups>
    </Border>
  </ControlTemplate></Setter.Value></Setter>
</Style>";
        return (Style)XamlReader.Load(xaml);
    }
    /// <summary>A destructive action as a button of its own, outside a dialog.</summary>
    internal static Button DestructiveButton(string label, string id, Action action)
    {
        var button = Id(new Button { Content = label, Style = DestructiveButtonStyle() }, id);
        AutomationProperties.SetName(button, label); button.Click += (_, _) => action(); return button;
    }
    internal static StackPanel Section(string title, params UIElement[] fields)
    {
        var panel = Stack(); panel.Children.Add(Text(title, 16)); foreach (var field in fields) panel.Children.Add(field); return panel;
    }
    internal static Grid Columns(params FrameworkElement[] items)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        for (var i = 0; i < items.Length; i++) { grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); Grid.SetColumn(items[i], i); grid.Children.Add(items[i]); }
        return grid;
    }
    /// <summary>A small outlined label such as a count, a status or a platform.</summary>
    internal static Border Chip(string text, string? tone = null)
    {
        var block = Text(text, 12, tone == null); if (tone != null) block.Foreground = Brush(tone);
        return new Border { Child = block, Padding = new(8, 2, 8, 2), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Brush(tone ?? "BorderBrush") };
    }
    /// <summary>A row of buttons that act like the official segmented controls: one is pressed, the others are not.</summary>
    internal static StackPanel Segmented(string label, IEnumerable<(string Value, string Label)> options, string value, Action<string> changed, string idPrefix)
    {
        // The official control is a group with an accessible name whose buttons carry their pressed state (aria-pressed); a toggle button reports On or Off.
        var row = new SegmentedRow { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; AutomationProperties.SetName(row, label); var buttons = new List<(ToggleButton Button, string Value)>(); var updating = false;
        var marks = new Dictionary<ToggleButton, (TextBlock Label, Microsoft.UI.Xaml.Shapes.Rectangle Bar)>();
        // The chosen segment carries a foreground bar along its bottom edge, because a fill alone sits close to its neighbours (official #4864); in a contrast theme its label is underlined too.
        void Mark() { var contrast = SystemContrast; foreach (var (segment, (text, bar)) in marks) { var on = segment.IsChecked == true; bar.Visibility = on ? Visibility.Visible : Visibility.Collapsed; text.TextDecorations = on && contrast ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None; } }
        foreach (var (optionValue, optionLabel) in options)
        {
            var text = new TextBlock { Text = optionLabel, VerticalAlignment = VerticalAlignment.Center };
            var bar = new Microsoft.UI.Xaml.Shapes.Rectangle { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Fill = Brush("PrimaryTextBrush"), Margin = new(-12, 0, -12, -4), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            var content = new Grid(); content.Children.Add(text); content.Children.Add(bar);
            var button = Id(new ToggleButton { Content = content, Tag = optionValue, IsChecked = optionValue == value, MinHeight = 32, Padding = new(12, 4, 12, 4) }, idPrefix + "_" + optionValue);
            OfficialSelected(button); marks[button] = (text, bar);
            AutomationProperties.SetName(button, optionLabel); buttons.Add((button, optionValue));
            // Pressed state follows Checked/Unchecked (pointer, keyboard and assistive technology alike); exactly one option stays pressed.
            button.Checked += (_, _) =>
            {
                if (updating) return; updating = true;
                foreach (var (other, otherValue) in buttons) other.IsChecked = otherValue == optionValue;
                updating = false; Mark(); changed(optionValue);
            };
            button.Unchecked += (_, _) => { if (updating) return; updating = true; button.IsChecked = true; updating = false; Mark(); };
            row.Children.Add(button);
        }
        Mark();
        WatchContrast(row, _ => Mark());   // the underline of the chosen label follows the setting while the page is open
        return row;
    }
    /// <summary>
    /// The selected look of the official segmented controls, saved views and profile tabs: the hover fill under the foreground label, whatever the
    /// system accent colour is, so the label stays readable. The label and counts inside inherit this colour. In a Windows contrast theme nothing is
    /// replaced (<see cref="AdminSelectionLook"/>): the platform's highlight fill and text mark the selected option; <paramref name="contrast"/> forces a
    /// theme for a test.
    /// </summary>
    internal static void OfficialSelected(ToggleButton button, bool? contrast = null)
    {
        // In a Windows contrast theme those brushes are the window and the window text, so a selected option would look like the page: the platform's
        // highlight pair stays, and the option keeps its default border.
        ApplySelectedLook(button, contrast ?? SystemContrast);
        // The setting can change while the page is open: a forced theme (a test) is fixed, the real one is followed until the button leaves the tree.
        if (contrast == null) WatchContrast(button, high => ApplySelectedLook(button, high));
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ToggleButton, object> OriginalBorder = new();
    /// <summary>Puts the official look on the button, or takes it off again (the platform's highlight pair shows) when <paramref name="contrast"/> is on, whichever it had before.</summary>
    private static void ApplySelectedLook(ToggleButton button, bool contrast)
    {
        foreach (var key in AdminSelectionLook.KeysRemoved(contrast)) button.Resources.Remove(key);
        if (!AdminSelectionLook.ReplacesCheckedLook(contrast))
        {
            if (OriginalBorder.TryGetValue(button, out var original)) { button.BorderThickness = (Thickness)original; OriginalBorder.Remove(button); }
            return;
        }
        var fill = Brush("SurfaceHoverBrush"); var text = Brush("PrimaryTextBrush"); var none = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in AdminSelectionLook.CheckedBackgroundKeys) button.Resources[key] = fill;
        foreach (var key in AdminSelectionLook.CheckedForegroundKeys) button.Resources[key] = text;
        foreach (var key in AdminSelectionLook.CheckedBorderKeys) button.Resources[key] = none;
        if (!OriginalBorder.TryGetValue(button, out _)) OriginalBorder.Add(button, button.BorderThickness);
        button.BorderThickness = new(0);
    }
    /// <summary>A text block that takes the colour of the button it sits in, so it follows that button's selected and hover states.</summary>
    internal static TextBlock Inherited(TextBlock block, double opacity = 1) { block.ClearValue(TextBlock.ForegroundProperty); block.Opacity = opacity; return block; }
}

/// <summary>The row of a segmented control: a named group for assistive technology, so the selected option is announced inside its group.</summary>
internal sealed class SegmentedRow : StackPanel
{
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this);
    private sealed class GroupPeer(SegmentedRow owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => "SegmentedRow";
    }
}

/// <summary>
/// A name that is cut with an ellipsis when the row is too narrow, followed by a small mark that is never cut: the flex row of a truncating
/// child and a shrink-0 sibling. The mark is measured first; the name takes the rest.
/// </summary>
internal sealed class NameWithMark : Panel
{
    private const double Gap = 6;
    internal NameWithMark(UIElement name, UIElement? mark) { Children.Add(name); if (mark != null) Children.Add(mark); }
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
    {
        double markWidth = 0, height = 0;
        if (Children.Count > 1) { Children[1].Measure(new(double.PositiveInfinity, available.Height)); markWidth = Children[1].DesiredSize.Width + Gap; height = Children[1].DesiredSize.Height; }
        var room = double.IsInfinity(available.Width) ? double.PositiveInfinity : Math.Max(0, available.Width - markWidth);
        Children[0].Measure(new(room, available.Height));
        return new(Children[0].DesiredSize.Width + markWidth, Math.Max(height, Children[0].DesiredSize.Height));
    }
    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
    {
        var name = Children[0]; name.Arrange(new(0, (final.Height - name.DesiredSize.Height) / 2, name.DesiredSize.Width, name.DesiredSize.Height));
        if (Children.Count > 1) { var mark = Children[1]; mark.Arrange(new(name.DesiredSize.Width + Gap, (final.Height - mark.DesiredSize.Height) / 2, mark.DesiredSize.Width, mark.DesiredSize.Height)); }
        return final;
    }
}

/// <summary>
/// Two cells in one row that grow from nothing in equal shares, each up to its full width (official Add users row: <c>max-w-max min-w-0 flex-1</c>
/// twice): a short name shows whole, and two long ones split the row. The second cell sits at the row's end.
/// </summary>
internal sealed class SharedNamesRow : Panel
{
    private const double Gap = 12;
    /// <summary>
    /// A pixel of slack in the "both fit" decision. A desired width is rounded to the device pixel grid, so on a display scaled to 125% or 150%
    /// (and with a gap that is not a whole device pixel) the rounded widths can be a fraction short of what the text needs: an exact comparison
    /// would trim a name that fits. (The same constant is <c>LayoutTolerance.Fit</c> on the scaled-display hardening branch.)
    /// </summary>
    private const double FitTolerance = 1;
    internal SharedNamesRow(UIElement first, UIElement second) { Children.Add(first); Children.Add(second); }
    private double _first, _second;
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
    {
        Children[0].Measure(new(double.PositiveInfinity, available.Height)); Children[1].Measure(new(double.PositiveInfinity, available.Height));
        var natural0 = Children[0].DesiredSize.Width; var natural1 = Children[1].DesiredSize.Width;
        (_first, _second) = Share(natural0, natural1, available.Width);
        // A cell that gets all of its natural width is not measured again: its desired width is rounded to the pixel grid, so measuring it at
        // exactly that width can offer a fraction less than its text needs, and an ellipsis would show on a name that fits.
        if (_first < natural0) Children[0].Measure(new(_first, available.Height));
        if (_second < natural1) Children[1].Measure(new(_second, available.Height));
        return new(double.IsInfinity(available.Width) ? _first + _second + Gap : available.Width, Math.Max(Children[0].DesiredSize.Height, Children[1].DesiredSize.Height));
    }
    /// <summary>The widths two cells get: their own when both fit, else equal shares in which a cell never takes more than it needs.</summary>
    internal static (double First, double Second) Share(double natural0, double natural1, double available)
    {
        var room = Math.Max(0, available - Gap);
        if (double.IsInfinity(available) || natural0 + natural1 <= room + FitTolerance) return (natural0, natural1);
        var half = room / 2;
        if (natural0 <= half) return (natural0, Math.Min(natural1, room - natural0));
        if (natural1 <= half) return (Math.Min(natural0, room - natural1), natural1);
        return (half, half);
    }
    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
    {
        Children[0].Arrange(new(0, 0, _first, final.Height));
        Children[1].Arrange(new(Math.Max(0, final.Width - _second), 0, _second, final.Height));
        return final;
    }
}

/// <summary>
/// Shared presentation lifetime, with owned modal dismissal and stale UI command guards. All callbacks stay on the page dispatcher.
/// The failure line and Reload sit outside the interaction root, so a locked page (unknown outcome, stale validator) can always be recovered.
/// </summary>
internal sealed class AdminUserResourceHost : IDisposable
{
    internal readonly StackPanel Body = AdminUserResourceUi.Stack(24);
    internal readonly StackPanel Content = AdminUserResourceUi.Stack(24);
    internal readonly TextBlock Status = AdminUserResourceUi.Id(AdminUserResourceUi.Text(""), "ResourceStatus");
    /// <summary>Extra recovery actions a page offers while a write needs review (for example, reload keeping the draft).</summary>
    internal readonly StackPanel Recovery = AdminUserResourceUi.Stack(8);
    internal readonly Button Reload;
    internal AdminUserResourceController Session { get; }
    private readonly Page _page;
    private readonly Func<Task> _load;
    private readonly Action _clear;
    private ContentDialog? _dialog;
    // A button pressed while the previous dialog is closing waits for it (bounded) instead of being dropped; leaving, reloading or retiring cancels what waits.
    private readonly AdminDialogSlot _slot = new();
    private CancellationTokenSource _waiting = new();
    // A short line for a press that was dropped without anything having failed (the previous dialog did not finish closing in time).
    private string _notice = "";
    private readonly ContentControl _interactionRoot = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private bool _disposed;
    internal AdminUserResourceHost(Page page, AdminUserResourceController controller, Func<Task> load, Action clear)
    {
        _page = page; Session = controller; _load = load; _clear = clear;
        Reload = AdminUserResourceUi.Button("Reload", "ResourceReload", () => _ = ReloadAsync());
        Reload.HorizontalAlignment = HorizontalAlignment.Left;
        _interactionRoot.Content = Content; Status.Visibility = Visibility.Collapsed; Recovery.Visibility = Visibility.Collapsed;
        Body.Children.Add(Status); Body.Children.Add(Recovery); Body.Children.Add(Reload); Body.Children.Add(_interactionRoot); Body.Padding = new(0, 0, 0, 24);
        page.Content = new ScrollViewer { Content = Body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        page.Loaded += Loaded; page.Unloaded += Unloaded;
        controller.Changed += Changed; controller.Retired += Retired;
    }
    internal event Action? StateChanged;
    private async void Loaded(object sender, RoutedEventArgs e)
    {
        try { Session.Open(); await _load(); } catch (Exception error) { Session.Report(error); }
        Changed();
    }
    private void Unloaded(object sender, RoutedEventArgs e) => Dispose();
    private void Changed()
    {
        if (_disposed) return;
        if (!_page.DispatcherQueue.HasThreadAccess) { _page.DispatcherQueue.TryEnqueue(Changed); return; }
        var message = Session.Error ?? (Session.Owner is { IsCurrent: true } ? _notice : "The admin session changed. Reload to continue.");
        Status.Text = message; Status.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Status.Foreground = AdminUserResourceUi.Brush(Session.Error != null ? "ErrorBrush" : "SecondaryTextBrush");
        Recovery.Visibility = Session.NeedsReview && Recovery.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Reload is the way back from a failed read or a locked page; with nothing wrong it stays out of the way.
        Reload.Visibility = Session.Error != null || Session.NeedsReview || Session.Owner is not { IsCurrent: true } ? Visibility.Visible : Visibility.Collapsed;
        Reload.IsEnabled = !Session.Busy; _interactionRoot.IsEnabled = Session.Owner is { IsCurrent: true } && !Session.Busy && !Session.NeedsReview;
        StateChanged?.Invoke();
    }
    private void Retired()
    {
        if (!_page.DispatcherQueue.HasThreadAccess) { _page.DispatcherQueue.TryEnqueue(Retired); return; }
        if (Session.Owner is { IsCurrent: true }) return;
        CancelWaiting(); _dialog?.Hide(); _clear(); Content.Children.Clear(); Changed();
    }
    /// <summary>Tells the slot when <paramref name="dialog"/> has closed, or has already left the screen (a popup is removed a moment before its Closed event and its owner's continuation run).</summary>
    private void TrackClosing(ContentDialog dialog, AdminDialogSlot.Lease slot)
    {
        // "Shown" is any sign that the dialog reached the screen, so one hidden the moment it appeared (before its Opened event) still counts once it is gone.
        var shown = false;
        dialog.Loaded += (_, _) => shown = true; dialog.Opened += (_, _) => shown = true; dialog.Closing += (_, _) => shown = true;
        dialog.Closed += (_, _) => slot.MarkClosed();
        slot.GoneWhen(() => shown && _page.XamlRoot is { } root && !VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => ContainsDialog(popup.Child, dialog)));
    }
    private static bool ContainsDialog(DependencyObject? node, ContentDialog dialog)
    {
        if (node == null) return false;
        if (ReferenceEquals(node, dialog)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) if (ContainsDialog(VisualTreeHelper.GetChild(node, i), dialog)) return true;
        return false;
    }
    /// <summary>Drops every dialog request still waiting for a closing dialog: what it was built from is being replaced or left.</summary>
    private void CancelWaiting() { _notice = ""; var old = _waiting; _waiting = new(); old.Cancel(); old.Dispose(); }
    /// <summary>Takes the dialog slot for a request built under <paramref name="owner"/> from state that <paramref name="stillCurrent"/> checks, or null (see <see cref="AdminDialogSlot.AcquireAsync"/>).</summary>
    private async Task<AdminDialogSlot.Lease?> OpenSlotAsync(MonitorAuthorityLease? owner, Func<bool>? stillCurrent)
    {
        // The slot checks once before any wait and once after it. A press that waited resumes after the closing dialog's owner has gone on, which may
        // have started a write (a value editor's Save): opening then would show a dialog whose action is refused or retired, so it is refused and said.
        var checks = 0; var writing = false;
        bool Current()
        {
            if (_disposed || !_page.IsLoaded || !Session.IsCurrent(owner) || !(stillCurrent?.Invoke() ?? true)) return false;
            if (checks++ > 0 && (Session.Busy || Session.NeedsReview)) { writing = true; return false; }
            return true;
        }
        var (lease, refusal) = await _slot.TryAcquireAsync(Current, _waiting.Token);
        if (_disposed) return lease;
        // A press that waited the whole patience for a closing dialog and got nothing, or that waited and found a write running, is a drop the admin is
        // told about; a stale request, a leave or reload, and a request that lost the slot to another press (whose dialog is then on screen) need no line.
        var notice = lease != null ? "" : refusal == AdminDialogSlot.Refusal.TimedOut ? AdminUserResourceText.T("admin.native.userResources.dialogStillClosing")
            : refusal == AdminDialogSlot.Refusal.Stale && writing ? AdminUserResourceText.T("admin.native.userResources.dialogWriteRunning") : null;
        if (notice != null && notice != _notice) { _notice = notice; Changed(); }
        return lease;
    }
    /// <summary>A dialog that could not be opened or failed while it was open (a WinUI exception such as "only a single ContentDialog can be open"): the page's error line says so instead of the failure vanishing with a discarded task.</summary>
    private void ReportOpenFailure(Exception error, MonitorAuthorityLease? owner)
    {
        if (_disposed || !Session.IsCurrent(owner)) return;
        Session.Report(new InvalidOperationException(AdminUserResourceText.T("admin.native.userResources.dialogOpenFailed"), error));
    }
    internal async Task ReloadAsync()
    {
        if (Session.Busy) return;
        CancelWaiting(); _dialog?.Hide(); _clear(); Content.Children.Clear();
        try { Session.BeginReview(); await _load(); } catch (Exception error) { Session.Report(error); }
        Changed();
    }
    internal void RaiseChanged() => Changed();
    /// <summary>A confirmation. <paramref name="destructive"/> draws the confirm button in the official danger fill. Names inside <paramref name="title"/> and <paramref name="description"/> must already be bounded plain text (<see cref="AdminUserResourceUi.Safe"/>).</summary>
    internal async Task<bool> ConfirmAsync(string title, string description, string confirm, bool destructive = false, Func<bool>? stillCurrent = null)
    {
        var content = AdminUserResourceUi.Text(description); content.MaxWidth = 520;
        return await DialogAsync(title, content, confirm, () => true, destructive, stillCurrent);
    }
    /// <summary>
    /// A dialog. If another is still closing, this one opens when it has finished (bounded; <paramref name="stillCurrent"/> is checked again then, so a
    /// request built from a row or list that has since been replaced is dropped, never shown); if one is showing, or the page is left or retired, it
    /// returns false.
    /// </summary>
    internal async Task<bool> DialogAsync(string title, UIElement content, string confirm, Func<bool> valid, bool destructive = false, Func<bool>? stillCurrent = null)
    {
        var owner = Session.Owner;
        using var slot = await OpenSlotAsync(owner, stillCurrent); if (slot == null) return false;
        var dialog = new ContentDialog { XamlRoot = _page.XamlRoot, Title = title, Content = content, PrimaryButtonText = confirm,
            CloseButtonText = AdminUserResourceUi.T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        if (destructive) dialog.PrimaryButtonStyle = AdminUserResourceUi.DestructiveButtonStyle();
        dialog.PrimaryButtonClick += (_, args) => { if (!Session.IsCurrent(owner) || !valid()) args.Cancel = true; };
        TrackClosing(dialog, slot);
        _dialog = dialog;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary && Session.IsCurrent(owner); }
        catch (Exception error) { ReportOpenFailure(error, owner); return false; }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    /// <summary>
    /// A dialog whose confirm button runs the write. The dialog stays open on a refusal or unknown outcome, showing the failure and a Reload
    /// action, so the admin can correct and retry (or reload); it closes only after the write succeeded or the owner retired.
    /// </summary>
    internal async Task<bool> SaveDialogAsync(string title, UIElement fields, string confirm, Func<bool> valid, Func<Task<bool>> save, Func<bool>? stillCurrent = null)
    {
        var owner = Session.Owner;
        using var slot = await OpenSlotAsync(owner, stillCurrent); if (slot == null) return false;
        var content = AdminUserResourceUi.Stack(); var message = AdminUserResourceUi.Text(""); message.Foreground = AdminUserResourceUi.Brush("ErrorBrush");
        var reload = AdminUserResourceUi.Button("Reload", "DialogReload", () => { });
        content.Children.Add(fields); content.Children.Add(message); content.Children.Add(reload);
        var dialog = new ContentDialog { XamlRoot = _page.XamlRoot, Title = title, Content = new ScrollViewer { Content = content, MaxHeight = 620 },
            PrimaryButtonText = confirm, CloseButtonText = AdminUserResourceUi.T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        reload.Click += (_, _) => { dialog.Hide(); _ = ReloadAsync(); };
        void State()
        {
            if (!_page.DispatcherQueue.HasThreadAccess) { _page.DispatcherQueue.TryEnqueue(State); return; }
            message.Text = Session.Error ?? ""; reload.Visibility = Session.NeedsReview ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = Session.IsCurrent(owner) && !Session.Busy && !Session.NeedsReview;
            fields.IsHitTestVisible = !Session.Busy;
        }
        dialog.Closing += (_, args) => { if (Session.Busy && Session.IsCurrent(owner)) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try { args.Cancel = !Session.IsCurrent(owner) || !valid() || !await save(); }
            catch (Exception error) { Session.Report(error); args.Cancel = true; }
            finally { State(); deferral.Complete(); }
        };
        TrackClosing(dialog, slot);
        Session.Changed += State; _dialog = dialog; State();
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary && Session.IsCurrent(owner); }
        catch (Exception error) { ReportOpenFailure(error, owner); return false; }
        finally { Session.Changed -= State; if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    /// <summary>
    /// A dialog that stays open while the admin works inside it (for example, mapping several users in turn): it has only a close action, shows the
    /// current failure and a Reload action, and is dismissed when the owner retires.
    /// </summary>
    internal async Task ToolDialogAsync(string title, UIElement content, string close, Func<bool>? stillCurrent = null)
    {
        var owner = Session.Owner;
        using var slot = await OpenSlotAsync(owner, stillCurrent); if (slot == null) return;
        var message = AdminUserResourceUi.Text(""); message.Foreground = AdminUserResourceUi.Brush("ErrorBrush");
        var reload = AdminUserResourceUi.Button("Reload", "ToolDialogReload", () => { });
        var panel = AdminUserResourceUi.Stack(); panel.Children.Add(content); panel.Children.Add(message); panel.Children.Add(reload);
        var dialog = new ContentDialog { XamlRoot = _page.XamlRoot, Title = title, Content = new ScrollViewer { Content = panel, MaxHeight = 620 }, CloseButtonText = close, DefaultButton = ContentDialogButton.Close };
        reload.Click += (_, _) => { dialog.Hide(); _ = ReloadAsync(); };
        void State()
        {
            if (!_page.DispatcherQueue.HasThreadAccess) { _page.DispatcherQueue.TryEnqueue(State); return; }
            message.Text = Session.Error ?? ""; reload.Visibility = Session.NeedsReview ? Visibility.Visible : Visibility.Collapsed; content.IsHitTestVisible = !Session.Busy && !Session.NeedsReview;
            if (!Session.IsCurrent(owner)) dialog.Hide();
        }
        dialog.Closing += (_, args) => { if (Session.Busy && Session.IsCurrent(owner)) args.Cancel = true; };
        TrackClosing(dialog, slot);
        Session.Changed += State; _dialog = dialog; State();
        try { await dialog.ShowAsync(); }
        catch (Exception error) { ReportOpenFailure(error, owner); }
        finally { Session.Changed -= State; if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    /// <summary>
    /// A dialog the page builds itself (the subtitle editor), through the same slot as every other dialog, so it and they never overlap and a press made
    /// while another is closing waits like theirs. <paramref name="create"/> runs once the slot is held (so it reads the state as it is then) and returns
    /// the dialog, or null to open nothing. The slot stays held until <paramref name="finish"/> has run after the dialog closed, so nothing opens before
    /// the page has done what it does at the end (a save that was already on its way, the read that follows it).
    /// </summary>
    internal async Task CustomDialogAsync(Func<ContentDialog?> create, Func<ContentDialog, Task> finish, Func<bool>? stillCurrent = null)
    {
        var owner = Session.Owner;
        using var slot = await OpenSlotAsync(owner, stillCurrent); if (slot == null) return;
        ContentDialog? dialog = null; var closed = false;
        try
        {
            dialog = create(); if (dialog == null) return;
            dialog.XamlRoot ??= _page.XamlRoot;
            TrackClosing(dialog, slot); _dialog = dialog;
            await dialog.ShowAsync(); closed = true;
        }
        catch (Exception error) { ReportOpenFailure(error, owner); }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
        if (dialog == null || !closed) return;
        try { await finish(dialog); }
        catch (Exception error) { if (!_disposed && Session.IsCurrent(owner)) Session.Report(error); }
    }
    internal void Suspend() { Session.Suspend(); CancelWaiting(); _dialog?.Hide(); _clear(); Content.Children.Clear(); Changed(); }
    public void Dispose()
    {
        if (_disposed) return;
        Suspend(); _page.Loaded -= Loaded; _page.Unloaded -= Unloaded; Session.Changed -= Changed; Session.Retired -= Retired; Session.Dispose();
        _disposed = true; StateChanged = null;
    }
}
