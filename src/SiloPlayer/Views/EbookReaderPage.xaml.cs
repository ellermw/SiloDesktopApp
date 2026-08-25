using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.System.Display;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class EbookReaderPage : Page
{
    private readonly EbooksApi _ebooksApi = App.Services.GetRequiredService<EbooksApi>();
    private readonly CatalogApi _catalogApi = App.Services.GetRequiredService<CatalogApi>();
    private readonly CancellationTokenSource _lifetime = new();
    private ExtractedEbook? _book;
    private MediaItemDetail? _item;
    private FileVersion? _selectedVersion;
    private List<FileVersion> _readerFiles = [];
    private byte[]? _sourceBytes;
    private MangaChapter? _nextMangaChapter;
    private string _contentId = "";
    private int _fileId;
    private int _chapterIndex;
    private double _chapterFraction;
    private bool _initialized;
    private bool _suppressControls;
    private bool _savePending;
    private string _activePanel = "contents";
    private bool _panelOpen = true;
    private bool _rulerDragging;
    private DisplayRequest? _displayRequest;
    private bool _displayRequested;
    private string? _pendingHighlightText;
    private List<EbookReaderAnnotation> _annotations = [];
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    public EbookReaderPage()
    {
        InitializeComponent();
        FontSizeSlider.Value = 112;
        FontWeightSlider.Value = 400;
        LineHeightSlider.Value = 1.65;
        MarginSlider.Value = 24;
        MaxWidthSlider.Value = 74;
        BrightnessSlider.Value = 100;
        RulerPositionSlider.Value = 50;
        ThemeCombo.SelectedIndex = 0;
        FontCombo.SelectedIndex = 0;
        WritingModeCombo.SelectedIndex = 0;
        SpreadCombo.SelectedIndex = 0;
        FlowCombo.SelectedIndex = 0;
        OpenPanel("contents");
        ReadingSurface.SizeChanged += (_, _) => UpdateRulerOverlay();
        _progressTimer.Tick += ProgressTimer_Tick;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _contentId = e.Parameter switch
        {
            EbookReaderNavigation n => n.ContentId,
            string id => id,
            _ => ""
        };
        var requestedFileId = e.Parameter is EbookReaderNavigation nav ? nav.FileId : null;
        if (string.IsNullOrWhiteSpace(_contentId))
        {
            ShowFailure("Ebook not found.");
            return;
        }

        try
        {
            _item = await _catalogApi.GetItemDetailAsync(_contentId, _lifetime.Token);
            if (!string.Equals(_item.Type, "ebook", StringComparison.OrdinalIgnoreCase))
            {
                ShowFailure("Ebook not found.");
                return;
            }
            TitleText.Text = _item.Title;
            if (App.MainWindowInstance is MainWindow window) window.SetDynamicTitle(_item.Title);
            _readerFiles = _item.Versions.Where(EbookReaderFormat.IsSupported).ToList();
            BuildFileSelector();
            await LoadMangaNavigationAsync();
            var version = ChooseVersion(_readerFiles, requestedFileId);
            if (version == null)
            {
                ShowFailure("Unsupported ebook format.");
                return;
            }
            await ReaderWebView.EnsureCoreWebView2Async();
            ReaderWebView.CoreWebView2.WebMessageReceived += ReaderWebView_WebMessageReceived;
            await LoadPreferencesAsync();
            await LoadAnnotationsAsync();
            await OpenVersionAsync(version, restoreProgress: true);
            _progressTimer.Start();
            Focus(FocusState.Programmatic);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowFailure(_item == null ? "Ebook not found." : ex.Message);
        }
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        _progressTimer.Stop();
        if (_initialized)
        {
            await ReadScrollFractionAsync();
            await SaveProgressAsync();
        }
        _lifetime.Cancel();
        ReleaseDisplayRequest();
        if (ReaderWebView.CoreWebView2 != null)
            ReaderWebView.CoreWebView2.WebMessageReceived -= ReaderWebView_WebMessageReceived;
        ReaderWebView.Close();
        base.OnNavigatedFrom(e);
    }

    private static FileVersion? ChooseVersion(IEnumerable<FileVersion> versions, int? requested)
    {
        var list = versions.ToList();
        if (requested.HasValue)
        {
            var requestedVersion = list.FirstOrDefault(v => v.FileId == requested.Value);
            if (requestedVersion != null && EbookReaderFormat.IsSupported(requestedVersion))
                return requestedVersion;
        }
        return list.FirstOrDefault(v => FormatOf(v).Equals("epub", StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(EbookReaderFormat.IsSupported);
    }

    private static string FormatOf(FileVersion version) => EbookReaderFormat.Detect(version);

    private void BuildFileSelector()
    {
        _suppressControls = true;
        FileSelector.Items.Clear();
        foreach (var version in _readerFiles)
        {
            var format = FormatOf(version).ToUpperInvariant();
            var name = Path.GetFileName(version.FileName ?? version.FilePath ?? "");
            if (string.IsNullOrWhiteSpace(name))
                name = $"File {version.FileId}";
            FileSelector.Items.Add(new ComboBoxItem
            {
                Content = string.IsNullOrWhiteSpace(format) ? name : $"{format} · {name}",
                Tag = version.FileId,
            });
        }
        FileSelector.Visibility = _readerFiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _suppressControls = false;
    }

    private async Task OpenVersionAsync(FileVersion version, bool restoreProgress)
    {
        if (_initialized)
        {
            await ReadScrollFractionAsync();
            await SaveProgressAsync();
        }

        _initialized = false;
        _selectedVersion = version;
        _fileId = version.FileId;
        FormatText.Text = FormatOf(version).ToUpperInvariant();
        LoadingLayer.Visibility = Visibility.Visible;
        if (LoadingLayer.Children.FirstOrDefault() is StackPanel panel && panel.Children.FirstOrDefault() is ProgressRing ring)
            ring.IsActive = true;
        LoadingText.Text = "Downloading book…";
        _sourceBytes = await _ebooksApi.ReadFileAsync(_contentId, _fileId, _lifetime.Token);
        LoadingText.Text = "Preparing reader…";
        var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "reader-cache", SafeName(_contentId), _fileId.ToString(CultureInfo.InvariantCulture));
        _book = await Task.Run(() => EbookPackageExtractor.Extract(_sourceBytes, cacheRoot, FormatOf(version)), _lifetime.Token);
        ReaderWebView.CoreWebView2.SetVirtualHostNameToFolderMapping("silo-reader.local", _book.RootDirectory, CoreWebView2HostResourceAccessKind.Allow);
        BuildContents();
        UpdateFormatSpecificChrome();

        var saved = restoreProgress ? await TryGetProgressAsync() : null;
        _chapterIndex = saved == null || saved.FileId != _fileId ? 0 : ChapterFromProgress(saved.Progress);
        _chapterFraction = saved == null || saved.FileId != _fileId ? 0 : FractionWithinChapter(saved.Progress);
        _suppressControls = true;
        FileSelector.SelectedItem = FileSelector.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, _fileId));
        _suppressControls = false;
        _initialized = true;
        NavigateToChapter(_chapterIndex, _chapterFraction);
    }

    private async Task LoadMangaNavigationAsync()
    {
        _nextMangaChapter = null;
        NextChapterButton.Visibility = Visibility.Collapsed;
        EndOfBookNextButton.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(_item?.SeriesId)) return;
        try
        {
            var series = await _catalogApi.GetItemDetailAsync(_item.SeriesId, _lifetime.Token);
            var chapters = series.Manga?.Chapters
                .OrderBy(chapter => MangaVolumeSort(chapter.Volume))
                .ThenBy(chapter => chapter.ChapterIndex ?? double.MaxValue)
                .ThenBy(chapter => chapter.Title, StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
            var index = chapters.FindIndex(chapter => chapter.ContentId == _contentId);
            if (index < 0 || index + 1 >= chapters.Count) return;
            _nextMangaChapter = chapters[index + 1];
            var label = MangaChapterLabel(_nextMangaChapter);
            NextChapterLabel.Text = label;
            EndOfBookNextLabel.Text = $"Next: {label}";
            AutomationProperties.SetName(NextChapterButton, $"Next chapter: {label}");
            AutomationProperties.SetName(EndOfBookNextButton, $"Next chapter: {label}");
            NextChapterButton.Visibility = Visibility.Visible;
        }
        catch (ApiException) { }
    }

    private void UpdateFormatSpecificChrome()
    {
        var comic = IsComicBook;
        SearchTab.IsEnabled = IsHtmlBook;
        SearchBookVisibility(IsHtmlBook);
        ReadingProfilesSection.Visibility = comic ? Visibility.Collapsed : Visibility.Visible;
        ReadAloudSection.Visibility = comic ? Visibility.Collapsed : Visibility.Visible;
        FontSection.Visibility = comic ? Visibility.Collapsed : Visibility.Visible;
        ProseLayoutSection.Visibility = comic ? Visibility.Collapsed : Visibility.Visible;
        WidthSection.Visibility = comic || SelectedTag(FlowCombo, "paginated") == "scrolled" ? Visibility.Collapsed : Visibility.Visible;
        HyphenationCheck.Visibility = comic ? Visibility.Collapsed : Visibility.Visible;
        ReadingRulerCheck.Visibility = comic ? Visibility.Collapsed : Visibility.Visible;
        ReadingRulerToolbarButton.Visibility = comic ? Visibility.Collapsed : Visibility.Visible;
        RulerPositionSection.Visibility = !comic && ReadingRulerCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WritingModeSection.Visibility = comic ? Visibility.Collapsed : Visibility.Visible;
        SpreadSection.Visibility = SelectedTag(FlowCombo, "paginated") == "scrolled" ? Visibility.Collapsed : Visibility.Visible;
        if (comic && _panelOpen) SetPanelOpen(false);
        UpdateRulerOverlay();
    }

    private void SearchBookVisibility(bool enabled)
    {
        if (!enabled && _activePanel == "search") OpenPanel("contents");
    }

    private async void FileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressControls || !_initialized || FileSelector.SelectedItem is not ComboBoxItem item || item.Tag is not int fileId || fileId == _fileId)
            return;
        var version = _readerFiles.FirstOrDefault(candidate => candidate.FileId == fileId);
        if (version == null) return;
        try { await OpenVersionAsync(version, restoreProgress: true); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowFailure(ex.Message); }
    }

    private static string MangaChapterLabel(MangaChapter chapter)
        => !string.IsNullOrWhiteSpace(chapter.Title) ? chapter.Title
            : chapter.ChapterIndex.HasValue ? $"Chapter {chapter.ChapterIndex:0.##}" : "Chapter";

    private static double MangaVolumeSort(string? volume)
        => double.TryParse(volume?.TrimStart('v', 'V'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value : double.MaxValue;

    private void BuildContents()
    {
        ContentsList.Items.Clear();
        if (_book == null) return;
        for (var i = 0; i < _book.Chapters.Count; i++)
            ContentsList.Items.Add(new ListViewItem { Content = _book.Chapters[i].Title, Tag = i });
    }

    private void NavigateToChapter(int index, double fraction = 0)
    {
        if (_book == null || _book.Chapters.Count == 0) return;
        _chapterIndex = Math.Clamp(index, 0, _book.Chapters.Count - 1);
        _chapterFraction = Math.Clamp(fraction, 0, 1);
        var chapter = _book.Chapters[_chapterIndex];
        ContentsList.SelectedIndex = _chapterIndex;
        PreviousButton.IsEnabled = true;
        NextButton.IsEnabled = true;
        var path = string.Join('/', chapter.RelativePath.Split('/').Select(Uri.EscapeDataString));
        ReaderWebView.Source = new Uri($"https://silo-reader.local/{path}");
        UpdateProgressControls();
    }

    private async void ReaderWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingLayer.Visibility = Visibility.Collapsed;
        if (!args.IsSuccess || _book == null) return;
        if (IsHtmlBook)
        {
            await ApplyAppearanceAsync();
            await ReaderWebView.ExecuteScriptAsync("(()=>{if(window.__siloSelectionBridge)return;window.__siloSelectionBridge=true;document.addEventListener('selectionchange',()=>{const text=(getSelection()?.toString()||'').trim();chrome.webview.postMessage(JSON.stringify({type:'selection',text}));});speechSynthesis.addEventListener?.('voiceschanged',()=>chrome.webview.postMessage(JSON.stringify({type:'voices'})));})()") ;
            await PopulateVoicesAsync();
            if (_chapterFraction > 0)
            {
                var axis = SelectedTag(FlowCombo, "paginated") == "paginated" ? "x" : "y";
                var script = axis == "x"
                    ? $"window.scrollTo(Math.max(0,(document.documentElement.scrollWidth-window.innerWidth)*{_chapterFraction.ToString(CultureInfo.InvariantCulture)}),0);"
                    : $"window.scrollTo(0,Math.max(0,(document.documentElement.scrollHeight-window.innerHeight)*{_chapterFraction.ToString(CultureInfo.InvariantCulture)}));";
                await ReaderWebView.ExecuteScriptAsync(script);
            }
            foreach (var annotation in _annotations.Where(a => a.Kind == "highlight" && AnnotationChapter(a) == _chapterIndex && !string.IsNullOrWhiteSpace(a.SelectedText)))
                await HighlightTextAsync(annotation.SelectedText);
            if (!string.IsNullOrWhiteSpace(_pendingHighlightText))
            {
                await HighlightTextAsync(_pendingHighlightText);
                _pendingHighlightText = null;
            }
        }
    }

    private async void ReaderWebView_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var message = JsonDocument.Parse(args.TryGetWebMessageAsString());
            var type = message.RootElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (type == "selection")
            {
                var text = message.RootElement.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
                HighlightButton.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
            }
            else if (type == "voices")
            {
                await PopulateVoicesAsync();
            }
        }
        catch (JsonException) { }
    }

    private async Task PopulateVoicesAsync()
    {
        if (ReaderWebView.CoreWebView2 == null || !IsHtmlBook) return;
        try
        {
            var selected = SelectedTag(VoiceCombo, "");
            var json = await ReaderWebView.ExecuteScriptAsync("JSON.stringify(speechSynthesis.getVoices().map(v=>({name:v.name,uri:v.voiceURI})))");
            var serialized = JsonSerializer.Deserialize<string>(json);
            var voices = string.IsNullOrWhiteSpace(serialized)
                ? []
                : JsonSerializer.Deserialize<List<ReaderVoice>>(serialized, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            _suppressControls = true;
            VoiceCombo.Items.Clear();
            VoiceCombo.Items.Add(new ComboBoxItem { Content = "Default", Tag = "" });
            foreach (var voice in voices)
                VoiceCombo.Items.Add(new ComboBoxItem { Content = voice.Name, Tag = voice.Uri });
            SetComboByTag(VoiceCombo, selected);
            _suppressControls = false;
        }
        catch (JsonException) { }
    }

    private async Task ApplyAppearanceAsync()
    {
        if (ReaderWebView.CoreWebView2 == null || !IsHtmlBook) return;
        var theme = SelectedTag(ThemeCombo, "light");
        var font = SelectedTag(FontCombo, "inherit");
        var colors = theme switch
        {
            "light" => ("#faf8f2", "#24211d"),
            "sepia" => ("#f4ecd8", "#433b2d"),
            _ => ("#111318", "#eceef3")
        };
        var flow = SelectedTag(FlowCombo, "paginated");
        var writingMode = SelectedTag(WritingModeCombo, "auto");
        var direction = RtlCheck.IsChecked == true ? "rtl" : "inherit";
        var hyphens = HyphenationCheck.IsChecked == true ? "auto" : "none";
        var brightness = BrightnessSlider.Value.ToString(CultureInfo.InvariantCulture);
        var maxWidth = MaxWidthSlider.Value.ToString(CultureInfo.InvariantCulture);
        var spread = SelectedTag(SpreadCombo, "auto");
        var columnRule = spread == "none" ? "column-count:1!important;" : $"column-width:{maxWidth}ch!important;";
        var flowCss = flow == "paginated"
            ? "height:calc(100vh - 64px)!important;max-width:none!important;" + columnRule + "column-gap:7vw!important;column-fill:auto!important;overflow-x:auto!important;overflow-y:hidden!important;"
            : "max-width:" + maxWidth + "ch!important;overflow:visible!important;";
        var css = $"html{{background:{colors.Item1}!important;color:{colors.Item2}!important;scroll-behavior:smooth;}}body{{{flowCss}margin:0 auto!important;padding:32px {MarginSlider.Value.ToString(CultureInfo.InvariantCulture)}px 80px!important;font-family:{font}!important;font-size:{FontSizeSlider.Value.ToString(CultureInfo.InvariantCulture)}%!important;font-weight:{FontWeightSlider.Value.ToString(CultureInfo.InvariantCulture)}!important;line-height:{LineHeightSlider.Value.ToString(CultureInfo.InvariantCulture)}!important;color:{colors.Item2}!important;background:{colors.Item1}!important;filter:brightness({brightness}%)!important;hyphens:{hyphens}!important;direction:{direction}!important;writing-mode:{(writingMode == "auto" ? "inherit" : writingMode)}!important;}}img,svg{{max-width:100%!important;height:auto!important;}}a{{color:#75a7ff!important;}}mark.silo-highlight{{background:#facc15;color:inherit;border-radius:2px;}}";
        var js = $"(()=>{{let s=document.getElementById('silo-reader-style');if(!s){{s=document.createElement('style');s.id='silo-reader-style';document.head.appendChild(s);}}s.textContent={JsonSerializer.Serialize(css)};document.documentElement.style.colorScheme={JsonSerializer.Serialize(theme == "light" || theme == "sepia" ? "light" : "dark")};}})();";
        await ReaderWebView.ExecuteScriptAsync(js);
        UpdateRulerOverlay();
    }

    private async Task ReadScrollFractionAsync()
    {
        if (ReaderWebView.CoreWebView2 == null || !IsHtmlBook) return;
        try
        {
            var script = SelectedTag(FlowCombo, "paginated") == "paginated"
                ? "Math.max(0,Math.min(1,window.scrollX/Math.max(1,document.documentElement.scrollWidth-window.innerWidth)))"
                : "Math.max(0,Math.min(1,window.scrollY/Math.max(1,document.documentElement.scrollHeight-window.innerHeight)))";
            var json = await ReaderWebView.ExecuteScriptAsync(script);
            if (double.TryParse(json, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) _chapterFraction = value;
        }
        catch { }
    }

    private double OverallProgress => _book == null || _book.Chapters.Count == 0 ? 0 : Math.Clamp((_chapterIndex + _chapterFraction) / _book.Chapters.Count, 0, 1);

    private void UpdateProgressControls()
    {
        _suppressControls = true;
        ProgressSlider.Value = OverallProgress;
        ProgressText.Text = $"{OverallProgress:P0}";
        HeaderProgressText.Text = ProgressText.Text;
        EndOfBookNextButton.Visibility = _nextMangaChapter != null && OverallProgress >= 0.995
            ? Visibility.Visible : Visibility.Collapsed;
        _suppressControls = false;
    }

    private int ChapterFromProgress(double progress)
    {
        if (_book == null || _book.Chapters.Count == 0) return 0;
        return Math.Clamp(
            (int)Math.Floor(Math.Clamp(progress, 0, 0.999999) * _book.Chapters.Count),
            0,
            _book.Chapters.Count - 1);
    }

    private double FractionWithinChapter(double progress)
    {
        if (_book == null || _book.Chapters.Count == 0) return 0;
        return Math.Clamp(progress * _book.Chapters.Count - ChapterFromProgress(progress), 0, 1);
    }

    private async void ProgressTimer_Tick(object? sender, object e)
    {
        if (_savePending) return;
        _savePending = true;
        try
        {
            await ReadScrollFractionAsync();
            UpdateProgressControls();
            await SaveProgressAsync();
        }
        finally { _savePending = false; }
    }

    private async Task SaveProgressAsync()
    {
        try
        {
            await _ebooksApi.SaveProgressAsync(_contentId, new EbookReaderProgressInput
            {
                FileId = _fileId,
                Location = $"chapter:{_chapterIndex};fraction:{_chapterFraction.ToString("F6", CultureInfo.InvariantCulture)}",
                Progress = OverallProgress
            }, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Navigating away cancels the page lifetime; progress saving is best effort.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ebook progress save failed: {ex}");
        }
    }

    private async Task<EbookReaderProgress?> TryGetProgressAsync()
    {
        try { return await _ebooksApi.GetProgressAsync(_contentId, _lifetime.Token); }
        catch (ApiException ex) when (ex.StatusCode == 404) { return null; }
    }

    private async Task LoadPreferencesAsync()
    {
        try
        {
            var envelope = await _ebooksApi.GetReaderConfigAsync(_contentId, _lifetime.Token);
            var settings = ReaderSettingsObject(envelope.Config);
            _suppressControls = true;
            SetComboByTag(ThemeCombo, GetString(settings, ["theme"], "light"));
            SetComboByTag(FontCombo, NormalizeFontFamily(GetString(settings, ["fontFamily", "font_family"], "inherit")));
            var fontSize = GetDouble(settings, ["fontSize", "font_size"], 112);
            FontSizeSlider.Value = Math.Clamp(fontSize <= 40 ? fontSize / 16d * 100d : fontSize, 80, 180);
            FontWeightSlider.Value = Math.Clamp(GetDouble(settings, ["fontWeight", "font_weight"], 400), 300, 800);
            LineHeightSlider.Value = Math.Clamp(GetDouble(settings, ["lineHeight", "line_height"], 1.65), 1.1, 2.4);
            MarginSlider.Value = Math.Clamp(GetDouble(settings, ["margin"], 24), 0, 64);
            BrightnessSlider.Value = Math.Clamp(GetDouble(settings, ["fontBrightness", "font_brightness"], 100), 70, 125);
            MaxWidthSlider.Value = Math.Clamp(GetDouble(settings, ["maxWidth", "max_width"], 74), 42, 96);
            RulerPositionSlider.Value = Math.Clamp(GetDouble(settings, ["readingRulerTop", "reading_ruler_top"], 50), 0, 100);
            HyphenationCheck.IsChecked = GetBool(settings, ["hyphenation"], true);
            RtlCheck.IsChecked = GetBool(settings, ["rtl"], false);
            ReadingRulerCheck.IsChecked = GetBool(settings, ["readingRuler", "reading_ruler"], false);
            SetComboByTag(WritingModeCombo, GetString(settings, ["writingMode", "writing_mode"], "auto"));
            SetComboByTag(SpreadCombo, GetString(settings, ["spread"], "auto"));
            SetComboByTag(FlowCombo, GetString(settings, ["flow"], "paginated"));
            _suppressControls = false;
            UpdateDisplayRequest();
            UpdateFormatSpecificChrome();
        }
        catch (ApiException ex) when (ex.StatusCode == 404) { }
    }

    private async Task SavePreferencesAsync()
    {
        if (!_initialized) return;
        await _ebooksApi.SaveReaderConfigAsync(_contentId, new Dictionary<string, object?>
        {
            ["settings"] = new Dictionary<string, object?>
            {
                ["theme"] = SelectedTag(ThemeCombo, "light"),
                ["fontFamily"] = SelectedTag(FontCombo, "inherit"),
                ["fontSize"] = FontSizeSlider.Value,
                ["fontWeight"] = FontWeightSlider.Value,
                ["hyphenation"] = HyphenationCheck.IsChecked == true,
                ["lineHeight"] = LineHeightSlider.Value,
                ["margin"] = MarginSlider.Value,
                ["maxWidth"] = MaxWidthSlider.Value,
                ["spread"] = SelectedTag(SpreadCombo, "auto"),
                ["flow"] = SelectedTag(FlowCombo, "paginated"),
                ["fontBrightness"] = BrightnessSlider.Value,
                ["rtl"] = RtlCheck.IsChecked == true,
                ["writingMode"] = SelectedTag(WritingModeCombo, "auto"),
                ["readingRuler"] = ReadingRulerCheck.IsChecked == true,
                ["readingRulerTop"] = RulerPositionSlider.Value,
            }
        }, _lifetime.Token);
    }

    private async Task LoadAnnotationsAsync()
    {
        AnnotationsList.Children.Clear();
        try
        {
            _annotations = await _ebooksApi.GetAnnotationsAsync(_contentId, _lifetime.Token);
            foreach (var annotation in _annotations)
                AnnotationsList.Children.Add(CreateAnnotationRow(annotation));
            EmptyAnnotationsText.Visibility = _annotations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _annotations = [];
            EmptyAnnotationsText.Visibility = Visibility.Visible;
        }
    }

    private async void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        await ReadScrollFractionAsync();
        await _ebooksApi.CreateAnnotationAsync(_contentId, new EbookReaderAnnotationInput
        {
            Kind = "bookmark", Location = $"chapter:{_chapterIndex};fraction:{_chapterFraction.ToString("F6", CultureInfo.InvariantCulture)}", Note = _book.Chapters[_chapterIndex].Title
        }, _lifetime.Token);
        await LoadAnnotationsAsync();
        OpenPanel("annotations");
    }

    private void NavigateToAnnotation(EbookReaderAnnotation annotation)
    {
        if (string.IsNullOrWhiteSpace(annotation.Location)) return;
        var parts = annotation.Location.Split(';');
        var chapter = int.TryParse(parts.FirstOrDefault(p => p.StartsWith("chapter:"))?[8..], out var c) ? c : 0;
        var fractionText = parts.FirstOrDefault(p => p.StartsWith("fraction:"))?[9..];
        var fraction = double.TryParse(fractionText, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0;
        NavigateToChapter(chapter, fraction);
    }

    private void ContentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || ContentsList.SelectedItem is not ListViewItem { Tag: int index } || index == _chapterIndex) return;
        NavigateToChapter(index);
    }

    private async void Appearance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressControls || !_initialized) return;
        UpdateFormatSpecificChrome();
        await ApplyAppearanceAsync();
        await SavePreferencesAsync();
    }

    private async void AppearanceSlider_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressControls || !_initialized) return;
        UpdateFormatSpecificChrome();
        await ApplyAppearanceAsync();
        await SavePreferencesAsync();
    }

    private async void AppearanceCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressControls || !_initialized) return;
        UpdateFormatSpecificChrome();
        await ApplyAppearanceAsync();
        await SavePreferencesAsync();
    }

    private void ProgressSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressControls || !_initialized || _book == null) return;
        NavigateToChapter(ChapterFromProgress(e.NewValue), FractionWithinChapter(e.NewValue));
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTag(FlowCombo, "paginated") == "paginated" && ReaderWebView.CoreWebView2 != null)
        {
            var moved = await ReaderWebView.ExecuteScriptAsync("(()=>{if(window.scrollX>4){window.scrollBy({left:-window.innerWidth,behavior:'smooth'});return true;}return false;})()");
            if (string.Equals(moved, "true", StringComparison.OrdinalIgnoreCase)) return;
        }
        await ReadScrollFractionAsync();
        if (_chapterIndex > 0) NavigateToChapter(_chapterIndex - 1, 1);
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTag(FlowCombo, "paginated") == "paginated" && ReaderWebView.CoreWebView2 != null)
        {
            var moved = await ReaderWebView.ExecuteScriptAsync("(()=>{const max=document.documentElement.scrollWidth-window.innerWidth;if(window.scrollX<max-4){window.scrollBy({left:window.innerWidth,behavior:'smooth'});return true;}return false;})()");
            if (string.Equals(moved, "true", StringComparison.OrdinalIgnoreCase)) return;
        }
        await ReadScrollFractionAsync();
        if (_book != null && _chapterIndex + 1 < _book.Chapters.Count) NavigateToChapter(_chapterIndex + 1);
    }
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        var navigation = App.Services.GetRequiredService<NavigationService>();
        if (!string.IsNullOrWhiteSpace(_item?.SeriesId))
            navigation.Navigate<ItemDetailPage>(_item.SeriesId);
        else
            navigation.GoBack();
    }
    private void Contents_Click(object sender, RoutedEventArgs e) => OpenPanel("contents");
    private void Search_Click(object sender, RoutedEventArgs e) => OpenPanel("search");
    private void Annotations_Click(object sender, RoutedEventArgs e) => OpenPanel("annotations");
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenPanel("settings");
    private void PanelToggle_Click(object sender, RoutedEventArgs e) => SetPanelOpen(!_panelOpen);

    private void OpenPanel(string panel)
    {
        _activePanel = panel;
        SetPanelOpen(true);
        SettingsPanel.Visibility = panel == "settings" ? Visibility.Visible : Visibility.Collapsed;
        ContentsList.Visibility = panel == "contents" ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = panel == "search" ? Visibility.Visible : Visibility.Collapsed;
        AnnotationsPanel.Visibility = panel == "annotations" ? Visibility.Visible : Visibility.Collapsed;
        ContentsTab.Opacity = panel == "contents" ? 1 : 0.62;
        SearchTab.Opacity = panel == "search" ? 1 : 0.62;
        AnnotationsTab.Opacity = panel == "annotations" ? 1 : 0.62;
        SettingsTab.Opacity = panel == "settings" ? 1 : 0.62;
        if (panel == "search") SearchBox.Focus(FocusState.Programmatic);
    }

    private void SetPanelOpen(bool open)
    {
        _panelOpen = open;
        SideColumn.Width = new GridLength(open ? 320 : 0);
        SidePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PanelToggleIcon.Glyph = open ? "\uE89F" : "\uE8A0";
        var label = open ? "Close reader panel" : "Open reader panel";
        ToolTipService.SetToolTip(PanelToggleButton, label);
        AutomationProperties.SetName(PanelToggleButton, label);
    }

    private async void Highlight_Click(object sender, RoutedEventArgs e)
    {
        if (ReaderWebView.CoreWebView2 == null || !IsHtmlBook) return;
        var json = await ReaderWebView.ExecuteScriptAsync("(()=>{const s=getSelection();const text=(s?.toString()||'').trim();if(!text)return null;try{document.execCommand('hiliteColor',false,'#facc15');}catch{}return {text};})()");
        var selection = JsonSerializer.Deserialize<ReaderSelection>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (string.IsNullOrWhiteSpace(selection?.Text)) return;
        await ReadScrollFractionAsync();
        await _ebooksApi.CreateAnnotationAsync(_contentId, new EbookReaderAnnotationInput
        {
            Kind = "highlight",
            Location = $"chapter:{_chapterIndex};fraction:{_chapterFraction.ToString("F6", CultureInfo.InvariantCulture)}",
            SelectedText = selection.Text,
            Style = "highlight",
            Color = "#facc15"
        }, _lifetime.Token);
        await LoadAnnotationsAsync();
    }

    private Border CreateAnnotationRow(EbookReaderAnnotation annotation)
    {
        var label = annotation.Kind == "bookmark"
            ? (string.IsNullOrWhiteSpace(annotation.Note) ? annotation.Location : annotation.Note)
            : (string.IsNullOrWhiteSpace(annotation.SelectedText) ? annotation.Note : annotation.SelectedText);
        var open = new Button
        {
            Content = new TextBlock { Text = label ?? annotation.Kind, TextWrapping = TextWrapping.Wrap, MaxLines = 3 },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = annotation
        };
        open.Click += (_, _) =>
        {
            NavigateToAnnotation(annotation);
        };
        var delete = new Button { Content = "Delete", Tag = annotation, HorizontalAlignment = HorizontalAlignment.Right };
        delete.Click += async (_, _) =>
        {
            await _ebooksApi.DeleteAnnotationAsync(_contentId, annotation.Id, _lifetime.Token);
            await LoadAnnotationsAsync();
            if (AnnotationChapter(annotation) == _chapterIndex)
                NavigateToChapter(_chapterIndex, _chapterFraction);
        };
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = annotation.Kind, FontSize = 11, Opacity = 0.65 });
        stack.Children.Add(open);
        stack.Children.Add(delete);
        return new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"], Child = stack };
    }

    private async void RunSearch_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        var query = SearchBox.Text.Trim();
        SearchResultsList.Items.Clear();
        if (_book == null || query.Length < 2) { SearchStatusText.Text = "Enter at least two characters."; return; }
        SearchStatusText.Text = "Searching...";
        var results = await Task.Run(() => SearchBook(query), _lifetime.Token);
        foreach (var result in results)
            SearchResultsList.Items.Add(new ListViewItem { Content = $"{result.ChapterTitle}\n{result.Snippet}", Tag = result });
        SearchStatusText.Text = results.Count == 0 ? "No matches" : $"{results.Count} match{(results.Count == 1 ? "" : "es")}";
    }

    private List<BookSearchResult> SearchBook(string query)
    {
        var results = new List<BookSearchResult>();
        if (_book == null || !IsHtmlBook) return results;
        for (var index = 0; index < _book.Chapters.Count && results.Count < 200; index++)
        {
            var chapter = _book.Chapters[index];
            var path = Path.GetFullPath(Path.Combine(_book.RootDirectory, chapter.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(Path.GetFullPath(_book.RootDirectory), StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
            var html = File.ReadAllText(path);
            var text = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline), "<[^>]+>", " "));
            text = Regex.Replace(text, "\\s+", " ").Trim();
            var start = 0;
            while (results.Count < 200 && (start = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var left = Math.Max(0, start - 55);
                var length = Math.Min(text.Length - left, query.Length + 110);
                results.Add(new BookSearchResult(index, chapter.Title, (left > 0 ? "..." : "") + text.Substring(left, length) + (left + length < text.Length ? "..." : ""), query));
                start += Math.Max(1, query.Length);
            }
        }
        return results;
    }

    private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResultsList.SelectedItem is not ListViewItem { Tag: BookSearchResult result }) return;
        _pendingHighlightText = result.Query;
        NavigateToChapter(result.ChapterIndex);
    }

    private async Task HighlightTextAsync(string text)
    {
        var encoded = JsonSerializer.Serialize(text);
        await ReaderWebView.ExecuteScriptAsync($"(()=>{{const q={encoded}.toLowerCase();const w=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT);let n;while(n=w.nextNode()){{const i=n.nodeValue.toLowerCase().indexOf(q);if(i<0)continue;const r=document.createRange();r.setStart(n,i);r.setEnd(n,i+q.length);const m=document.createElement('mark');m.className='silo-highlight';r.surroundContents(m);m.scrollIntoView({{block:'center'}});return true;}}return false;}})()");
    }

    private async void Speak_Click(object sender, RoutedEventArgs e)
    {
        if (ReaderWebView.CoreWebView2 == null) return;
        var rate = SpeechRateSlider.Value.ToString(CultureInfo.InvariantCulture);
        var voice = JsonSerializer.Serialize(SelectedTag(VoiceCombo, ""));
        await ReaderWebView.ExecuteScriptAsync($"(()=>{{speechSynthesis.cancel();const u=new SpeechSynthesisUtterance((getSelection()?.toString()||document.body.innerText).trim());u.rate={rate};const uri={voice};if(uri)u.voice=speechSynthesis.getVoices().find(v=>v.voiceURI===uri)||null;speechSynthesis.speak(u);}})()");
    }

    private async void PauseSpeech_Click(object sender, RoutedEventArgs e)
    {
        if (ReaderWebView.CoreWebView2 != null) await ReaderWebView.ExecuteScriptAsync("speechSynthesis.paused?speechSynthesis.resume():speechSynthesis.pause()");
    }

    private async void StopSpeech_Click(object sender, RoutedEventArgs e)
    {
        if (ReaderWebView.CoreWebView2 != null) await ReaderWebView.ExecuteScriptAsync("speechSynthesis.cancel()");
    }

    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        _suppressControls = true;
        SetComboByTag(ThemeCombo, "light"); SetComboByTag(FontCombo, "inherit"); SetComboByTag(WritingModeCombo, "auto"); SetComboByTag(SpreadCombo, "auto"); SetComboByTag(FlowCombo, "paginated");
        FontSizeSlider.Value = 112; FontWeightSlider.Value = 400; BrightnessSlider.Value = 100; LineHeightSlider.Value = 1.65; MarginSlider.Value = 24; MaxWidthSlider.Value = 74; RulerPositionSlider.Value = 50;
        HyphenationCheck.IsChecked = true; RtlCheck.IsChecked = false; ReadingRulerCheck.IsChecked = false;
        _suppressControls = false;
        UpdateFormatSpecificChrome();
        await ApplyAppearanceAsync();
        await SavePreferencesAsync();
    }

    private async void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string profile }) return;
        _suppressControls = true;
        switch (profile)
        {
            case "accessible": SetComboByTag(FontCombo, "ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"); FontSizeSlider.Value = 126; LineHeightSlider.Value = 1.9; MarginSlider.Value = 32; break;
            case "compact": SetComboByTag(FontCombo, "inherit"); FontSizeSlider.Value = 96; LineHeightSlider.Value = 1.5; MarginSlider.Value = 16; break;
            default: SetComboByTag(FontCombo, "ui-serif, Georgia, Cambria, 'Times New Roman', Times, serif"); FontSizeSlider.Value = 112; LineHeightSlider.Value = 1.75; MarginSlider.Value = 28; break;
        }
        _suppressControls = false;
        await ApplyAppearanceAsync();
        await SavePreferencesAsync();
    }

    private async void WakeLock_Click(object sender, RoutedEventArgs e)
    {
        UpdateDisplayRequest();
        await Task.CompletedTask;
    }

    private async void ReadingRulerToolbar_Click(object sender, RoutedEventArgs e)
    {
        if (IsComicBook) return;
        ReadingRulerCheck.IsChecked = ReadingRulerCheck.IsChecked != true;
        UpdateFormatSpecificChrome();
        await SavePreferencesAsync();
    }

    private void UpdateRulerOverlay()
    {
        var visible = !IsComicBook && ReadingRulerCheck.IsChecked == true;
        AutomationProperties.SetName(
            ReadingRulerToolbarButton,
            visible ? "Disable reading ruler" : "Enable reading ruler");
        RulerOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RulerDragButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RulerPositionSection.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible || ReadingSurface.ActualHeight <= 0) return;
        var y = ReadingSurface.ActualHeight * Math.Clamp(RulerPositionSlider.Value, 0, 100) / 100d;
        var bandHeight = Math.Clamp(16d * (FontSizeSlider.Value / 100d) * LineHeightSlider.Value + 6d, 28d, 96d);
        RulerBand.Height = bandHeight;
        RulerBand.Margin = new Thickness(0, Math.Max(0, y - bandHeight / 2d), 0, 0);
        RulerShadeAbove.Height = Math.Max(0, y - bandHeight / 2d);
        RulerDragButton.Margin = new Thickness(0, Math.Clamp(y - 22d, 0, Math.Max(0, ReadingSurface.ActualHeight - 44d)), 8, 0);
    }

    private void RulerDragButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _rulerDragging = true;
        RulerDragButton.CapturePointer(e.Pointer);
        MoveRulerTo(e.GetCurrentPoint(ReadingSurface).Position);
        e.Handled = true;
    }

    private void RulerDragButton_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_rulerDragging) return;
        MoveRulerTo(e.GetCurrentPoint(ReadingSurface).Position);
        e.Handled = true;
    }

    private async void RulerDragButton_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_rulerDragging) return;
        MoveRulerTo(e.GetCurrentPoint(ReadingSurface).Position);
        _rulerDragging = false;
        RulerDragButton.ReleasePointerCapture(e.Pointer);
        await SavePreferencesAsync();
        e.Handled = true;
    }

    private void MoveRulerTo(Point point)
    {
        if (ReadingSurface.ActualHeight <= 0) return;
        _suppressControls = true;
        RulerPositionSlider.Value = Math.Clamp(point.Y / ReadingSurface.ActualHeight * 100d, 0, 100);
        _suppressControls = false;
        UpdateRulerOverlay();
    }

    private async void NextChapter_Click(object sender, RoutedEventArgs e)
    {
        if (_nextMangaChapter == null) return;
        await ReadScrollFractionAsync();
        await SaveProgressAsync();
        App.Services.GetRequiredService<NavigationService>()
            .Navigate<EbookReaderPage>(new EbookReaderNavigation(_nextMangaChapter.ContentId));
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceBytes == null || _selectedVersion == null || App.MainWindowInstance == null) return;
        var format = FormatOf(_selectedVersion).ToLowerInvariant();
        var suggested = Path.GetFileNameWithoutExtension(_selectedVersion.FileName ?? _selectedVersion.FilePath ?? _item?.Title ?? "book");
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = suggested };
        picker.FileTypeChoices.Add(format.ToUpperInvariant(), [$".{format}"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance));
        var file = await picker.PickSaveFileAsync();
        if (file != null) await Windows.Storage.FileIO.WriteBytesAsync(file, _sourceBytes);
    }

    private void UpdateDisplayRequest()
    {
        try
        {
            if (WakeLockCheck.IsChecked == true && !_displayRequested)
            {
                _displayRequest ??= new DisplayRequest();
                _displayRequest.RequestActive();
                _displayRequested = true;
            }
            else if (WakeLockCheck.IsChecked != true) ReleaseDisplayRequest();
        }
        catch { _displayRequested = false; }
    }

    private void ReleaseDisplayRequest()
    {
        if (!_displayRequested || _displayRequest == null) return;
        try { _displayRequest.RequestRelease(); } catch { }
        _displayRequested = false;
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox or PasswordBox or ComboBox) return;
        if (e.Key == Windows.System.VirtualKey.Left) { Previous_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Windows.System.VirtualKey.Right) { Next_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Windows.System.VirtualKey.Escape) { Back_Click(sender, new RoutedEventArgs()); e.Handled = true; }
    }

    private void ShowFailure(string message)
    {
        LoadingText.Text = message;
        if (LoadingLayer.Children.FirstOrDefault() is StackPanel panel && panel.Children.FirstOrDefault() is ProgressRing ring) ring.IsActive = false;
    }

    private static string SafeName(string value) => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    private static string SelectedTag(ComboBox combo, string fallback) => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
    private static void SetComboByTag(ComboBox combo, string value) => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase)) ?? combo.Items[0];
    private static Dictionary<string, JsonElement> ReaderSettingsObject(Dictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("settings", out var settings) || settings.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, JsonElement>(config, StringComparer.OrdinalIgnoreCase);
        return settings.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.OrdinalIgnoreCase);
    }
    private static bool TryGet(Dictionary<string, JsonElement> config, IEnumerable<string> keys, out JsonElement value)
    {
        foreach (var key in keys)
            if (config.TryGetValue(key, out value)) return true;
        value = default;
        return false;
    }
    private static string GetString(Dictionary<string, JsonElement> config, IEnumerable<string> keys, string fallback)
        => TryGet(config, keys, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static double GetDouble(Dictionary<string, JsonElement> config, IEnumerable<string> keys, double fallback)
        => TryGet(config, keys, out var value) && value.TryGetDouble(out var result) ? result : fallback;
    private static bool GetBool(Dictionary<string, JsonElement> config, IEnumerable<string> keys, bool fallback)
        => TryGet(config, keys, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static string NormalizeFontFamily(string value) => value switch
    {
        "Georgia" or "Georgia, serif" or "Merriweather, Georgia, serif" or "ui-serif, Georgia, Cambria, serif" => "ui-serif, Georgia, Cambria, 'Times New Roman', Times, serif",
        "Segoe UI" or "Atkinson Hyperlegible" or "Inter, ui-sans-serif, system-ui, sans-serif" => "ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
        _ => value,
    };
    private bool IsHtmlBook => _book?.Format is "epub" or "fb2";
    private bool IsComicBook => _book?.Format is "cbz" or "cbr";
    private static int AnnotationChapter(EbookReaderAnnotation annotation)
    {
        var part = annotation.Location?.Split(';').FirstOrDefault(value => value.StartsWith("chapter:", StringComparison.OrdinalIgnoreCase));
        return part != null && int.TryParse(part[8..], out var chapter) ? chapter : -1;
    }
}

public sealed record EbookReaderNavigation(string ContentId, int? FileId = null);
internal sealed record ReaderSelection(string Text);
internal sealed record BookSearchResult(int ChapterIndex, string ChapterTitle, string Snippet, string Query);
internal sealed record ReaderVoice(string Name, string Uri);
