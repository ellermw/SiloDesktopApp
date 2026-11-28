using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using A = SiloPlayer.Controls.AdminMonitorUi;
using D = SiloPlayer.Core.Services.Admin.AdminInsightsDevicesPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Insights › Devices &amp; Quality (official <c>DevicesTab.tsx</c>, c59798116): how well each app plays what the library holds. The matrix of
/// direct play by app and video format, why streams were converted, who converts most, the hardware doing it, resolution, and the apps,
/// subtitles and audio people play. Every number that has sessions behind it opens them in Playback History.
/// </summary>
internal sealed class AdminInsightsDevicesTab(AdminInsightsPage page) : AdminInsightsTabBase<AdminInsightsDevices>(page)
{
    private D.SampleThreshold _threshold = D.SampleThreshold.Five;

    protected override string KeyFor(AdminInsightsFilterState filters) => AdminInsightsApi.DevicesPath(filters.ToWindow(), D.MinSessions(_threshold));

    protected override Task<AdminInsightsDevices> LoadAsync(MonitorAuthorityLease lease, AdminInsightsFilterState filters, bool refresh, CancellationToken ct)
        => Page.Insights.GetDevicesAsync(lease, filters.ToWindow(), D.MinSessions(_threshold), refresh, ct);

    protected override FrameworkElement BuildSkeleton() => AdminInsightsLayout.Stack(
        AdminInsightsLayout.KpiSkeleton(), AdminInsightsLayout.CardSkeleton(288),
        AdminInsightsLayout.TwoUp(AdminInsightsLayout.CardSkeleton(224), AdminInsightsLayout.CardSkeleton(224)));

    private static string I(string key, params (string, object?)[] values) => AdminInsightsText.T(key, values);

    private static string SessionCount(double value) => AdminInsightsRules.SessionCount((long)Math.Round(value));

    protected override FrameworkElement BuildBody(AdminInsightsDevices data, AdminInsightsFilterState filters)
    {
        var notice = D.Notice(data);
        FrameworkElement Card(string title, string? subtitle, FrameworkElement body, Func<IEnumerable<FrameworkElement?>>? actions = null)
            => new AdminInsightsCard(title, subtitle, actions, () => body);
        FrameworkElement List(IReadOnlyList<AdminInsightsBarRow> rows, int seriesIndex, string empty)
            => AdminInsightsLayout.RowList(rows, SessionCount, empty, seriesIndex, open: OpenHistory);
        var burnIn = D.BurnInCodecs(data);
        var burnInHistory = D.BurnInHistory(data, filters);
        var subtitles = D.Subtitles(data);
        var audio = D.Audio(data);
        var usage = AdminInsightsLayout.TwoUp(ProtocolCard(data), VersionsCard(data));

        return AdminInsightsLayout.Stack(
            notice == null ? null : AdminInsightsUi.Notice(notice),
            AdminInsightsLayout.KpiGrid(D.Kpis(data)),
            MatrixCard(data, filters),
            AdminInsightsLayout.TwoUp(
                Card(I("web.admin.insights.devices.reasons.title"), I("web.admin.insights.devices.reasons.subtitle"),
                    List(D.Reasons(data, filters), 2, I("web.admin.insights.devices.reasons.empty"))),
                Card(I("web.admin.insights.devices.topTranscoders.title"), null,
                    List(D.TopTranscoders(data, filters), 0, I("web.admin.insights.devices.topTranscoders.empty")))),
            AdminInsightsLayout.TwoUp(
                Card(I("web.admin.insights.devices.hwAccel.title"), null, List(D.HardwareAcceleration(data), 1, I("web.admin.insights.devices.hwAccel.empty"))),
                Card(I("web.admin.insights.devices.burnInCodecs.title"), null, List(burnIn, 3, I("web.admin.insights.devices.burnInCodecs.empty")),
                    burnInHistory == null ? null : () => [HistoryLink(burnInHistory)])),
            Card(I("web.admin.insights.devices.resolution.title"), I("web.admin.insights.devices.resolution.subtitle"),
                Sections(768, 3,
                    (I("web.admin.insights.devices.resolution.source"), List(D.Resolutions(data.SourceResolutions), 0, I("web.admin.insights.devices.resolution.empty"))),
                    (I("web.admin.insights.devices.resolution.delivered"), List(D.Resolutions(data.DeliveredResolutions), 1, I("web.admin.insights.devices.resolution.empty"))),
                    (I("web.admin.insights.devices.resolution.downscaled"), List(D.Downscales(data), 2, I("web.admin.insights.devices.resolution.downscaledEmpty"))))),
            usage,
            Card(I("web.admin.insights.devices.subtitles.title"), subtitles.Subtitle,
                Sections(768, 2,
                    (I("web.admin.insights.devices.languages"), List(subtitles.Languages, 0, I("web.admin.insights.devices.subtitles.empty"))),
                    (I("web.admin.insights.devices.subtitles.deliveryTitle"), WithNote(List(subtitles.Deliveries, 1, I("web.admin.insights.devices.subtitles.empty")), subtitles.Flags)))),
            Card(I("web.admin.insights.devices.audio.title"), audio.Subtitle,
                Sections(768, 3,
                    (I("web.admin.insights.devices.languages"), List(audio.Languages, 0, I("web.admin.insights.devices.resolution.empty"))),
                    (I("web.admin.insights.devices.audio.decisionTitle"), List(audio.Decisions, 1, I("web.admin.insights.devices.resolution.empty"))),
                    (I("web.admin.insights.devices.audio.channelsTitle"), List(audio.Channels, 2, I("web.admin.insights.devices.resolution.empty"))))));
    }

    /// <summary>A row's drilldown into Playback History, off while the result on screen is only the previous window's.</summary>
    private Action? OpenHistory(AdminInsightsBarRow row) => row.History is { } history ? Guarded(() => AdminShellPage.Open(history)) : null;

    private FrameworkElement HistoryLink(AdminRouteRequest history)
    {
        var link = DashboardUi.TextLink(I("web.admin.insights.openHistory"), 12, (_, _) => Guarded(() => AdminShellPage.Open(history))?.Invoke(), A.Primary, underlineOnHover: true);
        link.Tag = DashboardBarList.LinkTag;
        if (link.Content is TextBlock text) text.FontWeight = FontWeights.Medium;
        AutomationProperties.SetAutomationId(link, "AdminInsightsDevicesHistory");
        return link;
    }

    private static FrameworkElement WithNote(FrameworkElement list, string? note)
    {
        if (note == null) return list;
        var text = A.Text(note, 12, A.Muted, trim: false);
        text.Margin = new Thickness(0, 4, 0, 0);
        return new StackPanel { Spacing = 0, Children = { list, text } };
    }

    /// <summary>Titled columns inside a card (<c>grid grid-cols-1 gap-5 md:grid-cols-N</c>, each a muted 12px heading over its list).</summary>
    private static FrameworkElement Sections(double minWidth, int columns, params (string Heading, FrameworkElement Body)[] sections)
    {
        var grid = new AdminInsightsGrid(20, (minWidth, columns));
        foreach (var (heading, body) in sections)
            grid.Add(new StackPanel { Spacing = 6, Children = { A.Text(heading, 12, A.Muted, FontWeights.SemiBold, trim: false), body } });
        return grid;
    }

    // ── The matrix ───────────────────────────────────────────

    private FrameworkElement MatrixCard(AdminInsightsDevices data, AdminInsightsFilterState filters)
    {
        FrameworkElement Toggle() => AdminInsightsLayout.Toggle(
            [(D.SampleThreshold.Five, I("web.admin.insights.devices.matrix.minSessions"), I("web.admin.insights.devices.matrix.minSessions")),
             (D.SampleThreshold.All, I("web.admin.insights.devices.matrix.allSamples"), I("web.admin.insights.devices.matrix.allSamples"))],
            _threshold, ChooseThreshold);
        return new AdminInsightsCard(I("web.admin.insights.devices.matrix.title"), I("web.admin.insights.devices.matrix.subtitle"), () => [Toggle()],
            () => D.BuildMatrix(data, filters) is { } matrix ? MatrixTable(matrix) : EmptyLine(I("web.admin.insights.devices.matrix.empty"), 40));
    }

    private void ChooseThreshold(D.SampleThreshold threshold)
    {
        if (_threshold == threshold) return;
        _threshold = threshold;
        Reload();
    }

    private static FrameworkElement EmptyLine(string text, double padding)
    {
        var line = A.Text(text, 14, A.Muted, trim: false);
        line.TextAlignment = TextAlignment.Center;
        line.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border { Padding = new Thickness(0, padding, 0, padding), Child = line };
    }

    private FrameworkElement MatrixTable(D.Matrix matrix)
    {
        var table = new AdminInsightsTable(matrix.CodecLabels.Count + 2);
        for (var column = 1; column <= matrix.CodecLabels.Count; column++) table.Align(column, HorizontalAlignment.Center);
        table.Align(matrix.CodecLabels.Count + 1, HorizontalAlignment.Right);
        var header = new List<UIElement> { AdminInsightsTable.HeaderText(I("web.admin.insights.devices.matrix.clientColumn")) };
        header.AddRange(matrix.CodecLabels.Select(label => (UIElement)AdminInsightsTable.HeaderText(label)));
        header.Add(AdminInsightsTable.HeaderText(I("web.admin.insights.devices.matrix.totalColumn")));
        table.AddHeader(header);
        foreach (var row in matrix.Rows)
        {
            var cells = new List<UIElement> { ClientCell(row) };
            cells.AddRange(row.Cells.Select(cell => cell == null ? Dash() : (UIElement)MatrixCell(cell)));
            var total = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
            total.Children.Add(Right(A.Text(row.Total, 14, A.Foreground, FontWeights.SemiBold, trim: false)));
            total.Children.Add(Right(A.Text(row.TotalSessions, 11, A.Muted, trim: false)));
            cells.Add(total);
            table.AddRow(cells, verticalPadding: 6);
        }
        return table;
    }

    private static TextBlock Right(TextBlock text)
    {
        text.HorizontalAlignment = HorizontalAlignment.Right;
        text.TextAlignment = TextAlignment.Right;
        return text;
    }

    private static UIElement Dash()
    {
        var dash = A.Text("—", 12, A.Muted, trim: false);
        dash.HorizontalAlignment = HorizontalAlignment.Center;
        return dash;
    }

    private static UIElement ClientCell(D.MatrixRow row)
    {
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        name.Children.Add(A.Text(row.Client, 14, A.Foreground, FontWeights.Medium, trim: true));
        if (row.IsJellyfin) name.Children.Add(AdminInsightsTable.JellyfinPill());
        var stack = new StackPanel { Spacing = 2, Children = { name } };
        if (!string.IsNullOrEmpty(row.Platform)) stack.Children.Add(A.Text(row.Platform, 11, A.Muted, trim: true));
        return stack;
    }

    /// <summary>
    /// A colour-coded cell: the direct-play rate over its session count, 85% and up green, 50% and up amber, below that red. A click opens
    /// those sessions in History; the tooltip says the same in words.
    /// </summary>
    private UIElement MatrixCell(D.MatrixCell cell)
    {
        var tone = cell.Tone switch { D.CellTone.Good => ActivityTone.Success, D.CellTone.Fair => ActivityTone.Warning, _ => ActivityTone.Destructive };
        var color = A.ToneColor(tone);
        var text = A.LabelColor(tone);
        var percent = A.Text(cell.Percent, 12, text, FontWeights.Bold, trim: false);
        percent.HorizontalAlignment = HorizontalAlignment.Center;
        var sessions = A.Text(cell.Sessions, 10, text, trim: false);
        sessions.Opacity = 0.8;
        sessions.HorizontalAlignment = HorizontalAlignment.Center;
        var face = new Border
        {
            MinWidth = 72, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(8, 4, 8, 4),
            Background = A.Solid(color, 0.15), BorderBrush = A.Solid(color, 0.25), Child = new StackPanel { Children = { percent, sessions } },
        };
        var button = A.LinkButton(A.Text("", 1, text), cell.Tooltip, text, underlineOnHover: false);
        button.Content = face;
        button.Padding = new Thickness(0);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.Tag = DashboardBarList.LinkTag;
        ToolTipService.SetToolTip(button, cell.Tooltip);
        AutomationProperties.SetAutomationId(button, "AdminInsightsMatrixCell");
        button.PointerEntered += (_, _) => face.Background = A.Solid(color, 0.25);
        button.PointerExited += (_, _) => face.Background = A.Solid(color, 0.15);
        button.IsEnabledChanged += (_, _) => face.Opacity = button.IsEnabled ? 1 : 0.6;
        var history = cell.History;
        button.Click += (_, _) => Guarded(() => AdminShellPage.Open(history))?.Invoke();
        return button;
    }

    // ── App and track breakdowns ─────────────────────────────

    private FrameworkElement ProtocolCard(AdminInsightsDevices data)
        => new AdminInsightsCard(I("web.admin.insights.devices.protocol.title"), I("web.admin.insights.devices.protocol.subtitle"), null,
            () => D.ProtocolColumns(data) is { } shape
                ? DashboardInsightsCharts.Columns(shape, time => DashboardInsightsPresentation.BucketLabel(time, data.BucketUnit), DashboardInsightsCharts.WatchTimeOrCount(false),
                    I("web.admin.insights.devices.protocol.total"), I("web.admin.insights.devices.protocol.title"), 180, 0)
                : AdminInsightsLayout.Empty(I("web.admin.insights.devices.resolution.empty")));

    private FrameworkElement VersionsCard(AdminInsightsDevices data)
        => new AdminInsightsCard(I("web.admin.insights.devices.versions.title"), I("web.admin.insights.devices.versions.subtitle"), null,
            () =>
            {
                var rows = D.Versions(data);
                if (rows.Count == 0) return EmptyLine(I("web.admin.insights.devices.versions.empty"), 24);
                var table = new AdminInsightsTable(4);
                table.Align(2, HorizontalAlignment.Right).Align(3, HorizontalAlignment.Right);
                table.AddHeader([AdminInsightsTable.HeaderText(I("web.admin.insights.devices.matrix.clientColumn")), AdminInsightsTable.HeaderText(I("web.admin.insights.devices.versions.versionColumn")),
                    AdminInsightsTable.HeaderText(I("web.admin.insights.devices.versions.sessionsColumn")), AdminInsightsTable.HeaderText(I("web.admin.insights.devices.versions.lastSeenColumn"))]);
                foreach (var row in rows)
                {
                    var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    name.Children.Add(A.Text(row.Client, 12, A.Foreground, FontWeights.Medium, trim: true));
                    if (row.IsJellyfin) name.Children.Add(AdminInsightsTable.JellyfinPill());
                    var client = new StackPanel { Spacing = 2, Children = { name } };
                    if (!string.IsNullOrEmpty(row.Platform)) client.Children.Add(A.Text(row.Platform, 12, A.Muted, trim: true));
                    var version = A.Text(row.Version, 12, A.Foreground, trim: true);
                    version.FontFamily = A.Mono;
                    table.AddRow([client, version, A.Text(row.Sessions, 12, A.Foreground, trim: false), A.Text(row.LastSeen, 12, A.Muted, trim: false)], verticalPadding: 6);
                }
                // Many apps scroll inside the card (max-h-72), not past it.
                return new ScrollViewer { Content = table, MaxHeight = 288, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, IsTabStop = false };
            });
}
