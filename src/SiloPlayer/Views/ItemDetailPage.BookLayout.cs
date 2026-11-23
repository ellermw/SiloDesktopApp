using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Helpers;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

/// <summary>
/// The current WebUI book page (AudiobookContent.tsx with audiobooks.css):
/// a cover-tinted page with no backdrop, the square cover and listening
/// controls in a side column, the series/title/author/narrator head and a
/// body of facts, overview, genres, chapters and related rails. The ebook
/// page (EbookContent.tsx, #4645) uses the same layout with a 2:3 cover and a
/// side column from md; its own parts are in ItemDetailPage.EbookDetail.cs.
/// </summary>
public sealed partial class ItemDetailPage
{
    private bool _audiobookLayout;
    private readonly List<(UIElement Element, Panel Parent, int Index)> _audiobookBorrowed = [];
    private IReadOnlyList<AudiobookDetailPresentation.ChapterRow> _audiobookChapterRows = [];
    private bool _audiobookChaptersExpanded;
    private MediaItemDetail? _audiobookChapterOwner;
    private int _audiobookRenderedChapter = -2;
    private bool _audiobookChapterStartsShown = true;
    private TextBlock? _audiobookLeftInChapter;
    private double _audiobookDurationTotal;
    private double _audiobookResumeSeconds;
    private bool _audiobookHasProgress;
    private bool _audiobookHasFiles;
    private double _bookRailCardWidth;

    /// <summary>
    /// Switches the shared hero between DetailHero and the audiobook book page.
    /// Borrowed overview/genre controls return to their exact hero slots.
    /// </summary>
    private void ApplyAudiobookLayout(bool audiobook, bool ebook = false)
    {
        // An audiobook and an ebook borrow different shared controls; a retained
        // page switching between them first returns everything to DetailHero.
        if (audiobook && _audiobookLayout && _ebookLayout != ebook) ApplyAudiobookLayout(false);
        var hero = audiobook ? Visibility.Collapsed : Visibility.Visible;
        BackdropImage.Visibility = audiobook ? Visibility.Collapsed : Visibility.Visible;
        foreach (var layer in new UIElement[] { DetailBackdropTint, DetailBottomGradient, DetailLeftGradient, DetailVignette, AmbientGlowBorder, DetailHeroDivider })
            layer.Visibility = hero;
        AudiobookTint.Visibility = audiobook ? Visibility.Visible : Visibility.Collapsed;
        AudiobookSidePanel.Visibility = audiobook ? Visibility.Visible : Visibility.Collapsed;
        AudiobookHeadPanel.Visibility = audiobook ? Visibility.Visible : Visibility.Collapsed;
        AudiobookBodyPanel.Visibility = audiobook ? Visibility.Visible : Visibility.Collapsed;
        HeroInfoPanel.Visibility = hero;
        DetailContentPanel.Visibility = hero;
        if (audiobook) HeroPosterContainer.Visibility = Visibility.Collapsed;
        HeroContentGrid.VerticalAlignment = audiobook ? VerticalAlignment.Top : VerticalAlignment.Bottom;

        if (audiobook && !_audiobookLayout)
        {
            Borrow(RemoteStateRefreshError, AudiobookHeadPanel, 0);
            Borrow(MetadataRefreshError, AudiobookHeadPanel, 1);
            // EbookContent shows the content-rating/advisory pills and the
            // ratings between the facts and the overview.
            if (ebook)
                foreach (var element in new UIElement[] { ContentRatingBadge, AdvisoryAgeBadge, ScoresPanel })
                    Borrow(element, EbookBadgesRow, EbookBadgesRow.Children.Count);
            foreach (var element in new UIElement[] { OverviewText, OverviewTranslationLabel, TranslateOverviewButton, GenresBadgesPanel })
                Borrow(element, AudiobookAboutPanel, AudiobookAboutPanel.Children.Count);
        }
        else if (!audiobook && _audiobookLayout)
        {
            for (var index = _audiobookBorrowed.Count - 1; index >= 0; index--)
            {
                var (element, parent, slot) = _audiobookBorrowed[index];
                AudiobookHeadPanel.Children.Remove(element);
                AudiobookAboutPanel.Children.Remove(element);
                EbookBadgesRow.Children.Remove(element);
                parent.Children.Insert(Math.Clamp(slot, 0, parent.Children.Count), element);
            }
            _audiobookBorrowed.Clear();
            ReleaseEbookLayout();
            // Below lg the actions leave the side column; always return them.
            if (!AudiobookSidePanel.Children.Contains(AudiobookActionsPanel))
            {
                HeroContentGrid.Children.Remove(AudiobookActionsPanel);
                AudiobookSidePanel.Children.Add(AudiobookActionsPanel);
            }
            HeroContentGrid.ColumnDefinitions[0].Width = GridLength.Auto;
        }

        // Chapters and the related rails are body sections of the book page.
        // ArrangeCurrentWebUiContentOrder places them in the shared content
        // stack first; take them from whichever named host holds them. An
        // ebook ends with its Files list after the rails.
        if (audiobook)
        {
            foreach (var section in ebook
                ? new FrameworkElement[] { BookRelatedSection, MediaLocationsSection }
                : [AudiobookChaptersSection, BookRelatedSection])
            {
                DetailContentPanel.Children.Remove(section);
                AudiobookBodyPanel.Children.Remove(section);
                AudiobookBodyPanel.Children.Add(section);
            }
        }
        _audiobookLayout = audiobook;
        _ebookLayout = audiobook && ebook;
        if (_ebookLayout) PrepareEbookLayout();

        void Borrow(UIElement element, Panel host, int slot)
        {
            var parent = (element as FrameworkElement)?.Parent as Panel;
            if (parent == null) return;
            var index = parent.Children.IndexOf(element);
            parent.Children.RemoveAt(index);
            _audiobookBorrowed.Add((element, parent, index));
            host.Children.Insert(Math.Clamp(slot, 0, host.Children.Count), element);
        }
    }

    /// <summary>audiobooks.css: the side column from lg, the stacked grid below it.</summary>
    private void LayoutAudiobook(double width)
    {
        if (width <= 0) return;
        _bookSkeleton?.ApplyWidth(width);
        if (!_audiobookLayout) return;
        var large = width >= 1024;
        var small = width >= 640;
        // audiobooks.css: an ebook's side column starts at md (13.5rem), an
        // audiobook's at lg; from lg both share the 16.25rem column.
        var ebook = _ebookLayout;
        var twoColumn = large || ebook && width >= 768;
        var gutter = width >= 1280 ? 48d : width >= 1024 ? 40d : small ? 24d : 16d;
        // pt-16 / sm:pt-20 / lg:pt-24, pb-14.
        HeroContentGrid.Margin = new Thickness(gutter, large ? 96 : small ? 80 : 64, gutter, 56);
        HeroContentGrid.MaxWidth = Math.Max(0, 1520 - gutter * 2);
        // grid-template-rows: auto 1fr; the tall side column must not stretch the head's row.
        HeroContentGrid.RowDefinitions[1].Height = twoColumn ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        var middle = twoColumn && !large;
        HeroContentGrid.ColumnSpacing = large ? 56 : middle ? 40 : small ? 32 : 16;
        HeroContentGrid.RowSpacing = large ? 32 : middle ? 28 : small ? 24 : 20;
        var cover = large ? 260d : middle ? 216d : small ? 176d : 112d;
        HeroContentGrid.ColumnDefinitions[0].Width = new GridLength(cover);
        AudiobookSidePanel.Width = cover;
        AudiobookCover.Width = cover;
        // An ebook cover is a 2:3 poster, an audiobook cover is square.
        AudiobookCover.Height = ebook ? cover * 1.5 : cover;
        AudiobookCover.CornerRadius = new CornerRadius(large ? 16 : 12);

        if (twoColumn)
        {
            if (!AudiobookSidePanel.Children.Contains(AudiobookActionsPanel))
            {
                HeroContentGrid.Children.Remove(AudiobookActionsPanel);
                AudiobookSidePanel.Children.Add(AudiobookActionsPanel);
            }
            Grid.SetRowSpan(AudiobookSidePanel, 2);
            Grid.SetRow(AudiobookHeadPanel, 0); Grid.SetColumn(AudiobookHeadPanel, 1);
            Grid.SetRow(AudiobookBodyPanel, 1); Grid.SetColumn(AudiobookBodyPanel, 1); Grid.SetColumnSpan(AudiobookBodyPanel, 1);
            AudiobookHeadPanel.VerticalAlignment = VerticalAlignment.Top;
            // lg:pt-3 on both pages; the ebook head also has md:pt-1.
            AudiobookHeadPanel.Padding = new Thickness(0, large ? 12 : 4, 0, 0);
        }
        else
        {
            if (AudiobookSidePanel.Children.Contains(AudiobookActionsPanel))
            {
                AudiobookSidePanel.Children.Remove(AudiobookActionsPanel);
                HeroContentGrid.Children.Add(AudiobookActionsPanel);
            }
            Grid.SetRowSpan(AudiobookSidePanel, 1);
            Grid.SetRow(AudiobookActionsPanel, 1); Grid.SetColumn(AudiobookActionsPanel, 0); Grid.SetColumnSpan(AudiobookActionsPanel, 2);
            Grid.SetRow(AudiobookHeadPanel, 0); Grid.SetColumn(AudiobookHeadPanel, 1);
            Grid.SetRow(AudiobookBodyPanel, 2); Grid.SetColumn(AudiobookBodyPanel, 0); Grid.SetColumnSpan(AudiobookBodyPanel, 2);
            // align-items: end keeps the head level with the cover's foot.
            AudiobookHeadPanel.VerticalAlignment = VerticalAlignment.Bottom;
            AudiobookHeadPanel.Padding = new Thickness(0);
        }
        AudiobookHeadPanel.Spacing = large ? 8 : 6;
        AudiobookBodyPanel.Spacing = large ? 48 : 40;
        if (ebook) LayoutEbookActions(twoColumn, cover, Math.Min(width - gutter * 2, HeroContentGrid.MaxWidth), small);
        AudiobookTitleText.FontSize = large ? 48 : small ? 36 : 24;
        AudiobookTitleText.LineHeight = AudiobookTitleText.FontSize * 1.08;
        AudiobookTitleText.Margin = new Thickness(0, 0, 0, large ? 4 : 0);
        BookAuthorLine.FontSize = BookNarratorLine.FontSize = NarrationPickerButton.FontSize = small ? 16 : 14;

        var viewportHeight = XamlRoot?.Size.Height is > 0 ? XamlRoot.Size.Height : ActualHeight;
        AudiobookTint.Height = Math.Min(680, Math.Max(0, viewportHeight) * .95);

        if (small != _audiobookChapterStartsShown)
        {
            _audiobookChapterStartsShown = small;
            _audiobookRenderedChapter = -2;
            RenderAudiobookChapters();
        }
        var railWidth = AudiobookDetailPresentation.RelatedCardWidth(
            _uiCustomizationService.CardPresentation.PosterSize, WebUiViewport.Width(this, width), square: !ebook);
        if (Math.Abs(railWidth - _bookRailCardWidth) > .5 && ViewModel.Item is { } item && ebook == !IsAudiobook(item))
            BuildBookRelatedGroups(item, ebook ? item.Ebook?.Series : item.Audiobook?.Series, ebook ? item.Ebook?.Related : item.Audiobook?.Related,
                AuthorNames(item).FirstOrDefault(), square: !ebook);
    }

    private static bool IsAudiobook(MediaItemDetail item) => item.Type.Equals("audiobook", StringComparison.OrdinalIgnoreCase);

    private static List<string> AuthorNames(MediaItemDetail item)
        => PeopleNames(IsAudiobook(item) ? item.Audiobook?.Authors : item.Ebook?.Authors, item.Crew, "Author");

    /// <summary>AudiobookContent: head, facts, listening controls, chapters and rails.</summary>
    private void ConfigureAudiobookDetail(MediaItemDetail item)
    {
        App.Services.GetRequiredService<PlayerService>().SetAudiobookPresentation(item);
        _audiobookHasFiles = item.Versions.Count > 0;
        _audiobookChapterRows = AudiobookDetailPresentation.BuildChapterRows(item.Versions);
        // Retained metadata updates rebuild rows without losing expansion intent.
        if (!ReferenceEquals(_audiobookChapterOwner, item)) _audiobookChaptersExpanded = false;
        _audiobookChapterOwner = item;
        _audiobookRenderedChapter = -2;
        _audiobookResumeSeconds = Math.Max(0, item.UserData?.PositionSeconds ?? 0);
        _audiobookDurationTotal = item.Audiobook?.TotalDurationSeconds is > 0 ? item.Audiobook.TotalDurationSeconds
            : item.UserData?.DurationSeconds is > 0 ? item.UserData.DurationSeconds
            : item.Versions.Sum(version => Math.Max(0, version.Duration));
        _audiobookHasProgress = item.UserData is { } progress && _audiobookResumeSeconds > 0 && _audiobookDurationTotal > 0
            && (progress.IsInProgress || !progress.Played);

        // The generic DetailHero controls stay hidden with HeroInfoPanel; keep
        // the shared state they feed consistent for other observers.
        WatchedButton.Visibility = FavoriteButton.Visibility = StarRatingContainer.Visibility = MoreButton.Visibility = Visibility.Collapsed;
        VersionDropdownButton.Visibility = VersionSeparator.Visibility = EditionButton.Visibility = Visibility.Collapsed;
        AudioTracksButton.Visibility = SubtitlesPopoverButton.Visibility = QualityBadgesPanel.Visibility = Visibility.Collapsed;
        PlaybackOptionsRow.Visibility = MediaLocationsSection.Visibility = Visibility.Collapsed;
        TaglineText.Visibility = HeroCrewLine.Visibility = BookProgressSummaryText.Visibility = Visibility.Collapsed;
        AddCollectionButton.Visibility = ListenFromStartButton.Visibility = Visibility.Collapsed;
        PlayButtonIcon.Glyph = "";

        AudiobookTitleText.Text = item.Title;
        AudiobookCoverFallback.Visibility = string.IsNullOrWhiteSpace(item.PosterUrl) ? Visibility.Visible : Visibility.Collapsed;
        ApplyAudiobookTint(item.PosterThumbhash);

        var series = item.Audiobook?.Series;
        if (!string.IsNullOrWhiteSpace(series?.Name))
        {
            var index = series.Entries.FirstOrDefault(entry => entry.ContentId == item.ContentId)?.SeriesIndex;
            MediaItemCardActions.BindText(AudiobookSeriesText, "Text", element => element.Text = AudiobookDetailPresentation.SeriesLine(series.Name!, index));
            AudiobookSeriesText.Visibility = Visibility.Visible;
        }
        else AudiobookSeriesText.Visibility = Visibility.Collapsed;

        var authors = AuthorNames(item);
        var narrators = PeopleNames(item.Audiobook?.Narrators, item.Crew, "Narrator");
        SetBookCreditLine(BookAuthorLine, "web.detail.books.byAuthor", "author", "By", authors);
        SetBookCreditLine(BookNarratorLine, "web.detail.books.narratedBy", "narrator", "Narrated by", narrators);
        BuildNarrationPicker(item);
        BuildBookGenreLinks(item);
        BuildAudiobookFacts(item);

        var canCurate = AuthorizationPolicy.CanCurateMetadata(App.Services.GetRequiredService<AuthService>());
        AudiobookListenButton.Visibility = _audiobookHasFiles ? Visibility.Visible : Visibility.Collapsed;
        AudiobookCollectionButton.Visibility = _audiobookHasFiles ? Visibility.Visible : Visibility.Collapsed;
        AudiobookListenFromStartButton.Visibility = _audiobookHasFiles && _audiobookHasProgress ? Visibility.Visible : Visibility.Collapsed;
        AudiobookMoreButton.Visibility = canCurate ? Visibility.Visible : Visibility.Collapsed;
        // The ebook page collapses this whole row; a retained page showing an audiobook again needs it back.
        AudiobookSecondaryActions.Visibility = Visibility.Visible;
        AudiobookActionsPanel.Visibility = _audiobookHasFiles || canCurate ? Visibility.Visible : Visibility.Collapsed;
        BuildAudiobookMoreFlyout(item, canCurate);

        UpdateAudiobookListening();
        RenderAudiobookChapters();
        BuildBookRelatedGroups(item, series, item.Audiobook?.Related, authors.FirstOrDefault(), square: true);
    }

    /// <summary>useAmbientColor + .audiobook-tint colour mixes.</summary>
    private void ApplyAudiobookTint(string? thumbhash)
    {
        if (Application.Current.Resources["AppBackgroundColor"] is not Windows.UI.Color background) return;
        var ambient = ThumbhashDecoder.GetAmbientColor(thumbhash) is { } color
            ? Windows.UI.Color.FromArgb(255, color.R, color.G, color.B)
            : background;
        Windows.UI.Color Mix(double amount) => Windows.UI.Color.FromArgb(255,
            (byte)Math.Round(ambient.R * amount + background.R * (1 - amount)),
            (byte)Math.Round(ambient.G * amount + background.G * (1 - amount)),
            (byte)Math.Round(ambient.B * amount + background.B * (1 - amount)));
        AudiobookTintTop.Color = Mix(.34);
        AudiobookTintMiddle.Color = Mix(.12);
        AudiobookTintBottom.Color = background;
        AudiobookTintGlow.Color = Windows.UI.Color.FromArgb((byte)Math.Round(255 * .24), ambient.R, ambient.G, ambient.B);
        AudiobookTintGlowEnd.Color = Windows.UI.Color.FromArgb(0, ambient.R, ambient.G, ambient.B);
    }

    /// <summary>
    /// "By <b>Author</b>" / "Narrated by <b>Narrator</b>": the catalog message's
    /// label/name tags as a muted label and a semibold name, in message order.
    /// </summary>
    private static void SetBookCreditLine(TextBlock line, string key, string argument, string englishLabel, IReadOnlyList<string> names)
    {
        line.Visibility = names.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (names.Count == 0) { line.Inlines.Clear(); return; }
        var joined = string.Join(", ", names);
        MediaItemCardActions.BindText(line, "Text", element =>
        {
            const string marker = "";
            var text = AppStrings.Get(key, new Dictionary<string, object?> { [argument] = marker }, fallback: $"{englishLabel} {marker}");
            var parts = text.Split(marker);
            element.Inlines.Clear();
            for (var index = 0; index < parts.Length; index++)
            {
                if (parts[index].Length > 0) element.Inlines.Add(new Run { Text = parts[index] });
                if (index < parts.Length - 1)
                    element.Inlines.Add(new Run
                    {
                        Text = joined,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                    });
            }
        });
    }

    /// <summary>The bordered facts row: Length, Chapters, Released, Publisher.</summary>
    private void BuildAudiobookFacts(MediaItemDetail item)
    {
        AudiobookFactsPanel.Children.Clear();
        AudiobookFactsBorder.Visibility = Visibility.Visible;
        var rowCount = _audiobookChapterRows.Count;
        var publisher = !string.IsNullOrWhiteSpace(item.Audiobook?.Publisher) ? item.Audiobook!.Publisher
            : item.Studios is { Count: > 0 } ? item.Studios[0] : null;
        var duration = _audiobookDurationTotal;
        if (duration > 0)
            AddFact("web.audiobooks.detail.length", "Length", () => AudiobookDetailPresentation.FormatHoursMinutes(duration));
        if (rowCount > 0)
            AddFact("web.audiobooks.chapters.heading", "Chapters", () => rowCount.ToString("N0", AppStrings.FormatCulture));
        if (item.Year > 0)
            AddFact("web.audiobooks.detail.released", "Released", () => item.Year.ToString(AppStrings.FormatCulture));
        if (!string.IsNullOrWhiteSpace(publisher))
            AddFact("web.audiobooks.detail.publisher", "Publisher", () => publisher);

        void AddFact(string labelKey, string englishLabel, Func<string> value) => AddBookFact(labelKey, englishLabel, value);
    }

    /// <summary>One cell of the facts row: a small muted label over a semibold value.</summary>
    private void AddBookFact(string labelKey, string englishLabel, Func<string> value)
    {
        var fact = new StackPanel { Spacing = 2, MaxWidth = 320 };
        fact.Children.Add(MediaItemCardActions.BindText(new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        }, "Text", element => element.Text = AppStrings.Get(labelKey, fallback: englishLabel)));
        fact.Children.Add(MediaItemCardActions.BindText(new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        }, "Text", element => element.Text = value()));
        AudiobookFactsPanel.Children.Add(fact);
    }

    /// <summary>
    /// The primary button label, the listened bar and its time left: the live
    /// position while this book plays, else the saved one.
    /// </summary>
    private void UpdateAudiobookListening()
    {
        if (!_audiobookLayout || ViewModel.Item is not { } item || !IsAudiobook(item)) return;
        var active = IsActiveAudiobook();
        double? live = active ? Math.Max(0, _playerService!.Position) : null;
        var playing = active && !_playerService!.IsPaused;
        var shown = live ?? (_audiobookHasProgress ? _audiobookResumeSeconds : 0);
        string Label()
        {
            if (playing) return AppStrings.Get("web.detail.audiobook.pause", fallback: "Pause");
            if (active || _audiobookHasProgress && AudiobookDetailPresentation.ResumeChapter(_audiobookChapterRows, _audiobookResumeSeconds) is null)
                return AppStrings.Get("web.detail.actions.resume", fallback: "Resume");
            if (_audiobookHasProgress)
                return AppStrings.Get("web.detail.audiobook.resumeChapter",
                    new Dictionary<string, object?> { ["chapter"] = AudiobookDetailPresentation.ResumeChapter(_audiobookChapterRows, _audiobookResumeSeconds)!.Label },
                    fallback: "Resume · " + AudiobookDetailPresentation.ResumeChapter(_audiobookChapterRows, _audiobookResumeSeconds)!.Label);
            return AppStrings.Get("web.detail.audiobook.listen", fallback: "Listen");
        }
        MediaItemCardActions.BindText(AudiobookListenText, "Text", element => element.Text = Label());
        MediaItemCardActions.BindText(PlayButtonText, "Text", element => element.Text = Label());
        AudiobookListenIcon.Glyph = playing ? "" : "";

        var duration = _audiobookDurationTotal;
        var showProgress = _audiobookHasFiles && shown > 0 && duration > 0;
        AudiobookProgressPanel.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        if (showProgress)
        {
            var percent = AudiobookDetailPresentation.ShownPercent(shown, duration);
            AudiobookProgressDone.Width = new GridLength(percent, GridUnitType.Star);
            AudiobookProgressRest.Width = new GridLength(100 - percent, GridUnitType.Star);
            AudiobookProgressFill.Visibility = percent > 0 ? Visibility.Visible : Visibility.Collapsed;
            MediaItemCardActions.BindText(AudiobookListenedText, "Text", element => element.Text = AudiobookDetailPresentation.ListenedProgress(shown, duration));
            MediaItemCardActions.BindText(AudiobookTimeLeftText, "Text", element => element.Text = AudiobookDetailPresentation.FormatTimeLeft(shown, duration) ?? "");
        }

        var current = AudiobookDetailPresentation.CurrentChapterIndex(_audiobookChapterRows, ChapterPosition());
        if (current != _audiobookRenderedChapter) RenderAudiobookChapters();
        else UpdateLeftInChapter(current);
    }

    private double? ChapterPosition()
        => IsActiveAudiobook() ? Math.Max(0, _playerService!.Position) : _audiobookHasProgress ? _audiobookResumeSeconds : null;

    private void UpdateLeftInChapter(int current)
    {
        if (_audiobookLeftInChapter == null || current < 0 || current >= _audiobookChapterRows.Count || ChapterPosition() is not { } position) return;
        var row = _audiobookChapterRows[current];
        var left = Math.Max(0, row.AbsoluteStart + row.DurationSeconds - position);
        _audiobookLeftInChapter.Visibility = left > 0 ? Visibility.Visible : Visibility.Collapsed;
        _audiobookLeftInChapter.Text = AudiobookDetailPresentation.LeftInChapter(left);
    }

    /// <summary>ChaptersSection: numbered rows, the current one highlighted, finished ones ticked.</summary>
    private void RenderAudiobookChapters()
    {
        AudiobookChaptersPanel.Children.Clear();
        _audiobookLeftInChapter = null;
        var rows = _audiobookChapterRows;
        if (rows.Count == 0)
        {
            AudiobookChaptersSection.Visibility = Visibility.Collapsed;
            _audiobookRenderedChapter = -1;
            return;
        }
        AudiobookChaptersSection.Visibility = Visibility.Visible;
        var position = ChapterPosition();
        var current = AudiobookDetailPresentation.CurrentChapterIndex(rows, position);
        var collapsible = AudiobookDetailPresentation.IsCollapsible(rows.Count);
        AudiobookChaptersToggleButton.Visibility = collapsible ? Visibility.Visible : Visibility.Collapsed;
        MediaItemCardActions.BindText(AudiobookChaptersToggleText, "Text", element => element.Text = _audiobookChaptersExpanded
            ? AudiobookDetailPresentation.ShowFewerLabel()
            : AudiobookDetailPresentation.ShowAllLabel(rows.Count));
        AutomationProperties.SetName(AudiobookChaptersToggleButton, AudiobookChaptersToggleText.Text);

        var accent = Application.Current.Resources["AccentColor"] is Windows.UI.Color accentColor ? accentColor : Microsoft.UI.Colors.SteelBlue;
        // bg-primary/15 on the current chapter.
        var currentBackground = new SolidColorBrush(Windows.UI.Color.FromArgb(38, accent.R, accent.G, accent.B));
        var secondary = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        var (start, count) = AudiobookDetailPresentation.ChapterWindow(rows.Count, current, _audiobookChaptersExpanded);
        for (var index = start; index < start + count; index++)
        {
            var row = rows[index];
            var isCurrent = index == current;
            var isDone = current >= 0 && index < current;
            var grid = new Grid { ColumnSpacing = _audiobookChapterStartsShown ? 16 : 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = row.PositionIndex.ToString(AppStrings.FormatCulture),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isCurrent ? (Brush)Application.Current.Resources["PrimaryTextBrush"] : secondary,
            });
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(MediaItemCardActions.BindText(new TextBlock
            {
                FontSize = 14,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = isCurrent ? FontWeights.SemiBold : isDone ? FontWeights.Normal : FontWeights.Medium,
                Foreground = isDone ? secondary : (Brush)Application.Current.Resources["PrimaryTextBrush"],
            }, "Text", element => element.Text = row.Label));
            if (isCurrent)
            {
                _audiobookLeftInChapter = new TextBlock { FontSize = 12, Foreground = secondary, Margin = new Thickness(0, 2, 0, 0) };
                title.Children.Add(_audiobookLeftInChapter);
            }
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);
            if (_audiobookChapterStartsShown)
            {
                var startText = new TextBlock
                {
                    Text = AudiobookDetailPresentation.FormatChapterStart(row.AbsoluteStart),
                    FontSize = 12,
                    Foreground = secondary,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(startText, 2);
                grid.Children.Add(startText);
            }
            var length = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            if (isDone) length.Children.Add(WebUiIcon.Create("check", 14, (Brush)Application.Current.Resources["SuccessBrush"]));
            length.Children.Add(MediaItemCardActions.BindText(new TextBlock { FontSize = 12, Foreground = secondary }, "Text",
                element => element.Text = AudiobookDetailPresentation.FormatChapterDuration(row.DurationSeconds)));
            Grid.SetColumn(length, 3);
            grid.Children.Add(length);

            var button = new Button
            {
                Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8),
                Background = isCurrent ? currentBackground : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Content = grid,
                Tag = row,
            };
            MediaItemCardActions.BindText(button, "ChapterName", element => AutomationProperties.SetName(element, row.Label));
            if (isCurrent) AutomationProperties.SetItemStatus(button, "current");
            button.Click += AudiobookChapter_Click;
            AudiobookChaptersPanel.Children.Add(button);
        }
        _audiobookRenderedChapter = current;
        UpdateLeftInChapter(current);
    }

    private void AudiobookChaptersToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _audiobookChaptersExpanded = !_audiobookChaptersExpanded;
        RenderAudiobookChapters();
    }

    private async void AudiobookChapter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AudiobookDetailPresentation.ChapterRow chapter } || ViewModel.Item == null) return;
        await App.Services.GetRequiredService<PlayerService>().PlayAsync(
            ViewModel.Item.ContentId,
            fileId: chapter.FileId,
            startPositionOverride: chapter.AbsoluteStart, libraryId: ViewModel.LibraryId);
    }

    private async void AudiobookCollectionButton_Click(object sender, RoutedEventArgs e)
        => await ShowAddToCollectionDialogAsync();

    /// <summary>AudiobookContent's curator menu: Edit metadata, Match, Refresh metadata.</summary>
    private void BuildAudiobookMoreFlyout(MediaItemDetail item, bool canCurate)
    {
        AudiobookMoreFlyout.Items.Clear();
        if (!canCurate) return;
        var edit = MediaItemCardActions.Localize(new MenuFlyoutItem { Text = "Edit Metadata", Icon = new FontIcon { Glyph = "" } }, text: "web.detail.actions.editMetadata");
        // Same fresh read, write ownership and identity guards as the generic detail menu (#4241, #4329).
        edit.Click += async (_, _) => await OpenFreshEditMetadataAsync(item, edit);
        AudiobookMoreFlyout.Items.Add(edit);
        var match = MediaItemCardActions.Localize(new MenuFlyoutItem { Text = "Match Item", Icon = new FontIcon { Glyph = "" } }, text: "web.detail.actions.matchItem");
        match.Click += MatchButton_Click;
        AudiobookMoreFlyout.Items.Add(match);
        var refresh = MediaItemCardActions.Localize(new MenuFlyoutItem { Text = "Refresh Metadata", Icon = new FontIcon { Glyph = "" } }, text: "web.detail.actions.refreshMetadata");
        refresh.Click += async (_, _) => await ShowRefreshMetadataDialogAsync();
        AudiobookMoreFlyout.Items.Add(refresh);
    }

    /// <summary>RelatedRail rails: the series (current book ringed), also by the author, similar.</summary>
    private void BuildBookRelatedGroups(MediaItemDetail item, AudiobookSeriesGroup? series, AudiobookRelatedItems? related, string? author, bool square)
    {
        BookRelatedGroupsPanel.Children.Clear();
        _bookRailCardWidth = AudiobookDetailPresentation.RelatedCardWidth(
            _uiCustomizationService.CardPresentation.PosterSize, WebUiViewport.Width(this, ActualWidth > 0 ? ActualWidth : 1280), square);
        // The ebook rails are body sections: gap-10 lg:gap-12 like the rest of the body.
        BookRelatedSection.Spacing = BookRelatedGroupsPanel.Spacing = square ? 40 : ActualWidth >= 1024 ? 48 : 40;
        if (series?.Entries.Count > 0)
            AddBookRelatedGroup(
                () => !string.IsNullOrWhiteSpace(series.Name)
                    ? AppStrings.Get("web.detail.books.inSeries", new Dictionary<string, object?> { ["series"] = series.Name })
                    : AppStrings.Get("web.detail.books.inThisSeries"),
                series.Entries.Select(entry => BookRelatedCard(entry, square,
                    kicker: () => AudiobookDetailPresentation.BookNumber(entry.SeriesIndex),
                    subtitle: RailSubtitle(entry, square),
                    highlight: entry.ContentId == item.ContentId)),
                startIndex: series.Entries.FindIndex(entry => entry.ContentId == item.ContentId));
        if (related?.AlsoByAuthor.Count > 0)
            AddBookRelatedGroup(
                () => !string.IsNullOrWhiteSpace(author)
                    ? AppStrings.Get("web.detail.books.alsoByAuthor", new Dictionary<string, object?> { ["author"] = author })
                    : AppStrings.Get("web.detail.books.alsoByThisAuthor"),
                related.AlsoByAuthor.Select(entry => BookRelatedCard(entry, square, null, RailSubtitle(entry, square), false)));
        if (related?.Similar.Count > 0)
            AddBookRelatedGroup(
                () => AppStrings.Get("web.detail.books.youMightAlsoLike"),
                related.Similar.Select(entry => BookRelatedCard(entry, square, null, RailSubtitle(entry, square), false)));
        BookRelatedSection.Visibility = BookRelatedGroupsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        static Func<string?> RailSubtitle(AudiobookRelatedItem entry, bool square) => square
            ? () => AudiobookDetailPresentation.RelatedStatus(entry)
            : () => EbookDetailPresentation.RelatedStatus(entry);
    }

    private void AddBookRelatedGroup(Func<string> heading, IEnumerable<FrameworkElement> cards, int startIndex = -1)
    {
        var group = new StackPanel { Spacing = 16 };
        group.Children.Add(MediaItemCardActions.BindText(new TextBlock
        {
            Style = (Style)Application.Current.Resources["TitleTextStyle"],
            FontSize = 20,
        }, "Text", element => element.Text = heading()));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Padding = new Thickness(0, 4, 0, 8) };
        foreach (var card in cards) row.Children.Add(card);
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = row,
        };
        // MediaCarousel startSlide: the rail opens on the current book.
        if (startIndex > 0)
        {
            void Align(object? sender, object args)
            {
                if (scroll.ScrollableWidth <= 0 || scroll.ViewportWidth <= 0) return;
                scroll.LayoutUpdated -= Align;
                // Official slides scroll in groups of the cards that fit the rail, and the rail jumps to the group
                // that holds the current book. A book already in the first group leaves the rail where it is, so
                // the first card is never cut off just to show a book that is already on screen.
                var step = _bookRailCardWidth + row.Spacing;
                var perGroup = Math.Max(1, (int)Math.Floor((scroll.ViewportWidth + row.Spacing) / step));
                var offset = startIndex / perGroup * perGroup * step;
                if (offset > 0) scroll.ChangeView(Math.Min(offset, scroll.ScrollableWidth), null, null, true);
            }
            scroll.LayoutUpdated += Align;
            scroll.Unloaded += (_, _) => scroll.LayoutUpdated -= Align;
        }
        group.Children.Add(scroll);
        BookRelatedGroupsPanel.Children.Add(group);
    }

    /// <summary>
    /// One RelatedRail card: cover (square for audiobooks, 2:3 for ebooks)
    /// with a progress bar while started, then kicker, title and status.
    /// </summary>
    private FrameworkElement BookRelatedCard(AudiobookRelatedItem entry, bool square, Func<string?>? kicker, Func<string?> subtitle, bool highlight)
    {
        var width = _bookRailCardWidth;
        var secondary = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        var coverGrid = new Grid();
        coverGrid.Children.Add(new FontIcon
        {
            Glyph = square ? "" : "",
            FontSize = 28,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var image = new Image { Stretch = Stretch.UniformToFill };
        coverGrid.Children.Add(image);
        if (AudiobookDetailPresentation.RelatedProgress(entry) is { } progress)
        {
            var bar = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0)) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(progress, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - progress, GridUnitType.Star) });
            bar.Children.Add(new Border { Background = (Brush)Application.Current.Resources["AccentBrush"] });
            coverGrid.Children.Add(bar);
        }
        var cover = new Border
        {
            Width = width,
            Height = square ? width : width * 1.5,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            BorderThickness = new Thickness(highlight ? 2 : 0),
            BorderBrush = highlight ? (Brush)Application.Current.Resources["AccentBrush"] : null,
            Child = coverGrid,
        };
        var content = new StackPanel { Width = width };
        content.Children.Add(cover);
        TextBlock? kickerText = null;
        if (kicker?.Invoke() is { Length: > 0 })
        {
            kickerText = MediaItemCardActions.BindText(new TextBlock
            {
                FontSize = 12,
                Foreground = secondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 8, 0, 0),
            }, "Text", element => element.Text = kicker() ?? "");
            content.Children.Add(kickerText);
        }
        content.Children.Add(new TextBlock
        {
            Text = entry.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, kickerText != null ? 2 : 8, 0, 0),
        });
        if (subtitle() is { Length: > 0 })
            content.Children.Add(MediaItemCardActions.BindText(new TextBlock
            {
                FontSize = 12,
                Foreground = secondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            }, "Text", element => element.Text = subtitle() ?? ""));

        var button = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = content,
            Tag = entry.ContentId,
        };
        AutomationProperties.SetName(button, entry.Title);
        if (highlight) AutomationProperties.SetItemStatus(button, "current");
        button.Click += (_, _) => App.Services.GetRequiredService<NavigationService>()
            .Navigate<ItemDetailPage>(MediaNavigationContext.Detail(entry.ContentId, ViewModel.LibraryId));
        if (!string.IsNullOrWhiteSpace(entry.PosterUrl) && ViewModel.Item is { } owner)
            _ = LoadBookRailCoverAsync(owner, entry, image, (int)Math.Ceiling(width * 2), _imageCts?.Token ?? default);
        return button;
    }

    private async Task LoadBookRailCoverAsync(MediaItemDetail owner, AudiobookRelatedItem entry, Image image, int decodeWidth, CancellationToken ct)
    {
        await ReadDetailCompanionAsync(owner, ct, async current =>
        {
            var images = App.Services.GetRequiredService<ImageService>();
            var bytes = await images.GetImageAsync(entry.ContentId, "poster", entry.PosterUrl!, App.Services.GetRequiredService<HttpClient>(), ct);
            if (!current()) return false;
            if (bytes == null) return images.IsKnownUnavailable(entry.ContentId, "poster", entry.PosterUrl!);
            var bitmap = new BitmapImage { DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical };
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            if (!current()) return false;
            image.Source = bitmap;
            return true;
        });
    }
}
