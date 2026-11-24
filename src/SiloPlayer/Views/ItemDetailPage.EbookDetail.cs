using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Helpers;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

/// <summary>
/// The current WebUI ebook page (EbookContent.tsx, redesigned in Silo-Server
/// #4645 onto the audiobook layout): the 2:3 cover with Read / Continue, the
/// reading progress and Mark read / Download in a side column; the series,
/// title and author head; a body of facts, ratings, overview, genres, the
/// related rails and the Files list. The layout pieces are the audiobook
/// page's (ItemDetailPage.BookLayout.cs); this file holds what is the
/// ebook's own.
/// </summary>
public sealed partial class ItemDetailPage
{
    private bool _ebookLayout;
    private bool _ebookLayoutHooked;
    private int? _ebookFactsFileId = -1;
    private double _ebookActionsWidth;
    private double _ebookFactWidth = double.NaN;
    private BookDetailSkeleton? _bookSkeleton;
    private Button? _bookSkeletonBack;

    /// <summary>The ebook borrows the metadata pills and the ratings, and shows 4 overview lines.</summary>
    private void PrepareEbookLayout()
    {
        if (!_ebookLayoutHooked)
        {
            _ebookLayoutHooked = true;
            // The pills and ratings are filled in by the shared metadata code
            // (and the advisory read) after the layout is chosen.
            foreach (var element in new UIElement[] { ContentRatingBadge, AdvisoryAgeBadge, ScoresPanel })
                element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => UpdateEbookBadgesRow());
        }
        ScoresPanel.Margin = new Thickness(0);
        OverviewText.CollapsedLines = 4;
        UpdateEbookBadgesRow();
    }

    /// <summary>Returns the shared controls to the way DetailHero lays them out.</summary>
    private void ReleaseEbookLayout()
    {
        if (!_ebookLayout) return;
        ScoresPanel.Margin = new Thickness(0, 2, 0, 0);
        OverviewText.CollapsedLines = 3;
        EbookBadgesRow.Visibility = Visibility.Collapsed;
        AudiobookFactsBorder.Visibility = Visibility.Visible;
        EbookReadButton.Visibility = EbookPrimaryDownloadButton.Visibility = EbookProgressPanel.Visibility = EbookSecondaryActions.Visibility = Visibility.Collapsed;
        AudiobookCoverFallback.Glyph = "";
        _ebookFactsFileId = -1;
    }

    /// <summary>EbookContent: the head, facts, actions, overview and rails of one ebook.</summary>
    private void ConfigureEbookDetail(MediaItemDetail item)
    {
        _readerTargetContentId = item.ContentId;
        RestoreEbookResumeState(item);

        // The shared DetailHero controls stay hidden with HeroInfoPanel; keep
        // the state they feed consistent for other observers.
        FavoriteButton.Visibility = StarRatingContainer.Visibility = MoreButton.Visibility = Visibility.Collapsed;
        VersionDropdownButton.Visibility = VersionSeparator.Visibility = EditionButton.Visibility = Visibility.Collapsed;
        AudioTracksButton.Visibility = SubtitlesPopoverButton.Visibility = QualityBadgesPanel.Visibility = Visibility.Collapsed;
        PlaybackOptionsRow.Visibility = Visibility.Collapsed;
        TaglineText.Visibility = HeroCrewLine.Visibility = BookProgressSummaryText.Visibility = Visibility.Collapsed;
        AddCollectionButton.Visibility = ListenFromStartButton.Visibility = Visibility.Collapsed;
        AudiobookActionsPanel.Visibility = Visibility.Visible;
        AudiobookCollectionButton.Visibility = AudiobookMoreButton.Visibility = AudiobookListenFromStartButton.Visibility = Visibility.Collapsed;
        AudiobookListenButton.Visibility = AudiobookProgressPanel.Visibility = AudiobookSecondaryActions.Visibility = Visibility.Collapsed;
        SplitPlayButton.Visibility = _selectedVersion != null ? Visibility.Visible : Visibility.Collapsed;
        BookDownloadButton.Visibility = Visibility.Collapsed;
        PlayButtonIcon.Glyph = "";

        AudiobookTitleText.Text = item.Title;
        AudiobookCoverFallback.Glyph = "";
        AudiobookCoverFallback.Visibility = string.IsNullOrWhiteSpace(item.PosterUrl) ? Visibility.Visible : Visibility.Collapsed;
        ApplyAudiobookTint(item.PosterThumbhash);

        var series = item.Ebook?.Series;
        if (!string.IsNullOrWhiteSpace(series?.Name))
        {
            var index = series.Entries.FirstOrDefault(entry => entry.ContentId == item.ContentId)?.SeriesIndex;
            MediaItemCardActions.BindText(AudiobookSeriesText, "Text", element => element.Text = AudiobookDetailPresentation.SeriesLine(series.Name!, index));
            AudiobookSeriesText.Visibility = Visibility.Visible;
        }
        else AudiobookSeriesText.Visibility = Visibility.Collapsed;

        var authors = AuthorNames(item);
        SetBookCreditLine(BookAuthorLine, "web.detail.books.byAuthor", "author", "By", authors);
        BookNarratorLine.Visibility = NarrationPickerButton.Visibility = Visibility.Collapsed;
        BuildBookGenreLinks(item);
        ApplyOverviewSpoilers();
        _ebookFactsFileId = -1;
        UpdateEbookActions();
        BuildBookRelatedGroups(item, series, item.Ebook?.Related, authors.FirstOrDefault(), square: false);
    }

    /// <summary>
    /// Read / Continue (Download when no file opens in the reader), the
    /// reading progress, Mark read and Download, and the facts, from the
    /// current resume state.
    /// </summary>
    private void UpdateEbookActions()
    {
        if (!_ebookLayout || ViewModel.Item is not { } item || !item.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase)) return;
        var canDownload = item.Versions.Count > 0 && CanCurrentUserDownload();
        var state = EbookDetailPresentation.BuildActions(ViewModel.IsWatched, _selectedVersion != null, _playProgressFraction, canDownload);

        EbookReadButton.Visibility = state.ShowRead ? Visibility.Visible : Visibility.Collapsed;
        MediaItemCardActions.BindText(EbookReadText, "Text", element => element.Text = state.ContinueLabel
            ? AppStrings.Get("web.detail.books.continueReading", fallback: "Continue")
            : AppStrings.Get("web.detail.books.read", fallback: "Read"));
        EbookPrimaryDownloadButton.Visibility = state.ShowPrimaryDownload ? Visibility.Visible : Visibility.Collapsed;

        EbookProgressPanel.Visibility = state.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        if (state.ShowProgress)
        {
            EbookProgressDone.Width = new GridLength(state.ProgressPercent, GridUnitType.Star);
            EbookProgressRest.Width = new GridLength(100 - state.ProgressPercent, GridUnitType.Star);
            EbookProgressFill.Background = (Brush)Application.Current.Resources[state.IsRead ? "SuccessBrush" : "AccentBrush"];
            EbookReadCheck.Visibility = state.IsRead ? Visibility.Visible : Visibility.Collapsed;
            MediaItemCardActions.BindText(EbookProgressLabel, "Text", element => element.Text = state.IsRead
                ? AppStrings.Get("web.detail.manga.readStatus", fallback: "Read")
                : AppStrings.Get("web.reader.toolbar.progress", fallback: "Reading progress"));
            EbookProgressLabel.Foreground = (Brush)Application.Current.Resources[state.IsRead ? "PrimaryTextBrush" : "SecondaryTextBrush"];
            MediaItemCardActions.BindText(EbookProgressPercent, "Text", element => element.Text = AppStrings.Get("web.detail.manga.percent",
                new Dictionary<string, object?> { ["percent"] = state.ProgressPercent }, fallback: state.ProgressPercent + "%"));
        }

        // Mark read is always offered; Download joins it when a file also reads.
        EbookSecondaryActions.Visibility = Visibility.Visible;
        EbookDownloadButton.Visibility = state.ShowSecondaryDownload ? Visibility.Visible : Visibility.Collapsed;
        UpdateEbookWatched();
        ArrangeEbookSecondaryActions();

        var fileId = _selectedVersion?.FileId;
        if (_ebookFactsFileId != fileId || _ebookFactsFileId == -1)
        {
            _ebookFactsFileId = fileId;
            BuildEbookFacts(item);
        }
    }

    /// <summary>Mark read / Mark unread with its icon, from the shared watched state.</summary>
    private void UpdateEbookWatched()
    {
        if (!_ebookLayout) return;
        var read = ViewModel.IsWatched;
        EbookWatchedIcon.Content = WebUiIcon.Create(read ? "rotate-ccw" : "check", 16);
        MediaItemCardActions.BindText(EbookWatchedText, "Text", element => element.Text = read
            ? AppStrings.Get("web.detail.actions.markUnread", fallback: "Mark Unread")
            : AppStrings.Get("web.detail.actions.markRead", fallback: "Mark Read"));
        // flex-wrap: the two buttons share a row only while both fit.
        ArrangeEbookSecondaryActions();
    }

    private async void EbookWatchedButton_Click(object sender, RoutedEventArgs e)
    {
        // disabled={watchedMutation.isPending}
        EbookWatchedButton.IsEnabled = false;
        try { await ViewModel.ToggleWatchedCommand.ExecuteAsync(null); }
        finally
        {
            EbookWatchedButton.IsEnabled = true;
            UpdateWatchedButton();
        }
    }

    /// <summary>Mark read and Download in one row while they fit the column, else one under the other.</summary>
    private void ArrangeEbookSecondaryActions()
    {
        if (!_ebookLayout || _ebookActionsWidth <= 0) return;
        var unbounded = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
        EbookWatchedButton.Measure(unbounded);
        EbookDownloadButton.Measure(unbounded);
        var both = EbookDownloadButton.Visibility == Visibility.Visible;
        var sameRow = !both || EbookWatchedButton.DesiredSize.Width + EbookDownloadButton.DesiredSize.Width + EbookSecondaryActions.ColumnSpacing <= _ebookActionsWidth;
        Grid.SetRow(EbookDownloadButton, sameRow ? 0 : 1);
        Grid.SetColumn(EbookDownloadButton, sameRow ? 1 : 0);
        Grid.SetColumnSpan(EbookWatchedButton, both && !sameRow ? 2 : both ? 1 : 2);
        Grid.SetColumnSpan(EbookDownloadButton, sameRow ? 1 : 2);
    }

    /// <summary>Sizes the ebook's actions and facts to the column the layout chose.</summary>
    private void LayoutEbookActions(bool twoColumn, double cover, double contentWidth, bool small)
    {
        // md:h-9 from the two-column layout, h-10 below it.
        EbookWatchedButton.Height = EbookDownloadButton.Height = twoColumn ? 36 : 40;
        EbookWatchedButton.CornerRadius = EbookDownloadButton.CornerRadius = new CornerRadius(EbookWatchedButton.Height / 2);
        _ebookActionsWidth = twoColumn ? cover : contentWidth;
        // grid-cols-2 below sm, a wrapping row from sm.
        AudiobookFactsPanel.HorizontalSpacing = small ? 36 : 24;
        _ebookFactWidth = small ? double.NaN : Math.Max(0, (contentWidth - 24) / 2);
        foreach (var fact in AudiobookFactsPanel.Children.OfType<FrameworkElement>())
            fact.Width = _ebookFactWidth;
        ArrangeEbookSecondaryActions();
    }

    /// <summary>The bordered facts row: Format, File Size, Released, Publisher.</summary>
    private void BuildEbookFacts(MediaItemDetail item)
    {
        AudiobookFactsPanel.Children.Clear();
        var primary = _selectedVersion ?? item.Versions.FirstOrDefault();
        var publisher = EbookDetailPresentation.Publisher(item.Ebook?.Publisher, item.Studios);
        foreach (var fact in EbookDetailPresentation.Facts(item.Versions, primary, item.Year, publisher))
        {
            AddBookFact(fact.LabelKey, fact.EnglishLabel, () => fact.Value);
            if (!double.IsNaN(_ebookFactWidth) && AudiobookFactsPanel.Children.LastOrDefault() is FrameworkElement added) added.Width = _ebookFactWidth;
        }
        AudiobookFactsBorder.Visibility = AudiobookFactsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The pills and ratings row shows only while one of them does.</summary>
    private void UpdateEbookBadgesRow()
    {
        if (!_ebookLayout) return;
        EbookBadgesRow.Visibility = EbookBadgesRow.Children.Any(child => child.Visibility == Visibility.Visible)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== Loading placeholder =====

    /// <summary>
    /// AudiobookDetailSkeleton: while a book loads from a library page the
    /// placeholder takes the book layout, so nothing moves when it arrives.
    /// </summary>
    private void ShowBookSkeleton(int? libraryId)
    {
        var type = libraryId is { } id ? (App.MainWindowInstance as MainWindow)?.FindLibrary(id)?.Type : null;
        var kind = type is "ebook" or "ebooks" ? "ebook" : type is "audiobook" or "audiobooks" ? "audiobook" : null;
        ShowBookSkeletonKind(kind);
    }

    internal void ShowBookSkeletonKind(string? kind)
    {
        if (kind == null) { HideBookSkeleton(); return; }
        var ebook = kind == "ebook";
        if (_bookSkeleton == null || _bookSkeletonKindIsEbook != ebook)
        {
            if (_bookSkeletonHost == null)
            {
                _bookSkeletonBack = new Button
                {
                    Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(8, 24, 0, 0),
                    Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Padding = new Thickness(0),
                    Content = new FontIcon { Glyph = "", FontSize = 20, Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] },
                };
                _bookSkeletonBack.Click += BackButton_Click;
                MediaItemCardActions.BindText(_bookSkeletonBack, "Name", element => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, AppStrings.Get("web.shell.pageBack.goBack", fallback: "Go back")));
                _bookSkeletonHost = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"], Visibility = Visibility.Collapsed };
                Canvas.SetZIndex(_bookSkeletonHost, 10);
                _bookSkeletonHost.Children.Add(_bookSkeletonBack);
                ((Grid)DetailSkeleton.Parent).Children.Add(_bookSkeletonHost);
            }
            if (_bookSkeleton != null) _bookSkeletonHost.Children.Remove(_bookSkeleton);
            _bookSkeleton = new BookDetailSkeleton(ebook);
            _bookSkeletonKindIsEbook = ebook;
            _bookSkeletonHost.Children.Insert(0, _bookSkeleton);
        }
        _bookSkeleton.ApplyWidth(ActualWidth > 0 ? ActualWidth : 1280);
        _bookSkeletonHost!.Visibility = Visibility.Visible;
    }

    private void HideBookSkeleton()
    {
        if (_bookSkeletonHost != null) _bookSkeletonHost.Visibility = Visibility.Collapsed;
    }

    private Grid? _bookSkeletonHost;
    private bool _bookSkeletonKindIsEbook;
}
