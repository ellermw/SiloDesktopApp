using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The rendering contract of the page. A controller change (a progress reading, a poll, a selection toggle, an action starting) must
/// never replace a control the administrator is using, because a control replaced between pointer-down and pointer-up loses the
/// click and a replaced control loses keyboard focus. So every list keeps one stable element per row identity and every row keeps
/// its controls for as long as it is listed: only the cells whose displayed values changed are rebuilt, and only controls with no
/// focusable content are ever swapped. Nothing here is built per row that can be built once.
/// </summary>
public sealed partial class AdminPreparedFilesPage
{
    /// <summary>Makes <paramref name="host"/>'s children exactly <paramref name="wanted"/>, in order, touching only what differs.</summary>
    private static void SyncChildren(Panel host, IReadOnlyList<UIElement> wanted)
    {
        for (var index = 0; index < wanted.Count; index++)
        {
            var element = wanted[index];
            if (index < host.Children.Count && ReferenceEquals(host.Children[index], element)) continue;
            var existing = host.Children.IndexOf(element);
            if (existing >= 0) host.Children.RemoveAt(existing);
            host.Children.Insert(index, element);
        }
        while (host.Children.Count > wanted.Count) host.Children.RemoveAt(host.Children.Count - 1);
    }

    /// <summary>A check box that is built once; programmatic changes never read as the administrator's choice.</summary>
    private sealed class Tick
    {
        private bool _syncing;
        private string? _name;

        public Tick(string label, string id, Action<bool> changed)
        {
            Box = Check(label, id, false, true, on => { if (!_syncing) changed(on); });
        }

        public CheckBox Box { get; }

        public void Sync(bool isChecked, bool enabled, string? name = null)
        {
            _syncing = true;
            try
            {
                if (Box.IsChecked != isChecked) Box.IsChecked = isChecked;
                if (Box.IsEnabled != enabled) Box.IsEnabled = enabled;
                if (name != null && name != _name) { _name = name; AutomationProperties.SetName(Box, name); }
            }
            finally { _syncing = false; }
        }
    }

    /// <summary>A toggle button that is built once; programmatic changes never read as the administrator's choice.</summary>
    private sealed class Chip
    {
        private bool _syncing;

        public Chip(Microsoft.UI.Xaml.Controls.Primitives.ToggleButton toggle, Action<bool> changed)
        {
            Toggle = toggle;
            toggle.Checked += (_, _) => { if (!_syncing) changed(true); };
            toggle.Unchecked += (_, _) => { if (!_syncing) changed(false); };
        }

        public Microsoft.UI.Xaml.Controls.Primitives.ToggleButton Toggle { get; }

        public void Sync(bool isChecked, double opacity)
        {
            _syncing = true;
            try
            {
                if (Toggle.IsChecked != isChecked) Toggle.IsChecked = isChecked;
                if (Toggle.Opacity != opacity) Toggle.Opacity = opacity;
            }
            finally { _syncing = false; }
        }
    }

    /// <summary>A region that shows one message at a time and keeps the same message (and its Reload control) until the message changes.</summary>
    private sealed class StatusHost : ContentControl
    {
        private string? _kind;

        public StatusHost() { HorizontalContentAlignment = HorizontalAlignment.Stretch; Visibility = Visibility.Collapsed; }

        public void Show(string kind, Func<UIElement> build)
        {
            if (_kind != kind) { _kind = kind; Content = build(); }
            Visibility = Visibility.Visible;
        }

        public void Hide() => Visibility = Visibility.Collapsed;
    }

    /// <summary>One table row: a stable grid of cell hosts, each rebuilt only when its displayed values change.</summary>
    private sealed class CellRow
    {
        private readonly Border[] _cells;
        private readonly object?[] _signatures;
        private readonly ContentControl _below = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };

        public CellRow(double[] weights)
        {
            Grid = new Grid { Padding = new Thickness(0, 8, 0, 8) };
            _cells = new Border[weights.Length];
            _signatures = new object?[weights.Length];
            for (var index = 0; index < weights.Length; index++)
            {
                Grid.ColumnDefinitions.Add(new() { Width = new GridLength(weights[index], GridUnitType.Star) });
                _cells[index] = new Border { Padding = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                Microsoft.UI.Xaml.Controls.Grid.SetColumn(_cells[index], index);
                Grid.Children.Add(_cells[index]);
            }
            var stack = new StackPanel();
            stack.Children.Add(Grid);
            stack.Children.Add(_below);
            Rule = new Border { Child = stack, BorderBrush = Tint("BorderBrush", 0.3), BorderThickness = new Thickness(0, 0, 0, 1) };
        }

        public Border Rule { get; }
        public Grid Grid { get; }

        /// <summary>Content shown under the row (an open details panel); null removes it.</summary>
        public UIElement? Below { get => _below.Content as UIElement; set { if (!ReferenceEquals(_below.Content, value)) _below.Content = value; } }

        /// <summary>Puts a control that stays for the life of the row (a check box, a button group).</summary>
        public void Hold(int index, UIElement content)
        {
            if (!ReferenceEquals(_cells[index].Child, content)) _cells[index].Child = content;
        }

        /// <summary>Rebuilds a cell only when its signature (the values it displays) changed.</summary>
        public void Set(int index, object? signature, Func<UIElement?> build)
        {
            if (_cells[index].Child != null && Equals(_signatures[index], signature)) return;
            _signatures[index] = signature;
            _cells[index].Child = build();
        }
    }

    /// <summary>A header row over keyed body rows. Columns share the table width by their weights and the table scrolls sideways when narrow.</summary>
    private sealed class StableTable : Grid
    {
        private readonly double[] _weights;
        private readonly StackPanel _frame = new();

        public StableTable(IReadOnlyList<UIElement?> headers, double[] weights)
        {
            _weights = weights;
            var header = new Grid { Background = Tint("MutedBrush", 0.3), Padding = new Thickness(0, 10, 0, 10) };
            for (var index = 0; index < weights.Length; index++)
            {
                header.ColumnDefinitions.Add(new() { Width = new GridLength(weights[index], GridUnitType.Star) });
                var cell = new Border { Padding = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Child = index < headers.Count ? headers[index] : null };
                Microsoft.UI.Xaml.Controls.Grid.SetColumn(cell, index);
                header.Children.Add(cell);
            }
            _frame.Children.Add(header);
            _frame.Children.Add(Body);
            Children.Add(new ScrollViewer { Content = _frame, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled,
                VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        }

        public StackPanel Body { get; } = new();
        public double[] Weights => _weights;

        protected override Size MeasureOverride(Size available)
        {
            var minimum = _weights.Sum();
            _frame.Width = Math.Max(minimum, double.IsFinite(available.Width) ? available.Width : minimum);
            return base.MeasureOverride(available);
        }
    }

    /// <summary>The left-aligned text of a table header.</summary>
    private static TextBlock HeaderText(string label, bool end = false)
    {
        var heading = Text(label, 12, true);
        heading.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        heading.TextWrapping = TextWrapping.NoWrap;
        if (end) { heading.HorizontalAlignment = HorizontalAlignment.Right; heading.TextAlignment = TextAlignment.Right; }
        return heading;
    }

    /// <summary>
    /// Keeps keyboard focus on the control the administrator was using across a rebuild of the region that held it: a control that was
    /// replaced is found again by its automation identity and given focus back. Hot paths never replace such controls at all; this
    /// covers the rare rebuild of a card whose displayed values changed.
    /// </summary>
    private void KeepFocus(Action update)
    {
        var root = XamlRoot;
        var focused = root == null ? null : FocusManager.GetFocusedElement(root) as FrameworkElement;
        var id = focused == null ? "" : AutomationProperties.GetAutomationId(focused);
        var origin = focused?.FocusState ?? FocusState.Unfocused;
        update();
        if (root == null || focused == null || id.Length == 0 || origin == FocusState.Unfocused) return;
        if (ReferenceEquals(FocusManager.GetFocusedElement(root), focused)) return;
        if (Descendants(_bodyHost.Content as DependencyObject, id) is Control replacement) replacement.Focus(origin);
    }

    private static FrameworkElement? Descendants(DependencyObject? parent, string id)
    {
        if (parent == null) return null;
        if (parent is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id) return element;
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
            if (Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index), id) is { } found) return found;
        return null;
    }
}
