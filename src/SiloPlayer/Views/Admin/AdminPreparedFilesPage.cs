using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using Windows.UI.Text;

namespace SiloPlayer.Views.Admin;

/// <summary>Native five-tab `/admin/downloads` management page.</summary>
public sealed partial class AdminPreparedFilesPage : Page, IAdminGutterScrollPage, IAdminPageSuspension
{
    private readonly AdminPreparedFilesApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly DispatcherQueue _dispatcher;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _layout = new() { Spacing = 20 };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Border _notice = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(12), CornerRadius = new CornerRadius(8) };
    private readonly TextBlock _noticeText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Grid _body = new();
    private readonly Dictionary<string, Button> _tabButtons = [];
    private readonly HashSet<string> _selectedFiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectedPreparations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectedEntries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedDevices = new(StringComparer.Ordinal);
    private AdminRouteRequest _request = new(AdminRoute.PreparedFiles);
    private ContentDialog? _dialog;
    private bool _opened;
    private bool _renderQueued;
    private bool _disposed;

    public AdminPreparedFilesController Controller { get; private set; }
    ScrollViewer IAdminGutterScrollPage.AdminScrollViewer => _scroll;

    public AdminPreparedFilesPage() : this(App.Services.GetRequiredService<AdminPreparedFilesApi>(), App.Services.GetRequiredService<AdminMonitorAuthority>()) { }

    public AdminPreparedFilesPage(AdminPreparedFilesApi api, AdminMonitorAuthority authority)
    {
        _api = api; _authority = authority; _dispatcher = DispatcherQueue.GetForCurrentThread();
        Controller = CreateController();
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, "AdminPreparedFilesPage");
        BuildShell();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is AdminRouteRequest request && request.Route == AdminRoute.PreparedFiles) _request = request;
        base.OnNavigatedTo(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_opened) return;
        if (_disposed) { Controller = CreateController(); _disposed = false; }
        _opened = true;
        await Controller.OpenAsync(_request.Get("tab"), _request.Get("location"), _request.Get("stale") == "1");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Nested Frame pages may transiently unload while the Admin shell is still current.
        if (Frame?.Content != this) SuspendAdminPage();
    }

    public void SuspendAdminPage()
    {
        _dialog?.Hide(); _dialog = null;
        Controller.Changed -= QueueRender; Controller.Retired -= QueueReopen; Controller.Dispose();
        _selectedFiles.Clear(); _selectedPreparations.Clear(); _selectedEntries.Clear(); _expandedDevices.Clear();
        _preparationStateFilter = null; _preparationWorkerFilter = null; _preparationSearch = "";
        _opened = false; _disposed = true;
    }

    private AdminPreparedFilesController CreateController()
    {
        var controller = new AdminPreparedFilesController(_api, _authority);
        controller.Changed += QueueRender;
        controller.Retired += QueueReopen;
        return controller;
    }

    private void BuildShell()
    {
        _scroll.Content = _layout;
        _layout.MaxWidth = AdminShellPage.ContentMaxWidth;
        _layout.Margin = new Thickness(24, 20, 24, 40);
        var title = Text("Downloads", 30, FontWeights.SemiBold);
        var subtitle = Text("Preparing downloads, the prepared files on the server and nodes, and the copies on people's devices.", 14);
        subtitle.Foreground = Brush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray);
        _layout.Children.Add(new StackPanel { Spacing = 5, Children = { title, subtitle } });
        foreach (var tab in new[] { "storage", "preparation", "files", "devices", "history" })
        {
            var button = Button(AdminPreparedFilesText.Tab(tab), "PreparedFilesTab" + char.ToUpperInvariant(tab[0]) + tab[1..], () => OpenTab(tab));
            button.Padding = new Thickness(14, 8, 14, 8);
            _tabButtons[tab] = button;
            _tabs.Children.Add(button);
        }
        var tabScroller = new ScrollViewer { HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled, Content = _tabs };
        _layout.Children.Add(tabScroller);
        _notice.Child = _noticeText; _notice.Background = Brush("CardBackgroundFillColorSecondaryBrush", Microsoft.UI.Colors.DimGray);
        _layout.Children.Add(_notice);
        _layout.Children.Add(_body);
        PageRoot.Children.Add(_scroll);
    }

    private async void OpenTab(string tab)
    {
        if (_disposed) return;
        var request = new AdminRouteRequest(AdminRoute.PreparedFiles,
            tab == "storage" ? null : new Dictionary<string, string> { ["tab"] = tab });
        if (AdminShellPage.TryReplace(this, request)) _request = request;
        if (tab == "files" && Controller.State.ActiveTab != "files" && Controller.State.FileQuery.Location != null)
            await Controller.SetFileQueryAsync(Controller.State.FileQuery with { Location = null });
        if (tab == "devices" && Controller.State.ActiveTab != "devices" && Controller.State.DeviceQuery.Stale)
            await Controller.SetDeviceQueryAsync(Controller.State.DeviceQuery with { Stale = false });
        await Controller.SetActiveTabAsync(tab);
    }

    private async void OpenFilesAt(string location)
    {
        if (_disposed || !AdminPreparedFilesContract.IsLocation(location)) return;
        var request = new AdminRouteRequest(AdminRoute.PreparedFiles,
            new Dictionary<string, string> { ["tab"] = "files", ["location"] = location });
        if (AdminShellPage.TryReplace(this, request)) _request = request;
        await Controller.SetFileQueryAsync(Controller.State.FileQuery with { Location = location });
        await Controller.SetActiveTabAsync("files");
    }

    private async void OpenDevices(bool stale)
    {
        if (_disposed) return;
        var query = new Dictionary<string, string> { ["tab"] = "devices" };
        if (stale) query["stale"] = "1";
        var request = new AdminRouteRequest(AdminRoute.PreparedFiles, query);
        if (AdminShellPage.TryReplace(this, request)) _request = request;
        _expandedDevices.Clear(); _selectedEntries.Clear();
        await Controller.SetDeviceQueryAsync(Controller.State.DeviceQuery with { Stale = stale });
        await Controller.SetActiveTabAsync("devices");
    }

    private void QueueReopen()
    {
        _dispatcher.TryEnqueue(async () =>
        {
            _dialog?.Hide(); _dialog = null;
            if (_disposed || !_opened || !_authority.IsAvailable) return;
            await Controller.OpenAsync(_request.Get("tab"), _request.Get("location"), _request.Get("stale") == "1");
        });
    }

    private void QueueRender()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        _dispatcher.TryEnqueue(() => { _renderQueued = false; if (!_disposed) Render(Controller.State); });
    }

    private void Render(AdminPreparedFilesSnapshot state)
    {
        foreach (var pair in _tabButtons)
        {
            pair.Value.IsEnabled = pair.Key != state.ActiveTab;
            pair.Value.FontWeight = pair.Key == state.ActiveTab ? FontWeights.SemiBold : FontWeights.Normal;
            var count = pair.Key == "preparation"
                ? state.PreparationsLoaded ? state.PreparationCounts.Running + state.PreparationCounts.Queued + state.PreparationCounts.Retrying
                : state.Storage.Value?.PreparingJobs
                : null;
            pair.Value.Content = AdminPreparedFilesText.Tab(pair.Key) + (count is > 0 ? $"  {count}" : "");
        }
        if (state.LastAction is { } action)
        {
            _notice.Visibility = Visibility.Visible; _noticeText.Text = action.Message;
            _notice.Background = Brush(action.Outcome is AdminActionOutcome.Failed or AdminActionOutcome.Unknown ? "SystemFillColorCriticalBackgroundBrush" : "SystemFillColorSuccessBackgroundBrush",
                action.Outcome is AdminActionOutcome.Failed or AdminActionOutcome.Unknown ? Microsoft.UI.Colors.DarkRed : Microsoft.UI.Colors.DarkGreen);
        }
        else _notice.Visibility = Visibility.Collapsed;
        _body.Children.Clear();
        _body.Children.Add(state.Owner == null ? Message("Administrator access is required.") : state.ActiveTab switch
        {
            "preparation" => BuildPreparation(state), "files" => BuildFiles(state), "devices" => BuildDevices(state),
            "history" => BuildHistory(state), _ => BuildStorage(state),
        });
    }

    private async Task<bool> ConfirmAsync(string title, string description, string confirm, UIElement? extra = null)
    {
        if (_dialog != null || XamlRoot == null) return false;
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(Text(description, 14));
        if (extra != null) content.Children.Add(extra);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = confirm, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        _dialog = dialog;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        catch (OperationCanceledException) { return false; }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    private static TextBlock Text(string value, double size = 14, FontWeight? weight = null) => new()
    {
        Text = value, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
    };
    private static Button Button(string label, string id, Action click)
    {
        var button = new Button { Content = label };
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click(); return button;
    }
    private static Border Message(string value, string? action = null, Action? click = null)
    {
        var stack = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(Text(value, 14));
        if (action != null && click != null) stack.Children.Add(Button(action, "PreparedFilesRetry", click));
        return Card(stack, new Thickness(24));
    }
    private static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Child = child, Padding = padding ?? new Thickness(16), CornerRadius = new CornerRadius(10),
        Background = Brush("CardBackgroundFillColorDefaultBrush", Microsoft.UI.Colors.Black),
        BorderBrush = Brush("CardStrokeColorDefaultBrush", Microsoft.UI.Colors.DimGray), BorderThickness = new Thickness(1),
    };
    private static Brush Brush(string key, Windows.UI.Color fallback) => Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush
        ? brush : new SolidColorBrush(fallback);
    private static ComboBox Select(string id, IEnumerable<(string Value, string Label)> choices, string selected, Action<string> changed)
    {
        var combo = new ComboBox { MinWidth = 150 };
        AutomationProperties.SetAutomationId(combo, id);
        foreach (var choice in choices) combo.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => Equals(x.Tag, selected)) ?? combo.Items.FirstOrDefault();
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem item && item.Tag is string value) changed(value); };
        return combo;
    }
    private static StackPanel Toolbar(params UIElement[] children) { var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var child in children) panel.Children.Add(child); return panel; }
    private static ScrollViewer Horizontal(UIElement content) => new()
    {
        Content = content, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
    private static StackPanel Section(string title, string? subtitle = null)
    {
        var section = new StackPanel { Spacing = 12 }; section.Children.Add(Text(title, 20, FontWeights.SemiBold));
        if (!string.IsNullOrEmpty(subtitle)) { var text = Text(subtitle, 13); text.Foreground = Brush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray); section.Children.Add(text); }
        return section;
    }

    private static Border Stat(string label, string value, string detail)
    {
        var content = new StackPanel { Spacing = 3, MinWidth = 150 };
        var caption = Text(label.ToUpperInvariant(), 11, FontWeights.SemiBold);
        caption.CharacterSpacing = 70;
        caption.Foreground = Brush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray);
        content.Children.Add(caption);
        content.Children.Add(Text(value, 22, FontWeights.SemiBold));
        var note = Text(detail, 12);
        note.Foreground = Brush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray);
        content.Children.Add(note);
        return Card(content, new Thickness(14));
    }

    private static TextBox SearchBox(string id, string placeholder, string? value, Action<string> apply)
    {
        var box = new TextBox { PlaceholderText = placeholder, Text = value ?? "", MinWidth = 190 };
        AutomationProperties.SetAutomationId(box, id);
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            apply(box.Text.Trim());
        };
        box.LostFocus += (_, _) => apply(box.Text.Trim());
        return box;
    }

    private static string LocationName(AdminDownloadStorage? storage, string key) =>
        storage?.Locations.FirstOrDefault(x => x.Key == key)?.Name ?? (key == "server" ? "Server" : key);
}
