using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using R = SiloPlayer.Core.Services.Admin.AdminSearchSettingsRules;
using X = SiloPlayer.Core.Services.Admin.AdminNetworkAccessText;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminSearchSettingsPage
{
    private readonly StackPanel _railFields = new();
    private readonly StackPanel _statusBody = new();
    private readonly StackPanel _taskLinks = new() { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new(0, 12, 0, 12) };
    private readonly Button _rebuild = U.Action(T("web.admin.settings.search.status.rebuildIndex"), "SearchRebuildIndex");
    private readonly Button _maintenance = U.Action(T("web.admin.settings.search.status.automaticMaintenance"), "SearchAutomaticMaintenance");
    private Border _rail = null!;
    private Button _disclosure = null!;
    // AdvancedSection: persisted choice, a session override that outranks the current reason to force it open, and that reason.
    private bool _persistedOpen, _forced, _advancedOpen, _showCount, _disclosureHover;
    private bool? _override;
    private string _statusKey = "";

    /// <summary>Advanced: the four tuning rows, then the read-only Search status under its own heading (official order).</summary>
    private void BuildAdvanced()
    {
        _disclosure = NewDisclosure();
        _rail = new Border
        {
            Child = _railFields, BorderThickness = new(2, 0, 0, 0), Margin = new(6, 0, 0, 0), Padding = new(15, 0, 0, 0),
            BorderBrush = AdminMonitorUi.Solid(Windows.UI.Color.FromArgb(255, 167, 139, 250), .32), Visibility = Visibility.Collapsed,
        };
        AddText(R.Index, "web.admin.settings.search.meili.index.label", T("web.admin.settings.search.meili.index.description"), rail: true);
        AddText(R.TimeoutMs, "web.admin.settings.search.meili.timeout.label", T("web.admin.settings.search.meili.timeout.description"), number: true, rail: true, unitKey: "web.admin.common.units.milliseconds");
        AddText(R.SyncBatchSize, "web.admin.settings.search.meili.batch.label", T("web.admin.settings.search.meili.batch.description"), number: true, rail: true);
        AddText(R.SemanticRatio, "web.admin.settings.search.meili.ratio.label", T("web.admin.settings.search.meili.ratio.description"), number: true, rail: true);
        var heading = U.Subheading(T("web.admin.settings.search.status.heading"));
        heading.BorderThickness = new(0, 0, 0, 1);
        _railFields.Children.Add(heading);

        // The two index maintenance tasks are shown whether or not the status resolved.
        _maintenance.BorderThickness = new(0); // ghost
        _rebuild.Click += (_, _) => OpenTask(R.RebuildTaskHref);
        _maintenance.Click += (_, _) => OpenTask(R.SyncTaskHref);
        var host = new StackPanel();
        _taskLinks.Children.Add(_rebuild); _taskLinks.Children.Add(_maintenance);
        host.Children.Add(_statusBody); host.Children.Add(_taskLinks);
        _railFields.Children.Add(host);

        var advanced = new StackPanel { MinWidth = 0 };
        advanced.Children.Add(_disclosure); advanced.Children.Add(_rail);
        _group.Add(advanced);
        RenderDisclosure();
    }

    // ------------------------------------------------------------------ advanced disclosure (official AdvancedSection)

    private void ToggleAdvanced()
    {
        var next = !_advancedOpen;
        _override = next; _persistedOpen = next; _advancedOpen = next;
        try { (_preferences ?? DefaultPreferences.Value).Write("search", next); } catch { /* the disclosure still works this session */ }
        RenderDisclosure();
        _controller?.SetAdvancedOpen(next);
        if (_controller is { } controller) RenderStatusParts(controller.State, controller.State.Owner?.IsCurrent == true, controller.State.MeilisearchSelected);
    }

    /// <summary>A dirty field inside outranks an older manual collapse, so a hidden field is never why Save refuses.</summary>
    private void UpdateAdvanced(bool forced, bool showCount)
    {
        if (forced != _forced) { _forced = forced; if (forced) _override = null; }
        var open = _override ?? (_persistedOpen || forced);
        if (open == _advancedOpen && showCount == _showCount && _disclosure.Content != null) return;
        var changed = open != _advancedOpen;
        _advancedOpen = open; _showCount = showCount;
        RenderDisclosure();
        if (changed) _controller?.SetAdvancedOpen(open);
    }

    private Button NewDisclosure()
    {
        var template = (ControlTemplate)XamlReader.Load("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Border x:Name="DisclosureRoot" Background="Transparent" Padding="{TemplateBinding Padding}"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ContentPresenter Content="{TemplateBinding Content}" HorizontalContentAlignment="Stretch" VerticalAlignment="Center"/>
              </Border>
            </ControlTemplate>
            """);
        var button = new Button
        {
            Template = template, MinWidth = 0, MinHeight = 0, Padding = new(0, 12, 0, 12), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, UseSystemFocusVisuals = true,
            FocusVisualPrimaryBrush = AdminMonitorUi.Solid(AdminMonitorUi.ColorOf("FocusVisualPrimaryBrush", Colors.White), .5),
            FocusVisualPrimaryThickness = new(3), FocusVisualSecondaryBrush = new SolidColorBrush(Colors.Transparent),
            CornerRadius = new(6),
        };
        AutomationProperties.SetAutomationId(button, "SearchAdvanced");
        button.Click += (_, _) => ToggleAdvanced();
        button.PointerEntered += (_, _) => { _disclosureHover = true; RenderDisclosure(); };
        button.PointerExited += (_, _) => { _disclosureHover = false; RenderDisclosure(); };
        button.GotFocus += (_, _) => RenderDisclosure();
        return button;
    }

    private void RenderDisclosure()
    {
        var open = _advancedOpen;
        var count = R.AdvancedKeys.Count;
        var title = T("web.settings.advancedSection.title");
        var foreground = open || _disclosureHover ? U.Brush("PrimaryTextBrush") : U.Brush("SecondaryTextBrush");
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        // The chevron turns violet while open, as the official rotated icon does.
        var chevron = AdminMonitorUi.Icon(open ? "chevron-down" : "chevron-right", 14, open ? U.Accent : U.Brush("SecondaryTextBrush"));
        chevron.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(chevron);
        var label = U.Text(title, 13); label.FontWeight = FontWeights.Medium; label.Foreground = foreground; label.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(label, 1); content.Children.Add(label);
        if (_showCount)
        {
            var number = U.Text(AdminText.FormatNumber(count), 12, true); number.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(number, 2); content.Children.Add(number);
        }
        _disclosure.Content = content;
        AutomationProperties.SetName(_disclosure, _showCount
            ? T("web.settings.advancedSection.accessibleLabel", new Dictionary<string, object?> { ["title"] = title, ["count"] = (long)count, ["count_text"] = AdminText.FormatNumber(count) })
            : title);
        AutomationProperties.SetItemStatus(_disclosure, open ? "expanded" : "collapsed");
        _rail.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ status

    private void RenderStatusParts(AdminSearchSettingsState state, bool current, bool meili)
    {
        var status = current ? state.Status : null;

        // One line under the engine row while Meilisearch answers and is not degraded.
        var summary = current && meili && status is { Degraded: false } s ? SummaryText(s) : "";
        U.UpdateLiveText(_summary, summary);
        _summary.Visibility = summary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        RenderDegraded(status, meili);

        foreach (var link in new[] { _rebuild, _maintenance })
        {
            link.IsEnabled = current && state.Loaded && _tasksAvailable;
            ToolTipService.SetToolTip(link, _tasksAvailable ? null : T("configure.native.search.tasksUnavailable"));
            AutomationProperties.SetHelpText(link, _tasksAvailable ? "" : T("configure.native.search.tasksUnavailable"));
        }

        // The panel is built only while its disclosure is open, and only when what it shows changed.
        if (!_advancedOpen || !current) { if (!current) ClearStatusUi(); return; }
        var key = string.Join('|', state.StatusLoading, state.StatusFailure, status == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(status));
        if (key == _statusKey) return;
        _statusKey = key;
        _statusBody.Children.Clear();
        if (state.StatusFailure != null) BuildStatusError(state.StatusFailure.Value);
        else if (status == null) BuildStatusLoading();
        else BuildStatusRows(status);
    }

    private static string SummaryText(AdminSearchStatus status) => T("web.admin.settings.search.status.summary", new Dictionary<string, object?>
    {
        ["count"] = status.Index.DocumentCount,
        ["count_text"] = status.Index.DocumentCount.ToString("N0", CultureInfo.CurrentCulture),
        ["last_sync"] = R.FormatDateTime(status.Index.LastSyncAt) is { Length: > 0 } text ? text : T("web.admin.settings.search.status.summaryNever"),
    });

    private void RenderDegraded(AdminSearchStatus? status, bool meili)
    {
        var show = meili && status is { Degraded: true };
        if (!show) { _degradedRow.Visibility = Visibility.Collapsed; _degradedSignature = ""; _group.RefreshRules(); return; }
        var rebuild = status!.Index.RebuildRequired;
        var reason = string.IsNullOrEmpty(status.DegradedReason) ? T("web.admin.settings.search.degradedFallback") : status.DegradedReason!;
        var signature = string.Join('\u001f', reason, rebuild, _tasksAvailable);
        _degradedRow.Visibility = Visibility.Visible;
        if (signature != _degradedSignature)
        {
            _degradedSignature = signature;
            _degradedText.Inlines.Clear();
            _degradedText.Inlines.Add(new Run { Text = reason });
            if (rebuild)
            {
                _degradedText.Inlines.Add(new Run { Text = " " });
                if (!_tasksAvailable) _degradedText.Inlines.Add(new Run { Text = T("configure.native.search.rebuildInBackgroundPlain") });
                else
                {
                    foreach (var run in X.Rich("web.admin.settings.search.rebuildInBackground"))
                    {
                        if (run.Tag != "link") { _degradedText.Inlines.Add(new Run { Text = run.Text }); continue; }
                        var link = new Hyperlink { Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Amber400) };
                        link.Inlines.Add(new Run { Text = run.Text, FontWeight = FontWeights.Medium });
                        link.Click += (_, _) => OpenTask(R.SyncTaskHref);
                        _degradedText.Inlines.Add(link);
                    }
                }
            }
            AutomationProperties.SetName(_degradedText, _degradedText.Text);
        }
        _group.RefreshRules();
    }

    private void BuildStatusLoading()
    {
        var box = new StackPanel { Spacing = 12, Padding = new(0, 12, 0, 12) };
        AutomationProperties.SetName(box, T("web.admin.settings.fields.loadingSettings"));
        box.Children.Add(U.Skeleton(20, 224)); box.Children.Add(U.Skeleton(20, 288)); box.Children.Add(U.Skeleton(20, 192));
        _statusBody.Children.Add(box);
    }

    private void BuildStatusError(AdminFailureKind failure)
    {
        var reasonKey = failure switch
        {
            AdminFailureKind.Unauthorized => "authenticationRequired",
            AdminFailureKind.Forbidden or AdminFailureKind.DemoRestricted or AdminFailureKind.ProfileVerificationRequired => "forbidden",
            AdminFailureKind.CapabilityUnavailable => "unavailable",
            AdminFailureKind.Transport or AdminFailureKind.Cancelled => "transport",
            AdminFailureKind.Server => "server",
            AdminFailureKind.InvalidResponse => "invalid",
            AdminFailureKind.RateLimited => "rateLimited",
            _ => null,
        };
        var text = reasonKey == null ? T("web.admin.settings.search.status.loadError")
            : T("web.admin.settings.search.status.loadErrorWithReason", new { reason = T("configure.native.search.status.reason." + reasonKey) });
        var box = new StackPanel { Spacing = 12, Padding = new(0, 12, 0, 0) };
        var warning = U.Status(text, "SearchStatusError"); warning.Visibility = Visibility.Visible;
        box.Children.Add(warning);
        var retry = U.Action(T("configure.native.search.status.retry"), "SearchRetryStatus");
        retry.HorizontalAlignment = HorizontalAlignment.Left;
        retry.Click += (_, _) => _controller?.RefreshStatus();
        box.Children.Add(retry);
        _statusBody.Children.Add(box);
    }

    private void BuildStatusRows(AdminSearchStatus status)
    {
        var t = (Func<string, string>)(suffix => T("web.admin.settings.search.status." + suffix));
        var rows = new List<FrameworkElement>();
        if (status.Degraded)
        {
            var warning = U.Status(string.IsNullOrEmpty(status.DegradedReason) ? T("web.admin.settings.search.degradedFallback") : status.DegradedReason!, "SearchStatusDegraded");
            warning.Visibility = Visibility.Visible;
            rows.Add(new Border { Child = warning, Padding = new(0, 12, 0, 12) });
        }
        void Row(string label, string value, string? chip = null) => rows.Add(new StatusRowPanel(label, value, chip));
        Row(t("answering"), status.ActiveProvider == R.Meilisearch ? status.Index.RebuildRequired ? t("meiliKeywordOnly") : "Meilisearch" : t("postgresFullText"), status.ConfiguredProvider);
        Row(t("health"), status.Meilisearch.Healthy ? t("healthy") : status.Meilisearch.CircuitState, status.Meilisearch.Configured ? t("configured") : t("notConfigured"));
        Row(t("activeIndex"), string.IsNullOrEmpty(status.Index.ActiveIndexUid) ? t("notBuilt") : status.Index.ActiveIndexUid,
            status.Index.RebuildRequired ? t("rebuildRequired") : T("web.admin.settings.search.status.schema", new { version = status.Index.SchemaVersion, expected = status.Index.ExpectedSchemaVersion }));
        Row(t("documents"), status.Index.DocumentCount.ToString(CultureInfo.InvariantCulture));
        Row(t("indexedTypes"), status.Meilisearch.IndexTypes.Count == 0 ? t("allTypes") : string.Join(", ", status.Meilisearch.IndexTypes));
        Row(t("compressedVectors"), status.Meilisearch.BinaryQuantized ? t("enabled") : t("disabled"));
        Row(t("semantic"), status.Meilisearch.SemanticEnabled ? t("enabled") : t("disabled"), status.Meilisearch.Embedder);
        Row(t("semanticRatio"), R.FormatSemanticRatio(status.Meilisearch.SemanticRatio));
        Row(t("itemsWithVectors"), status.Index.VectorDocumentCount.ToString(CultureInfo.InvariantCulture));
        if (status.Semantic is { } semantic)
        {
            Row(t("semanticReadiness"), semantic.Ready ? t("ready") : t("notReady"), semantic.Ready ? null : semantic.DisabledReason);
            Row(t("vectorCoverage"), Percent(semantic.CoverageRatio));
            Row(t("coverageUpdated"), R.FormatDateTime(semantic.CoverageUpdatedAt) is { Length: > 0 } updated ? updated : t("never"));
            Row(t("embeddingModel"), semantic.Capability.Ok ? t("ok") : semantic.Capability.Reason ?? t("unavailable"), semantic.Capability.Embedder);
            if (semantic.PerType.Count > 0)
            {
                var block = new StackPanel { Spacing = 8, Padding = new(0, 12, 0, 12) };
                var heading = U.Text(t("coverageByType"), 14); heading.FontWeight = FontWeights.Medium; block.Children.Add(heading);
                foreach (var entry in semantic.PerType)
                {
                    var line = new Grid { ColumnSpacing = 8, Padding = new(0, 6, 0, 6) };
                    line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    var text = U.Text(T("web.admin.settings.search.status.typeCoverage", new { type = entry.Type, vectorized = entry.Vectorized, eligible = entry.Eligible, percent = Percent(entry.CoverageRatio) }), 14, true);
                    text.TextTrimming = TextTrimming.CharacterEllipsis; text.TextWrapping = TextWrapping.NoWrap;
                    line.Children.Add(text);
                    var chip = Badge(entry.Ready ? t("ready") : t("notReady"), filled: entry.Ready); Grid.SetColumn(chip, 1); line.Children.Add(chip);
                    block.Children.Add(line);
                }
                rows.Add(new Border { Child = block });
            }
        }
        Row(t("pendingEvents"), status.Index.PendingEvents.ToString(CultureInfo.InvariantCulture));
        if (status.Index.DeadLetteredEvents > 0) Row(t("droppedEvents"), status.Index.DeadLetteredEvents.ToString(CultureInfo.InvariantCulture), t("staleUntilRebuild"));
        Row(t("lastSync"), R.FormatDateTime(status.Index.LastSyncAt) is { Length: > 0 } sync ? sync : t("never"));
        if (!string.IsNullOrEmpty(status.Meilisearch.LastFallback)) Row(t("lastFallback"), status.Meilisearch.LastFallback!);
        // divide-y: a hairline above every row but the first; the task links below carry the last one.
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index] is StatusRowPanel panel) panel.Divider(index > 0);
            else if (rows[index] is Border border) { border.BorderThickness = new(0, index == 0 ? 0 : 1, 0, 0); border.BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6); }
            _statusBody.Children.Add(rows[index]);
        }
        _taskLinks.BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6); _taskLinks.BorderThickness = new(0, 1, 0, 0);
    }

    private static string Percent(double ratio) => T("web.common.format.percent", new { percent = R.Percent(ratio) });

    /// <summary>Official outline <c>Badge</c> (secondary when filled): 12px semibold text in a small rounded chip.</summary>
    private static Border Badge(string text, bool filled = false)
    {
        var label = U.Text(text, 12); label.FontWeight = FontWeights.SemiBold; label.LineHeight = 16;
        return new Border
        {
            Child = label, CornerRadius = new(6), Padding = new(8, 2, 8, 2), BorderThickness = new(1),
            BorderBrush = filled ? new SolidColorBrush(Colors.Transparent) : AdminMonitorUi.Solid(AdminMonitorUi.Border, .9),
            Background = filled ? U.Brush("SecondaryBrush") : new SolidColorBrush(Colors.Transparent), VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// Official <c>StatusRow</c>: label left, value and optional badge right-aligned; stacked with the value under the label when
    /// the row is too narrow. 12px vertical padding.
    /// </summary>
    private sealed class StatusRowPanel : Panel
    {
        private readonly TextBlock _label;
        private readonly Grid _value = new() { ColumnSpacing = 8 };
        private bool _wrap;
        internal StatusRowPanel(string label, string value, string? badge)
        {
            _label = U.Text(label, 14); _label.FontWeight = FontWeights.Medium;
            var text = U.Text(value, 14, true); text.TextAlignment = TextAlignment.Right; text.HorizontalAlignment = HorizontalAlignment.Right;
            _value.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            _value.Children.Add(text);
            if (!string.IsNullOrEmpty(badge))
            {
                _value.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var chip = Badge(badge); Grid.SetColumn(chip, 1); _value.Children.Add(chip);
            }
            Children.Add(_label); Children.Add(_value);
            AutomationProperties.SetName(this, string.IsNullOrEmpty(badge) ? $"{label}: {value}" : $"{label}: {value} {badge}");
        }
        private Border? _line;
        internal void Divider(bool visible)
        {
            if (!visible) return;
            _line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6) };
            Children.Add(_line);
        }
        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
        {
            var width = double.IsInfinity(available.Width) ? 4000 : available.Width;
            var inner = Math.Max(0, width);
            _label.Measure(new(inner, double.PositiveInfinity));
            _value.Measure(new(inner, double.PositiveInfinity));
            _wrap = _label.DesiredSize.Width + 16 + _value.DesiredSize.Width > inner + SiloPlayer.Controls.LayoutTolerance.Fit;
            if (_wrap) _value.Measure(new(inner, double.PositiveInfinity));
            // A value that fits only by the tolerance keeps the width it asked for: measuring it a fraction narrower would trim it.
            else if (_value.DesiredSize.Width <= Math.Max(0, inner - _label.DesiredSize.Width - 16)) _value.Measure(new(Math.Max(0, inner - _label.DesiredSize.Width - 16), double.PositiveInfinity));
            _line?.Measure(new(inner, 1));
            var height = _wrap ? _label.DesiredSize.Height + 8 + _value.DesiredSize.Height : Math.Max(_label.DesiredSize.Height, _value.DesiredSize.Height);
            return new(width, height + 24);
        }
        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
        {
            var inner = final.Width;
            _line?.Arrange(new(0, 0, inner, 1));
            if (_wrap)
            {
                _label.Arrange(new(0, 12, inner, _label.DesiredSize.Height));
                _value.Arrange(new(0, 12 + _label.DesiredSize.Height + 8, inner, _value.DesiredSize.Height));
            }
            else
            {
                var line = Math.Max(_label.DesiredSize.Height, _value.DesiredSize.Height);
                _label.Arrange(new(0, 12 + (line - _label.DesiredSize.Height) / 2, _label.DesiredSize.Width, _label.DesiredSize.Height));
                var valueWidth = Math.Max(Math.Max(0, inner - _label.DesiredSize.Width - 16), _value.DesiredSize.Width);
                _value.Arrange(new(inner - valueWidth, 12 + (line - _value.DesiredSize.Height) / 2, valueWidth, _value.DesiredSize.Height));
            }
            return final;
        }
    }

    private void ClearStatusUi()
    {
        _statusKey = ""; _degradedSignature = "";
        _statusBody.Children.Clear();
        _degradedText.Inlines.Clear(); _degradedRow.Visibility = Visibility.Collapsed;
    }

    private void OpenTask(string href)
    {
        if (!_tasksAvailable || _controller?.State is not { Loaded: true, Owner: { IsCurrent: true } }) return;
        AdminShellPage.TryOpenHref(href);
    }
}
