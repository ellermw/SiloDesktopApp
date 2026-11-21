using Microsoft.UI.Text;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using A = SiloPlayer.Controls.AdminMonitorUi;
using P = SiloPlayer.Core.Services.Admin.AdminActivityPresentation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminActivityPage
{
    private FrameworkElement ExpandedPanel(AdminActivitySession s, RowState state)
    {
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(16, 12, 16, 12), BorderBrush = A.Solid(A.Border, 0.4), BorderThickness = new Thickness(0, 1, 0, 0), Background = A.Solid(A.Surface, 0.3) };
        var columns = new Grid { ColumnSpacing = 20, RowSpacing = 16 };
        if (IsWideTable)
            foreach (var star in new[] { 1.7, 1d, 1d }) columns.ColumnDefinitions.Add(new() { Width = new GridLength(star, GridUnitType.Star) });
        else
            for (var i = 0; i < 3; i++) columns.RowDefinitions.Add(new() { Height = GridLength.Auto });

        var decision = new StackPanel { Spacing = 16 };
        var facts = FactList(wide: true);
        var meta = P.MethodMeta(P.ClassifyActivityMethod(s));
        var method = new LogsWrapPanel { Spacing = 8, RowSpacing = 2 };
        var badge = A.Badge(meta.Label, A.ToneColor(meta.Tone), fontSize: 10, foreground: A.LabelColor(meta.Tone));
        method.Children.Add(badge); method.Children.Add(A.Text(meta.Description, 12, A.Foreground, trim: false));
        Fact(facts, "web.admin.activity.panel.decision", method);
        var reasons = new StackPanel { Spacing = 2 };
        foreach (var reason in AdminActivityReasons.Get(s))
        {
            var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = reason.Label, FontWeight = FontWeights.Medium, Foreground = A.Solid(A.Foreground) });
            if (ActivityViewportWidth >= 640) line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "\u2002" });
            else line.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
            line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = reason.Description, Foreground = A.Solid(A.Muted) });
            reasons.Children.Add(line);
        }
        if (reasons.Children.Count > 0) Fact(facts, "web.admin.activity.panel.reasons", reasons);
        decision.Children.Add(facts);

        var stream = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        // The official table: th/td cells with py-0.5 and pe-3, a header row of muted font-normal labels (pb-1), the decision chip beside the row label and the
        // tags as inline px-1 text-[10px] leading-4 bordered chips after the delivered text.
        stream.ColumnDefinitions.Add(new() { Width = new GridLength(ActivityViewportWidth >= 640 ? 144 : 96) });
        stream.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        stream.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        void StreamRow(string? key, string? source, string? delivered, string? streamDecision = null, bool header = false, params string?[] tags)
        {
            var row = stream.RowDefinitions.Count; stream.RowDefinitions.Add(new() { Height = GridLength.Auto });
            void Cell(FrameworkElement element, int column) { Grid.SetColumn(element, column); Grid.SetRow(element, row); stream.Children.Add(element); }
            var label = new LogsWrapPanel { Spacing = 6, RowSpacing = 2 };
            if (key != null) label.Children.Add(A.Text(AdminText.T(key), 12, A.Foreground, trim: false));
            if (!string.IsNullOrEmpty(streamDecision))
            {
                var tone = P.DecisionTone(streamDecision);
                var chip = A.Badge(P.FormatDecisionLabel(streamDecision), A.ToneColor(tone), fontSize: 10, foreground: A.LabelColor(tone));
                chip.Padding = new Thickness(4, 2, 4, 2); label.Children.Add(chip);
            }
            Cell(label, 0);
            Cell(A.Text(source ?? "", 12, A.Muted, trim: false), 1);
            var output = new LogsWrapPanel { Spacing = 6, RowSpacing = 2 };
            output.Children.Add(A.Text(delivered ?? "", 12, header ? A.Muted : A.Foreground, trim: false));
            foreach (var tag in tags.Where(t => !string.IsNullOrEmpty(t)))
                output.Children.Add(new Border
                {
                    BorderBrush = A.Solid(A.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 0), MinHeight = 16,
                    Child = new TextBlock { Text = tag!, FontSize = 10, Foreground = A.Solid(A.Muted), VerticalAlignment = VerticalAlignment.Center },
                });
            Cell(output, 2);
        }
        StreamRow(null, AdminText.T("web.admin.activity.detail.source"), AdminText.T("web.admin.activity.detail.delivered"), header: true);
        var video = P.VideoDecision(s); var audio = P.AudioDecision(s); var mode = P.FormatTranscodeModeSummary(s);
        StreamRow("web.admin.activity.row.video", P.FormatVideoSummary(s), P.FormatDeliveredVideoSummary(s), video, false,
            video == "transcode" ? mode : null, video == "transcode" ? P.FormatToneMapSummary(s)?.Badge : null, P.FormatRequestedSourceSwitch(s));
        StreamRow("web.admin.activity.row.audio", P.FormatAudioSummary(s), P.FormatDeliveredAudioSummary(s), audio, false, audio == "transcode" && video != "transcode" ? mode : null);
        StreamRow("web.admin.activity.row.container", P.FormatSourceContainerSummary(s), P.FormatDeliveredContainerSummary(s), P.NormalizeContainerDecision(s.PlayMethod));
        var bitrate = P.GetSessionBitrate(s);
        if (bitrate.Live != null || bitrate.Source != null)
            StreamRow("web.admin.activity.column.bitrate", bitrate.Source, bitrate.Live, null, false, bitrate.Limit != null ? AdminText.T("web.admin.activity.bitrate.limit", new { rate = bitrate.Limit }) : null);
        decision.Children.Add(stream);

        var client = new StackPanel { Spacing = 6 };
        client.Children.Add(A.Text(AdminText.T("web.admin.activity.client.heading"), 11, A.Muted, FontWeights.Medium));
        var clientFacts = FactList();
        Fact(clientFacts, "web.admin.activity.client.app", P.GetSessionClientLabelFull(s) is { Length: > 0 } label ? label : AdminText.T("web.admin.activity.client.unknown"));
        var device = string.Join("\n", new[] { s.DevicePlatform?.Trim(), s.DeviceId?.Trim() }.Where(v => !string.IsNullOrEmpty(v)));
        if (device.Length > 0) Fact(clientFacts, "web.admin.activity.client.device", device);
        if (s.ClientUserAgent?.Trim() is { Length: > 0 } agent) Fact(clientFacts, "web.admin.activity.client.agent", agent, muted: true);
        if (!ShowRoute || !IsWideTable)
        {
            var route = new StackPanel { Spacing = 4 }; route.Children.Add(RouteBadges(s));
            if (s.ClientIp?.Trim() is { Length: > 0 } ip) route.Children.Add(IpButton(ip));
            Fact(clientFacts, "web.admin.activity.column.route", route);
        }
        client.Children.Add(clientFacts);

        var session = new StackPanel { Spacing = 6 };
        session.Children.Add(A.Text(AdminText.T("web.admin.activity.session.heading"), 11, A.Muted, FontWeights.Medium));
        var sessionFacts = FactList(); var now = DateTimeOffset.UtcNow;
        var started = DateTimeOffset.TryParse(s.StartedAt, out var date) ? SiloPlayer.Helpers.DateTimeDisplay.FormatTime(date, seconds: false) : s.StartedAt;
        Fact(sessionFacts, "web.admin.activity.session.started", AdminText.T("web.admin.activity.session.startedAgo", new { time = started, elapsed = P.GetElapsed(s.StartedAt, now) }));
        Fact(sessionFacts, "web.admin.activity.session.lastReport", AdminText.T("web.admin.activity.session.lastReportAgo", new { elapsed = P.GetElapsed(s.UpdatedAt, now) }));
        Fact(sessionFacts, "web.admin.activity.session.remoteControl", AdminText.T(s.HasPlaybackControl ? "web.admin.activity.session.remoteControlAvailable" : "web.admin.activity.session.remoteControlUnavailable"), muted: !s.HasPlaybackControl);
        var id = A.Text(s.SessionId, 11, A.Muted, trim: false); id.FontFamily = A.Mono; id.IsTextSelectionEnabled = true;
        Fact(sessionFacts, "web.admin.activity.session.id", id); session.Children.Add(sessionFacts);
        var sections = new[] { decision, client, session };
        for (var i = 0; i < sections.Length; i++)
        {
            var section = new Border { Child = sections[i] };
            if (IsWideTable) { Grid.SetColumn(section, i); if (i > 0) { section.BorderBrush = A.Solid(A.Border, 0.4); section.BorderThickness = new Thickness(1, 0, 0, 0); section.Padding = new Thickness(20, 0, 0, 0); } }
            else Grid.SetRow(section, i);
            columns.Children.Add(section);
        }
        panel.Children.Add(columns);
        if (!IsWideTable) { var ffmpeg = FfmpegButton(s, state); ffmpeg.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(ffmpeg); }
        if (state.Ffmpeg) panel.Children.Add(Console(s, state));
        return panel;
    }

    private Grid FactList(bool wide = false)
    {
        var list = new Grid { RowSpacing = 4, ColumnSpacing = 12 };
        list.ColumnDefinitions.Add(new() { Width = new GridLength(ActivityViewportWidth >= 640 ? wide ? 144 : 112 : 96) });
        list.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        return list;
    }

    private static void Fact(Grid list, string key, string value, bool muted = false) => Fact(list, key, A.Text(value, 12, muted ? A.Muted : A.Foreground, trim: false));

    private static void Fact(Grid list, string key, FrameworkElement value)
    {
        var row = list.RowDefinitions.Count; list.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = A.Text(AdminText.T(key), 12, A.Muted, trim: false); label.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(label, row); list.Children.Add(label); Grid.SetRow(value, row); Grid.SetColumn(value, 1); list.Children.Add(value);
    }

    private Button FfmpegButton(AdminActivitySession s, RowState state, bool iconOnly = false)
    {
        var label = AdminText.T(state.Ffmpeg ? "web.admin.activity.row.hideFfmpeg" : "web.admin.activity.row.ffmpeg");
        var button = iconOnly ? A.Plain(A.Icon("terminal", 14, A.Foreground), label) : A.Outline(label, "terminal", height: 28, fontSize: 12);
        if (iconOnly) button.Width = button.Height = 28;
        ToolTipService.SetToolTip(button, label);
        button.Click += (_, _) => { state.Ffmpeg = !state.Ffmpeg; if (state.Ffmpeg) { state.Details = true; _ = LoadFfmpegAsync(s.SessionId, state, force: false); } RenderTable(); };
        return button;
    }
}
