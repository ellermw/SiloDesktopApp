using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

/// <summary>
/// Native counterpart of the current WebUI EditMetadataDialog. It keeps the
/// same sections, editable fields, metadata lock behavior, provider reset, and
/// immediate remote-image selection workflow.
/// </summary>
public sealed class EditMetadataDialog : ContentDialog
{
    private const int FieldName = 0;
    private const int FieldOverview = 1;
    private const int FieldGenres = 2;
    private const int FieldStudios = 3;
    private const int FieldRating = 6;
    private const int FieldRuntime = 7;
    private const int FieldTags = 8;
    private const int FieldContentRating = 9;
    private const int FieldAirSchedule = 11;
    private const int FieldReleaseDates = 13;

    private static readonly string[] AirTimezones =
    [
        "America/New_York", "America/Chicago", "America/Denver",
        "America/Los_Angeles", "Europe/London", "Europe/Paris",
        "Asia/Tokyo", "Asia/Seoul", "Australia/Sydney",
    ];

    private readonly MediaItemDetail _item;
    private readonly AdminApi _adminApi;
    private readonly CatalogApi _catalogApi;
    private readonly ToastService _toast;
    private readonly HashSet<int> _lockedFields;
    private readonly Dictionary<int, List<Button>> _lockButtons = [];
    private readonly List<(string Name, FrameworkElement Content)> _sections = [];
    private readonly Grid _sectionHost = new();
    private readonly ListView _sectionList = new();
    private readonly TextBlock _lockedCount = new();

    private readonly TextBox _title = new();
    private readonly TextBox _sortTitle = new();
    private readonly TextBox _originalTitle = new();
    private readonly TextBox _overview = new();
    private readonly TextBox _tagline = new();
    private readonly TextBox _contentRating = new();
    private readonly NumberBox _year = NumberField();
    private readonly NumberBox _runtime = NumberField();
    private readonly NumberBox _ratingImdb = NumberField(0, 10, 0.1);
    private readonly NumberBox _ratingTmdb = NumberField(0, 10, 0.1);
    private readonly NumberBox _ratingRtCritic = NumberField(0, 100);
    private readonly NumberBox _ratingRtAudience = NumberField(0, 100);
    private readonly NumberBox _seasonNumber = NumberField(0, 10000);
    private readonly NumberBox _episodeNumber = NumberField(0, 10000);
    private readonly TextBox _genres = new();
    private readonly TextBox _studios = new();
    private readonly TextBox _networks = new();
    private readonly TextBox _countries = new();
    private readonly TextBox _imdbId = new();
    private readonly TextBox _tmdbId = new();
    private readonly TextBox _tvdbId = new();
    private readonly CalendarDatePicker _releaseDate = DateField();
    private readonly CalendarDatePicker _firstAirDate = DateField();
    private readonly CalendarDatePicker _lastAirDate = DateField();
    private readonly CalendarDatePicker _airDate = DateField();
    private readonly TextBox _airTime = new();
    private readonly ComboBox _airTimezone = new() { IsEditable = true };
    private readonly ComboBox _status = new();

    private readonly StackPanel _translationPanel = new() { Spacing = 10 };
    private Border? _translationContainer;
    private readonly ComboBox _translationLanguage = new() { MinWidth = 180 };
    private readonly ToggleSwitch _translationForce = new() { Header = "Re-translate existing" };
    private readonly Button _translateButton = new() { Content = "Translate", Padding = new Thickness(14, 7, 14, 7) };
    private readonly TextBlock _translationProgress = new()
    {
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
    };
    private readonly CancellationTokenSource _translationCts = new();

    private readonly StackPanel _imagesPanel = new() { Spacing = 12 };
    private readonly StackPanel _imageTabs = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
    };
    private readonly GridView _imageGrid = new()
    {
        SelectionMode = ListViewSelectionMode.None,
        IsItemClickEnabled = false,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MaxHeight = 390,
    };
    private readonly TextBlock _imageStatus = new();
    private readonly Button _applyImage = new()
    {
        Content = "Apply image",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        IsEnabled = false,
    };
    private ItemImagesResponse? _images;
    private RemoteItemImage? _selectedImage;
    private string _activeImageType = "poster";
    private bool _imageLoadStarted;

    public bool HasAppliedChanges { get; private set; }

    public EditMetadataDialog(MediaItemDetail item)
    {
        _item = item;
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        _catalogApi = App.Services.GetRequiredService<CatalogApi>();
        _toast = App.Services.GetRequiredService<ToastService>();
        _lockedFields = new HashSet<int>(item.LockedFields ?? []);

        Title = BuildHeader();
        PrimaryButtonText = "Save Changes";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += SaveButton_Click;

        InitializeFields();
        BuildSections();
        Content = BuildLayout();
        UpdateLockedCount();
        Closed += (_, _) => _translationCts.Cancel();
        _ = LoadTranslationPanelAsync();
    }

    private FrameworkElement BuildHeader()
    {
        var panel = new Grid { ColumnSpacing = 12 };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        panel.Children.Add(new TextBlock
        {
            Text = "Edit Metadata",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var typeBadge = new Border
        {
            Background = Brush("SurfaceRaisedBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = TypeLabel(_item.Type),
                FontSize = 11,
                Foreground = Brush("SecondaryTextBrush"),
            },
        };
        Grid.SetColumn(typeBadge, 1);
        panel.Children.Add(typeBadge);

        _lockedCount.FontSize = 11;
        _lockedCount.Foreground = Brush("SecondaryTextBrush");
        _lockedCount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_lockedCount, 3);
        panel.Children.Add(_lockedCount);
        return panel;
    }

    private FrameworkElement BuildLayout()
    {
        var root = new Grid
        {
            Width = 900,
            Height = 570,
            MaxWidth = 900,
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _sectionList.SelectionMode = ListViewSelectionMode.Single;
        _sectionList.IsItemClickEnabled = true;
        _sectionList.Padding = new Thickness(0, 8, 0, 8);
        _sectionList.Background = new SolidColorBrush(Color.FromArgb(24, 0, 0, 0));
        foreach (var section in _sections)
        {
            _sectionList.Items.Add(new ListViewItem
            {
                Content = section.Name,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Padding = new Thickness(14, 9, 14, 9),
            });
        }
        _sectionList.SelectionChanged += SectionList_SelectionChanged;
        Grid.SetRowSpan(_sectionList, 2);
        root.Children.Add(_sectionList);

        _sectionHost.Padding = new Thickness(24, 20, 24, 16);
        Grid.SetColumn(_sectionHost, 1);
        root.Children.Add(_sectionHost);

        var resetPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Padding = new Thickness(24, 10, 24, 0),
            Visibility = IsLockable ? Visibility.Visible : Visibility.Collapsed,
        };
        var resetButton = new Button { Content = "Reset to Provider" };
        resetPanel.Children.Add(resetButton);
        Grid.SetRow(resetPanel, 1);
        Grid.SetColumn(resetPanel, 1);
        root.Children.Add(resetPanel);

        var resetConfirmation = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(184, 0, 0, 0)),
            Visibility = Visibility.Collapsed,
        };
        Canvas.SetZIndex(resetConfirmation, 100);
        Grid.SetRowSpan(resetConfirmation, 2);
        Grid.SetColumnSpan(resetConfirmation, 2);
        var confirmationCard = new Border
        {
            Width = 460,
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(12),
            Background = Brush("SurfaceRaisedBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var confirmationContent = new StackPanel { Spacing = 16 };
        confirmationContent.Children.Add(new TextBlock
        {
            Text = "Reset to Provider",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
        });
        confirmationContent.Children.Add(new TextBlock
        {
            Text = "This will unlock all fields and refresh metadata from your providers. Any manual edits will be overwritten on the next refresh.",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("SecondaryTextBrush"),
        });
        var confirmationActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancelReset = new Button { Content = "Cancel" };
        var confirmReset = new Button
        {
            Content = "Reset & Refresh",
            Style = (Style)Application.Current.Resources["DestructiveButtonStyle"],
        };
        cancelReset.Click += (_, _) => resetConfirmation.Visibility = Visibility.Collapsed;
        confirmReset.Click += async (_, _) =>
        {
            try
            {
                cancelReset.IsEnabled = false;
                confirmReset.IsEnabled = false;
                confirmReset.Content = "Resetting…";
                await _adminApi.RefreshItemMetadataAsync(_item.ContentId, "quick");
                HasAppliedChanges = true;
                _toast.Success("Metadata refresh started.");
                Hide();
            }
            catch (Exception ex)
            {
                _toast.Error(ex.Message);
                cancelReset.IsEnabled = true;
                confirmReset.IsEnabled = true;
                confirmReset.Content = "Reset & Refresh";
            }
        };
        confirmationActions.Children.Add(cancelReset);
        confirmationActions.Children.Add(confirmReset);
        confirmationContent.Children.Add(confirmationActions);
        confirmationCard.Child = confirmationContent;
        resetConfirmation.Children.Add(confirmationCard);
        root.Children.Add(resetConfirmation);
        resetButton.Click += (_, _) =>
        {
            resetConfirmation.Visibility = Visibility.Visible;
            cancelReset.Focus(FocusState.Programmatic);
        };

        _sectionList.SelectedIndex = 0;
        return root;
    }

    private void InitializeFields()
    {
        _title.Text = _item.Title ?? "";
        _sortTitle.Text = _item.SortTitle ?? "";
        _originalTitle.Text = _item.OriginalTitle ?? "";
        _overview.Text = _item.Overview ?? "";
        _overview.AcceptsReturn = true;
        _overview.TextWrapping = TextWrapping.Wrap;
        _overview.MinHeight = 110;
        _tagline.Text = _item.Tagline ?? "";
        _tagline.PlaceholderText = "No tagline";
        _contentRating.Text = _item.ContentRating ?? "";
        _year.Value = _item.Year > 0 ? _item.Year : double.NaN;
        _runtime.Value = _item.Runtime > 0 ? _item.Runtime : double.NaN;
        _ratingImdb.Value = _item.RatingImdb ?? double.NaN;
        _ratingTmdb.Value = _item.RatingTmdb ?? double.NaN;
        _ratingRtCritic.Value = _item.RatingRtCritic ?? double.NaN;
        _ratingRtAudience.Value = _item.RatingRtAudience ?? double.NaN;
        _seasonNumber.Value = _item.SeasonNumber ?? double.NaN;
        _episodeNumber.Value = _item.EpisodeNumber ?? double.NaN;
        _genres.Text = string.Join(", ", _item.Genres ?? []);
        _studios.Text = string.Join(", ", _item.Studios ?? []);
        _networks.Text = string.Join(", ", _item.Networks ?? []);
        _countries.Text = string.Join(", ", _item.Countries ?? []);
        _imdbId.Text = _item.ImdbId ?? "";
        _tmdbId.Text = _item.TmdbId ?? "";
        _tvdbId.Text = _item.TvdbId ?? "";
        _releaseDate.Date = ParseDate(_item.ReleaseDate);
        _firstAirDate.Date = ParseDate(_item.FirstAirDate);
        _lastAirDate.Date = ParseDate(_item.LastAirDate);
        _airDate.Date = ParseDate(_item.AirDate);
        _airTime.Text = _item.AirTime ?? "";
        foreach (var timezone in AirTimezones)
            _airTimezone.Items.Add(timezone);
        _airTimezone.Text = _item.AirTimezone ?? "";
        _status.Items.Add("Continuing");
        _status.Items.Add("Ended");
        _status.SelectedItem = string.IsNullOrWhiteSpace(_item.Status) ? "Continuing" : _item.Status;

        Watch(_title, FieldName);
        Watch(_sortTitle, FieldName);
        Watch(_originalTitle, FieldName);
        Watch(_tagline, FieldName);
        Watch(_overview, FieldOverview);
        Watch(_genres, FieldGenres);
        Watch(_studios, FieldStudios);
        Watch(_networks, FieldStudios);
        Watch(_countries, FieldTags);
        Watch(_contentRating, FieldContentRating);
        Watch(_airTime, FieldAirSchedule);
        Watch(_year, FieldReleaseDates);
        Watch(_releaseDate, FieldReleaseDates);
        Watch(_firstAirDate, FieldReleaseDates);
        Watch(_lastAirDate, FieldReleaseDates);
        Watch(_runtime, FieldRuntime);
        Watch(_ratingImdb, FieldRating);
        Watch(_ratingTmdb, FieldRating);
        Watch(_ratingRtCritic, FieldRating);
        Watch(_ratingRtAudience, FieldRating);
        _airTimezone.TextSubmitted += (_, _) => AutoLock(FieldAirSchedule);
        _airTimezone.SelectionChanged += (_, _) => AutoLock(FieldAirSchedule);
    }

    private void BuildSections()
    {
        _sections.Add(("General", BuildGeneralSection()));
        _sections.Add(("Dates & Ratings", BuildDatesSection()));
        if (_item.Type is "movie" or "series")
            _sections.Add(("Tags & Genres", BuildTagsSection()));
        _sections.Add(("External IDs", BuildIdsSection()));
        if (_item.Type is "movie" or "series" or "season")
            _sections.Add(("Images", BuildImagesSection()));
    }

    private FrameworkElement BuildGeneralSection()
    {
        var panel = FormPanel();
        panel.Children.Add(Field("Title", _title, FieldName));
        panel.Children.Add(TwoColumns(
            Field("Sort Title", _sortTitle, FieldName),
            Field("Original Title", _originalTitle, FieldName)));
        panel.Children.Add(Field("Overview", _overview, FieldOverview));
        panel.Children.Add(Field("Tagline", _tagline, FieldName));
        panel.Children.Add(Field("Content Rating", _contentRating, FieldContentRating));

        if (_item.Type == "movie")
            panel.Children.Add(Field("Runtime (min)", _runtime, FieldRuntime));
        else if (_item.Type == "series")
            panel.Children.Add(Field("Status", _status));
        else if (_item.Type == "season")
            panel.Children.Add(Field("Season Number", _seasonNumber));
        else if (_item.Type == "episode")
        {
            var disabledSeason = NumberField();
            disabledSeason.Value = _seasonNumber.Value;
            disabledSeason.IsEnabled = false;
            panel.Children.Add(TwoColumns(
                Field("Season Number", disabledSeason),
                Field("Episode Number", _episodeNumber)));
            panel.Children.Add(Field("Runtime (min)", _runtime, FieldRuntime));
        }
        panel.Children.Add(BuildTranslationPanel());
        return Scroll(panel);
    }

    private FrameworkElement BuildTranslationPanel()
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        heading.Children.Add(new FontIcon
        {
            Glyph = "\uE8C1",
            FontSize = 15,
            Foreground = Brush("SecondaryTextBrush"),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Translate with AI",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });
        _translationPanel.Children.Add(heading);

        var scope = _item.Type switch
        {
            "series" => "Translates the overview and tagline plus all season and episode overviews",
            "movie" => "Translates the overview and tagline",
            _ => "Translates the overview",
        };
        _translationPanel.Children.Add(new TextBlock
        {
            Text = scope + " into the chosen language. Translations are served to libraries using that metadata language; provider data replaces them when it becomes available.",
            FontSize = 11,
            Foreground = Brush("SecondaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        _translationLanguage.Items.Add(new ComboBoxItem { Content = "Select…", Tag = "", IsSelected = true });
        foreach (var language in MediaLanguageCatalog.All)
            _translationLanguage.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        controls.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Language", FontSize = 11 },
                _translationLanguage,
            },
        });
        controls.Children.Add(_translationForce);
        _translateButton.VerticalAlignment = VerticalAlignment.Bottom;
        _translateButton.Click += TranslateButton_Click;
        controls.Children.Add(_translateButton);
        _translationPanel.Children.Add(controls);
        _translationProgress.Foreground = Brush("SecondaryTextBrush");
        _translationPanel.Children.Add(_translationProgress);

        _translationContainer = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12),
            Child = _translationPanel,
        };
        return _translationContainer;
    }

    private FrameworkElement BuildDatesSection()
    {
        var panel = FormPanel();
        if (_item.Type == "movie")
        {
            panel.Children.Add(TwoColumns(
                Field("Year", _year, FieldReleaseDates),
                Field("Release Date", _releaseDate, FieldReleaseDates)));
        }
        else if (_item.Type == "series")
        {
            panel.Children.Add(TwoColumns(
                Field("Year", _year, FieldReleaseDates),
                Field("First Air Date", _firstAirDate, FieldReleaseDates)));
            panel.Children.Add(Field("Last Air Date", _lastAirDate, FieldReleaseDates));
            panel.Children.Add(TwoColumns(
                Field("Air Time", _airTime, FieldAirSchedule),
                Field("Air Timezone", _airTimezone, FieldAirSchedule)));
        }
        else
        {
            panel.Children.Add(Field("Air Date", _airDate));
        }

        if (_item.Type is "movie" or "series")
        {
            panel.Children.Add(new TextBlock
            {
                Text = "RATINGS",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                CharacterSpacing = 100,
                Foreground = Brush("SecondaryTextBrush"),
                Margin = new Thickness(0, 8, 0, 0),
            });
            panel.Children.Add(TwoColumns(
                Field("IMDb Rating", _ratingImdb, FieldRating),
                Field("TMDB Rating", _ratingTmdb, FieldRating)));
            panel.Children.Add(TwoColumns(
                Field("RT Critic Score", _ratingRtCritic, FieldRating),
                Field("RT Audience Score", _ratingRtAudience, FieldRating)));
        }
        return Scroll(panel);
    }

    private FrameworkElement BuildTagsSection()
    {
        var panel = FormPanel();
        panel.Children.Add(Field("Genres", _genres, FieldGenres, "Add genre..."));
        panel.Children.Add(Field("Studios", _studios, FieldStudios, "Add studio..."));
        if (_item.Type == "series")
            panel.Children.Add(Field("Networks", _networks, FieldStudios, "Add network..."));
        panel.Children.Add(Field("Countries", _countries, FieldTags, "Add country..."));
        return Scroll(panel);
    }

    private FrameworkElement BuildIdsSection()
    {
        var panel = FormPanel();
        _imdbId.PlaceholderText = "tt...";
        panel.Children.Add(Field("IMDb ID", _imdbId));
        panel.Children.Add(Field("TMDB ID", _tmdbId));
        panel.Children.Add(Field("TVDB ID", _tvdbId));
        return Scroll(panel);
    }

    private FrameworkElement BuildImagesSection()
    {
        _imagesPanel.Children.Add(new Border
        {
            Background = Brush("SurfaceRaisedBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Child = new TextBlock
            {
                Text = "Image changes apply immediately and are not affected by Cancel.",
                FontSize = 11,
                Foreground = Brush("SecondaryTextBrush"),
            },
        });

        AddImageTab("poster", "Posters");
        if (_item.Type != "season")
        {
            AddImageTab("backdrop", "Backdrops");
            AddImageTab("logo", "Logos");
        }
        _imagesPanel.Children.Add(_imageTabs);

        _imageStatus.Text = "Loading images…";
        _imageStatus.Foreground = Brush("SecondaryTextBrush");
        _imageStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _imageStatus.Margin = new Thickness(0, 24, 0, 12);
        _imagesPanel.Children.Add(_imageStatus);
        _imagesPanel.Children.Add(_imageGrid);
        _applyImage.Click += ApplyImage_Click;
        _imagesPanel.Children.Add(_applyImage);
        return _imagesPanel;
    }

    private async Task LoadTranslationPanelAsync()
    {
        try
        {
            var status = await _catalogApi.GetMetadataAiStatusAsync(_translationCts.Token);
            if (!status.Enabled || _translationCts.IsCancellationRequested) return;
            if (_translationContainer != null)
                _translationContainer.Visibility = Visibility.Visible;

            var jobs = await _adminApi.GetItemMetadataTranslationJobsAsync(
                _item.ContentId,
                _translationCts.Token);
            var active = jobs.Jobs.FirstOrDefault(job => job.IsActive);
            if (active != null)
                await PollTranslationJobAsync(active.Id, _translationCts.Token);
        }
        catch (OperationCanceledException) { }
        catch
        {
            // This optional panel stays hidden when status discovery is unavailable.
        }
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_translationLanguage.SelectedItem is not ComboBoxItem { Tag: string language }
            || string.IsNullOrWhiteSpace(language))
        {
            _toast.Error("Pick a target language first.");
            return;
        }

        SetTranslationBusy(true);
        try
        {
            var response = await _adminApi.StartItemMetadataTranslationAsync(
                _item.ContentId,
                new TranslateItemMetadataRequest
                {
                    TargetLanguage = language,
                    IncludeChildren = _item.Type == "series",
                    Force = _translationForce.IsOn,
                },
                _translationCts.Token);
            await PollTranslationJobAsync(response.Job.Id, _translationCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetTranslationBusy(false);
            _translationProgress.Text = ex.Message;
            _translationProgress.Visibility = Visibility.Visible;
            _toast.Error(ex.Message);
        }
    }

    private async Task PollTranslationJobAsync(int jobId, CancellationToken ct)
    {
        SetTranslationBusy(true);
        while (!ct.IsCancellationRequested)
        {
            var response = await _adminApi.GetItemMetadataTranslationJobsAsync(_item.ContentId, ct);
            var job = response.Jobs.FirstOrDefault(candidate => candidate.Id == jobId)
                ?? response.Jobs.FirstOrDefault(candidate => candidate.IsActive)
                ?? response.Jobs.FirstOrDefault();
            if (job == null)
            {
                SetTranslationBusy(false);
                return;
            }

            var progress = string.IsNullOrWhiteSpace(job.ProgressMessage) ? "Working" : job.ProgressMessage;
            _translationProgress.Text = job.FieldsTotal > 0
                ? $"{progress}… {job.FieldsDone}/{job.FieldsTotal} fields"
                : progress + "…";
            _translationProgress.Visibility = Visibility.Visible;

            if (!job.IsActive)
            {
                SetTranslationBusy(false);
                if (job.Status == "completed")
                {
                    HasAppliedChanges = true;
                    _translationProgress.Text = job.FieldsTotal == 0
                        ? "Nothing to translate — all descriptions are already localized."
                        : $"Translated {job.FieldsDone} description{(job.FieldsDone == 1 ? "" : "s")}.";
                    _toast.Success(_translationProgress.Text);
                }
                else if (job.Status == "failed")
                {
                    _translationProgress.Text = string.IsNullOrWhiteSpace(job.ErrorMessage)
                        ? "Translation failed."
                        : job.ErrorMessage;
                    _toast.Error(_translationProgress.Text);
                }
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
        }
    }

    private void SetTranslationBusy(bool busy)
    {
        _translationLanguage.IsEnabled = !busy;
        _translationForce.IsEnabled = !busy;
        _translateButton.IsEnabled = !busy;
        _translateButton.Content = busy ? "Translating…" : "Translate";
    }

    private void AddImageTab(string type, string label)
    {
        var button = new Button
        {
            Content = label,
            Tag = type,
            Padding = new Thickness(12, 6, 12, 6),
        };
        button.Click += (_, _) =>
        {
            _activeImageType = type;
            _selectedImage = null;
            RenderImages();
        };
        _imageTabs.Children.Add(button);
    }

    private async Task LoadImagesAsync()
    {
        if (_imageLoadStarted) return;
        _imageLoadStarted = true;
        try
        {
            _images = await _adminApi.GetItemImagesAsync(_item.ContentId);
            RenderImages();
        }
        catch (Exception ex)
        {
            _imageStatus.Text = "Failed to load images.";
            _toast.Error(ex.Message);
        }
    }

    private void RenderImages()
    {
        _imageGrid.Items.Clear();
        _applyImage.IsEnabled = _selectedImage != null;
        if (_images == null) return;

        var providerErrors = _images.ProviderErrors;
        if (providerErrors?.Count > 0)
        {
            _imageStatus.Text = "Could not load from " +
                string.Join(", ", providerErrors.Keys.Select(key => key.ToUpperInvariant()));
            _imageStatus.Foreground = new SolidColorBrush(Color.FromArgb(255, 232, 184, 110));
        }
        else
        {
            _imageStatus.Text = "";
        }

        var filtered = _images.Images
            .Where(image => string.Equals(image.Type, _activeImageType, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (filtered.Count == 0)
        {
            _imageStatus.Text = "No images available.";
            return;
        }

        var current = _activeImageType switch
        {
            "poster" => _images.Current.PosterUrl,
            "backdrop" => _images.Current.BackdropUrl,
            "logo" => _images.Current.LogoUrl,
            _ => null,
        };

        foreach (var remote in filtered)
        {
            var isSelected = ReferenceEquals(remote, _selectedImage);
            var isCurrent = string.Equals(remote.OriginalUrl, current, StringComparison.Ordinal);
            var card = new Grid
            {
                Width = _activeImageType == "poster" ? 120 : 180,
                Height = _activeImageType == "poster" ? 180 : 102,
            };
            var image = new Image { Stretch = Stretch.UniformToFill };
            if (Uri.TryCreate(remote.Url, UriKind.Absolute, out var uri))
                image.Source = new BitmapImage(uri);
            card.Children.Add(image);

            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 5, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(5),
                Child = new TextBlock
                {
                    Text = remote.ProviderId.ToUpperInvariant(),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Colors.White),
                },
            };
            card.Children.Add(badge);
            if (isCurrent)
            {
                card.Children.Add(new Border
                {
                    Background = Brush("AccentFillColorDefaultBrush"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(5),
                    Child = new TextBlock { Text = "✓ Current", FontSize = 9 },
                });
            }

            var button = new Button
            {
                Content = card,
                Padding = new Thickness(0),
                Margin = new Thickness(3),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                BorderBrush = isSelected
                    ? Brush("AccentFillColorDefaultBrush")
                    : Brush("CardStrokeColorDefaultBrush"),
                CornerRadius = new CornerRadius(8),
                Tag = remote,
            };
            button.Click += (_, _) =>
            {
                _selectedImage = ReferenceEquals(_selectedImage, remote) ? null : remote;
                RenderImages();
            };
            _imageGrid.Items.Add(button);
        }
    }

    private async void ApplyImage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedImage == null) return;
        var selected = _selectedImage;
        try
        {
            _applyImage.IsEnabled = false;
            _applyImage.Content = "Applying…";
            var applied = await _adminApi.ApplyItemImageAsync(_item.ContentId, new ApplyItemImageRequest
            {
                OriginalUrl = selected.OriginalUrl,
                Type = selected.Type,
                ProviderId = selected.ProviderId,
            });
            HasAppliedChanges = true;
            _images ??= new ItemImagesResponse();
            switch (selected.Type)
            {
                case "poster": _images.Current.PosterUrl = selected.OriginalUrl; break;
                case "backdrop": _images.Current.BackdropUrl = selected.OriginalUrl; break;
                case "logo": _images.Current.LogoUrl = selected.OriginalUrl; break;
            }
            _selectedImage = null;
            _toast.Success("Image applied successfully.");
            RenderImages();
        }
        catch (Exception ex)
        {
            _toast.Error(ex.Message);
        }
        finally
        {
            _applyImage.Content = "Apply image";
            _applyImage.IsEnabled = _selectedImage != null;
        }
    }

    private async void SaveButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var payload = BuildChangedPayload();
            if (payload.Count == 0)
                return;

            IsPrimaryButtonEnabled = false;
            await _adminApi.UpdateItemMetadataAsync(_item.ContentId, payload);
            HasAppliedChanges = true;
            _toast.Success("Metadata updated.");
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            IsPrimaryButtonEnabled = true;
            _toast.Error(ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private Dictionary<string, object?> BuildChangedPayload()
    {
        var current = BuildPayload();
        var original = BuildOriginalPayload();
        return current
            .Where(pair => !original.TryGetValue(pair.Key, out var originalValue)
                || !PayloadValuesEqual(pair.Value, originalValue))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private Dictionary<string, object?> BuildPayload()
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = _title.Text.Trim(),
            ["sort_title"] = _sortTitle.Text.Trim(),
            ["original_title"] = _originalTitle.Text.Trim(),
            ["overview"] = _overview.Text.Trim(),
            ["tagline"] = _tagline.Text.Trim(),
            ["content_rating"] = _contentRating.Text.Trim(),
            ["imdb_id"] = _imdbId.Text.Trim(),
            ["tmdb_id"] = _tmdbId.Text.Trim(),
            ["tvdb_id"] = _tvdbId.Text.Trim(),
        };

        if (_item.Type is "movie" or "series")
        {
            payload["year"] = IntValue(_year);
            payload["rating_imdb"] = NullableValue(_ratingImdb);
            payload["rating_tmdb"] = NullableValue(_ratingTmdb);
            payload["rating_rt_critic"] = IntValue(_ratingRtCritic);
            payload["rating_rt_audience"] = IntValue(_ratingRtAudience);
            payload["genres"] = Tags(_genres.Text);
            payload["studios"] = Tags(_studios.Text);
            payload["countries"] = Tags(_countries.Text);
            payload["locked_fields"] = _lockedFields.OrderBy(value => value).ToArray();
        }

        switch (_item.Type)
        {
            case "movie":
                payload["runtime"] = IntValue(_runtime);
                payload["release_date"] = DateValue(_releaseDate);
                break;
            case "series":
                payload["networks"] = Tags(_networks.Text);
                payload["first_air_date"] = DateValue(_firstAirDate);
                payload["last_air_date"] = DateValue(_lastAirDate);
                payload["air_time"] = EmptyToNull(_airTime.Text);
                payload["air_timezone"] = _airTimezone.Text.Trim();
                payload["status"] = _status.SelectedItem?.ToString() ?? "Continuing";
                break;
            case "season":
                payload["season_number"] = IntValue(_seasonNumber);
                payload["air_date"] = DateValue(_airDate);
                break;
            case "episode":
                payload["episode_number"] = IntValue(_episodeNumber);
                payload["runtime"] = IntValue(_runtime);
                payload["air_date"] = DateValue(_airDate);
                break;
        }

        return payload;
    }

    private Dictionary<string, object?> BuildOriginalPayload()
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = (_item.Title ?? "").Trim(),
            ["sort_title"] = (_item.SortTitle ?? "").Trim(),
            ["original_title"] = (_item.OriginalTitle ?? "").Trim(),
            ["overview"] = (_item.Overview ?? "").Trim(),
            ["tagline"] = (_item.Tagline ?? "").Trim(),
            ["content_rating"] = (_item.ContentRating ?? "").Trim(),
            ["imdb_id"] = (_item.ImdbId ?? "").Trim(),
            ["tmdb_id"] = (_item.TmdbId ?? "").Trim(),
            ["tvdb_id"] = (_item.TvdbId ?? "").Trim(),
        };

        if (_item.Type is "movie" or "series")
        {
            payload["year"] = _item.Year > 0 ? _item.Year : null;
            payload["rating_imdb"] = _item.RatingImdb;
            payload["rating_tmdb"] = _item.RatingTmdb;
            payload["rating_rt_critic"] = _item.RatingRtCritic;
            payload["rating_rt_audience"] = _item.RatingRtAudience;
            payload["genres"] = NormalizeTags(_item.Genres);
            payload["studios"] = NormalizeTags(_item.Studios);
            payload["countries"] = NormalizeTags(_item.Countries);
            payload["locked_fields"] = (_item.LockedFields ?? []).OrderBy(value => value).ToArray();
        }

        switch (_item.Type)
        {
            case "movie":
                payload["runtime"] = _item.Runtime > 0 ? _item.Runtime : null;
                payload["release_date"] = NormalizeDate(_item.ReleaseDate);
                break;
            case "series":
                payload["networks"] = NormalizeTags(_item.Networks);
                payload["first_air_date"] = NormalizeDate(_item.FirstAirDate);
                payload["last_air_date"] = NormalizeDate(_item.LastAirDate);
                payload["air_time"] = EmptyToNull(_item.AirTime);
                payload["air_timezone"] = (_item.AirTimezone ?? "").Trim();
                payload["status"] = string.IsNullOrWhiteSpace(_item.Status) ? "Continuing" : _item.Status;
                break;
            case "season":
                payload["season_number"] = _item.SeasonNumber;
                payload["air_date"] = NormalizeDate(_item.AirDate);
                break;
            case "episode":
                payload["episode_number"] = _item.EpisodeNumber;
                payload["runtime"] = _item.Runtime > 0 ? _item.Runtime : null;
                payload["air_date"] = NormalizeDate(_item.AirDate);
                break;
        }

        return payload;
    }

    private static bool PayloadValuesEqual(object? current, object? original)
    {
        if (ReferenceEquals(current, original)) return true;
        if (current is null || original is null) return false;
        if (current is IEnumerable<string> currentStrings && original is IEnumerable<string> originalStrings)
            return currentStrings.SequenceEqual(originalStrings, StringComparer.Ordinal);
        if (current is IEnumerable<int> currentInts && original is IEnumerable<int> originalInts)
            return currentInts.SequenceEqual(originalInts);
        return Equals(current, original);
    }

    private static string? NormalizeDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToString("yyyy-MM-dd") : null;

    private static string[] NormalizeTags(IEnumerable<string>? values) => (values ?? [])
        .Select(value => value.Trim())
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = Math.Max(0, _sectionList.SelectedIndex);
        if (index >= _sections.Count) return;
        _sectionHost.Children.Clear();
        _sectionHost.Children.Add(_sections[index].Content);
        if (_sections[index].Name == "Images")
            _ = LoadImagesAsync();
    }

    private FrameworkElement Field(
        string label,
        FrameworkElement control,
        int? lockField = null,
        string? hint = null)
    {
        var panel = new StackPanel { Spacing = 6 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Brush("SecondaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (lockField.HasValue && IsLockable)
        {
            var button = new Button
            {
                Tag = lockField.Value,
                Padding = new Thickness(5, 1, 5, 1),
                MinHeight = 22,
                MinWidth = 22,
                FontSize = 10,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            button.Click += LockButton_Click;
            if (!_lockButtons.TryGetValue(lockField.Value, out var buttons))
                _lockButtons[lockField.Value] = buttons = [];
            buttons.Add(button);
            UpdateLockButton(button, lockField.Value);
            Grid.SetColumn(button, 1);
            header.Children.Add(button);
        }
        panel.Children.Add(header);
        if (!string.IsNullOrWhiteSpace(hint) && control is TextBox textBox)
            textBox.PlaceholderText = hint;
        panel.Children.Add(control);
        return panel;
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int field }) return;
        if (!_lockedFields.Add(field))
            _lockedFields.Remove(field);
        RefreshLockButtons(field);
        UpdateLockedCount();
    }

    private void AutoLock(int field)
    {
        if (!IsLockable || !_lockedFields.Add(field)) return;
        RefreshLockButtons(field);
        UpdateLockedCount();
    }

    private void RefreshLockButtons(int field)
    {
        if (!_lockButtons.TryGetValue(field, out var buttons)) return;
        foreach (var button in buttons)
            UpdateLockButton(button, field);
    }

    private void UpdateLockButton(Button button, int field)
    {
        var locked = _lockedFields.Contains(field);
        button.Content = locked ? "🔒 locked" : "🔓";
        button.Foreground = locked
            ? new SolidColorBrush(Color.FromArgb(220, 232, 184, 110))
            : Brush("TertiaryTextBrush");
        ToolTipService.SetToolTip(button, locked
            ? "Locked — click to unlock"
            : "Unlocked — edits will auto-lock");
    }

    private void UpdateLockedCount()
    {
        _lockedCount.Text = IsLockable && _lockedFields.Count > 0
            ? $"🔒 {_lockedFields.Count} locked"
            : "";
    }

    private void Watch(TextBox control, int field) =>
        control.TextChanged += (_, _) => AutoLock(field);
    private void Watch(NumberBox control, int field) =>
        control.ValueChanged += (_, _) => AutoLock(field);
    private void Watch(CalendarDatePicker control, int field) =>
        control.DateChanged += (_, _) => AutoLock(field);

    private bool IsLockable => _item.Type is "movie" or "series";

    private static StackPanel FormPanel() => new() { Spacing = 16 };

    private static ScrollViewer Scroll(FrameworkElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollMode = ScrollMode.Enabled,
    };

    private static Grid TwoColumns(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static NumberBox NumberField(
        double minimum = double.MinValue,
        double maximum = double.MaxValue,
        double change = 1) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        SmallChange = change,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
    };

    private static CalendarDatePicker DateField() => new()
    {
        DateFormat = "{day.integer(2)}/{month.integer(2)}/{year.full}",
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string? DateValue(CalendarDatePicker picker) =>
        picker.Date?.ToString("yyyy-MM-dd");

    private static int? IntValue(NumberBox box) =>
        double.IsNaN(box.Value) ? null : (int)Math.Round(box.Value);

    private static double? NullableValue(NumberBox box) =>
        double.IsNaN(box.Value) ? null : box.Value;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[] Tags(string value) => value
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string TypeLabel(string type) => type switch
    {
        "movie" => "Movie",
        "series" => "Series",
        "season" => "Season",
        "episode" => "Episode",
        _ => type,
    };

    private static SolidColorBrush Brush(string resourceName)
    {
        if (Application.Current.Resources.TryGetValue(resourceName, out var resource) &&
            resource is SolidColorBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Colors.Transparent);
    }
}
