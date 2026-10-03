using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace SiloPlayer.Views;

public sealed partial class CollectionsPage : Page
{
    private const string CollectionDragPrefix = "collection:";
    private const string GroupDragPrefix = "group:";
    public CollectionsViewModel ViewModel { get; }
    private ContentDialog? _templateDialog;
    private StackPanel? _templateCardsPanel;
    private StackPanel? _templateCategoriesPanel;
    private FrameworkElement? _templateGalleryDescription;
    private FrameworkElement? _templateGallerySearchBox;
    private FrameworkElement? _templateGalleryCategoryScroll;
    private int _templateGalleryColumnCount = 3;
    private string _templateCategoryFilter = "all";
    private string _templateSearch = "";
    private bool _openTemplatesOnLoad;
    private bool _returnAfterTemplates;
    private bool _collectionBuildQueued;
    private int _collectionColumnCount;
    private int _serverCardSizeClass;

    public CollectionsPage()
    {
        ViewModel = App.Services.GetRequiredService<CollectionsViewModel>();
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        CollectionsLoadingRepeater.ItemsSource = Enumerable.Range(0, 6).ToArray();
        var serverSkeletonItems = Enumerable.Range(0, 7).ToArray();
        ServerCollectionsLoadingRowOne.ItemsSource = serverSkeletonItems;
        ServerCollectionsLoadingRowTwo.ItemsSource = serverSkeletonItems;

        ViewModel.Collections.CollectionChanged += (_, _) => QueueCollectionBuild();
        ViewModel.Groups.CollectionChanged += (_, _) => QueueCollectionBuild();
        ViewModel.ServerLibraries.CollectionChanged += (_, _) => QueueCollectionBuild();

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.IsEmpty))
                DispatcherQueue.TryEnqueue(UpdateEmptyState);
            else if (args.PropertyName == nameof(ViewModel.IsLoadingServerCollections))
                DispatcherQueue.TryEnqueue(BuildServerCollectionRows);
        };
        SizeChanged += CollectionsPage_SizeChanged;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCollectionsCommand.ExecuteAsync(null);
        BuildCollectionCards();
        if (_openTemplatesOnLoad)
        {
            _openTemplatesOnLoad = false;
            await ShowCollectionTemplateGalleryAsync();
            if (_returnAfterTemplates)
                App.Services.GetRequiredService<NavigationService>().GoBack();
        }
    }

    private void QueueCollectionBuild()
    {
        if (_collectionBuildQueued) return;
        _collectionBuildQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _collectionBuildQueued = false;
            BuildCollectionCards();
        });
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is CollectionsNavigationArgs args)
        {
            _openTemplatesOnLoad = args.OpenTemplates;
            _returnAfterTemplates = args.ReturnAfterTemplates;
        }
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = ViewModel.IsEmpty && !ViewModel.IsLoading
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CreateCollection_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<SmartCollectionWizardPage>(new SmartCollectionWizardNavigationArgs());
    }

    private void SmartWizard_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<SmartCollectionWizardPage>(new SmartCollectionWizardNavigationArgs());
    }

    private async void BrowseTemplates_Click(object sender, RoutedEventArgs e)
    {
        await ShowCollectionTemplateGalleryAsync();
    }

    private async Task ShowCollectionTemplateGalleryAsync()
    {
        await ViewModel.LoadTemplateFlowAsync();

        var dialog = BuildTemplateGalleryDialog();
        await dialog.ShowAsync();

        _templateDialog = null;
        _templateCardsPanel = null;
        _templateCategoriesPanel = null;
        _templateGalleryDescription = null;
        _templateGallerySearchBox = null;
        _templateGalleryCategoryScroll = null;
        _templateCategoryFilter = "all";
        _templateSearch = "";
    }

    private ContentDialog BuildTemplateGalleryDialog()
    {
        var viewport = XamlRoot?.Size.Width ?? ActualWidth;
        var compact = viewport < 640;
        var dialogWidth = Math.Min(viewport - 32, viewport >= 1024 ? 896 : 768);
        _templateGalleryColumnCount = compact ? 1 : viewport < 1024 ? 2 : 3;
        var dialog = new ContentDialog
        {
            Title = "Browse Collection Templates",
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close,
            MaxWidth = double.PositiveInfinity,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0)
        };
        // WinUI's ContentDialog template otherwise clamps custom content to its
        // historical 548px maximum, regardless of the values above.
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMinWidth"] = dialogWidth;
        EditorDialogPresentation.Configure(dialog, dialogWidth, new Thickness(24));
        // The native sizing pass clears an explicit Width on narrow windows.
        // Keep the visible shell constrained through the template's own minimum.
        dialog.Resources["ContentDialogMinWidth"] = dialogWidth;
        // The template's outer border takes two pixels in addition to the
        // scoped BackgroundElement; the visible shell retains its exact width.
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth + 2;
        dialog.Opened += (_, _) =>
        {
            var shell = EditorDialogPresentation.Descendants<Border>(dialog).FirstOrDefault(b => b.Name == "BackgroundElement");
            if (shell is not null) { shell.Width = dialogWidth; shell.MaxWidth = dialogWidth; shell.MaxHeight = Math.Max(160, (XamlRoot?.Size.Height ?? 900) - 64); }
        };
        _templateDialog = dialog;

        var root = new Grid
        {
            RowSpacing = 16
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var description = new TextBlock
        {
            Text = "Pick a curated source — TMDB or MDBList — and we'll seed a synced collection.",
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        };
        _templateGalleryDescription = description;
        Grid.SetRow(description, 0);
        root.Children.Add(description);

        var searchBox = new TextBox
        {
            PlaceholderText = "Search templates",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            MinHeight = 36
        };
        _templateGallerySearchBox = searchBox;
        searchBox.TextChanged += (_, _) =>
        {
            _templateSearch = searchBox.Text;
            RenderTemplateCards();
        };
        Grid.SetRow(searchBox, 1);
        root.Children.Add(searchBox);

        var categoriesPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        _templateCategoriesPanel = categoriesPanel;
        BuildTemplateCategoryButtons(categoriesPanel);
        var categoryScroll = new ScrollViewer
        {
            Content = categoriesPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled
        };
        _templateGalleryCategoryScroll = categoryScroll;
        Grid.SetRow(categoryScroll, 2);
        root.Children.Add(categoryScroll);

        _templateCardsPanel = new StackPanel { Spacing = 16 };
        if (!string.IsNullOrWhiteSpace(ViewModel.TemplateErrorMessage))
        {
            _templateCardsPanel.Children.Add(new TextBlock
            {
                Text = ViewModel.TemplateErrorMessage,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"]
            });
        }
        var scroll = new ScrollViewer
        {
            Content = _templateCardsPanel,
            MaxHeight = Math.Max(160, (XamlRoot?.Size.Height ?? 900) - 260),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroll, 3);
        root.Children.Add(scroll);

        dialog.Content = root;
        RenderTemplateCards();
        return dialog;
    }

    private void CollectionsPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 760;
        var gutter = e.NewSize.Width < 640 ? 16 : e.NewSize.Width < 1024 ? 24 : 40;
        var top = e.NewSize.Width < 640 ? 16 : 24;
        CollectionsPageShell.Padding = new Thickness(gutter, top, gutter, 40);
        CollectionsLoadingShell.Padding = new Thickness(gutter, top, gutter, 40);
        CollectionsHeaderGrid.ColumnDefinitions[1].Width = compact
            ? new GridLength(0)
            : GridLength.Auto;
        Grid.SetRow(CollectionsHeaderActions, compact ? 1 : 0);
        Grid.SetColumn(CollectionsHeaderActions, compact ? 0 : 1);
        Grid.SetColumnSpan(CollectionsHeaderActions, compact ? 2 : 1);
        CollectionsHeaderActions.HorizontalAlignment = compact
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;

        var columnCount = e.NewSize.Width >= 1280 ? 3 : e.NewSize.Width >= 640 ? 2 : 1;
        var serverCardSizeClass = e.NewSize.Width >= 1024 ? 2 : e.NewSize.Width >= 640 ? 1 : 0;
        if (_collectionColumnCount != columnCount || _serverCardSizeClass != serverCardSizeClass)
        {
            _collectionColumnCount = columnCount;
            _serverCardSizeClass = serverCardSizeClass;
            QueueCollectionBuild();
        }
    }

    private void BuildTemplateCategoryButtons(StackPanel categoriesPanel)
    {
        categoriesPanel.Children.Clear();

        AddCategoryButton("all", "All templates");
        foreach (var group in ViewModel.TemplateGroups)
            AddCategoryButton(group.Category, group.Label);

        void AddCategoryButton(string category, string label)
        {
            var button = new Button
            {
                Content = category == "all" ? "All" : label,
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(14),
                Background = category == _templateCategoryFilter
                    ? (Brush)Application.Current.Resources["AccentBackgroundBrush"]
                    : (Brush)Application.Current.Resources["SurfaceBrush"],
                Foreground = category == _templateCategoryFilter
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["SecondaryTextBrush"]
            };
            button.Click += (_, _) =>
            {
                _templateCategoryFilter = category;
                BuildTemplateCategoryButtons(categoriesPanel);
                RenderTemplateCards();
            };
            categoriesPanel.Children.Add(button);
        }
    }

    private void RenderTemplateCards()
    {
        if (_templateCardsPanel == null)
            return;

        if (_templateDialog != null)
            _templateDialog.Title = "Browse Collection Templates";
        if (_templateGalleryDescription != null)
            _templateGalleryDescription.Visibility = Visibility.Visible;
        if (_templateGallerySearchBox != null)
            _templateGallerySearchBox.Visibility = Visibility.Visible;
        if (_templateGalleryCategoryScroll != null)
            _templateGalleryCategoryScroll.Visibility = Visibility.Visible;

        _templateCardsPanel.Children.Clear();

        var groups = ViewModel.TemplateGroups
            .Where(group => _templateCategoryFilter == "all" || group.Category == _templateCategoryFilter)
            .Select(group => new
            {
                group.Label,
                Templates = group.Templates.Where(TemplateMatchesSearch).ToList()
            })
            .Where(group => group.Templates.Count > 0)
            .ToList();

        if (groups.Count == 0)
        {
            _templateCardsPanel.Children.Add(new TextBlock
            {
                Text = "No templates match your filters.",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 120, 0, 0)
            });
            return;
        }

        foreach (var group in groups)
        {
            _templateCardsPanel.Children.Add(new TextBlock
            {
                Text = group.Label.ToUpperInvariant(),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                CharacterSpacing = 70,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            });

            var row = TemplateRow();
            for (var i = 0; i < group.Templates.Count; i++)
            {
                var card = BuildTemplateCard(group.Templates[i]); Grid.SetColumn(card, i % _templateGalleryColumnCount); row.Children.Add(card);
                if ((i + 1) % _templateGalleryColumnCount == 0)
                {
                    _templateCardsPanel.Children.Add(row);
                    row = TemplateRow();
                }
            }

            if (row.Children.Count > 0)
                _templateCardsPanel.Children.Add(row);
        }

        Grid TemplateRow()
        {
            var row = new Grid { ColumnSpacing = 12 };
            for (var column = 0; column < _templateGalleryColumnCount; column++) row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            return row;
        }

        bool TemplateMatchesSearch(CollectionTemplate template)
        {
            if (string.IsNullOrWhiteSpace(_templateSearch))
                return true;

            var query = _templateSearch.Trim();
            return template.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || template.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || template.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
                || template.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
    }

    private Button BuildTemplateCard(CollectionTemplate template)
    {
        Brush Brush(string key) => (Brush)Application.Current.Resources[key];
        Border Badge(string text, bool secondary) => new()
        {
            Background = Brush(secondary ? "SecondaryBackgroundBrush" : "AppBackgroundBrush"),
            BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(secondary ? 0 : 1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2),
            Child = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.Medium, Foreground = Brush("PrimaryTextBrush") }
        };
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        badges.Children.Add(Badge(FormatCollectionType(template.Source).ToUpperInvariant(), false));
        if (template.RequiresProfile) badges.Children.Add(Badge("Profile", true));
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new(10), Background = Brush("AccentBackgroundBrush"),
            Child = new TextBlock { Text = template.Icon, FontSize = 20, Foreground = Brush("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        Grid.SetColumn(badges, 1); header.Children.Add(badges);
        var copy = new StackPanel { Spacing = 4 };
        copy.Children.Add(new TextBlock { Text = template.Title, FontSize = 14, FontWeight = FontWeights.Medium, LineHeight = 17.5,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = Brush("PrimaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        copy.Children.Add(new TextBlock { Text = template.Description, FontSize = 12, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap, MaxLines = 3 });
        var media = template.MediaKind switch { "movie" => "Movies", "series" => "TV", _ => "Mixed" };
        var cron = template.DefaultSyncSchedule?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var schedule = "on schedule";
        if (cron?.Length == 5)
        {
            if (cron[1].StartsWith("*/")) schedule = $"every {cron[1][2..]} hours";
            else if (cron[1] == "*") schedule = "hourly";
            else if (cron[2] == "1" && cron[3] == "*") schedule = "monthly";
            else if (cron[2] == "*" && cron[3] == "*" && cron[4] == "*") schedule = "daily";
            else if (cron[2] == "*" && cron[3] == "*") schedule = "weekly";
        }
        var stack = new StackPanel { Spacing = 12, Children = { header, copy,
            new TextBlock { Text = media + (string.IsNullOrWhiteSpace(template.DefaultSyncSchedule) ? "" : $"  •  syncs {schedule}"), FontSize = 11,
                Foreground = Brush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis } } };
        var surface = new Border { Background = Brush("CardBackgroundBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new(1),
            CornerRadius = new(10), Padding = new(16), Child = stack, Tag = template };
        var card = new Button { Content = surface, Padding = new(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        AutomationProperties.SetName(card, $"Use {template.Title} collection template");
        card.PointerEntered += (_, _) => { surface.Background = Brush("SurfaceHoverBrush"); surface.BorderBrush = Brush("AccentBrush"); };
        card.PointerExited += (_, _) => { surface.Background = Brush("CardBackgroundBrush"); surface.BorderBrush = Brush("BorderBrush"); };
        card.Click += (_, _) => ShowTemplateConfigInGallery(template);
        return card;
    }
    private void ShowTemplateConfigInGallery(CollectionTemplate template)
    {
        if (_templateCardsPanel == null || _templateDialog == null)
            return;

        _templateDialog.Title = null;
        if (_templateGalleryDescription != null)
            _templateGalleryDescription.Visibility = Visibility.Collapsed;
        if (_templateGallerySearchBox != null)
            _templateGallerySearchBox.Visibility = Visibility.Collapsed;
        if (_templateGalleryCategoryScroll != null)
            _templateGalleryCategoryScroll.Visibility = Visibility.Collapsed;

        _templateCardsPanel.Children.Clear();
        _templateCardsPanel.Children.Add(new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72B", FontSize = 12 },
                    new TextBlock { Text = "Back to templates", FontSize = 13 }
                }
            }
        });
        if (_templateCardsPanel.Children[0] is Button backButton)
            backButton.Click += (_, _) => RenderTemplateCards();

        _templateCardsPanel.Children.Add(new TextBlock
        {
            Text = "Confirm details, then we'll create and sync the collection for you.",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        _templateCardsPanel.Children.Add(BuildTemplateConfigPanel(template, _templateDialog));
    }

    private static void ClipTemplatePoster(Border frame)
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(frame);
        var geometry = visual.Compositor.CreateRoundedRectangleGeometry();
        geometry.Size = new((float)frame.ActualWidth, (float)frame.ActualHeight); geometry.CornerRadius = new(10, 10);
        visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
    }

    private UIElement BuildTemplateConfigPanel(CollectionTemplate template, ContentDialog dialog)
    {
        ViewModel.MdblistResults.Clear();
        ViewModel.TemplateErrorMessage = null;

        var titleBox = new TextBox
        {
            Text = template.Title,
            PlaceholderText = "Collection title",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        var descriptionBox = new TextBox
        {
            Text = template.Description,
            PlaceholderText = "Optional summary",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        var maxItemsBox = new TextBox
        {
            Text = template.DefaultLimit > 0 ? template.DefaultLimit.ToString() : "",
            PlaceholderText = "No limit",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        var scheduleBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(10)
        };
        foreach (var item in new[]
        {
            ("none", "Manual only"),
            ("daily", "Daily"),
            ("weekly", "Weekly"),
            ("monthly", "Monthly")
        })
        {
            scheduleBox.Items.Add(new ComboBoxItem { Content = item.Item2, Tag = item.Item1 });
        }
        scheduleBox.SelectedIndex = GetScheduleIndex(template.DefaultSyncSchedule);

        var sharedToggle = new ToggleSwitch
        {
            OnContent = "",
            OffContent = "",
            IsOn = false
        };

        var libraryChecks = new List<CheckBox>();
        var libraryPanel = new StackPanel { Spacing = 4 };
        foreach (var library in ViewModel.Libraries)
        {
            var check = new CheckBox
            {
                Content = library.Name,
                Tag = library.Id,
                IsChecked = false
            };
            libraryChecks.Add(check);
            libraryPanel.Children.Add(check);
        }

        var watchFilterBox = BuildTaggedComboBox(new[]
        {
            ("all", "All"),
            ("unwatched", "Unwatched"),
            ("watched", "Watched")
        });
        var mediaFilterBox = BuildTaggedComboBox(new[]
        {
            ("all", "All"),
            ("movie", "Movies"),
            ("series", "Shows")
        });
        var defaultSortBox = BuildTaggedComboBox(CollectionDefaultSortOptions());

        var posterModeBox = BuildTaggedComboBox(new[]
        {
            ("default", "Server default"),
            ("custom", "Custom URL")
        });
        posterModeBox.SelectedIndex = string.IsNullOrWhiteSpace(template.PosterPath) ? 1 : 0;
        var posterChoices = new Grid { ColumnSpacing = 8 };
        posterChoices.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        posterChoices.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        posterChoices.RowDefinitions.Add(new() { Height = GridLength.Auto }); posterChoices.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var defaultChoice = new RadioButton { Content = "Server default", IsChecked = posterModeBox.SelectedIndex == 0, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 36 };
        var customChoice = new RadioButton { Content = "Custom URL", IsChecked = posterModeBox.SelectedIndex == 1, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 36 };
        var posterChoiceGroup = "template-poster-" + Guid.NewGuid().ToString("N");
        defaultChoice.GroupName = posterChoiceGroup; customChoice.GroupName = posterChoiceGroup;
        defaultChoice.Checked += (_, _) => posterModeBox.SelectedIndex = 0;
        customChoice.Checked += (_, _) => posterModeBox.SelectedIndex = 1;
        Grid.SetColumn(customChoice, 1); posterChoices.Children.Add(defaultChoice); posterChoices.Children.Add(customChoice);
        posterChoices.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 440;
            Grid.SetRow(customChoice, narrow ? 1 : 0); Grid.SetColumn(customChoice, narrow ? 0 : 1);
            Grid.SetColumnSpan(defaultChoice, narrow ? 2 : 1); Grid.SetColumnSpan(customChoice, narrow ? 2 : 1);
        };
        AutomationProperties.SetName(posterChoices, "Poster source");
        var customPosterBox = new TextBox
        {
            PlaceholderText = "https://example.com/poster.jpg",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            Visibility = posterModeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed
        };
        var defaultPosterPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Visibility = posterModeBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed,
            Children =
            {
                new FontIcon { Glyph = "\uE91B", FontSize = 22 },
                new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "Server default", FontSize = 13, FontWeight = FontWeights.SemiBold },
                        new TextBlock
                        {
                            Text = template.PosterPath ?? "",
                            FontSize = 11,
                            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            MaxWidth = 680
                        }
                    }
                }
            }
        };
        var defaultPosterUrl = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>().ResolveServerUrl(template.PosterPath);
        if (Uri.TryCreate(defaultPosterUrl, UriKind.Absolute, out var defaultPosterUri))
        {
            var posterFrame = new Border { Width = 56, Height = 80, CornerRadius = new CornerRadius(10), Child = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(defaultPosterUri), Stretch = Stretch.UniformToFill } };
            posterFrame.Loaded += (_, _) => ClipTemplatePoster(posterFrame);
            posterFrame.SizeChanged += (_, _) => ClipTemplatePoster(posterFrame);
            defaultPosterPanel.Children[0] = posterFrame;
        }
        posterModeBox.SelectionChanged += (_, _) =>
        {
            var custom = SelectedComboTag(posterModeBox) == "custom";
            customPosterBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            defaultPosterPanel.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
        };

        TextBox? mdblistUrlBox = null;
        var stack = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };
        var summaryHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        summaryHeader.Children.Add(new TextBlock { Text = template.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        summaryHeader.Children.Add(BuildBadge(FormatCollectionType(template.Source).ToUpperInvariant()));
        summaryHeader.Children.Add(BuildBadge(FormatMediaKind(template.MediaKind)));
        var summaryCopy = new StackPanel { Spacing = 4, Children = { summaryHeader,
            new TextBlock { Text = template.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] } } };
        var summaryGrid = new Grid { ColumnSpacing = 12 };
        summaryGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); summaryGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        summaryGrid.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new(12), Background = (Brush)Application.Current.Resources["AppBackgroundBrush"],
            Child = new TextBlock { Text = template.Icon, FontSize = 20, Foreground = (Brush)Application.Current.Resources["AccentBrush"], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        Grid.SetColumn(summaryCopy, 1); summaryGrid.Children.Add(summaryCopy);
        stack.Children.Add(new Border { Background = (Brush)Application.Current.Resources["SurfaceBrush"], BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new(1),
            CornerRadius = new(12), Padding = new(12), Child = summaryGrid });
        stack.Children.Add(MakeLabeledControl("Collection Title", titleBox));
        stack.Children.Add(MakeLabeledControl("Description", descriptionBox));

        if (template.Source == "mdblist")
        {
            mdblistUrlBox = new TextBox
            {
                Text = template.Mdblist?.Url ?? "",
                PlaceholderText = "https://mdblist.com/lists/.../json",
                Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
            };
            stack.Children.Add(BuildMDBListBrowser(titleBox, mdblistUrlBox));
            stack.Children.Add(MakeLabeledControl("MDBList URL", mdblistUrlBox));
        }
        else if (template.Source == "tmdb_list")
        {
            mdblistUrlBox = new TextBox { Text = template.TmdbList?.Url ?? "", PlaceholderText = "https://www.themoviedb.org/list/... or list ID", Style = (Style)Application.Current.Resources["DarkTextBoxStyle"] };
            stack.Children.Add(MakeLabeledControl("Public TMDB list", mdblistUrlBox));
        }

        stack.Children.Add(MakeLabeledControl("Libraries", new ScrollViewer
        {
            MaxHeight = 132,
            Content = libraryPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }, "Leave empty to span every library you can see."));

        var displayFilters = new Grid { ColumnSpacing = 16 };
        displayFilters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        displayFilters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var watchPanel = MakeLabeledControl("Watch state", watchFilterBox);
        var mediaPanel = MakeLabeledControl("Content", mediaFilterBox);
        Grid.SetColumn(mediaPanel, 1);
        displayFilters.Children.Add(watchPanel);
        displayFilters.Children.Add(mediaPanel);
        stack.Children.Add(displayFilters);
        stack.Children.Add(new TextBlock
        {
            Text = "Uses the active profile's watched state. Shared profiles may see different results.",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        if (template.RequiresProfile) stack.Children.Add(new TextBlock { Text = "This template uses your active profile’s connected Trakt account.", FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        stack.Children.Add(MakeLabeledControl(
            "Default Sort",
            defaultSortBox,
            "The order viewers get when they open this collection. They can still sort it their own way, and that choice is remembered for them."));

        var posterContent = new StackPanel { Spacing = 10 };
        if (!string.IsNullOrWhiteSpace(template.PosterPath))
            posterContent.Children.Add(posterChoices);
        posterContent.Children.Add(defaultPosterPanel);
        posterContent.Children.Add(customPosterBox);
        stack.Children.Add(MakeLabeledControl("Poster", new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = posterContent
        }));

        var twoColumn = new Grid { ColumnSpacing = 16 };
        twoColumn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        twoColumn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var maxPanel = MakeLabeledControl("Max Items", maxItemsBox);
        var schedulePanel = MakeLabeledControl("Auto Refresh", scheduleBox);
        Grid.SetColumn(schedulePanel, 1);
        twoColumn.Children.Add(maxPanel);
        twoColumn.Children.Add(schedulePanel);
        stack.Children.Add(twoColumn);
        var refreshSummary = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
        void UpdateRefreshSummary() => refreshSummary.Text = $"Up to {(string.IsNullOrWhiteSpace(maxItemsBox.Text) ? "the provider's default number of" : maxItemsBox.Text)} items • {(SelectedComboTag(scheduleBox) is "manual" or null or "" ? "Refresh manually" : "Refresh " + SelectedComboTag(scheduleBox))}";
        maxItemsBox.TextChanged += (_, _) => UpdateRefreshSummary(); scheduleBox.SelectionChanged += (_, _) => UpdateRefreshSummary(); UpdateRefreshSummary(); stack.Children.Add(refreshSummary);
        stack.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 640;
            foreach (var pair in new[] { (displayFilters, mediaPanel), (twoColumn, schedulePanel) })
            {
                if (pair.Item1.RowDefinitions.Count == 0) { pair.Item1.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); pair.Item1.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); pair.Item1.RowSpacing = 12; }
                Grid.SetColumn(pair.Item2, compact ? 0 : 1); Grid.SetRow(pair.Item2, compact ? 1 : 0); Grid.SetColumnSpan(pair.Item2, compact ? 2 : 1);
            }
        };

        stack.Children.Add(new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Child = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = "Share with other profiles", FontSize = 13, FontWeight = FontWeights.SemiBold },
                            new TextBlock
                            {
                                Text = "When on, profiles you choose can browse this collection too.",
                                FontSize = 11,
                                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"]
                            }
                        }
                    },
                    sharedToggle
                }
            }
        });
        if (stack.Children[^1] is Border shareBorder && shareBorder.Child is Grid shareGrid)
            Grid.SetColumn(sharedToggle, 1);

        var statusText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            Visibility = Visibility.Collapsed
        };
        var importButton = new Button
        {
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Right,
            Height = 36, FontSize = 14, Padding = new Thickness(16, 4, 16, 4),
            Content = "Create Collection"
        };
        importButton.Click += async (_, _) =>
        {
            importButton.IsEnabled = false;
            statusText.Visibility = Visibility.Collapsed;

            try
            {
                var collection = await ImportTemplateAsync(
                    template,
                    titleBox,
                    descriptionBox,
                    maxItemsBox,
                    scheduleBox,
                    sharedToggle,
                    mdblistUrlBox,
                    libraryChecks,
                    watchFilterBox,
                    mediaFilterBox,
                    defaultSortBox,
                    posterModeBox,
                    customPosterBox);

                if (collection != null)
                {
                    dialog.Hide();
                    BuildCollectionCards();
                    return;
                }

                statusText.Text = ViewModel.TemplateErrorMessage ?? "The collection could not be created.";
                statusText.Visibility = Visibility.Visible;
            }
            finally
            {
                importButton.IsEnabled = true;
            }
        };

        stack.Children.Add(statusText);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancelButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Content = "Cancel"
        };
        cancelButton.Click += (_, _) => RenderTemplateCards();
        actions.Children.Add(cancelButton);
        actions.Children.Add(importButton);
        stack.Children.Add(actions);

        return new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 620
        };
    }

    private UIElement BuildMDBListBrowser(TextBox titleBox, TextBox urlBox)
    {
        var resultsPanel = new StackPanel { Spacing = 6 };
        var searchBox = new TextBox
        {
            PlaceholderText = "Search MDBList",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        var searchButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Content = "Search"
        };
        var topButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Content = "Top lists"
        };
        var configuredText = new TextBlock
        {
            Text = "MDBList discovery needs an admin API key. You can still paste a list URL below.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            Visibility = ViewModel.IsMDBListConfigured ? Visibility.Collapsed : Visibility.Visible
        };

        async Task RefreshResultsAsync(Func<Task> loader)
        {
            await loader();
            configuredText.Visibility = ViewModel.IsMDBListConfigured ? Visibility.Collapsed : Visibility.Visible;
            resultsPanel.Children.Clear();

            foreach (var list in ViewModel.MdblistResults.Take(8))
            {
                resultsPanel.Children.Add(BuildMDBListResultRow(list, titleBox, urlBox));
            }

            if (ViewModel.MdblistResults.Count == 0 && string.IsNullOrWhiteSpace(ViewModel.TemplateErrorMessage))
            {
                resultsPanel.Children.Add(new TextBlock
                {
                    Text = "No MDBList results yet.",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"]
                });
            }
        }

        var searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); if (searchBox.Text.Trim().Length >= 2) await RefreshResultsAsync(() => ViewModel.SearchMDBListAsync(searchBox.Text)); };
        searchBox.TextChanged += (_, _) => { searchTimer.Stop(); if (searchBox.Text.Trim().Length >= 2) searchTimer.Start(); };
        searchBox.Unloaded += (_, _) => searchTimer.Stop();
        searchButton.Click += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(searchBox.Text))
                await RefreshResultsAsync(() => ViewModel.SearchMDBListAsync(searchBox.Text));
        };
        topButton.Click += async (_, _) =>
        {
            searchTimer.Stop(); searchBox.Text = "";
            await RefreshResultsAsync(ViewModel.LoadTopMDBListAsync);
        };

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(searchBox, 0);
        Grid.SetColumn(searchButton, 1);
        Grid.SetColumn(topButton, 2);
        row.Children.Add(searchBox);
        row.Children.Add(searchButton);
        row.Children.Add(topButton);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "MDBList browser",
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
                    },
                    configuredText,
                    row,
                    resultsPanel
                }
            }
        };
    }

    private Border BuildMDBListResultRow(MDBListListSummary list, TextBox titleBox, TextBox urlBox)
    {
        var title = string.IsNullOrWhiteSpace(list.UserName)
            ? list.Name
            : $"{list.Name} by {list.UserName}";
        var detail = $"{list.Mediatype} - {list.Items:N0} items - {list.Likes:N0} likes";

        var button = new Button
        {
            Content = "Use",
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(12, 5, 12, 5)
        };
        button.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(list.Name))
                titleBox.Text = list.Name;

            if (!string.IsNullOrWhiteSpace(list.Url))
                urlBox.Text = list.Url.EndsWith("/json", StringComparison.OrdinalIgnoreCase)
                    ? list.Url
                    : $"{list.Url.TrimEnd('/')}/json";
        };

        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(8, 6, 8, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = detail,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        });
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(8),
            Child = grid
        };
    }

    private async Task<Collection?> ImportTemplateAsync(
        CollectionTemplate template,
        TextBox titleBox,
        TextBox descriptionBox,
        TextBox maxItemsBox,
        ComboBox scheduleBox,
        ToggleSwitch sharedToggle,
        TextBox? mdblistUrlBox,
        IReadOnlyList<CheckBox> libraryChecks,
        ComboBox watchFilterBox,
        ComboBox mediaFilterBox,
        ComboBox defaultSortBox,
        ComboBox posterModeBox,
        TextBox customPosterBox)
    {
        int? maxItems = null;
        if (int.TryParse(maxItemsBox.Text, out var parsedMax) && parsedMax > 0)
            maxItems = parsedMax;

        var schedule = scheduleBox.SelectedItem is ComboBoxItem scheduleItem
            ? scheduleItem.Tag?.ToString()
            : "none";

        var draft = new TemplateImportDraft
        {
            Template = template,
            Title = titleBox.Text,
            Description = descriptionBox.Text,
            MaxItems = maxItems,
            SyncSchedule = schedule,
            IsShared = sharedToggle.IsOn,
            MDBListUrl = mdblistUrlBox?.Text,
            TMDBListUrl = template.Source == "tmdb_list" ? mdblistUrlBox?.Text : null,
            PosterUrl = SelectedComboTag(posterModeBox) == "custom"
                ? customPosterBox.Text
                : template.PosterPath,
            DisplayQueryDefinition = BuildDisplayQueryDefinition(
                SelectedComboTag(watchFilterBox),
                SelectedComboTag(mediaFilterBox)),
            SortConfig = BuildCollectionSortConfig(SelectedComboTag(defaultSortBox)),
        };

        foreach (var check in libraryChecks)
        {
            if (check.IsChecked == true && check.Tag is int libraryId)
                draft.LibraryIds.Add(libraryId);
        }

        return await ViewModel.ImportTemplateAsync(draft);
    }

    private static StackPanel MakeLabeledControl(string label, UIElement control, string? helpText = null)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14, Height = 14, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
        });
        stack.Children.Add(control);

        if (!string.IsNullOrWhiteSpace(helpText))
        {
            stack.Children.Add(new TextBlock
            {
                Text = helpText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"]
            });
        }

        return stack;
    }

    private static ComboBox BuildTaggedComboBox(IEnumerable<(string Value, string Label)> options)
    {
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(10)
        };
        foreach (var option in options)
            combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
        combo.SelectedIndex = 0;
        return combo;
    }

    private static string SelectedComboTag(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";

    private static IEnumerable<(string Value, string Label)> CollectionDefaultSortOptions()
    {
        yield return ("__source_order", "Collection order (default)");
        yield return ("title:asc", "Title");
        yield return ("added_at:desc", "Date Added");
        yield return ("release_date:desc", "Release Date");
        yield return ("last_air_date:desc", "Latest Episode Air Date");
        yield return ("latest_episode_added:desc", "Latest Episode Added");
        yield return ("year:desc", "Year");
        yield return ("content_rating:asc", "Content Rating");
        yield return ("runtime:desc", "Duration");
        yield return ("rating_imdb:desc", "IMDb Rating");
        yield return ("rating_tmdb:desc", "TMDB Rating");
        yield return ("rating_rt_critic:desc", "RT Critic Rating");
        yield return ("rating_rt_audience:desc", "RT Audience Rating");
        yield return ("resolution:desc", "Resolution");
        yield return ("bitrate:desc", "Bitrate");
        yield return ("progress:desc", "Progress");
        yield return ("last_watched_at:desc", "Date Viewed");
        yield return ("play_count:desc", "Plays");
        yield return ("author:asc", "Author");
        yield return ("narrator:asc", "Narrator");
        yield return ("series:asc", "Series");
    }

    private static Dictionary<string, object> BuildCollectionSortConfig(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "__source_order") return [];
        var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) return [];
        var order = parts.Length > 1 && parts[1] is "asc" or "desc"
            ? parts[1]
            : parts[0] is "title" or "content_rating" or "author" or "narrator" or "series" ? "asc" : "desc";
        return new Dictionary<string, object>
        {
            ["field"] = parts[0],
            ["order"] = order,
        };
    }

    private static Border BuildBadge(string text)
        => new()
        {
            Background = (Brush)Application.Current.Resources["AccentBackgroundBrush"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(7, 2, 7, 2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"]
            }
        };

    private static string FormatMediaKind(string? mediaKind)
        => mediaKind?.Trim().ToLowerInvariant() switch
        {
            "movie" or "movies" => "Movies",
            "tv" or "series" or "show" or "shows" => "TV",
            _ => "Movies + TV"
        };

    private static DisplayQueryDefinition? BuildDisplayQueryDefinition(string watch, string media)
    {
        var rules = new List<QueryRule>();
        if (watch == "watched")
            rules.Add(new QueryRule { Field = "watched", Op = "is", Value = true });
        else if (watch == "unwatched")
            rules.Add(new QueryRule { Field = "watched", Op = "is", Value = false });

        if (media == "movie")
            rules.Add(new QueryRule { Field = "type", Op = "is", Value = "movie" });
        else if (media == "series")
            rules.Add(new QueryRule { Field = "type", Op = "is", Value = "series" });

        return rules.Count == 0
            ? null
            : new DisplayQueryDefinition
            {
                Match = "all",
                Groups = [new QueryGroup { Match = "all", Rules = rules }]
            };
    }

    private static int GetScheduleIndex(string? schedule)
    {
        return MapTemplateSchedule(schedule) switch
        {
            "daily" => 1,
            "weekly" => 2,
            "monthly" => 3,
            _ => 0
        };
    }

    private static string MapTemplateSchedule(string? schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
            return "none";

        var trimmed = schedule.Trim();
        if (trimmed is "daily" or "weekly" or "monthly" or "none")
            return trimmed;

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return "daily";

        var dayOfMonth = parts[2];
        var month = parts[3];
        var dayOfWeek = parts[4];

        if (dayOfMonth == "1" && month == "*")
            return "monthly";

        if (dayOfMonth == "*" && month == "*" && dayOfWeek != "*")
            return "weekly";

        return "daily";
    }

    private void BuildCollectionCards()
    {
        CollectionsGrid.Children.Clear();
        UpdateEmptyState();
        AddGroupButton.Visibility = AddGroupEditor.Visibility != Visibility.Visible &&
            ViewModel.Collections.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (ViewModel.Collections.Count > 0)
        {
            var ungrouped = ViewModel.Collections
                .Where(item => string.IsNullOrWhiteSpace(item.GroupId))
                .OrderBy(item => item.SortOrder)
                .ToList();
            if (ungrouped.Count > 0)
                CollectionsGrid.Children.Add(BuildCollectionGroupSection(null, "Ungrouped", ungrouped));

            foreach (var group in ViewModel.Groups.OrderBy(item => item.SortOrder))
            {
                var items = ViewModel.Collections
                    .Where(item => string.Equals(item.GroupId, group.Id, StringComparison.Ordinal))
                    .OrderBy(item => item.SortOrder)
                    .ToList();
                CollectionsGrid.Children.Add(BuildCollectionGroupSection(group, group.Name, items));
            }
        }

        BuildServerCollectionRows();
    }

    private Border BuildCollectionCard(Collection collection)
    {
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        badges.Children.Add(BuildCollectionBadge(FormatCollectionType(collection.CollectionType), true));
        if (collection.IsShared) badges.Children.Add(BuildCollectionBadge("Shared"));
        if (!string.IsNullOrWhiteSpace(collection.SyncSchedule))
            badges.Children.Add(BuildCollectionBadge(collection.SyncSchedule));
        if (collection.LastSyncStatus is "warning" or "failed")
            badges.Children.Add(BuildCollectionBadge(collection.LastSyncStatus == "failed" ? "Sync failed" : "Sync warning"));

        var details = new StackPanel
        {
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = collection.Name,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                badges
            }
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0
        };
        if (CanSyncCollection(collection))
            actions.Children.Add(BuildCollectionActionButton("\uE895", "Sync collection", async () =>
                await ViewModel.SyncCollectionCommand.ExecuteAsync(collection.Id)));
        actions.Children.Add(BuildCollectionActionButton("\uE70F", "Edit collection", () =>
        {
            NavigateToCollectionEditor(collection);
            return Task.CompletedTask;
        }));
        actions.Children.Add(BuildCollectionActionButton("\uE74D", "Delete collection", async () =>
            await ShowDeleteConfirmationAsync(collection)));

        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var grip = new FontIcon
        {
            Glyph = "\uE700",
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0
        };
        Grid.SetColumn(grip, 0);
        Grid.SetColumn(details, 1);
        content.Children.Add(grip);
        content.Children.Add(details);

        var openButton = new Button
        {
            Content = content,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        AutomationProperties.SetName(openButton, $"Open {collection.Name}");
        openButton.Click += (_, _) =>
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
            {
                CollectionId = collection.Id,
                Title = collection.Name,
                Subtitle = collection.IsShared ? "Shared collection" : "Personal collection",
                IsUserCollection = true,
            });
        };

        var cardLayer = new Grid();
        cardLayer.Children.Add(openButton);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        cardLayer.Children.Add(actions);

        var card = new Border
        {
            MinHeight = 116,
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(20, 16, 14, 16),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Child = cardLayer,
            Tag = collection
        };

        // Hover effect
        card.PointerEntered += (s, _) =>
        {
            if (s is Border b)
            {
                b.Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"];
                b.BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
                actions.Opacity = 1;
                grip.Opacity = 1;
            }
        };
        card.PointerExited += (s, _) =>
        {
            if (s is Border b)
            {
                b.Background = (Brush)Application.Current.Resources["CardBackgroundBrush"];
                b.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
                actions.Opacity = 0;
                grip.Opacity = 0;
            }
        };
        actions.GotFocus += (_, _) =>
        {
            actions.Opacity = 1;
            grip.Opacity = 1;
        };

        // Right-click context menu
        var menuFlyout = new MenuFlyout();
        var currentProfileId = App.Services
            .GetRequiredService<SiloPlayer.Core.Services.AuthService>()
            .SelectedProfileId;
        var canManage = string.IsNullOrWhiteSpace(currentProfileId) ||
            string.Equals(collection.CreatorProfileId, currentProfileId, StringComparison.Ordinal);

        if (canManage && CanSyncCollection(collection))
        {
            var syncItem = new MenuFlyoutItem
            {
                Text = "Sync now",
                Icon = new FontIcon { Glyph = "\uE895" }
            };
            syncItem.Click += async (_, _) =>
            {
                await ViewModel.SyncCollectionCommand.ExecuteAsync(collection.Id);
            };
            menuFlyout.Items.Add(syncItem);
        }

        var editItem = new MenuFlyoutItem
        {
            Text = canManage ? "Edit" : "View details",
            Icon = new FontIcon { Glyph = "\uE70F" }
        };
        editItem.Click += (_, _) =>
        {
            NavigateToCollectionEditor(collection);
        };

        var deleteItem = new MenuFlyoutItem
        {
            Text = "Delete",
            Icon = new FontIcon { Glyph = "\uE74D" }
        };
        deleteItem.Click += async (_, _) =>
        {
            await ShowDeleteConfirmationAsync(collection);
        };

        menuFlyout.Items.Add(editItem);
        menuFlyout.Items.Add(new MenuFlyoutSeparator());
        var moveMenu = new MenuFlyoutSubItem { Text = "Move to group", Icon = new FontIcon { Glyph = "\uE8B7" } };
        var ungroupedItem = new MenuFlyoutItem { Text = "Ungrouped" };
        ungroupedItem.Click += async (_, _) => await ViewModel.MoveCollectionToGroupAsync(collection, null);
        moveMenu.Items.Add(ungroupedItem);
        foreach (var group in ViewModel.Groups)
        {
            var targetGroup = group;
            var groupItem = new MenuFlyoutItem { Text = group.Name };
            groupItem.Click += async (_, _) => await ViewModel.MoveCollectionToGroupAsync(collection, targetGroup.Id);
            moveMenu.Items.Add(groupItem);
        }
        menuFlyout.Items.Add(moveMenu);
        var moveUp = new MenuFlyoutItem { Text = "Move earlier", Icon = new FontIcon { Glyph = "\uE70E" } };
        moveUp.Click += async (_, _) => await ViewModel.MoveCollectionAsync(collection, -1);
        var moveDown = new MenuFlyoutItem { Text = "Move later", Icon = new FontIcon { Glyph = "\uE70D" } };
        moveDown.Click += async (_, _) => await ViewModel.MoveCollectionAsync(collection, 1);
        menuFlyout.Items.Add(moveUp);
        menuFlyout.Items.Add(moveDown);
        if (canManage) menuFlyout.Items.Add(deleteItem);
        card.ContextFlyout = menuFlyout;
        ConfigureCollectionDrag(card, collection);

        return card;
    }

    private void ConfigureCollectionDrag(Border card, Collection collection)
    {
        card.CanDrag = true;
        card.AllowDrop = true;
        card.DragStarting += (_, args) =>
        {
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.SetText($"{CollectionDragPrefix}{collection.Id}");
        };
        card.DragOver += (_, args) => args.AcceptedOperation = DataPackageOperation.Move;
        card.Drop += async (_, args) =>
        {
            if (!args.DataView.Contains(StandardDataFormats.Text)) return;
            args.Handled = true;
            var payload = await args.DataView.GetTextAsync();
            if (!payload.StartsWith(CollectionDragPrefix, StringComparison.Ordinal)) return;

            var sourceId = payload[CollectionDragPrefix.Length..];
            await ViewModel.DropCollectionAsync(sourceId, collection.Id, collection.GroupId);
        };
    }

    private static void NavigateToCollectionEditor(Collection collection)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (string.Equals(collection.CollectionType, "smart", StringComparison.OrdinalIgnoreCase))
        {
            nav.Navigate<SmartCollectionWizardPage>(new SmartCollectionWizardNavigationArgs(
                CollectionId: collection.Id));
            return;
        }

        nav.Navigate<CollectionEditorPage>(collection.Id);
    }

    private UIElement BuildCollectionGroupSection(CollectionGroup? group, string title, IReadOnlyList<Collection> items)
    {
        var section = new StackPanel { Spacing = 14, Margin = new Thickness(0, 0, 0, 28) };
        section.AllowDrop = true;
        section.DragOver += (_, args) => args.AcceptedOperation = DataPackageOperation.Move;
        section.Drop += async (_, args) =>
        {
            if (!args.DataView.Contains(StandardDataFormats.Text)) return;
            args.Handled = true;
            var payload = await args.DataView.GetTextAsync();
            if (payload.StartsWith(CollectionDragPrefix, StringComparison.Ordinal))
            {
                await ViewModel.DropCollectionAsync(payload[CollectionDragPrefix.Length..], null, group?.Id);
            }
            else if (group != null && payload.StartsWith(GroupDragPrefix, StringComparison.Ordinal))
            {
                await ViewModel.DropGroupAsync(payload[GroupDragPrefix.Length..], group.Id);
            }
        };
        var header = new Grid
        {
            Padding = new Thickness(0, 0, 0, 9),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (group != null)
        {
            var groupGrip = new FontIcon
            {
                Glyph = "\uE700",
                FontSize = 14,
                Opacity = 0,
                CanDrag = true,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            groupGrip.DragStarting += (_, args) =>
            {
                args.Data.RequestedOperation = DataPackageOperation.Move;
                args.Data.SetText($"{GroupDragPrefix}{group.Id}");
            };
            header.PointerEntered += (_, _) => groupGrip.Opacity = 1;
            header.PointerExited += (_, _) => groupGrip.Opacity = 0;
            heading.Children.Add(groupGrip);
        }
        var headingTitle = new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = FontWeights.Light,
            Foreground = group == null
                ? (Brush)Application.Current.Resources["SecondaryTextBrush"]
                : (Brush)Application.Current.Resources["PrimaryTextBrush"]
        };
        heading.Children.Add(headingTitle);
        heading.Children.Add(new TextBlock
        {
            Text = items.Count.ToString(),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"]
        });
        header.Children.Add(heading);

        if (group != null)
        {
            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Opacity = 0
            };
            var renameBox = new TextBox
            {
                Text = group.Name,
                Width = 300,
                MaxWidth = 300,
                Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
            };
            var renameEditor = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Visibility = Visibility.Collapsed
            };
            renameEditor.Children.Add(renameBox);

            void CancelRename()
            {
                renameBox.Text = group.Name;
                renameEditor.Visibility = Visibility.Collapsed;
                heading.Visibility = Visibility.Visible;
                controls.Visibility = Visibility.Visible;
            }

            async Task CommitRenameAsync()
            {
                if (!await ViewModel.RenameGroupAsync(group, renameBox.Text)) return;
                CancelRename();
            }

            renameEditor.Children.Add(BuildCollectionActionButton("\uE73E", "Save group name", CommitRenameAsync));
            renameEditor.Children.Add(BuildCollectionActionButton("\uE711", "Cancel", () =>
            {
                CancelRename();
                return Task.CompletedTask;
            }));
            renameBox.KeyDown += async (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                {
                    args.Handled = true;
                    await CommitRenameAsync();
                }
                else if (args.Key == Windows.System.VirtualKey.Escape)
                {
                    args.Handled = true;
                    CancelRename();
                }
            };
            header.Children.Add(renameEditor);

            controls.Children.Add(BuildCollectionActionButton("\uE70F", "Rename group", () =>
            {
                controls.Visibility = Visibility.Collapsed;
                heading.Visibility = Visibility.Collapsed;
                renameEditor.Visibility = Visibility.Visible;
                renameBox.Focus(FocusState.Programmatic);
                renameBox.SelectAll();
                return Task.CompletedTask;
            }));
            controls.Children.Add(BuildCollectionActionButton("\uE74D", "Delete group", () => DeleteGroupAsync(group)));
            header.PointerEntered += (_, _) => controls.Opacity = 1;
            header.PointerExited += (_, _) => controls.Opacity = 0;
            controls.GotFocus += (_, _) => controls.Opacity = 1;
            Grid.SetColumn(controls, 1);
            header.Children.Add(controls);
        }
        section.Children.Add(header);

        if (items.Count == 0)
        {
            section.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 22, 16, 22),
                Child = new TextBlock
                {
                    Text = "Drop a collection here",
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"]
                }
            });
            return section;
        }

        var repeater = new ItemsRepeater
        {
            ItemsSource = items.Select(BuildCollectionCard).ToList(),
            Layout = new UniformGridLayout
            {
                MaximumRowsOrColumns = Math.Max(1, _collectionColumnCount),
                MinItemWidth = 280,
                MinColumnSpacing = 16,
                MinRowSpacing = 16,
                ItemsStretch = UniformGridLayoutItemsStretch.Fill
            }
        };
        section.Children.Add(repeater);
        return section;
    }

    private static Border BuildCollectionBadge(string text, bool accent = false)
        => new()
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            Background = accent
                ? (Brush)Application.Current.Resources["AccentBackgroundBrush"]
                : (Brush)Application.Current.Resources["SurfaceBrush"],
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = accent
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };

    private static Button BuildCollectionActionButton(string glyph, string tooltip, Func<Task> action)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(9),
            Content = new FontIcon { Glyph = glyph, FontSize = 13 }
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private void BuildServerCollectionRows()
    {
        ServerCollectionsRows.Children.Clear();
        ServerCollectionsSection.Visibility = ViewModel.IsLoadingServerCollections || ViewModel.ServerLibraries.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var library in ViewModel.ServerLibraries)
        {
            var row = new StackPanel { Spacing = 14 };
            var rowHeader = new Grid();
            rowHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new Button
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = new TextBlock
                {
                    Text = library.LibraryName,
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
                }
            };
            title.Click += (_, _) => NavigateToLibraryCollections(library);
            rowHeader.Children.Add(title);

            if (library.TotalCount > library.Collections.Count)
            {
                var exploreAll = new Button
                {
                    Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                    Content = "Explore all"
                };
                exploreAll.Click += (_, _) => NavigateToLibraryCollections(library);
                Grid.SetColumn(exploreAll, 1);
                rowHeader.Children.Add(exploreAll);
            }
            row.Children.Add(rowHeader);

            var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            foreach (var collection in library.Collections)
                cards.Children.Add(BuildServerCollectionCard(library, collection));

            row.Children.Add(new ScrollViewer
            {
                Content = cards,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollMode = ScrollMode.Enabled,
                VerticalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            ServerCollectionsRows.Children.Add(row);
        }
    }

    private static void NavigateToLibraryCollections(ServerCollectionsLibrary library)
    {
        var libraryModel = App.MainWindowInstance?.FindLibrary(library.LibraryId)
            ?? new SiloPlayer.Core.Models.Catalog.Library
            {
                Id = library.LibraryId,
                Name = library.LibraryName
            };
        App.Services.GetRequiredService<NavigationService>()
            .Navigate<LibraryPage>(new LibraryPage.NavigationArgs(libraryModel, "Collections"));
    }

    private Border BuildServerCollectionCard(ServerCollectionsLibrary library, ServerCollectionSummary collection)
    {
        var cardWidth = _serverCardSizeClass switch
        {
            0 => 130d,
            1 => 150d,
            _ => 178d,
        };
        var poster = new Border
        {
            Width = cardWidth,
            Height = cardWidth * 1.5,
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"]
        };
        if (!string.IsNullOrWhiteSpace(collection.PosterUrl) &&
            Uri.TryCreate(collection.PosterUrl, UriKind.Absolute, out var posterUri))
        {
            poster.Child = new Image
            {
                Source = new BitmapImage(posterUri),
                Stretch = Stretch.UniformToFill
            };
        }
        else
        {
            poster.Child = new TextBlock
            {
                Text = collection.Title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16),
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            };
        }

        var count = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            Child = new TextBlock
            {
                Text = collection.ItemCount.ToString(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
            }
        };
        var posterGrid = new Grid();
        posterGrid.Children.Add(poster);
        posterGrid.Children.Add(count);

        var isPinned = App.MainWindowInstance?.IsSidebarPin(
            library.LibraryId, "collection", collection.Id) == true;
        var pinIcon = new FontIcon
        {
            Glyph = isPinned ? "\uE841" : "\uE840",
            FontSize = 14
        };
        var pinButton = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(190, 0, 0, 0)),
            Content = pinIcon,
            Opacity = isPinned ? 1 : 0
        };
        ToolTipService.SetToolTip(pinButton, isPinned ? "Unpin from sidebar" : "Pin to sidebar");
        AutomationProperties.SetName(pinButton, isPinned
            ? $"Unpin {collection.Title} from sidebar"
            : $"Pin {collection.Title} to sidebar");
        pinButton.Tapped += (_, args) => args.Handled = true;
        pinButton.Click += async (_, _) =>
        {
            if (App.MainWindowInstance is not MainWindow window) return;
            pinButton.IsEnabled = false;
            try
            {
                var nowPinned = await window.ToggleSidebarPinAsync(
                    library.LibraryId, "collection", collection.Id, collection.Title);
                pinIcon.Glyph = nowPinned ? "\uE841" : "\uE840";
                pinButton.Opacity = nowPinned ? 1 : 0;
                ToolTipService.SetToolTip(pinButton, nowPinned ? "Unpin from sidebar" : "Pin to sidebar");
                AutomationProperties.SetName(pinButton, nowPinned
                    ? $"Unpin {collection.Title} from sidebar"
                    : $"Pin {collection.Title} to sidebar");
            }
            finally
            {
                pinButton.IsEnabled = true;
            }
        };
        var stack = new StackPanel
        {
            Width = cardWidth,
            Spacing = 9,
            Children =
            {
                posterGrid,
                new TextBlock
                {
                    Text = collection.Title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
                }
            }
        };
        var openButton = new Button
        {
            Content = stack,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetName(openButton, $"Open {collection.Title}");
        openButton.Click += (_, _) => App.Services.GetRequiredService<NavigationService>()
            .Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
            {
                CollectionId = collection.Id,
                Title = collection.Title,
                Subtitle = $"{library.LibraryName} collection",
                IsUserCollection = false,
                LibraryId = library.LibraryId
            });

        var cardLayer = new Grid();
        cardLayer.Children.Add(openButton);
        cardLayer.Children.Add(pinButton);
        var card = new Border { Child = cardLayer, Tag = collection };
        card.PointerEntered += (_, _) => pinButton.Opacity = 1;
        card.PointerExited += (_, _) =>
        {
            if (App.MainWindowInstance?.IsSidebarPin(
                    library.LibraryId, "collection", collection.Id) != true &&
                pinButton.FocusState == FocusState.Unfocused)
                pinButton.Opacity = 0;
        };
        pinButton.GotFocus += (_, _) => pinButton.Opacity = 1;
        return card;
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        AddGroupButton.Visibility = Visibility.Collapsed;
        AddGroupEditor.Visibility = Visibility.Visible;
        AddGroupNameBox.Text = "";
        AddGroupNameBox.Focus(FocusState.Programmatic);
    }

    private async void AddGroupCommit_Click(object sender, RoutedEventArgs e)
    {
        if (!await ViewModel.CreateGroupAsync(AddGroupNameBox.Text)) return;
        CloseAddGroupEditor();
    }

    private void AddGroupCancel_Click(object sender, RoutedEventArgs e) => CloseAddGroupEditor();

    private async void AddGroupNameBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            if (await ViewModel.CreateGroupAsync(AddGroupNameBox.Text))
                CloseAddGroupEditor();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            CloseAddGroupEditor();
        }
    }

    private void CloseAddGroupEditor()
    {
        AddGroupNameBox.Text = "";
        AddGroupEditor.Visibility = Visibility.Collapsed;
        AddGroupButton.Visibility = ViewModel.Collections.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task RenameGroupAsync(CollectionGroup group)
    {
        var input = new TextBox
        {
            Text = group.Name,
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(input);
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = "Rename group",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                error.Text = "Enter a group name.";
                error.Visibility = Visibility.Visible;
                input.Focus(FocusState.Programmatic);
                return;
            }

            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.PrimaryButtonText = "Saving...";
                error.Visibility = Visibility.Collapsed;
                if (await ViewModel.RenameGroupAsync(group, input.Text))
                {
                    args.Cancel = false;
                    return;
                }

                error.Text = ViewModel.ErrorMessage ?? "The group could not be renamed.";
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                dialog.PrimaryButtonText = "Save";
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        dialog.Opened += (_, _) =>
        {
            input.Focus(FocusState.Programmatic);
            input.SelectAll();
        };
        await dialog.ShowAsync();
    }

    private async Task DeleteGroupAsync(CollectionGroup group)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = $"Delete group \"{group.Name}\"? Its collections will move to Ungrouped.",
            TextWrapping = TextWrapping.Wrap
        });
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = "Delete group",
            Content = content,
            PrimaryButtonText = "Delete",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.PrimaryButtonText = "Deleting...";
                error.Visibility = Visibility.Collapsed;
                if (await ViewModel.DeleteGroupAsync(group))
                {
                    args.Cancel = false;
                    return;
                }

                error.Text = ViewModel.ErrorMessage ?? "The group could not be deleted.";
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                dialog.PrimaryButtonText = "Delete";
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }

    private static bool CanSyncCollection(Collection collection)
        => collection.CollectionType is "mdblist" or "tmdb" or "trakt";

    private static string FormatCollectionType(string type) => type switch
    {
        "tmdb_list" => "TMDB List",
        "smart" => "Smart",
        "mdblist" => "MDBList",
        "tmdb" => "TMDB",
        "trakt" => "Trakt",
        _ => "Manual"
    };

    private static string FormatCollectionInfo(Collection collection)
    {
        var count = collection.ItemCount > 0
            ? $"{collection.ItemCount:N0} items"
            : $"{FormatCollectionType(collection.CollectionType)} collection";

        return string.IsNullOrWhiteSpace(collection.LastSyncStatus)
            ? count
            : $"{count} - sync {collection.LastSyncStatus}";
    }

    private async Task ShowDeleteConfirmationAsync(Collection collection)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = $"Delete collection \"{collection.Name}\"? This action cannot be undone.",
            TextWrapping = TextWrapping.Wrap
        });
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = "Delete collection",
            Content = content,
            PrimaryButtonText = "Delete",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.PrimaryButtonText = "Deleting...";
                error.Visibility = Visibility.Collapsed;
                await ViewModel.DeleteCollectionCommand.ExecuteAsync(collection.Id);
                if (ViewModel.Collections.All(candidate => candidate.Id != collection.Id))
                {
                    args.Cancel = false;
                    return;
                }

                error.Text = ViewModel.ErrorMessage ?? "The collection could not be deleted.";
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                dialog.PrimaryButtonText = "Delete";
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }
}

public sealed record CollectionsNavigationArgs(bool OpenTemplates = false, bool ReturnAfterTemplates = false);
