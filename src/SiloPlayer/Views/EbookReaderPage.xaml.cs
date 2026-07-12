using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Windows.System.Display;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
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
    private string _contentId = "";
    private int _fileId;
    private int _chapterIndex;
    private double _chapterFraction;
    private bool _initialized;
    private bool _suppressControls;
    private bool _savePending;
    private string _activePanel = "contents";
    private DisplayRequest? _displayRequest;
    private bool _displayRequested;
    private string? _pendingHighlightText;
    private List<EbookReaderAnnotation> _annotations = [];
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    public EbookReaderPage()
    {
        InitializeComponent();
        FontSizeSlider.Value = 18;
        LineHeightSlider.Value = 1.55;
        MarginSlider.Value = 48;
        MaxWidthSlider.Value = 72;
        RulerPositionSlider.Value = 45;
        ThemeCombo.SelectedIndex = 0;
        FontCombo.SelectedIndex = 0;
        WritingModeCombo.SelectedIndex = 0;
        FlowCombo.SelectedIndex = 0;
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
            ShowFailure("This book could not be identified.");
            return;
        }

        try
        {
            _item = await _catalogApi.GetItemDetailAsync(_contentId, _lifetime.Token);
            TitleText.Text = _item.Title;
            if (App.MainWindowInstance is MainWindow window) window.SetDynamicTitle(_item.Title);
            var version = ChooseVersion(_item.Versions, requestedFileId)
                ?? throw new InvalidOperationException("No readable book file is available.");
            _fileId = version.FileId;
            LoadingText.Text = "Downloading book…";
            var bytes = await _ebooksApi.ReadFileAsync(_contentId, _fileId, _lifetime.Token);
            LoadingText.Text = "Preparing reader…";
            var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "reader-cache", SafeName(_contentId), _fileId.ToString(CultureInfo.InvariantCulture));
            _book = await Task.Run(() => EbookPackageExtractor.Extract(bytes, cacheRoot, FormatOf(version)), _lifetime.Token);
            SearchBookButton.IsEnabled = IsHtmlBook;
            HighlightButton.IsEnabled = IsHtmlBook;

            await ReaderWebView.EnsureCoreWebView2Async();
            ReaderWebView.CoreWebView2.SetVirtualHostNameToFolderMapping("silo-reader.local", _book.RootDirectory, CoreWebView2HostResourceAccessKind.Allow);
            BuildContents();
            await LoadPreferencesAsync();
            await LoadAnnotationsAsync();
            var saved = await TryGetProgressAsync();
            _chapterIndex = saved == null || saved.FileId != _fileId ? 0 : ChapterFromProgress(saved.Progress);
            _chapterFraction = saved == null || saved.FileId != _fileId ? 0 : FractionWithinChapter(saved.Progress);
            _initialized = true;
            NavigateToChapter(_chapterIndex, _chapterFraction);
            _progressTimer.Start();
            Focus(FocusState.Programmatic);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowFailure(ex.Message);
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
        ReaderWebView.Close();
        base.OnNavigatedFrom(e);
    }

    private static FileVersion? ChooseVersion(IEnumerable<FileVersion> versions, int? requested)
    {
        var list = versions.ToList();
        if (requested.HasValue) return list.FirstOrDefault(v => v.FileId == requested.Value);
        return list.FirstOrDefault(v => SupportedFormats.Contains(FormatOf(v)));
    }

    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
        { "epub", "pdf", "mobi", "azw", "azw3", "cbz", "cbr", "fb2", "fbz" };

    private static string FormatOf(FileVersion version)
    {
        var extension = Path.GetExtension(version.FileName ?? version.FilePath ?? "").TrimStart('.');
        return string.IsNullOrWhiteSpace(extension) ? version.Container.TrimStart('.') : extension;
    }

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
        ChapterText.Text = chapter.Title;
        ContentsList.SelectedIndex = _chapterIndex;
        PreviousButton.IsEnabled = _chapterIndex > 0;
        NextButton.IsEnabled = _chapterIndex < _book.Chapters.Count - 1;
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

    private async Task ApplyAppearanceAsync()
    {
        if (ReaderWebView.CoreWebView2 == null || !IsHtmlBook) return;
        var theme = SelectedTag(ThemeCombo, "dark");
        var font = SelectedTag(FontCombo, "Georgia");
        var colors = theme switch
        {
            "light" => ("#faf8f2", "#24211d"),
            "sepia" => ("#f4ecd8", "#433b2d"),
            _ => ("#111318", "#eceef3")
        };
        var flow = SelectedTag(FlowCombo, "paginated");
        var writingMode = SelectedTag(WritingModeCombo, "auto");
        var direction = RtlCheck.IsChecked == true ? "rtl" : "ltr";
        var hyphens = HyphenationCheck.IsChecked == true ? "auto" : "none";
        var brightness = BrightnessSlider.Value.ToString(CultureInfo.InvariantCulture);
        var maxWidth = MaxWidthSlider.Value.ToString(CultureInfo.InvariantCulture);
        var flowCss = flow == "paginated"
            ? "height:calc(100vh - 64px)!important;max-width:none!important;column-width:" + maxWidth + "ch!important;column-gap:72px!important;column-fill:auto!important;overflow-x:auto!important;overflow-y:hidden!important;"
            : "max-width:" + maxWidth + "ch!important;overflow:visible!important;";
        var css = $"html{{background:{colors.Item1}!important;color:{colors.Item2}!important;scroll-behavior:smooth;}}body{{{flowCss}margin:0 auto!important;padding:32px {MarginSlider.Value.ToString(CultureInfo.InvariantCulture)}px 80px!important;font-family:'{font}',serif!important;font-size:{FontSizeSlider.Value.ToString(CultureInfo.InvariantCulture)}px!important;line-height:{LineHeightSlider.Value.ToString(CultureInfo.InvariantCulture)}!important;color:{colors.Item2}!important;background:{colors.Item1}!important;filter:brightness({brightness}%)!important;hyphens:{hyphens}!important;direction:{direction}!important;writing-mode:{writingMode}!important;}}img,svg{{max-width:100%!important;height:auto!important;}}a{{color:#75a7ff!important;}}mark.silo-highlight{{background:#f5d547;color:inherit;border-radius:2px;}}";
        var rulerTop = RulerPositionSlider.Value.ToString(CultureInfo.InvariantCulture);
        var rulerVisible = ReadingRulerCheck.IsChecked == true ? "block" : "none";
        var js = $"(()=>{{let s=document.getElementById('silo-reader-style');if(!s){{s=document.createElement('style');s.id='silo-reader-style';document.head.appendChild(s);}}s.textContent={JsonSerializer.Serialize(css)};document.documentElement.style.colorScheme={JsonSerializer.Serialize(theme == "light" || theme == "sepia" ? "light" : "dark")};let r=document.getElementById('silo-reading-ruler');if(!r){{r=document.createElement('div');r.id='silo-reading-ruler';document.body.appendChild(r);}}Object.assign(r.style,{{display:'{rulerVisible}',position:'fixed',left:'0',right:'0',top:'{rulerTop}%',height:'36px',background:'rgba(117,167,255,.13)',borderTop:'1px solid rgba(117,167,255,.35)',borderBottom:'1px solid rgba(117,167,255,.35)',pointerEvents:'none',zIndex:'2147483647'}});}})();";
        await ReaderWebView.ExecuteScriptAsync(js);
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
        _suppressControls = false;
    }

    private int ChapterFromProgress(double progress) => _book == null ? 0 : Math.Clamp((int)Math.Floor(Math.Clamp(progress, 0, 0.999999) * _book.Chapters.Count), 0, _book.Chapters.Count - 1);
    private double FractionWithinChapter(double progress) => _book == null ? 0 : Math.Clamp(progress * _book.Chapters.Count - ChapterFromProgress(progress), 0, 1);

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

    private Task SaveProgressAsync() => _ebooksApi.SaveProgressAsync(_contentId, new EbookReaderProgressInput
    {
        FileId = _fileId,
        Location = $"chapter:{_chapterIndex};fraction:{_chapterFraction.ToString("F6", CultureInfo.InvariantCulture)}",
        Progress = OverallProgress
    }, _lifetime.Token);

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
            _suppressControls = true;
            SetComboByTag(ThemeCombo, GetString(envelope.Config, "theme", "dark"));
            SetComboByTag(FontCombo, GetString(envelope.Config, "font_family", "Georgia"));
            FontSizeSlider.Value = GetDouble(envelope.Config, "font_size", 18);
            LineHeightSlider.Value = GetDouble(envelope.Config, "line_height", 1.55);
            MarginSlider.Value = GetDouble(envelope.Config, "margin", 48);
            BrightnessSlider.Value = GetDouble(envelope.Config, "font_brightness", 100);
            MaxWidthSlider.Value = GetDouble(envelope.Config, "max_width", 72);
            RulerPositionSlider.Value = GetDouble(envelope.Config, "reading_ruler_top", 45);
            HyphenationCheck.IsChecked = GetBool(envelope.Config, "hyphenation", false);
            RtlCheck.IsChecked = GetBool(envelope.Config, "rtl", false);
            ReadingRulerCheck.IsChecked = GetBool(envelope.Config, "reading_ruler", false);
            WakeLockCheck.IsChecked = GetBool(envelope.Config, "keep_awake", false);
            SetComboByTag(WritingModeCombo, GetString(envelope.Config, "writing_mode", "auto"));
            SetComboByTag(FlowCombo, GetString(envelope.Config, "flow", "paginated"));
            _suppressControls = false;
            UpdateDisplayRequest();
        }
        catch (ApiException ex) when (ex.StatusCode == 404) { }
    }

    private async Task SavePreferencesAsync()
    {
        if (!_initialized) return;
        await _ebooksApi.SaveReaderConfigAsync(_contentId, new Dictionary<string, object?>
        {
            ["theme"] = SelectedTag(ThemeCombo, "dark"), ["font_family"] = SelectedTag(FontCombo, "Georgia"),
            ["font_size"] = FontSizeSlider.Value, ["line_height"] = LineHeightSlider.Value, ["margin"] = MarginSlider.Value,
            ["font_brightness"] = BrightnessSlider.Value, ["max_width"] = MaxWidthSlider.Value,
            ["hyphenation"] = HyphenationCheck.IsChecked == true, ["rtl"] = RtlCheck.IsChecked == true,
            ["reading_ruler"] = ReadingRulerCheck.IsChecked == true, ["reading_ruler_top"] = RulerPositionSlider.Value,
            ["writing_mode"] = SelectedTag(WritingModeCombo, "auto"), ["flow"] = SelectedTag(FlowCombo, "paginated"),
            ["keep_awake"] = WakeLockCheck.IsChecked == true
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
        }
        catch (ApiException ex) when (ex.StatusCode == 404) { _annotations = []; }
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
        await ApplyAppearanceAsync();
        await SavePreferencesAsync();
    }

    private async void AppearanceSlider_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressControls || !_initialized) return;
        await ApplyAppearanceAsync();
        await SavePreferencesAsync();
    }

    private async void AppearanceCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressControls || !_initialized) return;
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
    private void Back_Click(object sender, RoutedEventArgs e) => App.Services.GetRequiredService<NavigationService>().GoBack();
    private void Contents_Click(object sender, RoutedEventArgs e) => OpenPanel("contents");
    private void Search_Click(object sender, RoutedEventArgs e) => OpenPanel("search");
    private void Annotations_Click(object sender, RoutedEventArgs e) => OpenPanel("annotations");
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenPanel("settings");
    private void ClosePanel_Click(object sender, RoutedEventArgs e) { SideColumn.Width = new GridLength(0); SidePanel.Visibility = Visibility.Collapsed; }

    private void OpenPanel(string panel)
    {
        _activePanel = panel;
        SideColumn.Width = new GridLength(340);
        SidePanel.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = panel == "settings" ? Visibility.Visible : Visibility.Collapsed;
        ContentsList.Visibility = panel == "contents" ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = panel == "search" ? Visibility.Visible : Visibility.Collapsed;
        AnnotationsPanel.Visibility = panel == "annotations" ? Visibility.Visible : Visibility.Collapsed;
        PanelTitle.Text = panel switch { "settings" => "Reader settings", "search" => "Search", "annotations" => "Annotations", _ => "Contents" };
        if (panel == "search") SearchBox.Focus(FocusState.Programmatic);
    }

    private async void Highlight_Click(object sender, RoutedEventArgs e)
    {
        if (ReaderWebView.CoreWebView2 == null || !IsHtmlBook) return;
        var json = await ReaderWebView.ExecuteScriptAsync("(()=>{const s=getSelection();const text=(s?.toString()||'').trim();if(!text)return null;try{document.execCommand('hiliteColor',false,'#f5d547');}catch{}return {text};})()");
        var selection = JsonSerializer.Deserialize<ReaderSelection>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (string.IsNullOrWhiteSpace(selection?.Text)) return;
        await ReadScrollFractionAsync();
        await _ebooksApi.CreateAnnotationAsync(_contentId, new EbookReaderAnnotationInput
        {
            Kind = "highlight",
            Location = $"chapter:{_chapterIndex};fraction:{_chapterFraction.ToString("F6", CultureInfo.InvariantCulture)}",
            SelectedText = selection.Text,
            Style = "highlight",
            Color = "#f5d547"
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
        await ReaderWebView.ExecuteScriptAsync($"(()=>{{speechSynthesis.cancel();const u=new SpeechSynthesisUtterance((getSelection()?.toString()||document.body.innerText).trim());u.rate={rate};speechSynthesis.speak(u);}})()");
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
        SetComboByTag(ThemeCombo, "dark"); SetComboByTag(FontCombo, "Georgia"); SetComboByTag(WritingModeCombo, "auto"); SetComboByTag(FlowCombo, "paginated");
        FontSizeSlider.Value = 18; BrightnessSlider.Value = 100; LineHeightSlider.Value = 1.55; MarginSlider.Value = 48; MaxWidthSlider.Value = 72; RulerPositionSlider.Value = 45;
        HyphenationCheck.IsChecked = false; RtlCheck.IsChecked = false; ReadingRulerCheck.IsChecked = false; WakeLockCheck.IsChecked = false;
        _suppressControls = false;
        UpdateDisplayRequest();
        await ApplyAppearanceAsync();
        await SavePreferencesAsync();
    }

    private async void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string profile }) return;
        _suppressControls = true;
        switch (profile)
        {
            case "accessible": SetComboByTag(FontCombo, "Atkinson Hyperlegible"); FontSizeSlider.Value = 21; LineHeightSlider.Value = 1.9; MarginSlider.Value = 64; break;
            case "compact": FontSizeSlider.Value = 16; LineHeightSlider.Value = 1.5; MarginSlider.Value = 24; break;
            default: SetComboByTag(FontCombo, "Georgia"); FontSizeSlider.Value = 18; LineHeightSlider.Value = 1.75; MarginSlider.Value = 48; break;
        }
        _suppressControls = false;
        await ApplyAppearanceAsync();
        await SavePreferencesAsync();
    }

    private async void WakeLock_Click(object sender, RoutedEventArgs e)
    {
        UpdateDisplayRequest();
        await SavePreferencesAsync();
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
    private static string GetString(Dictionary<string, JsonElement> config, string key, string fallback) => config.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static double GetDouble(Dictionary<string, JsonElement> config, string key, double fallback) => config.TryGetValue(key, out var value) && value.TryGetDouble(out var result) ? result : fallback;
    private static bool GetBool(Dictionary<string, JsonElement> config, string key, bool fallback) => config.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private bool IsHtmlBook => _book?.Format is "epub" or "fb2";
    private static int AnnotationChapter(EbookReaderAnnotation annotation)
    {
        var part = annotation.Location?.Split(';').FirstOrDefault(value => value.StartsWith("chapter:", StringComparison.OrdinalIgnoreCase));
        return part != null && int.TryParse(part[8..], out var chapter) ? chapter : -1;
    }
}

public sealed record EbookReaderNavigation(string ContentId, int? FileId = null);
internal sealed record ReaderSelection(string Text);
internal sealed record BookSearchResult(int ChapterIndex, string ChapterTitle, string Snippet, string Query);
