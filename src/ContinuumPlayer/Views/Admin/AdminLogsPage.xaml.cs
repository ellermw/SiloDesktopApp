using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminLogsPage : Page
{
    public AdminLogsViewModel ViewModel { get; }

    private bool _isAppTab = true;
    private OperationalLogEntry? _selectedAppEntry;

    public AdminLogsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminLogsViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AppLogs.CollectionChanged += (_, _) => RebuildAppTable();
        ViewModel.AuditLogs.CollectionChanged += (_, _) => RebuildAuditTable();

        SetActiveTab(true);
        try { await ViewModel.LoadAppLogsCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }

    // ===== Tab switching =====

    private void TabApp_Click(object sender, RoutedEventArgs e)
    {
        if (_isAppTab) return;
        SetActiveTab(true);
        if (ViewModel.AppLogs.Count == 0)
            _ = ViewModel.LoadAppLogsCommand.ExecuteAsync(null);
    }

    private void TabAudit_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAppTab) return;
        SetActiveTab(false);
        if (ViewModel.AuditLogs.Count == 0)
            _ = ViewModel.LoadAuditLogsCommand.ExecuteAsync(null);
    }

    private void SetActiveTab(bool appTab)
    {
        _isAppTab = appTab;

        var accentBg = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"];
        var accentFg = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        var secondaryFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        var transparent = new SolidColorBrush(Colors.Transparent);

        TabAppBtn.Background = appTab ? accentBg : transparent;
        TabAppBtn.Foreground = appTab ? accentFg : secondaryFg;

        TabAuditBtn.Background = !appTab ? accentBg : transparent;
        TabAuditBtn.Foreground = !appTab ? accentFg : secondaryFg;

        AppLogsPanel.Visibility = appTab ? Visibility.Visible : Visibility.Collapsed;
        AuditLogsPanel.Visibility = !appTab ? Visibility.Visible : Visibility.Collapsed;

        // Clear detail panel when switching
        HideAppDetail();
    }

    // ===== Search button handlers =====

    private async void BtnSearchApp_Click(object sender, RoutedEventArgs e)
    {
        HideAppDetail();
        try { await ViewModel.LoadAppLogsCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }

    private async void BtnSearchAudit_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.LoadAuditLogsCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }

    // ===== KeyDown handlers =====

    private void FilterBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) BtnSearchApp_Click(sender, e);
    }

    private void AuditFilterBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) BtnSearchAudit_Click(sender, e);
    }

    // ===== App log table =====

    private void RebuildAppTable()
    {
        AppLogsPanel_Rows.Children.Clear();
        HideAppDetail();

        var logs = ViewModel.AppLogs;
        if (logs.Count == 0)
        {
            AppLogsEmpty.Visibility = Visibility.Visible;
            return;
        }

        AppLogsEmpty.Visibility = Visibility.Collapsed;

        bool first = true;
        foreach (var entry in logs)
        {
            if (!first)
            {
                AppLogsPanel_Rows.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            first = false;

            var row = BuildAppLogRow(entry);
            AppLogsPanel_Rows.Children.Add(row);
        }
    }

    private FrameworkElement BuildAppLogRow(OperationalLogEntry entry)
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 8, 16, 8),
            ColumnSpacing = 8,
            Tag = entry
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        // Time
        row.Children.Add(MakeMonoCell(0, AdminLogsViewModel.FormatTimestamp(entry.Timestamp), 11));

        // Level (colored)
        var levelBlock = new TextBlock
        {
            Text = entry.Level.ToUpperInvariant(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetLevelBrush(entry.Level),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(levelBlock, 1);
        row.Children.Add(levelBlock);

        // Component
        row.Children.Add(MakeTextCell(2, entry.Component, 12));

        // User
        row.Children.Add(MakeTextCell(3, entry.UserId.HasValue ? $"#{entry.UserId}" : "-", 12));

        // Session (short)
        row.Children.Add(MakeMonoCell(4, ShortId(entry.SessionId), 11));

        // Playback session (short)
        row.Children.Add(MakeMonoCell(5, ShortId(entry.PlaybackSessionId), 11));

        // Method from attrs
        row.Children.Add(MakeTextCell(6, AdminLogsViewModel.GetAttr(entry, "method"), 12));

        // Path from attrs (truncated)
        var pathAttr = AdminLogsViewModel.GetAttr(entry, "path");
        row.Children.Add(MakeMonoCell(7, AdminLogsViewModel.TruncatePath(pathAttr), 11));

        // Status from attrs
        row.Children.Add(MakeTextCell(8, AdminLogsViewModel.GetAttr(entry, "status"), 12));

        // Duration from attrs
        row.Children.Add(MakeTextCell(9, AdminLogsViewModel.GetDurationAttr(entry), 11));

        // Message (truncated)
        var msgBlock = new TextBlock
        {
            Text = entry.Message,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(msgBlock, 10);
        row.Children.Add(msgBlock);

        // Request ID
        row.Children.Add(MakeMonoCell(11, ShortId(entry.RequestId), 11));

        // Make row clickable
        row.PointerPressed += (_, _) => ShowAppDetail(entry, row);
        row.PointerEntered += (_, _) =>
            row.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
        row.PointerExited += (_, _) =>
            row.Background = new SolidColorBrush(Colors.Transparent);

        return row;
    }

    // ===== App detail panel =====

    private void ShowAppDetail(OperationalLogEntry entry, Grid clickedRow)
    {
        // Deselect previous
        if (_selectedAppEntry == entry)
        {
            HideAppDetail();
            return;
        }

        _selectedAppEntry = entry;
        AppDetailPanel.Visibility = Visibility.Visible;
        AppDetailContent.Children.Clear();

        // Title: message
        AppDetailContent.Children.Add(new TextBlock
        {
            Text = entry.Message,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        // Subtitle line
        AppDetailContent.Children.Add(new TextBlock
        {
            Text = $"{entry.Component} · {entry.Level.ToUpperInvariant()} · {AdminLogsViewModel.FormatDate(entry.Timestamp)}",
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });

        // Detail grid
        var detailGrid = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fields = new[]
        {
            ("Request ID", entry.RequestId ?? "-", true),
            ("Node", entry.NodeId ?? "-", false),
            ("Method", AdminLogsViewModel.GetAttr(entry, "method"), false),
            ("Status", AdminLogsViewModel.GetAttr(entry, "status"), false),
            ("Duration", AdminLogsViewModel.GetDurationAttr(entry), false),
            ("Client IP", !string.IsNullOrEmpty(entry.ClientIp) ? entry.ClientIp : AdminLogsViewModel.GetAttr(entry, "client_ip"), true),
            ("User ID", entry.UserId.HasValue ? entry.UserId.ToString()! : "-", false),
            ("Session ID", entry.SessionId ?? AdminLogsViewModel.GetAttr(entry, "session_id"), true),
            ("Playback Session", entry.PlaybackSessionId ?? AdminLogsViewModel.GetAttr(entry, "playback_session_id"), true),
        };

        for (int i = 0; i < fields.Length; i++)
        {
            var (label, value, mono) = fields[i];
            int row = i / 2;
            int col = i % 2;

            while (detailGrid.RowDefinitions.Count <= row)
                detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var fieldStack = new StackPanel { Spacing = 2 };
            fieldStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
            fieldStack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 12,
                FontFamily = mono ? new Microsoft.UI.Xaml.Media.FontFamily("Consolas") : null,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            });

            Grid.SetRow(fieldStack, row);
            Grid.SetColumn(fieldStack, col);
            detailGrid.Children.Add(fieldStack);
        }

        AppDetailContent.Children.Add(detailGrid);

        // Path block
        var pathVal = AdminLogsViewModel.GetAttr(entry, "path");
        if (pathVal != "-")
        {
            var pathStack = new StackPanel { Spacing = 4 };
            pathStack.Children.Add(new TextBlock
            {
                Text = "Path",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
            });
            pathStack.Children.Add(new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6),
                Child = new TextBlock
                {
                    Text = pathVal,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap
                }
            });
            AppDetailContent.Children.Add(pathStack);
        }

        // Attributes block
        var attrsStack = new StackPanel { Spacing = 4 };
        attrsStack.Children.Add(new TextBlock
        {
            Text = "Attributes",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        string attrsJson = entry.Attrs != null
            ? JsonSerializer.Serialize(entry.Attrs, new JsonSerializerOptions { WriteIndented = true })
            : "{}";

        attrsStack.Children.Add(new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Child = new ScrollViewer
            {
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = attrsJson,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap
                }
            }
        });

        AppDetailContent.Children.Add(attrsStack);
    }

    private void HideAppDetail()
    {
        _selectedAppEntry = null;
        AppDetailPanel.Visibility = Visibility.Collapsed;
        AppDetailContent.Children.Clear();
    }

    // ===== Audit log table =====

    private void RebuildAuditTable()
    {
        AuditLogsPanel_Rows.Children.Clear();

        var logs = ViewModel.AuditLogs;
        if (logs.Count == 0)
        {
            AuditLogsEmpty.Visibility = Visibility.Visible;
            return;
        }

        AuditLogsEmpty.Visibility = Visibility.Collapsed;

        bool first = true;
        foreach (var entry in logs)
        {
            if (!first)
            {
                AuditLogsPanel_Rows.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            first = false;
            AuditLogsPanel_Rows.Children.Add(BuildAuditLogRow(entry));
        }
    }

    private static FrameworkElement BuildAuditLogRow(AuditLogEntry entry)
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 8, 16, 8),
            ColumnSpacing = 8
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        // Time
        row.Children.Add(MakeMonoCell(0, AdminLogsViewModel.FormatDate(entry.Timestamp), 11));

        // Method
        row.Children.Add(MakeTextCell(1, entry.Method, 12, bold: true));

        // Path (monospace, truncated)
        row.Children.Add(MakeMonoCell(2, AdminLogsViewModel.TruncatePath(entry.Path, 70), 11));

        // Status (colored)
        var statusBlock = new TextBlock
        {
            Text = entry.StatusCode.ToString(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetStatusBrush(entry.StatusCode),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(statusBlock, 3);
        row.Children.Add(statusBlock);

        // Client IP
        row.Children.Add(MakeMonoCell(4, FormatClientIp(entry.ClientIp), 11));

        // User
        row.Children.Add(MakeTextCell(5, entry.UserId.HasValue ? $"#{entry.UserId}" : "-", 12));

        // Session
        row.Children.Add(MakeMonoCell(6, ShortId(entry.SessionId), 11));

        // Playback session
        row.Children.Add(MakeMonoCell(7, ShortId(entry.PlaybackSessionId), 11));

        // Request ID
        row.Children.Add(MakeMonoCell(8, ShortId(entry.RequestId), 11));

        row.PointerEntered += (_, _) =>
            row.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
        row.PointerExited += (_, _) =>
            row.Background = new SolidColorBrush(Colors.Transparent);

        return row;
    }

    // ===== Helpers =====

    private static TextBlock MakeTextCell(int col, string text, double fontSize, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = bold
                ? (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
                : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static TextBlock MakeMonoCell(int col, string text, double fontSize)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static SolidColorBrush GetLevelBrush(string level) =>
        level.ToLowerInvariant() switch
        {
            "error" or "fatal" => new SolidColorBrush(Color.FromArgb(255, 248, 81, 73)),
            "warn" or "warning" => new SolidColorBrush(Color.FromArgb(255, 210, 153, 34)),
            "debug" or "trace" => (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            _ => (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };

    private static SolidColorBrush GetStatusBrush(int status) =>
        status switch
        {
            >= 500 => new SolidColorBrush(Color.FromArgb(255, 248, 81, 73)),
            >= 400 => new SolidColorBrush(Color.FromArgb(255, 210, 153, 34)),
            >= 200 and < 300 => new SolidColorBrush(Color.FromArgb(255, 63, 185, 80)),
            _ => (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };

    private static string FormatClientIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return "-";
        // Strip CIDR suffix if present (e.g. "1.2.3.4/32" -> "1.2.3.4")
        var slashIdx = ip.IndexOf('/');
        return slashIdx >= 0 ? ip[..slashIdx] : ip;
    }

    private static string ShortId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "-";
        if (id.Length <= 12) return id;
        return $"{id[..8]}...{id[^4..]}";
    }
}
