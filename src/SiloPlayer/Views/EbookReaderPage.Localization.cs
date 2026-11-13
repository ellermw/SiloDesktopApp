using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

/// <summary>
/// Interface-language presentation for the reader. Static chrome uses
/// <see cref="Localized"/> keys in XAML; text that depends on reader state is
/// rebuilt here from retained state on every locale change. Book titles,
/// chapter/TOC labels, annotation text, search snippets, file names and the
/// book's own direction are data and are never translated or mirrored.
/// </summary>
public sealed partial class EbookReaderPage
{
    // Same-key payload for the authored renderer (Assets/Reader/reader.mjs).
    // Catalog keys where the meaning matches; desktop ids carry English for
    // renderer phrases that have no upstream key.
    internal const string RendererOpenFailedKey = "web.reader.error.openFailed";
    internal const string RendererLocationUnresolvedId = "desktop.reader.locationUnresolved";
    internal const string RendererLocationUnavailableId = "desktop.reader.locationUnavailable";
    private const string LocationUnavailableEnglish = "This reading location is unavailable in this file.";
    private const string LocationUnresolvedEnglish = "This saved reading location could not be resolved. Choose a chapter or move the progress slider to continue.";

    private static readonly (string Tag, string Key, string English)[] ThemeChoices =
        [("light", "web.reader.theme.light", "Light"), ("sepia", "web.reader.theme.sepia", "Sepia"), ("dark", "web.reader.theme.dark", "Dark")];
    private static readonly (string Tag, string Key, string English)[] FontChoices =
    [
        ("inherit", "web.reader.font.bookDefault", "Book default"),
        ("ui-serif, Georgia, Cambria, 'Times New Roman', Times, serif", "web.reader.font.systemSerif", "System serif"),
        ("ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif", "web.reader.font.systemSans", "System sans"),
        ("ui-monospace, Consolas, 'Courier New', monospace", "web.reader.font.monospace", "Monospace"),
    ];
    private static readonly (string Tag, string Key, string English)[] WritingModeChoices =
        [("auto", "web.reader.writingMode.auto", "Auto"), ("horizontal-tb", "web.reader.writingMode.horizontal", "Horizontal"), ("vertical-rl", "web.reader.writingMode.vertical", "Vertical")];
    private static readonly (string Tag, string Key, string English)[] SpreadChoices =
        [("auto", "web.reader.spread.auto", "Auto"), ("none", "web.reader.spread.single", "Single page")];
    private static readonly (string Tag, string Key, string English)[] FlowChoices =
        [("paginated", "web.reader.flow.paginated", "Paginated"), ("scrolled", "web.reader.flow.scrolled", "Scrolled")];

    private Func<string> _loadingText = () => T("web.reader.loading", "Loading reader...");
    private Func<string>? _syncStatusText;
    private Func<string>? _searchStatusText;

    private static string T(string key, string fallback, IReadOnlyDictionary<string, object?>? arguments = null)
        => AppStrings.Get(key, arguments, fallback);

    private void InitializeLocalization() => AppLocalization.Register(this, RefreshLocalizedText);

    private void RefreshLocalizedText()
    {
        LoadingText.Text = _loadingText();
        if (_syncStatusText != null) ReaderSyncStatus.Text = _syncStatusText();
        if (_searchStatusText != null) SearchStatusText.Text = _searchStatusText();
        LocalizeChoices();
        UpdatePanelToggleLabel();
        UpdateRulerOverlay();
        ApplyNextChapterLabels();
        RefreshFileSelectorLabels();
        RenderAnnotations();
        UpdateProgressControls();
        _ = PushRendererLocaleAsync();
    }

    private void SetLoading(Func<string> text)
    {
        _loadingText = text;
        LoadingText.Text = text();
    }

    private void SetSyncStatus(Func<string>? text)
    {
        _syncStatusText = text;
        ReaderSyncStatus.Text = text?.Invoke() ?? "";
        ReaderSyncStatus.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetSearchStatus(Func<string> text)
    {
        _searchStatusText = text;
        SearchStatusText.Text = text();
    }

    private void ShowFailure(Exception exception)
        => ShowFailure(() => exception is ApiException api ? ApiProblemText.Display(api) : T(RendererOpenFailedKey, "Unable to open ebook"));

    private void ShowFailure(Func<string> message)
    {
        SetLoading(message);
        if (LoadingLayer.Children.FirstOrDefault() is StackPanel panel && panel.Children.FirstOrDefault() is ProgressRing ring) ring.IsActive = false;
    }

    private static Func<string> NotFound => () => T("web.reader.page.notFound", "Ebook not found.");

    private void LocalizeChoices()
    {
        var suppressed = _suppressControls;
        _suppressControls = true;
        try
        {
            LocalizeChoices(ThemeCombo, ThemeChoices);
            LocalizeChoices(FontCombo, FontChoices);
            LocalizeChoices(WritingModeCombo, WritingModeChoices);
            LocalizeChoices(SpreadCombo, SpreadChoices);
            LocalizeChoices(FlowCombo, FlowChoices);
            if (VoiceCombo.Items.FirstOrDefault() is ComboBoxItem { Tag: "" } voiceDefault)
            {
                var selected = VoiceCombo.SelectedIndex;
                voiceDefault.Content = T("web.reader.settings.voiceDefault", "Default");
                if (selected == 0) { VoiceCombo.SelectedIndex = -1; VoiceCombo.SelectedIndex = 0; }
            }
        }
        finally { _suppressControls = suppressed; }
    }

    private static void LocalizeChoices(ComboBox combo, (string Tag, string Key, string English)[] choices)
    {
        var selected = combo.SelectedIndex;
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
            foreach (var choice in choices)
                if (Equals(item.Tag as string, choice.Tag)) item.Content = T(choice.Key, choice.English);
        // The closed ComboBox shows a copy of the selected content; reselect
        // (with controls suppressed) so it shows the new language.
        if (selected < 0) return;
        combo.SelectedIndex = -1;
        combo.SelectedIndex = selected;
    }

    private void UpdatePanelToggleLabel()
    {
        var label = _panelOpen ? T("web.reader.toolbar.closePanel", "Close reader panel") : T("web.reader.toolbar.openPanel", "Open reader panel");
        ToolTipService.SetToolTip(PanelToggleButton, label);
        AutomationProperties.SetName(PanelToggleButton, label);
    }

    private void ApplyNextChapterLabels()
    {
        if (_nextMangaChapter == null) return;
        var label = MangaChapterLabel(_nextMangaChapter);
        var arguments = new Dictionary<string, object?> { ["chapter"] = label };
        NextChapterLabel.Text = label;
        EndOfBookNextLabel.Text = T("web.reader.endOfBook.nextChapter", "Next: " + label, arguments);
        var title = T("web.reader.toolbar.nextChapterTitle", "Next: " + label, arguments);
        ToolTipService.SetToolTip(NextChapterButton, title);
        AutomationProperties.SetName(NextChapterButton, title);
        AutomationProperties.SetName(EndOfBookNextButton, title);
    }

    private static string MangaChapterLabel(MangaChapter chapter)
        => !string.IsNullOrWhiteSpace(chapter.Title) ? chapter.Title
            : chapter.ChapterIndex.HasValue
                ? T("web.common.format.manga.chapter", "Chapter " + chapter.ChapterIndex.Value.ToString("0.##", AppStrings.FormatCulture),
                    new Dictionary<string, object?> { ["number"] = chapter.ChapterIndex.Value.ToString("0.##", AppStrings.FormatCulture) })
                : T("web.common.format.manga.chapterFallback", "Chapter");

    private static string FileOptionLabel(FileVersion version)
    {
        var format = FormatOf(version).ToUpperInvariant();
        var name = Path.GetFileName(version.FileName ?? version.FilePath ?? "");
        if (string.IsNullOrWhiteSpace(name))
            name = T("web.reader.fileOption.unnamed", "File " + version.FileId,
                new Dictionary<string, object?> { ["id"] = version.FileId.ToString(CultureInfo.InvariantCulture) });
        return string.IsNullOrWhiteSpace(format) ? name : $"{format} · {name}";
    }

    private void RefreshFileSelectorLabels()
    {
        var suppressed = _suppressControls;
        _suppressControls = true;
        try
        {
            var selected = FileSelector.SelectedIndex;
            foreach (var item in FileSelector.Items.OfType<ComboBoxItem>())
                if (item.Tag is int fileId && _readerFiles.FirstOrDefault(version => version.FileId == fileId) is { } version)
                    item.Content = FileOptionLabel(version);
            if (selected >= 0) { FileSelector.SelectedIndex = -1; FileSelector.SelectedIndex = selected; }
        }
        finally { _suppressControls = suppressed; }
    }

    private void RenderAnnotations()
    {
        AnnotationsList.Children.Clear();
        foreach (var annotation in _annotations)
            AnnotationsList.Children.Add(CreateAnnotationRow(annotation));
        EmptyAnnotationsText.Visibility = _annotations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string AnnotationKindLabel(EbookReaderAnnotation annotation)
        => T("web.reader.notes.kind", annotation.Kind ?? "", new Dictionary<string, object?> { ["kind"] = annotation.Kind ?? "" });

    private string ProgressPercentText() => OverallProgress.ToString("P0", AppStrings.FormatCulture);

    private string PageDetailText(string percent)
        => _book?.Format == "pdf" && _rendererPageCount > 0
            ? T("web.requests.toAdd.pageOf", $"Page {_chapterIndex + 1} of {_rendererPageCount}",
                new Dictionary<string, object?> { ["page"] = _chapterIndex + 1, ["total"] = _rendererPageCount }) + " · " + percent
            : percent;

    private static string MatchCountText(int count)
        => count == 0 ? T("web.search.noMatches", "No matches")
            : T("web.settings.search.matchCount", count == 1 ? "1 match" : $"{count} matches",
                new Dictionary<string, object?> { ["count"] = count, ["count_text"] = count.ToString("N0", AppStrings.FormatCulture) });

    private static object RendererLocale() => new
    {
        language = AppStrings.Language,
        strings = new Dictionary<string, string>
        {
            [RendererOpenFailedKey] = T(RendererOpenFailedKey, "Unable to open ebook"),
            [RendererLocationUnresolvedId] = LocationUnresolvedEnglish,
            [RendererLocationUnavailableId] = LocationUnavailableEnglish,
        },
    };

    /// <summary>Sends the current interface language to the authored renderer. The shell's language never changes the book's direction.</summary>
    private async Task PushRendererLocaleAsync()
    {
        if (_leaving || !UsesSharedRenderer || ReaderWebView.CoreWebView2 == null) return;
        try { await ReaderWebView.ExecuteScriptAsync($"window.siloReader?.locale({JsonSerializer.Serialize(RendererLocale())})"); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException) { }
    }
}
