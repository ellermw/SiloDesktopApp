using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

internal static class AdminUserResourceUi
{
    internal static string T(string key, params (string Name, object? Value)[] args) => AdminUserResourceText.T(key, args);
    internal static StackPanel Stack(double spacing = 12) => new() { Spacing = spacing };
    internal static TextBlock Text(string text, double size = 14, bool muted = false) => LogsNativeUi.Text(text, size, muted);
    internal static T Id<T>(T element, string id) where T : DependencyObject { AutomationProperties.SetAutomationId(element, id); return element; }
    internal static Button Button(string label, string id, Action action)
    {
        var button = Id(new Button { Content = label, MinHeight = 36 }, id);
        AutomationProperties.SetName(button, label); button.Click += (_, _) => action(); return button;
    }
    internal static TextBox Input(string label, string id, string value, Action? changed = null)
    {
        var input = Id(new TextBox { Header = label, Text = value, MinWidth = 160 }, id);
        AutomationProperties.SetName(input, label); if (changed != null) input.TextChanged += (_, _) => changed(); return input;
    }
    internal static CheckBox Toggle(string label, bool value, Action<bool> changed, bool enabled = true)
    {
        var box = new CheckBox { Content = label, IsChecked = value, IsEnabled = enabled, MinHeight = 36 };
        box.Checked += (_, _) => changed(true); box.Unchecked += (_, _) => changed(false); return box;
    }
    internal static ComboBox Select(string label, IEnumerable<(string Value, string Label)> options, string value, Action<string>? changed = null)
    {
        var select = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 160 };
        foreach (var option in options) select.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
        select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == value);
        AutomationProperties.SetName(select, label);
        if (changed != null) select.SelectionChanged += (_, _) => changed((select.SelectedItem as ComboBoxItem)?.Tag as string ?? "");
        return select;
    }
    internal static Border Panel(UIElement content) => new() { Child = content, Padding = new(20), CornerRadius = new(16), Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] };
    internal static StackPanel Section(string title, params UIElement[] fields)
    {
        var panel = Stack(); panel.Children.Add(Text(title, 16)); foreach (var field in fields) panel.Children.Add(field); return panel;
    }
    internal static Grid Columns(params UIElement[] items)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        for (var i = 0; i < items.Length; i++) { grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); Grid.SetColumn(items[i], i); grid.Children.Add(items[i]); }
        return grid;
    }
}

/// <summary>Shared presentation lifetime, with owned modal dismissal and stale UI command guards. All callbacks stay on the page dispatcher.</summary>
internal sealed class AdminUserResourceHost : IDisposable
{
    internal readonly StackPanel Body = AdminUserResourceUi.Stack(24);
    internal readonly StackPanel Content = AdminUserResourceUi.Stack(24);
    internal readonly TextBlock Status = AdminUserResourceUi.Id(AdminUserResourceUi.Text(""), "ResourceStatus");
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
        Reload = AdminUserResourceUi.Button("Reload and review", "ResourceReload", () => _ = ReloadAsync());
        _interactionRoot.Content = Content;
        Body.Children.Add(Status); Body.Children.Add(Reload); Body.Children.Add(_interactionRoot); Body.Padding = new(0, 0, 0, 24);
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
        Status.Text = Session.Error ?? (Session.Owner is { IsCurrent: true } ? "" : "The admin session changed. Reload to continue.");
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
    internal async Task<bool> SaveDialogAsync(string title, UIElement fields, string confirm, Func<bool> valid, Func<Task<bool>> save)
    {
        var owner = Session.Owner;
        if (!Session.IsCurrent(owner) || _dialog != null || !_page.IsLoaded) return false;
        var content = AdminUserResourceUi.Stack(); var message = AdminUserResourceUi.Text("");
        content.Children.Add(fields); content.Children.Add(message);
        content.Children.Add(AdminUserResourceUi.Button("Reload and review", "DialogReload", () => _ = ReloadAsync()));
        var dialog = new ContentDialog { XamlRoot = _page.XamlRoot, Title = title, Content = new ScrollViewer { Content = content, MaxHeight = 620 },
            PrimaryButtonText = confirm, CloseButtonText = AdminUserResourceUi.T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        void State()
        {
            if (!_page.DispatcherQueue.HasThreadAccess) { _page.DispatcherQueue.TryEnqueue(State); return; }
            message.Text = Session.Error ?? ""; dialog.IsPrimaryButtonEnabled = Session.IsCurrent(owner) && !Session.Busy && !Session.NeedsReview;
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
    internal void Suspend() { Session.Suspend(); _dialog?.Hide(); _clear(); Content.Children.Clear(); Changed(); }
    public void Dispose()
    {
        if (_disposed) return;
        Suspend(); _page.Loaded -= Loaded; _page.Unloaded -= Unloaded; Session.Changed -= Changed; Session.Retired -= Retired; Session.Dispose();
        _disposed = true; StateChanged = null;
    }
}
