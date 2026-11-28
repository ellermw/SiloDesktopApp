using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.System;
using static SiloPlayer.Controls.Admin.DashboardUi;

namespace SiloPlayer.Controls.Admin;

/// <summary>
/// The weekday × hour grid of play starts (official <c>ViewingHeatmap</c>, c59798116): the compact form of a dashboard card has 16px cells,
/// the Playback tab's full form 20px cells with wider gaps. Five intensity levels
/// by share of the busiest cell's plays, one tab stop that the arrow keys, Home and End move (Ctrl for the corners), a tooltip and an
/// accessible name per cell, and a Less/More legend. The grid keeps at least 440px and scrolls sideways below that.
/// </summary>
internal static class DashboardViewingHeatmap
{
    private const double RowHeaderWidth = 44;
    private const double MinGridWidth = 440;
    private const double CompactCellHeight = 16;
    private const double CellHeight = 20;

    /// <summary>Fills for levels 0 to 4 (bg-muted/35 then bg-primary at 20, 40, 65 and 90 percent).</summary>
    public static SolidColorBrush LevelBrush(int level) => level switch
    {
        // The theme's muted tone can equal the card, which would hide an empty cell; a faint foreground wash keeps the grid readable.
        <= 0 => B(Mix(Muted, 0.5, Foreground), 0.35),
        1 => B(Primary, 0.20),
        2 => B(Primary, 0.40),
        3 => B(Primary, 0.65),
        _ => B(Primary, 0.90),
    };

    public static FrameworkElement Create(IReadOnlyList<AdminInsightsHeatmapCell> cells, bool compact = true)
    {
        var culture = CultureInfo.CurrentCulture;
        var weekdays = AdminInsightsRules.WeekdayLabels(culture);
        var hours = AdminInsightsRules.HourLabels(culture);
        var grid = AdminInsightsRules.BuildHeatmapGrid(cells);

        var cellHeight = compact ? CompactCellHeight : CellHeight;
        var content = new Grid { ColumnSpacing = 4, RowSpacing = compact ? 2 : 4, MinWidth = MinGridWidth };
        AutomationProperties.SetName(content, AdminInsightsText.T("web.admin.insights.activity.heatmap.title"));
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RowHeaderWidth) });
        for (var hour = 0; hour < 24; hour++) content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var day = 0; day < 7; day++) content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cellHeight) });

        // Hour labels every sixth column, centred over their cell; padding below as the official pb-1.
        for (var hour = 0; hour < 24; hour += 6)
        {
            var label = Text(hours[hour], 10, color: MutedForeground);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(-12, 0, -12, 4);
            Grid.SetColumn(label, hour + 1);
            content.Children.Add(label);
        }
        content.RowDefinitions[0].Height = new GridLength(15 + 4);

        var cellControls = new ContentControl[7, 24];
        var active = (Day: 0, Hour: 0);
        void MoveFocus(int day, int hour)
        {
            cellControls[active.Day, active.Hour].IsTabStop = false;
            active = (day, hour);
            cellControls[day, hour].IsTabStop = true;
            cellControls[day, hour].Focus(FocusState.Keyboard);
        }

        for (var day = 0; day < 7; day++)
        {
            var rowHeader = Text(weekdays[day], 11, Medium, MutedForeground);
            rowHeader.HorizontalAlignment = HorizontalAlignment.Right;
            rowHeader.VerticalAlignment = VerticalAlignment.Center;
            rowHeader.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetRow(rowHeader, day + 1);
            content.Children.Add(rowHeader);
            for (var hour = 0; hour < 24; hour++)
            {
                var (dayIndex, hourIndex) = (day, hour);
                var plays = grid.Plays(day, hour);
                var tooltip = AdminInsightsText.T("web.admin.insights.activity.heatmap.cellTooltip",
                    ("day", weekdays[day]), ("hour", hours[hour]),
                    ("plays", AdminInsightsRules.PlayCount(plays)), ("watch_time", DashboardFormat.WatchTime(grid.WatchSeconds(day, hour))));
                var swatch = new Border { CornerRadius = new CornerRadius(4), Background = LevelBrush(grid.Level(day, hour)) };
                var cell = new ContentControl
                {
                    Content = swatch,
                    IsTabStop = day == 0 && hour == 0,
                    UseSystemFocusVisuals = true,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    Padding = new Thickness(0),
                    MinWidth = 0,
                    MinHeight = 0,
                    RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 },
                };
                AutomationProperties.SetName(cell, tooltip);
                ToolTipService.SetToolTip(cell, tooltip);
                // hover:scale-110, lifted so the neighbours do not paint over it.
                cell.PointerEntered += (_, _) => Scale(cell, 1.1);
                cell.PointerExited += (_, _) => Scale(cell, 1);
                cell.GotFocus += (_, _) =>
                {
                    if (active == (dayIndex, hourIndex)) return;
                    cellControls[active.Day, active.Hour].IsTabStop = false;
                    active = (dayIndex, hourIndex);
                    cell.IsTabStop = true;
                };
                cell.KeyDown += (_, e) =>
                {
                    var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                    (int Day, int Hour)? target = e.Key switch
                    {
                        VirtualKey.Right => (dayIndex, Math.Min(23, hourIndex + 1)),
                        VirtualKey.Left => (dayIndex, Math.Max(0, hourIndex - 1)),
                        VirtualKey.Down => (Math.Min(6, dayIndex + 1), hourIndex),
                        VirtualKey.Up => (Math.Max(0, dayIndex - 1), hourIndex),
                        VirtualKey.Home => (ctrl ? 0 : dayIndex, 0),
                        VirtualKey.End => (ctrl ? 6 : dayIndex, 23),
                        _ => null,
                    };
                    if (target is not { } next) return;
                    e.Handled = true;
                    MoveFocus(next.Day, next.Hour);
                };
                cellControls[day, hour] = cell;
                Grid.SetRow(cell, day + 1);
                Grid.SetColumn(cell, hour + 1);
                content.Children.Add(cell);
            }
        }

        var scroller = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };

        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        legend.Children.Add(Text(AdminInsightsText.T("web.admin.insights.activity.heatmap.less"), 11, color: MutedForeground));
        for (var level = 0; level < 5; level++)
            legend.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = LevelBrush(level), VerticalAlignment = VerticalAlignment.Center });
        legend.Children.Add(Text(AdminInsightsText.T("web.admin.insights.activity.heatmap.more"), 11, color: MutedForeground));

        var stack = new StackPanel { Spacing = compact ? 8 : 12 };
        stack.Children.Add(scroller);
        stack.Children.Add(legend);
        return stack;
    }

    private static void Scale(ContentControl cell, double scale)
    {
        if (cell.RenderTransform is ScaleTransform transform) { transform.ScaleX = scale; transform.ScaleY = scale; }
        Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(cell, scale > 1 ? 1 : 0);
    }
}
