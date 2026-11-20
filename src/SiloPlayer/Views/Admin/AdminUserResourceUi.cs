using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

internal static class AdminUserResourceUi
{
    internal static string T(string key, params (string Name, object? Value)[] args) => AdminUserResourceText.T(key, args);
    internal static StackPanel Stack(double spacing = 12) => new() { Spacing = spacing };
    internal static StackPanel Inline(double spacing = 8) => new() { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
    internal static TextBlock Text(string text, double size = 14, bool muted = false) => LogsNativeUi.Text(text, size, muted);
    internal static TextBlock Mono(string text, double size = 12, bool muted = true) => LogsNativeUi.Text(text, size, muted, mono: true);
    internal static T Id<T>(T element, string id) where T : DependencyObject { AutomationProperties.SetAutomationId(element, id); return element; }
    internal static Brush Brush(string key) => LogsNativeUi.Brush(key);
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
        var row = Inline(0); AutomationProperties.SetName(row, label); var buttons = new List<(ToggleButton Button, string Value)>(); var updating = false;
        foreach (var (optionValue, optionLabel) in options)
        {
            var button = Id(new ToggleButton { Content = optionLabel, IsChecked = optionValue == value, MinHeight = 32, Padding = new(12, 4, 12, 4) }, idPrefix + "_" + optionValue);
            AutomationProperties.SetName(button, optionLabel); buttons.Add((button, optionValue));
            // Pressed state follows Checked/Unchecked (pointer, keyboard and assistive technology alike); exactly one option stays pressed.
            button.Checked += (_, _) =>
            {
                if (updating) return; updating = true;
                foreach (var (other, otherValue) in buttons) other.IsChecked = otherValue == optionValue;
                updating = false; changed(optionValue);
            };
            button.Unchecked += (_, _) => { if (updating) return; updating = true; button.IsChecked = true; updating = false; };
            row.Children.Add(button);
        }
        return row;
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
        var message = Session.Error ?? (Session.Owner is { IsCurrent: true } ? "" : "The admin session changed. Reload to continue.");
        Status.Text = message; Status.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Status.Foreground = AdminUserResourceUi.Brush(Session.Error != null ? "ErrorBrush" : "SecondaryTextBrush");
        Recovery.Visibility = Session.NeedsReview && Recovery.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Reload.IsEnabled = !Session.Busy; _interactionRoot.IsEnabled = Session.Owner is { IsCurrent: true } && !Session.Busy && !Session.NeedsReview;
        StateChanged?.Invoke();
    }
    private void Retired()
    {
        if (!_page.DispatcherQueue.HasThreadAccess) { _page.DispatcherQueue.TryEnqueue(Retired); return; }
        if (Session.Owner is { IsCurrent: true }) return;
        _dialog?.Hide(); _clear(); Content.Children.Clear(); Changed();
    }
    internal async Task ReloadAsync()
    {
        if (Session.Busy) return;
        _dialog?.Hide(); _clear(); Content.Children.Clear();
        try { Session.BeginReview(); await _load(); } catch (Exception error) { Session.Report(error); }
        Changed();
    }
    internal void RaiseChanged() => Changed();
    internal async Task<bool> ConfirmAsync(string title, string description, string confirm)
    {
        var content = AdminUserResourceUi.Text(description); content.MaxWidth = 520;
        return await DialogAsync(title, content, confirm, () => true);
    }
    internal async Task<bool> DialogAsync(string title, UIElement content, string confirm, Func<bool> valid)
    {
        var owner = Session.Owner;
        if (!Session.IsCurrent(owner) || _dialog != null || !_page.IsLoaded) return false;
        var dialog = new ContentDialog { XamlRoot = _page.XamlRoot, Title = title, Content = content, PrimaryButtonText = confirm,
            CloseButtonText = AdminUserResourceUi.T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        dialog.PrimaryButtonClick += (_, args) => { if (!Session.IsCurrent(owner) || !valid()) args.Cancel = true; };
        _dialog = dialog;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary && Session.IsCurrent(owner); }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    /// <summary>
    /// A dialog whose confirm button runs the write. The dialog stays open on a refusal or unknown outcome, showing the failure and a Reload
    /// action, so the admin can correct and retry (or reload); it closes only after the write succeeded or the owner retired.
    /// </summary>
    internal async Task<bool> SaveDialogAsync(string title, UIElement fields, string confirm, Func<bool> valid, Func<Task<bool>> save)
    {
        var owner = Session.Owner;
        if (!Session.IsCurrent(owner) || _dialog != null || !_page.IsLoaded) return false;
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
        Session.Changed += State; _dialog = dialog; State();
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary && Session.IsCurrent(owner); }
        finally { Session.Changed -= State; if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    /// <summary>
    /// A dialog that stays open while the admin works inside it (for example, mapping several users in turn): it has only a close action, shows the
    /// current failure and a Reload action, and is dismissed when the owner retires.
    /// </summary>
    internal async Task ToolDialogAsync(string title, UIElement content, string close)
    {
        var owner = Session.Owner;
        if (!Session.IsCurrent(owner) || _dialog != null || !_page.IsLoaded) return;
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
        Session.Changed += State; _dialog = dialog; State();
        try { await dialog.ShowAsync(); }
        finally { Session.Changed -= State; if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    internal void Suspend() { Session.Suspend(); _dialog?.Hide(); _clear(); Content.Children.Clear(); Changed(); }
    public void Dispose()
    {
        if (_disposed) return;
        Suspend(); _page.Loaded -= Loaded; _page.Unloaded -= Unloaded; Session.Changed -= Changed; Session.Retired -= Retired; Session.Dispose();
        _disposed = true; StateChanged = null;
    }
}
