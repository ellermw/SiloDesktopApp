using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
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
        _templateCategoryFilter = "all";
        _templateSearch = "";
    }

    private ContentDialog BuildTemplateGalleryDialog()
    {
        var compact = ActualWidth > 0 && ActualWidth < 960;
        var dialogWidth = compact ? Math.Max(320, ActualWidth - 80) : 860;
        var dialog = new ContentDialog
        {
            Title = "Browse collection templates",
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close,
            MaxWidth = compact ? dialogWidth : 980,
            MinWidth = dialogWidth
        };
        _templateDialog = dialog;

        var root = new Grid
        {
            ColumnSpacing = 20,
            MinHeight = compact ? 480 : 560
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var filterPanel = new StackPanel { Spacing = 14 };
        filterPanel.Children.Add(new TextBlock
        {
            Text = "Pick a curated source, choose libraries, then let Silo seed and sync the collection.",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });

        var searchBox = new TextBox
        {
            PlaceholderText = "Search templates",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        searchBox.TextChanged += (_, _) =>
        {
            _templateSearch = searchBox.Text;
            RenderTemplateCards();
        };
        filterPanel.Children.Add(searchBox);

        var categoriesPanel = new StackPanel { Spacing = 6 };
        BuildTemplateCategoryButtons(categoriesPanel);
        filterPanel.Children.Add(categoriesPanel);

        if (!string.IsNullOrWhiteSpace(ViewModel.TemplateErrorMessage))
        {
            filterPanel.Children.Add(new TextBlock
            {
                Text = ViewModel.TemplateErrorMessage,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"]
            });
        }

        _templateCardsPanel = new StackPanel { Spacing = 16 };
        var scroll = new ScrollViewer
        {
            Content = _templateCardsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        if (compact)
        {
            root.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            root.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(filterPanel, 0);
            Grid.SetColumnSpan(filterPanel, 2);
            Grid.SetRow(filterPanel, 0);
            Grid.SetColumn(scroll, 0);
            Grid.SetColumnSpan(scroll, 2);
            Grid.SetRow(scroll, 1);
        }
        else
        {
            Grid.SetColumn(filterPanel, 0);
            Grid.SetColumn(scroll, 1);
        }
        root.Children.Add(filterPanel);
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
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(10),
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

        _templateCardsPanel.Children.Clear();

        var templates = ViewModel.TemplateGroups
            .Where(group => _templateCategoryFilter == "all" || group.Category == _templateCategoryFilter)
            .SelectMany(group => group.Templates)
            .Where(TemplateMatchesSearch)
            .ToList();

        if (templates.Count == 0)
        {
            _templateCardsPanel.Children.Add(new TextBlock
            {
                Text = "No templates match this search.",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 120, 0, 0)
            });
            return;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        for (var i = 0; i < templates.Count; i++)
        {
            row.Children.Add(BuildTemplateCard(templates[i]));
            if ((i + 1) % 3 == 0)
            {
                _templateCardsPanel.Children.Add(row);
                row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 14
                };
            }
        }

        if (row.Children.Count > 0)
            _templateCardsPanel.Children.Add(row);

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

    private Border BuildTemplateCard(CollectionTemplate template)
    {
        var sourceBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["AccentBackgroundBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 3, 7, 3),
            Child = new TextBlock
            {
                Text = FormatCollectionType(template.Source).ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"]
            }
        };

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(template.Icon) ? "\uE8B7" : template.Icon,
            FontSize = 24,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
        });
        Grid.SetColumn(sourceBadge, 1);
        header.Children.Add(sourceBadge);

        var tagsText = string.Join("  ", template.Tags.Take(3));
        var stack = new StackPanel
        {
            Width = 205,
            Spacing = 8,
            Children =
            {
                header,
                new TextBlock
                {
                    Text = template.Title,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2
                },
                new TextBlock
                {
                    Text = template.Description,
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 3
                },
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(tagsText) ? template.MediaKind : tagsText,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };

        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14),
            Child = stack,
            Tag = template
        };

        card.PointerEntered += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"];
        };
        card.PointerExited += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Brush)Application.Current.Resources["CardBackgroundBrush"];
        };
        card.Tapped += (_, _) =>
        {
            ShowTemplateConfigInGallery(template);
        };

        return card;
    }

    private void ShowTemplateConfigInGallery(CollectionTemplate template)
    {
        if (_templateCardsPanel == null || _templateDialog == null)
            return;

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
            Text = template.Title,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
        });
        _templateCardsPanel.Children.Add(BuildTemplateConfigPanel(template, _templateDialog));
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
            PlaceholderText = "Description",
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Height = 80,
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
            CornerRadius = new CornerRadius(8)
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
            OnContent = "Shared with all profiles",
            OffContent = "Private to your profile",
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

        TextBox? mdblistUrlBox = null;
        var stack = new StackPanel { Spacing = 16, Width = 660 };
        stack.Children.Add(new TextBlock
        {
            Text = template.Description,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        stack.Children.Add(MakeLabeledControl("Title", titleBox));
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

        var twoColumn = new Grid { ColumnSpacing = 12 };
        twoColumn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        twoColumn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var maxPanel = MakeLabeledControl("Max items", maxItemsBox);
        var schedulePanel = MakeLabeledControl("Auto refresh", scheduleBox);
        Grid.SetColumn(maxPanel, 0);
        Grid.SetColumn(schedulePanel, 1);
        twoColumn.Children.Add(maxPanel);
        twoColumn.Children.Add(schedulePanel);
        stack.Children.Add(twoColumn);

        stack.Children.Add(MakeLabeledControl("Libraries", new ScrollViewer
        {
            MaxHeight = 132,
            Content = libraryPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }, "Leave every library unchecked to use all visible libraries."));
        stack.Children.Add(sharedToggle);

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
            Padding = new Thickness(18, 9, 18, 9),
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
                    libraryChecks);

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
        stack.Children.Add(importButton);

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

        searchButton.Click += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(searchBox.Text))
                await RefreshResultsAsync(() => ViewModel.SearchMDBListAsync(searchBox.Text));
        };
        topButton.Click += async (_, _) =>
        {
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
        IReadOnlyList<CheckBox> libraryChecks)
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
            MDBListUrl = mdblistUrlBox?.Text
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
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
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
        Grid.SetColumn(actions, 2);
        content.Children.Add(grip);
        content.Children.Add(details);
        content.Children.Add(actions);

        var card = new Border
        {
            MinHeight = 116,
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(20, 16, 14, 16),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Child = content,
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

        // B40: card click opens the browse view (catalog grid of the
        // collection's items), NOT the editor. The editor is accessible via
        // right-click context menu (Edit) to match the webui dual-path.
        card.Tapped += (s, _) =>
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
        var card = new Border { Child = stack, Tag = collection };
        card.Tapped += (_, _) => App.Services.GetRequiredService<NavigationService>()
            .Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
            {
                CollectionId = collection.Id,
                Title = collection.Title,
                Subtitle = $"{library.LibraryName} collection",
                IsUserCollection = false,
                LibraryId = library.LibraryId
            });
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
        var dialog = new ContentDialog
        {
            Title = "Rename group",
            Content = input,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RenameGroupAsync(group, input.Text);
    }

    private async Task DeleteGroupAsync(CollectionGroup group)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete group",
            Content = $"Delete group \"{group.Name}\"? Its collections will move to Ungrouped.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteGroupAsync(group);
    }

    private static bool CanSyncCollection(Collection collection)
        => collection.CollectionType is "mdblist" or "tmdb" or "trakt";

    private static string FormatCollectionType(string type) => type switch
    {
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
        var dialog = new ContentDialog
        {
            Title = "Delete Collection",
            Content = $"Delete \"{collection.Name}\"? This action cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteCollectionCommand.ExecuteAsync(collection.Id);
        }
    }
}

public sealed record CollectionsNavigationArgs(bool OpenTemplates = false, bool ReturnAfterTemplates = false);
