using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminDiagnosticsPage : Page
{
    private const int PageSize = 25;
    private readonly AdminApi _adminApi;
    private readonly ToastService _toast;
    private readonly List<string> _cursorStack = [];
    private DiagnosticReportListResponse? _currentPage;
    private DiagnosticReport? _selectedReport;
    private string? _cursor;
    private bool _loaded;
    private bool _settingUploadsSwitch;
    private CancellationTokenSource? _loadCts;

    public AdminDiagnosticsPage()
    {
        InitializeComponent();
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        _toast = App.Services.GetRequiredService<ToastService>();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        AttachDetailOverlay();
        await Task.WhenAll(LoadStatusAsync(), LoadReportsAsync());
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _loadCts?.Cancel();
        CloseDetail();
    }

    private void AttachDetailOverlay()
    {
        if (FindAdminShell() is { } shell)
            shell.AttachPageOverlay(DetailOverlay);
    }

    private AdminShellPage? FindAdminShell()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is AdminShellPage shell) return shell;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(0, e.NewSize.Width);
        AdminPageContent.Width = Math.Min(1640, width);
        var horizontal = width >= 1280 ? 40 : width >= 900 ? 28 : 16;
        AdminPageContent.Padding = new Thickness(horizontal, width >= 900 ? 32 : 20, horizontal, 40);
        PageTitle.FontSize = Math.Clamp(width * .04, 32, 48);

        var wrapHeader = width < 820;
        Grid.SetColumn(PageHeaderActions, wrapHeader ? 0 : 1);
        Grid.SetRow(PageHeaderActions, wrapHeader ? 1 : 0);
        PageHeaderActions.HorizontalAlignment = wrapHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        var compactFilters = width < 1220;
        if (compactFilters)
        {
            FilterGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            FilterGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            FilterGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            for (var i = 0; i < FilterGrid.Children.Count; i++)
            {
                var child = (FrameworkElement)FilterGrid.Children[i];
                Grid.SetRow(child, i / 3);
                Grid.SetColumn(child, i % 3);
            }
            FilterGrid.RowDefinitions.Clear();
            FilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            FilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            FilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            FilterGrid.ColumnDefinitions[0].Width = new GridLength(100);
            FilterGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            FilterGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            for (var i = 0; i < FilterGrid.Children.Count; i++)
            {
                var child = (FrameworkElement)FilterGrid.Children[i];
                Grid.SetRow(child, 0);
                Grid.SetColumn(child, i);
            }
            FilterGrid.RowDefinitions.Clear();
        }
        DetailDrawer.Width = Math.Min(760, Math.Max(420, width * .72));
    }

    private async Task LoadStatusAsync()
    {
        try
        {
            var status = await _adminApi.GetDiagnosticsStatusAsync();
            RetentionText.Text = $"{status.RetentionDays} days";
            _settingUploadsSwitch = true;
            UploadsSwitch.IsOn = !string.Equals(status.Status, "disabled", StringComparison.OrdinalIgnoreCase);
            _settingUploadsSwitch = false;
            UploadsSwitch.IsEnabled = true;
            if (status.Status is "disabled" or "storage_unavailable")
            {
                StatusBanner.Visibility = Visibility.Visible;
                StatusBannerText.Text = status.Status == "disabled"
                    ? "Client diagnostic uploads are currently disabled. Use the Client uploads toggle above to enable them. Reports from when the feature was enabled may still be available below."
                    : "Client diagnostic storage is currently unavailable. Existing report metadata may still be available below.";
            }
            else StatusBanner.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            RetentionText.Text = "Load failed";
            UploadsSwitch.IsEnabled = false;
            ShowError(ex.Message);
        }
    }

    private async void UploadsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingUploadsSwitch || !_loaded) return;
        UploadsSwitch.IsEnabled = false;
        try
        {
            await _adminApi.UpdateAdminSettingAsync("diagnostics.uploads_enabled", UploadsSwitch.IsOn ? "true" : "false");
            _toast.Success(UploadsSwitch.IsOn ? "Client diagnostic uploads enabled" : "Client diagnostic uploads disabled");
            await LoadStatusAsync();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
            await LoadStatusAsync();
        }
    }

    private async Task LoadReportsAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        LoadingText.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        ReportRows.Children.Clear();
        HideError();
        PreviousButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        try
        {
            _currentPage = await _adminApi.GetDiagnosticReportsAsync(BuildQuery(), ct);
            if (ct.IsCancellationRequested) return;
            RenderRows(_currentPage.Reports);
            var page = _cursorStack.Count + 1;
            PageText.Text = _currentPage.Reports.Count == 0 ? "" : _currentPage.NextCursor is not null ? $"Page {page} · More reports" : $"Page {page}";
            PreviousButton.IsEnabled = _cursorStack.Count > 0;
            NextButton.IsEnabled = !string.IsNullOrWhiteSpace(_currentPage.NextCursor);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            PageText.Text = "";
        }
        finally
        {
            if (!ct.IsCancellationRequested) LoadingText.Visibility = Visibility.Collapsed;
        }
    }

    private string BuildQuery()
    {
        var parts = new List<string> { $"limit={PageSize}" };
        Add("user_id", UserIdBox.Text.Trim());
        Add("platform", SelectedTag(PlatformBox));
        Add("report_type", SelectedTag(ReportTypeBox));
        Add("from", SelectedDateTime(FromDate, FromTime));
        Add("to", SelectedDateTime(ToDate, ToTime));
        Add("short_id", ShortIdBox.Text.Trim());
        Add("cursor", _cursor);
        return "?" + string.Join("&", parts);

        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private static string? SelectedDateTime(DatePicker date, TimePicker time)
    {
        if (date.SelectedDate is not { } selected) return null;
        var chosenTime = time.SelectedTime ?? TimeSpan.Zero;
        var local = new DateTime(selected.Year, selected.Month, selected.Day, chosenTime.Hours, chosenTime.Minutes, 0, DateTimeKind.Local);
        return local.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private void RenderRows(IReadOnlyList<DiagnosticReportSummary> reports)
    {
        ReportRows.Children.Clear();
        if (reports.Count == 0)
        {
            EmptyDescription.Text = HasFilters()
                ? "No reports match these filters. Clients create reports from their Diagnostics setting."
                : "Users can enable diagnostics in the client settings, then send a crash or manual report.";
            EmptyPanel.Visibility = Visibility.Visible;
            return;
        }
        EmptyPanel.Visibility = Visibility.Collapsed;
        foreach (var report in reports)
        {
            var grid = new Grid { Padding = new Thickness(14, 11, 14, 11), ColumnSpacing = 12 };
            foreach (var width in new[] { "150", "100", "140", "155", "125", "*", "100", "90" })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width == "*" ? new GridLength(1, GridUnitType.Star) : new GridLength(double.Parse(width, CultureInfo.InvariantCulture)) });
            AddCell(grid, 0, FormatDateTime(report.ReceivedAt));
            AddCell(grid, 1, report.ShortId, true);
            var user = $"#{report.UserId}" + (string.IsNullOrWhiteSpace(report.ProfileId) ? "" : $"\n{report.ProfileId}");
            AddCell(grid, 2, user);
            AddCell(grid, 3, $"{FormatPlatform(report.Platform)}\nv{report.AppVersion}{(string.IsNullOrWhiteSpace(report.AppBuild) ? "" : $" ({report.AppBuild})")}");
            AddBadgeCell(grid, 4, FormatToken(report.ReportType), false);
            AddCell(grid, 5, string.IsNullOrWhiteSpace(report.CrashSummary) ? "—" : report.CrashSummary!);
            AddBadgeCell(grid, 6, FormatToken(report.State), report.State == "failed");
            AddCell(grid, 7, FormatBytes(report.BlobBytes), true, HorizontalAlignment.Right);

            var button = new Button { Tag = report, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Content = grid };
            AutomationProperties.SetName(button, $"Open diagnostic report {report.ShortId}");
            button.Click += ReportRow_Click;
            ReportRows.Children.Add(new Border { BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(0, 0, 0, 1), Child = button });
        }
    }

    private static void AddCell(Grid grid, int column, string text, bool mono = false, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var block = new TextBlock { Text = text, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = alignment };
        if (mono) block.FontFamily = new FontFamily("Consolas");
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static void AddBadgeCell(Grid grid, int column, string text, bool error)
    {
        var badge = new Border { Background = (Brush)Application.Current.Resources["SurfaceBrush"], BorderBrush = error ? (Brush)Application.Current.Resources["ErrorBrush"] : (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 3, 7, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = text, FontSize = 11 } };
        Grid.SetColumn(badge, column);
        grid.Children.Add(badge);
    }

    private async void ReportRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DiagnosticReportSummary summary }) return;
        DetailTitle.Text = summary.ShortId;
        DetailSubtitle.Text = $"{FormatToken(summary.ReportType)} · {FormatDateTime(summary.ReceivedAt)}";
        DetailContent.Children.Clear();
        DetailContent.Children.Add(new TextBlock { Text = "Loading report details...", Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        FindAdminShell()?.SetPageOverlayVisible(DetailOverlay, true);
        try
        {
            _selectedReport = await _adminApi.GetDiagnosticReportAsync(summary.Id);
            RenderDetail(_selectedReport);
        }
        catch (Exception ex)
        {
            DetailContent.Children.Clear();
            DetailContent.Children.Add(new TextBlock { Text = ex.Message, Foreground = (Brush)Application.Current.Resources["ErrorBrush"], TextWrapping = TextWrapping.Wrap });
        }
    }

    private void RenderDetail(DiagnosticReport report)
    {
        DetailContent.Children.Clear();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var download = new Button { Content = "↓  Download bundle", Style = (Style)Application.Current.Resources["AccentButtonStyle"], IsEnabled = report.State == "ready", Tag = report };
        download.Click += Download_Click;
        var delete = new Button { Content = "Delete", Tag = report };
        delete.Click += Delete_Click;
        actions.Children.Add(download); actions.Children.Add(delete);
        DetailContent.Children.Add(actions);
        if (report.State != "ready") DetailContent.Children.Add(new TextBlock { Text = "Only ready reports can be downloaded.", FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });

        var device = ManifestObject(report.Manifest, "device_summary");
        var reportInfo = ManifestObject(report.Manifest, "report");
        var summary = new Grid { ColumnSpacing = 16, Background = (Brush)Application.Current.Resources["SurfaceBrush"], Padding = new Thickness(14) };
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddDetailGrid(summary, 0, "Device", JoinNonempty(JsonString(device, "manufacturer"), JsonString(device, "model")));
        AddDetailGrid(summary, 1, "OS", JsonString(device, "os") ?? JsonString(reportInfo, "os_version") ?? "—");
        AddDetailGrid(summary, 2, "App", report.AppVersion + (string.IsNullOrWhiteSpace(report.AppBuild) ? "" : $" ({report.AppBuild})"));
        DetailContent.Children.Add(summary);

        var fields = new WrapPanel { HorizontalSpacing = 24, VerticalSpacing = 16 };
        foreach (var field in new[] { ("State", FormatToken(report.State)), ("User", $"#{report.UserId}"), ("Profile", report.ProfileId ?? "—"), ("Captured", FormatDateTime(report.CapturedAt)), ("Received", FormatDateTime(report.ReceivedAt)), ("Form factor", FormatToken(JsonString(device, "form_factor") ?? "")), ("Compressed", FormatBytes(report.BlobBytes)), ("Uncompressed", FormatBytes(report.UncompressedBytes)), ("Report ID", report.Id) })
            fields.Children.Add(DetailField(field.Item1, field.Item2));
        DetailContent.Children.Add(fields);

        if (!string.IsNullOrWhiteSpace(report.CrashSummary))
        {
            DetailContent.Children.Add(SectionTitle("Crash summary"));
            DetailContent.Children.Add(new Border { Background = (Brush)Application.Current.Resources["SurfaceBrush"], CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = new TextBlock { Text = report.CrashSummary, TextWrapping = TextWrapping.Wrap, FontSize = 13 } });
        }
        DetailContent.Children.Add(SectionTitle("Playback sessions"));
        if (report.PlaybackSessionIds.Count == 0)
            DetailContent.Children.Add(new TextBlock { Text = "No playback sessions were attached.", FontSize = 13, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        else foreach (var id in report.PlaybackSessionIds)
        {
            var link = new Button { Content = id + "  ↗", Tag = id, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0), FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = (Brush)Application.Current.Resources["AccentBrush"] };
            link.Click += PlaybackSession_Click;
            DetailContent.Children.Add(link);
        }
        DetailContent.Children.Add(SectionTitle("Full manifest JSON"));
        DetailContent.Children.Add(new Border { Background = (Brush)Application.Current.Resources["SurfaceBrush"], CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = new TextBlock { Text = JsonSerializer.Serialize(report.Manifest, new JsonSerializerOptions { WriteIndented = true }), FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap } });
    }

    private static JsonElement ManifestObject(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) ? value : default;
    private static string? JsonString(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string JoinNonempty(params string?[] values) { var result = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))); return string.IsNullOrEmpty(result) ? "—" : result; }
    private static TextBlock SectionTitle(string text) => new() { Text = text, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private static FrameworkElement DetailField(string label, string value) => new StackPanel { Width = 195, Spacing = 4, Children = { new TextBlock { Text = label, FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] }, new TextBlock { Text = value, FontSize = 12, TextWrapping = TextWrapping.Wrap } } };
    private static void AddDetailGrid(Grid grid, int column, string label, string value) { var field = DetailField(label, value); Grid.SetColumn(field, column); grid.Children.Add(field); }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DiagnosticReport report } button) return;
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = $"silo-diagnostics-{(string.IsNullOrWhiteSpace(report.ShortId) ? report.Id : report.ShortId)}.tar" };
        picker.FileTypeChoices.Add("Compressed diagnostic bundle", [".gz"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        button.IsEnabled = false;
        try { await Windows.Storage.FileIO.WriteBytesAsync(file, await _adminApi.DownloadDiagnosticReportAsync(report.Id)); _toast.Success("Diagnostic bundle downloaded"); }
        catch (Exception ex) { _toast.Error(ex.Message); }
        finally { button.IsEnabled = true; }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DiagnosticReport report }) return;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Delete diagnostic report?", Content = $"This permanently deletes report {report.ShortId} and its uploaded bundle.", PrimaryButtonText = "Delete report", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { await _adminApi.DeleteDiagnosticReportAsync(report.Id); _toast.Success("Diagnostic report deleted"); CloseDetail(); await LoadReportsAsync(); }
        catch (Exception ex) { _toast.Error(ex.Message); }
    }

    private void PlaybackSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string sessionId } && FindAdminShell() is { } shell)
        {
            CloseDetail();
            shell.OpenLogsForSession(sessionId);
        }
    }

    private async void ApplyFilters_Click(object sender, RoutedEventArgs e)
    {
        var raw = UserIdBox.Text.Trim();
        if (raw.Length > 0 && (!int.TryParse(raw, out var id) || id < 1)) { _toast.Error("User ID must be a positive whole number."); return; }
        _cursor = null; _cursorStack.Clear();
        await LoadReportsAsync();
    }

    private async void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        UserIdBox.Text = ""; PlatformBox.SelectedIndex = 0; ReportTypeBox.SelectedIndex = 0; FromDate.SelectedDate = null; FromTime.SelectedTime = null; ToDate.SelectedDate = null; ToTime.SelectedTime = null; ShortIdBox.Text = "";
        _cursor = null; _cursorStack.Clear();
        await LoadReportsAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPage?.NextCursor)) return;
        _cursorStack.Add(_cursor ?? ""); _cursor = _currentPage.NextCursor;
        await LoadReportsAsync();
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_cursorStack.Count == 0) return;
        _cursor = _cursorStack[^1]; if (string.IsNullOrEmpty(_cursor)) _cursor = null; _cursorStack.RemoveAt(_cursorStack.Count - 1);
        await LoadReportsAsync();
    }

    private bool HasFilters() => !string.IsNullOrWhiteSpace(UserIdBox.Text) || PlatformBox.SelectedIndex > 0 || ReportTypeBox.SelectedIndex > 0 || FromDate.SelectedDate is not null || ToDate.SelectedDate is not null || !string.IsNullOrWhiteSpace(ShortIdBox.Text);
    private void CloseDetail() { _selectedReport = null; FindAdminShell()?.SetPageOverlayVisible(DetailOverlay, false); }
    private void CloseDetail_Click(object sender, RoutedEventArgs e) => CloseDetail();
    private void DetailBackdrop_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => CloseDetail();
    private void DetailDrawer_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => e.Handled = true;
    private void ShowError(string message) { ErrorText.Text = message; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private static string FormatDateTime(string value) => DateTimeOffset.TryParse(value, out var date) ? DateTimeDisplay.FormatDateTime(date) : value;
    private static string FormatPlatform(string value) => value switch { "android-tv" => "Android TV", "ios" => "iOS", "tvos" => "tvOS", _ => "Android" };
    private static string FormatToken(string value) => string.Join(" ", value.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    private static string FormatBytes(long? value) { if (value is null) return "—"; if (value < 1024) return $"{value} B"; string[] units = ["KiB", "MiB", "GiB", "TiB"]; var size = value.Value / 1024d; var unit = 0; while (unit < units.Length - 1 && size >= 1024) { size /= 1024; unit++; } return $"{size:0.#} {units[unit]}"; }
}
