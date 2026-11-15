using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Subtitle Files table (WebUI <c>AdminSubtitlesTable</c>): ten columns, row actions always reachable by keyboard.</summary>
public sealed partial class AdminSubtitlesPage
{
    private const string None = "—";

    private FrameworkElement Table(AdminSubtitlesSnapshot state)
    {
        var headers = new[] { "media", "file", "language", "provider", "release", "format", "hearingImpaired", "uploader", "added", "actions" }
            .Select(key => S("web.admin.common.subtitles.table.column." + key)).ToArray();
        var table = new LogsNativeTable(headers, [220, 180, 0, 0, 200, 0, 40, 0, 0, 120]);
        var now = DateTimeOffset.Now;
        foreach (var row in state.Items)
        {
            var cells = Cells(row, state, now);
            var grid = table.Add(cells);
            if (cells[9] is StackPanel actions)
                BindRowActions(grid, actions);
        }
        table.AlignEnd(9);
        var panel = Panel(table, 16, 0); panel.BorderThickness = new(0);
        AutomationProperties.SetAutomationId(panel, "SubtitlesTable");
        return panel;
    }

    private void BindRowActions(Grid row, StackPanel actions)
    {
        var pointerOver = false;
        void Sync()
        {
            var focused = row.XamlRoot == null ? null : Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(row.XamlRoot) as DependencyObject;
            var within = false;
            for (var current = focused; current != null; current = VisualTreeHelper.GetParent(current))
                if (ReferenceEquals(current, row)) { within = true; break; }
            actions.Opacity = _narrow || pointerOver || within ? 1 : 0;
        }
        row.PointerEntered += (_, _) => { pointerOver = true; Sync(); };
        row.PointerExited += (_, _) => { pointerOver = false; Sync(); };
        row.GotFocus += (_, _) => Sync();
        row.LostFocus += (_, _) => EnqueueUi(Sync);
        Sync();
    }

    private UIElement[] Cells(AdminStoredSubtitle row, AdminSubtitlesSnapshot state, DateTimeOffset now)
    {
        // Media: a link to the item when the server named it; episode badge.
        var media = Stack(4); media.MaxWidth = 220;
        var title = string.IsNullOrEmpty(row.MediaTitle) ? row.MediaContentId ?? "" : row.MediaTitle;
        if (!string.IsNullOrEmpty(row.MediaContentId))
        {
            var label = Text(title, 14); label.FontWeight = FontWeights.SemiBold; label.MaxLines = 2; label.TextTrimming = TextTrimming.CharacterEllipsis;
            var link = new HyperlinkButton { Content = label, Padding = new(0), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Left, Foreground = Brush("PrimaryTextBrush") };
            AutomationProperties.SetAutomationId(link, "SubtitlesMedia_" + row.Id); AutomationProperties.SetName(link, title);
            var contentId = row.MediaContentId;
            link.Click += (_, _) => OpenItem(contentId);
            media.Children.Add(link);
        }
        else
        {
            var label = Text(string.IsNullOrEmpty(row.MediaTitle) ? S("web.admin.common.subtitles.unknownMedia") : row.MediaTitle, 14);
            label.FontWeight = FontWeights.SemiBold; label.MaxLines = 2; label.TextTrimming = TextTrimming.CharacterEllipsis;
            media.Children.Add(label);
        }
        if (row.MediaType == "episode")
        {
            var episode = Text(Core.Localization.AppStrings.Uppercase(S("web.admin.common.subtitles.table.episode")), 10, true); episode.CharacterSpacing = 120; episode.LineHeight = 14;
            media.Children.Add(new Border { Child = episode, BorderBrush = Brush("BorderBrush"), BorderThickness = new(1), CornerRadius = new(6), Padding = new(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Left });
        }

        // File: basename, full administrator path as the tooltip.
        var file = Cell(AdminSubtitlesText.Basename(row.FilePath), muted: true, mono: true, max: 180);
        ToolTipService.SetToolTip(file, string.IsNullOrEmpty(row.FilePath) ? None : row.FilePath);

        // Language chip: code, plus the name when there is room.
        var languageRow = Inline(4);
        var code = Text(Core.Localization.AppStrings.Uppercase(row.Language), 12); code.FontWeight = FontWeights.SemiBold; code.CharacterSpacing = 80; languageRow.Children.Add(code);
        if (!_narrow) languageRow.Children.Add(Text(AdminSubtitlesText.LanguageName(row.Language), 12, true));
        var language = Chip(languageRow, Tint("AccentBrush", 0.10), Tint("AccentBrush", 0.25));

        // Provider badge in its provider tone.
        var providerLabel = Text(AdminSubtitlesText.ProviderLabel(row.Provider), 12); providerLabel.FontWeight = FontWeights.Medium;
        var tone = AdminSubtitlesText.ProviderTone(row.Provider);
        var provider = tone == 0
            ? Chip(providerLabel, Tint("MutedBrush", 0.40), Tint("BorderBrush", 0.70))
            : Chip(providerLabel, ToneBrush(tone, 0.12), ToneBrush(tone, 0.32));
        if (tone == 0) providerLabel.Foreground = Brush("SecondaryTextBrush");

        var release = Cell(string.IsNullOrEmpty(row.ReleaseName) ? None : row.ReleaseName, mono: true, max: 200);
        ToolTipService.SetToolTip(release, string.IsNullOrEmpty(row.ReleaseName) ? None : row.ReleaseName);

        var formatText = Text(Core.Localization.AppStrings.Uppercase(AdminSubtitlesText.FormatExtension(row.Format)), 10, true, mono: true); formatText.CharacterSpacing = 140;
        var format = new Border { Child = formatText, Background = Tint("MutedBrush", 0.30), CornerRadius = new(4), Padding = new(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };

        UIElement hearing = new Border();
        if (row.HearingImpaired)
        {
            var amber = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 253, 230, 138));
            var hi = Inline(4); hi.Children.Add(Glyph("ear", 14, amber));
            var hiText = Text(S("web.admin.common.subtitles.table.hearingImpaired"), 12); hiText.FontWeight = FontWeights.Medium; hiText.Foreground = amber; hi.Children.Add(hiText);
            hearing = hi;
        }

        var uploader = Cell(string.IsNullOrEmpty(row.UploaderUsername) ? None : row.UploaderUsername);
        var added = Cell(AdminSubtitlesText.Relative(row.CreatedAt, now, value => DateTimeDisplay.FormatDate(value)), muted: true);
        ToolTipService.SetToolTip(added, DateTimeDisplay.FormatDateTime(row.CreatedAt));

        var actions = Inline(4); actions.HorizontalAlignment = HorizontalAlignment.Right;
        var edit = IconButton("pencil", S("web.admin.common.subtitles.table.edit", ("id", row.Id)), "SubtitlesEdit_" + row.Id);
        edit.Click += (_, _) => BeginEdit(row);
        actions.Children.Add(edit);
        var downloading = state.DownloadingId == row.Id;
        var download = IconButton(downloading ? "loader" : "download", S("web.admin.common.subtitles.table.download", ("id", row.Id)), "SubtitlesDownload_" + row.Id);
        download.IsEnabled = state.DownloadingId == null;
        _downloadButtons.Add(row.Id, download);
        download.Click += (_, _) => _ = DownloadAsync(row);
        actions.Children.Add(download);
        var delete = IconButton("trash-2", S("web.admin.common.subtitles.table.delete", ("id", row.Id)), "SubtitlesDelete_" + row.Id, Brush("ErrorBrush"));
        delete.Click += (_, _) => _ = ConfirmDeleteAsync(row);
        actions.Children.Add(delete);

        return [media, file, language, provider, release, format, hearing, uploader, added, actions];
    }

    private static Border Chip(UIElement child, Brush background, Brush border)
    {
        var chip = new Border
        {
            Child = child, Background = background, BorderBrush = border, BorderThickness = new(1), CornerRadius = new(12), Padding = new(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left,
        };
        chip.SizeChanged += (_, _) => chip.CornerRadius = new(chip.ActualHeight / 2);
        return chip;
    }

    private static SolidColorBrush ToneBrush(uint argb, double alpha) =>
        new(Windows.UI.Color.FromArgb((byte)Math.Round(255 * alpha), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));

    private static Button IconButton(string icon, string name, string id, Brush? foreground = null)
    {
        var button = Button("", id, "GhostButtonStyle");
        button.Content = Glyph(icon, 16, foreground);
        button.Width = 32; button.Height = 32; button.MinHeight = 32; button.Padding = new(0);
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name);
        return button;
    }

    /// <summary>Lucide glyphs this page needs beyond the shared set (ear, loader-circle with a spin).</summary>
    private static UIElement Glyph(string name, double size, Brush? foreground = null)
    {
        var data = name switch
        {
            "ear" => "M6 8.5a6.5 6.5 0 1 1 13 0c0 6-6 6-6 10a3.5 3.5 0 1 1-7 0 M15 8.5a2.5 2.5 0 0 0-5 0v1a2 2 0 1 1 0 4",
            "loader" => "M21 12a9 9 0 1 1-6.219-8.56",
            _ => null,
        };
        if (data == null) return Icon(name, size, foreground);
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='" + data + "' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
        path.Stroke = foreground ?? Brush("PrimaryTextBrush");
        var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(path);
        var box = new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
        if (name == "loader")
        {
            var rotate = new RotateTransform(); box.RenderTransform = rotate; box.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            var spin = new DoubleAnimation { From = 0, To = 360, Duration = new Duration(TimeSpan.FromSeconds(1)), RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(spin, rotate); Storyboard.SetTargetProperty(spin, "Angle");
            var story = new Storyboard(); story.Children.Add(spin);
            box.Loaded += (_, _) => story.Begin(); box.Unloaded += (_, _) => story.Stop();
        }
        return box;
    }
}
