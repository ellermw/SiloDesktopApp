using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using Presentation = SiloPlayer.Core.Services.Admin.AdminPreparedFilesPresentation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    /// <summary>The Storage tab (official <c>StorageTab.tsx</c>): totals, warnings and one card per location.</summary>
    private sealed class StorageView : TabView
    {
        private readonly StackPanel _root = Stack(20);
        private readonly ContentControl _content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private string? _renderedKind;
        private string _fingerprint = "";
        // Buttons whose enabled state follows the page state, so a state change updates them in place instead of redrawing the cards.
        private readonly List<(Microsoft.UI.Xaml.Controls.Primitives.ButtonBase Button, Func<AdminPreparedFilesSnapshot, bool> Enabled)> _gates = [];

        public StorageView(AdminPreparedFilesPage page) : base(page) { _root.Children.Add(_content); }
        public override UIElement Root => _root;

        public override void Reset() { base.Reset(); _renderedKind = null; _fingerprint = ""; _gates.Clear(); _content.Content = null; }

        protected override object Inputs(AdminPreparedFilesSnapshot state) => (state.Storage, state.ActionPending, state.DestructiveBlocked);

        public override void Update(AdminPreparedFilesSnapshot state)
        {
            var storage = state.Storage;
            if (storage.Value == null)
            {
                var kind = storage.Error != null ? "error" : "loading";
                if (_renderedKind == kind) return;
                _renderedKind = kind; _fingerprint = ""; _gates.Clear();
                AutomationProperties.SetAutomationId(_content, "PreparedFilesStorageState");
                _content.Content = storage.Error != null || storage.Loaded
                    ? Message(storage.Error ?? T("web.admin.downloads.storage.loadError"), "PreparedFilesStorageReload", () => _ = P.Controller.ReloadStorageAsync())
                    : Message(T("web.admin.downloads.storage.loading"));
                return;
            }
            // Redrawn only when the overview itself (or the minute its relative times are read in) changed; a retained old overview is kept only while a background read fails.
            // A change of page state (an action starting, a poll that found the same numbers) only updates the buttons that follow it.
            _renderedKind = "ready";
            var fingerprint = Fingerprint(storage.Value) + "|" + (storage.Error != null) + "|" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            if (fingerprint != _fingerprint || _content.Content == null)
            {
                _fingerprint = fingerprint; _gates.Clear();
                _content.Content = Build(state, storage.Value, storage.Error != null);
            }
            foreach (var (button, enabled) in _gates)
            {
                var on = enabled(state);
                if (button.IsEnabled != on) button.IsEnabled = on;
            }
        }

        private static string Fingerprint(AdminDownloadStorage value)
        {
            try { return System.Text.Json.JsonSerializer.Serialize(value); }
            catch (Exception) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value).ToString(System.Globalization.CultureInfo.InvariantCulture); }
        }

        private void Gate(Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button, AdminPreparedFilesSnapshot state, Func<AdminPreparedFilesSnapshot, bool> enabled)
        {
            button.IsEnabled = enabled(state);
            _gates.Add((button, enabled));
        }

        private UIElement Build(AdminPreparedFilesSnapshot state, AdminDownloadStorage data, bool stale)
        {
            var root = Stack(20);
            if (stale) root.Children.Add(StaleBanner("PreparedFilesStorageReload", () => _ = P.Controller.ReloadStorageAsync()));
            var totals = Presentation.StorageTotals(data);
            var warnings = Presentation.StorageWarnings(data);
            var stats = new LogsMetricGrid(5, 2);
            stats.Add(Tile(T("web.admin.downloads.storage.stats.onDisk"), Presentation.FormatBytes(totals.OnDisk),
                Text(T("web.admin.downloads.storage.stats.onDiskDetail", new
                {
                    locations = T("web.admin.downloads.storage.stats.locationCount", Presentation.Count(data.Locations.Count)),
                    files = T("web.admin.downloads.storage.fileCount", Presentation.With(Presentation.Count(totals.Files), ("count_text", totals.Files.ToString("N0", AppStrings.FormatCulture)))),
                }), 12, true)));
            stats.Add(Tile(T("web.admin.downloads.storage.stats.inUse"), Presentation.FormatBytes(totals.InUse), Text(T("web.admin.downloads.storage.stats.inUseDetail"), 12, true)));
            stats.Add(Tile(T("web.admin.downloads.storage.stats.cached"), Presentation.FormatBytes(totals.Cached),
                Text(T("web.admin.downloads.storage.stats.cachedDetail", Presentation.Count(data.CacheHours)), 12, true)));
            stats.Add(Tile(T("web.admin.downloads.storage.stats.untracked"), Presentation.FormatBytes(totals.Untracked),
                Text(T("web.admin.downloads.storage.stats.untrackedDetail"), 12, true), totals.Untracked > 0 ? Brush("WarningBrush") : null));
            UIElement freedDetail;
            if (data.PreparingJobs > 0)
            {
                var link = new HyperlinkButton { Content = T("web.admin.downloads.storage.stats.preparingNow", Presentation.Count(data.PreparingJobs)), Padding = new Thickness(0), FontSize = 12 };
                AutomationProperties.SetAutomationId(link, "PreparedFilesPreparingLink");
                link.Click += (_, _) => P.OpenTab("preparation");
                freedDetail = link;
            }
            else freedDetail = Text(T("web.admin.downloads.storage.stats.nothingPreparing"), 12, true);
            stats.Add(Tile(T("web.admin.downloads.storage.stats.freed"), Presentation.FormatBytes(data.FreedLast30DaysBytes), freedDetail));
            root.Children.Add(stats);

            if (warnings.Count > 0)
            {
                var list = Stack(8);
                foreach (var warning in warnings) list.Children.Add(Warning(warning, data));
                root.Children.Add(list);
            }

            var cards = new LogsMetricGrid(2, 1);
            foreach (var location in data.Locations) cards.Add(Card(location, data, state));
            root.Children.Add(cards);
            root.Children.Add(Key(data.DiskCeilingPercent));
            return root;
        }

        private UIElement Warning(AdminStorageWarning warning, AdminDownloadStorage data)
        {
            var isWarning = warning.Tone == "warning";
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var icon = Icon(isWarning ? "triangle-alert" : "circle-alert", 16, Brush(isWarning ? "WarningBrush" : "InfoBrush"));
            if (icon is FrameworkElement glyph) glyph.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(icon);
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 20, Foreground = Brush("PrimaryTextBrush") };
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = warning.Title, FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " " + warning.Body });
            Grid.SetColumn(text, 1); grid.Children.Add(text);
            if (warning.Action != null)
            {
                var label = warning.Action switch
                {
                    "edit_location" => T("web.admin.downloads.storage.editLocation"),
                    "review_untracked" => T("web.admin.downloads.storage.review"),
                    "review_devices" => T("web.admin.downloads.storage.warningAction.reviewDevices"),
                    _ => T("web.admin.downloads.storage.warningAction.viewFiles"),
                };
                var action = Button(label, "PreparedFilesWarning_" + warning.Id);
                action.VerticalAlignment = VerticalAlignment.Top;
                var location = warning.Location == null ? null : data.Locations.FirstOrDefault(x => x.Key == warning.Location);
                action.Click += (_, _) =>
                {
                    switch (warning.Action)
                    {
                        case "edit_location": if (location != null) P.EditLocation(location); break;
                        case "review_untracked": if (location != null) P.ReviewUntracked(location); break;
                        case "review_devices": P.OpenDevices(true); break;
                        case "view_location": if (location != null) P.OpenFilesAt(location.Key); break;
                    }
                };
                Grid.SetColumn(action, 2); grid.Children.Add(action);
            }
            var panel = Panel(grid, 12, 0, true);
            panel.Padding = new Thickness(16, 12, 16, 12);
            panel.BorderBrush = Tint(isWarning ? "WarningBrush" : "InfoBrush", 0.35);
            AutomationProperties.SetAutomationId(panel, "PreparedFilesWarningBanner_" + warning.Id);
            return panel;
        }

        private UIElement Card(AdminDownloadStorageLocation location, AdminDownloadStorage data, AdminPreparedFilesSnapshot state)
        {
            var usage = location.Usage;
            var segments = Presentation.MeterSegments(location);
            var fill = Presentation.DiskFillPercent(location);
            var nearCeiling = fill is { } f && f >= data.DiskCeilingPercent - 5;
            var drift = Presentation.RecordsDrift(location);
            var indexed = Presentation.PreparedBytes(location);
            var offline = location.Kind == "node" && !location.Online;
            var measured = Presentation.Relative(usage?.MeasuredAt);
            var files = location.InUseFiles + location.CachedFiles;
            var root = Stack(14);

            var header = new Grid { ColumnSpacing = 8 };
            header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var heading = Inline(8);
            var name = Strong(location.Name, 17);
            name.TextTrimming = TextTrimming.CharacterEllipsis; name.TextWrapping = TextWrapping.NoWrap;
            AutomationProperties.SetHeadingLevel(name, AutomationHeadingLevel.Level2);
            heading.Children.Add(name);
            heading.Children.Add(StatusPill(location));
            header.Children.Add(heading);
            var cleanUp = Button(T("web.admin.downloads.storage.card.cleanUp"), "PreparedFilesCleanup_" + location.Key, "GhostButtonStyle");
            Gate(cleanUp, state, s => !s.ActionPending && !s.DestructiveBlocked && !offline);
            cleanUp.Click += (_, _) => _ = P.Controller.CleanupLocationAsync(location);
            Grid.SetColumn(cleanUp, 1); header.Children.Add(cleanUp);
            root.Children.Add(header);

            var code = Text(Presentation.LocationDirectoryLabel(location), 12, true, true); code.TextWrapping = TextWrapping.WrapWholeWords;
            var badges = new LogsWrapPanel { Spacing = 6 };
            if (usage?.SharesScratch == true) badges.Children.Add(Pill(T("web.admin.downloads.storage.card.sharesScratch"), "warning"));
            else if (usage != null)
                badges.Children.Add(Pill(string.IsNullOrEmpty(usage.FSType) ? T("web.admin.downloads.storage.card.ownDisk") : T("web.admin.downloads.storage.card.ownDiskWithType", new { fs_type = usage.FSType }), "muted"));
            if (usage?.Ephemeral == true) badges.Children.Add(Pill(T("web.admin.downloads.storage.card.temporary"), "warning"));
            var meta = new LogsWrapPanel { Spacing = 8 };
            meta.Children.Add(code); meta.Children.Add(badges);
            root.Children.Add(meta);
            if (!string.IsNullOrEmpty(location.PendingDir))
                root.Children.Add(RichText(AdminPreparedFilesText.Rich("web.admin.downloads.storage.card.pendingDir", new { dir = location.PendingDir, name = location.Name }), 12, true));

            if (segments != null && usage != null)
            {
                var meter = Stack(8);
                var line = new Grid { ColumnSpacing = 8 };
                line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                line.Children.Add(RichText(AdminPreparedFilesText.Rich("web.admin.downloads.storage.card.diskUsed", new
                {
                    used = Presentation.FormatBytes(usage.FSUsedBytes), total = Presentation.FormatBytes(usage.FSTotalBytes), percent = Presentation.FormatPercent(fill ?? 0),
                }), 12, true));
                if (usage.Stale || offline)
                {
                    var asOf = Text(measured != null ? T("web.admin.downloads.storage.card.asOf", new { when = measured }) : T("web.admin.downloads.storage.card.asOfEarlier"), 12, true);
                    Grid.SetColumn(asOf, 1); line.Children.Add(asOf);
                }
                meter.Children.Add(line);
                meter.Children.Add(Meter(segments, data.DiskCeilingPercent, usage.SharesScratch, offline));
                var legend = new LogsMetricGrid(2, 2) { ColumnSpacing = 16, RowSpacing = 4 };
                foreach (var segment in segments.Where(s => (s.Kind != "free" || s.Bytes > 0) && (s.Kind != "untracked" || s.Bytes > 0)))
                {
                    var row = new Grid { ColumnSpacing = 8 };
                    row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    var label = Inline(6);
                    label.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = SegmentBrush(segment.Kind), VerticalAlignment = VerticalAlignment.Center });
                    label.Children.Add(Text(segment.Label, 12, true));
                    row.Children.Add(label);
                    var amount = Text(Presentation.FormatBytes(segment.Bytes), 12); Grid.SetColumn(amount, 1); row.Children.Add(amount);
                    legend.Add(row);
                }
                meter.Children.Add(legend);
                root.Children.Add(meter);
            }
            else root.Children.Add(Text(offline ? T("web.admin.downloads.storage.card.notMeasuredOffline") : T("web.admin.downloads.storage.card.waitingMeasurement"), 14, true));

            var records = Stack(6);
            var prepared = new Grid { ColumnSpacing = 8 };
            prepared.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            prepared.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            prepared.Children.Add(RichText(AdminPreparedFilesText.Rich(location.BudgetBytes > 0 ? "web.admin.downloads.storage.card.preparedWithBudget" : "web.admin.downloads.storage.card.prepared",
                location.BudgetBytes > 0 ? new { size = Presentation.FormatBytes(indexed), budget = Presentation.FormatBytes(location.BudgetBytes) } : (object)new { size = Presentation.FormatBytes(indexed) }), 12));
            var source = Text(location.BudgetBytes > 0 ? Presentation.BudgetSourceLabel(location) : Presentation.BudgetLabel(location), 12, true);
            Grid.SetColumn(source, 1); prepared.Children.Add(source);
            records.Children.Add(prepared);
            if (location.BudgetBytes > 0)
            {
                var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Min(100, indexed * 100d / location.BudgetBytes), Height = 4 };
                AutomationProperties.SetName(bar, T("web.admin.downloads.storage.card.prepared", new { size = Presentation.FormatBytes(indexed) }));
                records.Children.Add(bar);
            }
            if (location.UntrackedBytes > 0)
            {
                var runs = AdminPreparedFilesText.Rich("web.admin.downloads.storage.card.recordsUntracked", new { indexed = Presentation.FormatBytes(indexed), untracked = Presentation.FormatBytes(location.UntrackedBytes) });
                var wrap = new LogsWrapPanel { Spacing = 0 };
                foreach (var run in runs)
                {
                    if (run.Has("review"))
                    {
                        var review = new HyperlinkButton { Content = run.Text, Padding = new Thickness(0), FontSize = 12 };
                        AutomationProperties.SetAutomationId(review, "PreparedFilesReviewUntracked_" + location.Key);
                        Gate(review, state, s => !s.ActionPending && !s.DestructiveBlocked);
                        review.Click += (_, _) => P.ReviewUntracked(location);
                        wrap.Children.Add(review);
                    }
                    else
                    {
                        var piece = Text(run.Text, 12); piece.Foreground = Brush("WarningBrush"); piece.TextWrapping = TextWrapping.NoWrap;
                        wrap.Children.Add(piece);
                    }
                }
                records.Children.Add(wrap);
            }
            else if (drift != 0)
                records.Children.Add(WarningLine(T("web.admin.downloads.storage.card.recordsDrift", new { indexed = Presentation.FormatBytes(indexed), bytes = Presentation.FormatBytes(usage?.Bytes) })));
            else if (usage != null)
                records.Children.Add(Text(measured != null ? T("web.admin.downloads.storage.card.recordsMatchMeasured", new { when = measured }) : T("web.admin.downloads.storage.card.recordsMatch"), 12, true));
            if (location.StorageFull)
                records.Children.Add(WarningLine(location.Kind == "server" ? T("web.admin.downloads.storage.card.fullServer") : T("web.admin.downloads.storage.card.fullNode")));
            var divider = new Border { BorderBrush = Tint("BorderBrush", 0.6), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0), Child = records };
            root.Children.Add(divider);

            var footer = new Grid { ColumnSpacing = 8 };
            footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var parts = new List<string> { T("web.admin.downloads.storage.fileCount", Presentation.With(Presentation.Count(files), ("count_text", files.ToString("N0", AppStrings.FormatCulture)))) };
            if (location.WaitingDownloads > 0) parts.Add(T("web.admin.downloads.storage.card.waiting", Presentation.Count(location.WaitingDownloads)));
            if (location.CleanupBacklog > 0) parts.Add(T("web.admin.downloads.storage.card.queued", Presentation.Count(location.CleanupBacklog)));
            footer.Children.Add(Text(string.Join(" · ", parts), 12, true));
            var actions = Inline(8);
            var browse = Button(T("web.admin.downloads.storage.card.browse"), "PreparedFilesBrowse_" + location.Key, "GhostButtonStyle");
            browse.Click += (_, _) => P.OpenFilesAt(location.Key);
            var edit = Button(T("web.admin.downloads.storage.editLocation"), "PreparedFilesEditLocation_" + location.Key);
            edit.Click += (_, _) => P.EditLocation(location);
            actions.Children.Add(browse); actions.Children.Add(edit);
            Grid.SetColumn(actions, 1); footer.Children.Add(actions);
            root.Children.Add(footer);

            var panel = Panel(root, 16, 20, true);
            if (nearCeiling) panel.BorderBrush = Tint("WarningBrush", 0.4);
            if (offline) panel.Opacity = 0.8;
            AutomationProperties.SetAutomationId(panel, "PreparedFilesStorageCard_" + location.Key);
            AutomationProperties.SetName(panel, location.Name);
            return panel;
        }

        private static UIElement WarningLine(string text)
        {
            var line = Text(text, 12); line.Foreground = Brush("WarningBrush");
            return line;
        }

        private static UIElement StatusPill(AdminDownloadStorageLocation location)
        {
            if (location.Kind == "server" || location.Online) return Pill(T("web.admin.downloads.storage.status.online"), "success");
            return Pill(location.Enabled ? T("web.admin.downloads.storage.status.offline") : T("web.admin.downloads.storage.status.disabled"), "muted");
        }

        private static Microsoft.UI.Xaml.Media.Brush SegmentBrush(string kind) => kind switch
        {
            "in_use" => Brush("Chart1Brush"),
            "cached" => Brush("Chart2Brush"),
            "untracked" => Brush("WarningBrush"),
            "other" => Tint("SecondaryTextBrush", 0.35),
            _ => Tint("BorderBrush", 0.7),
        };

        /// <summary>The segmented disk meter with the ceiling marker and, on a disk shared with scratch, the scratch-admission marker.</summary>
        private static UIElement Meter(IReadOnlyList<AdminMeterSegment> segments, int ceiling, bool scratch, bool dimmed)
        {
            var visible = segments.Where(s => s.Bytes > 0).ToArray();
            var label = string.Join(", ", visible.Select(s => T("web.admin.downloads.storage.meter.segment", new { label = s.Label, size = Presentation.FormatBytes(s.Bytes) })));
            var bar = new Grid { Height = 10, ColumnSpacing = 2 };
            for (var index = 0; index < visible.Length; index++)
            {
                bar.ColumnDefinitions.Add(new() { Width = new(Math.Max(visible[index].Fraction, 0.002), GridUnitType.Star), MinWidth = 2 });
                var piece = new Border { Background = SegmentBrush(visible[index].Kind), CornerRadius = new CornerRadius(index == 0 ? 4 : 0, index == visible.Length - 1 ? 4 : 0, index == visible.Length - 1 ? 4 : 0, index == 0 ? 4 : 0) };
                Grid.SetColumn(piece, index); bar.Children.Add(piece);
            }
            if (dimmed) bar.Opacity = 0.6;
            var host = new Grid { Height = 18 };
            host.Children.Add(new Border { Child = bar, VerticalAlignment = VerticalAlignment.Center });
            host.Children.Add(Marker(ceiling, "WarningBrush"));
            if (scratch) host.Children.Add(Marker(Presentation.ScratchAdmissionPercent, "ErrorBrush"));
            AutomationProperties.SetName(host, T("web.admin.downloads.storage.meter.label", new { segments = label }));
            AutomationProperties.SetAutomationId(host, "PreparedFilesMeter");
            return host;
        }

        private static UIElement Marker(int percent, string brush)
        {
            var clamped = Math.Clamp(percent, 0, 100);
            var layer = new Grid { IsHitTestVisible = false };
            layer.ColumnDefinitions.Add(new() { Width = new(Math.Max(clamped, 0.001), GridUnitType.Star) });
            layer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            layer.ColumnDefinitions.Add(new() { Width = new(Math.Max(100 - clamped, 0.001), GridUnitType.Star) });
            var line = new Border { Width = 2, Background = Brush(brush), CornerRadius = new CornerRadius(1) };
            Grid.SetColumn(line, 1); layer.Children.Add(line);
            return layer;
        }

        private static UIElement Key(int ceiling)
        {
            var key = new LogsWrapPanel { Spacing = 16 };
            var first = Inline(6);
            first.Children.Add(new Border { Width = 2, Height = 10, Background = Brush("WarningBrush") });
            first.Children.Add(Text(T("web.admin.downloads.storage.meter.ceiling", new { ceiling }), 12, true));
            var second = Inline(6);
            second.Children.Add(new Border { Width = 2, Height = 10, Background = Brush("ErrorBrush") });
            second.Children.Add(Text(T("web.admin.downloads.storage.meter.scratch", new { percent = Presentation.ScratchAdmissionPercent }), 12, true));
            key.Children.Add(first); key.Children.Add(second);
            return key;
        }
    }

    /// <summary>Confirms and runs the official "delete untracked files" action for exactly this location.</summary>
    private async void ReviewUntracked(AdminDownloadStorageLocation location)
    {
        if (Controller.State.ActionPending || Controller.State.DestructiveBlocked) return;
        MonitorAuthorityLease lease;
        try { lease = Controller.CaptureAction(); }
        catch (OperationCanceledException) { return; }
        using (lease)
        {
            var content = Stack(12);
            content.Children.Add(Text(T("web.admin.downloads.storage.untracked.confirmDescription",
                Presentation.With(Presentation.Count(location.UntrackedFiles), ("size", Presentation.FormatBytes(location.UntrackedBytes))))));
            content.Children.Add(Text(T("web.shell.confirmDialog.irreversible"), 12, true));
            var dialog = NewDialog(T("web.admin.downloads.storage.untracked.confirmTitle", new { name = location.Name }), content,
                T("web.admin.downloads.storage.untracked.confirm"), destructive: true);
            AutomationProperties.SetAutomationId(dialog, "PreparedFilesUntrackedDialog");
            if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
            // The location belongs to the authority the dialog opened under: if that is no longer current nothing is sent.
            if (!Controller.IsActionCurrent(lease)) return;
            await Controller.DeleteUntrackedAsync(location, lease);
        }
    }
}
