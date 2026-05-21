using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Collections;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class SmartCollectionWizardPage : Page
{
    public SmartCollectionWizardViewModel ViewModel { get; }

    private SmartCollectionWizardNavigationArgs? _args;
    private bool _loaded;
    private bool _suppressSelectionChanges;

    public bool CanSave => !ViewModel.IsSaving && !ViewModel.IsLoading;
    public string PreviewCountText
        => ViewModel.PreviewTotal > 0
            ? $"{ViewModel.PreviewTotal:N0} matched"
            : "Preview the first matching items before saving.";

    public SmartCollectionWizardPage()
    {
        ViewModel = App.Services.GetRequiredService<SmartCollectionWizardViewModel>();
        this.InitializeComponent();

        ViewModel.Saved += OnSaved;
        ViewModel.Rules.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(BuildRulesPanel);
        ViewModel.PreviewItems.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(BuildPreviewItemsPanel);
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ViewModel.IsSaving)
                or nameof(ViewModel.IsLoading)
                or nameof(ViewModel.PreviewTotal))
            {
                Bindings.Update();
            }
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _args = e.Parameter as SmartCollectionWizardNavigationArgs;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        PopulateStaticCombos();
        await ViewModel.ConfigureAsync(_args);
        ApplyModeVisibility();
        BuildLibrariesPanel();
        BuildRulesPanel();
        BuildPreviewItemsPanel();
        Bindings.Update();
    }

    private void PopulateStaticCombos()
    {
        _suppressSelectionChanges = true;

        AddComboItems(MediaScopeCombo, [
            ("Movies", "movie"),
            ("Series", "series"),
            ("Episodes", "episode")
        ], ViewModel.MediaScope);

        AddComboItems(SortFieldCombo, [
            ("Recently Added", "added_at"),
            ("Title", "title"),
            ("Year", "year"),
            ("IMDb Rating", "rating_imdb"),
            ("Runtime", "runtime")
        ], ViewModel.SortField);

        AddComboItems(SortOrderCombo, [
            ("Descending", "desc"),
            ("Ascending", "asc")
        ], ViewModel.SortOrder);

        AddComboItems(MatchModeCombo, [
            ("Match all rules", "all"),
            ("Match any rule", "any")
        ], ViewModel.MatchMode);

        _suppressSelectionChanges = false;
    }

    private static void AddComboItems(ComboBox combo, IEnumerable<(string Label, string Value)> items, string selected)
    {
        combo.Items.Clear();
        foreach (var (label, value) in items)
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            combo.Items.Add(item);
            if (value == selected)
                combo.SelectedItem = item;
        }

        if (combo.SelectedItem == null && combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private void ApplyModeVisibility()
    {
        SharedToggle.Visibility = ViewModel.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        IncludeServerToggle.Visibility = ViewModel.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        FeaturedToggle.Visibility = ViewModel.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildLibrariesPanel()
    {
        LibrariesPanel.Children.Clear();

        foreach (var library in ViewModel.Libraries)
        {
            var check = new CheckBox
            {
                Content = library.Name,
                Tag = library.Id,
                IsChecked = ViewModel.SelectedLibraryIds.Contains(library.Id),
                FontSize = 13
            };
            check.Checked += LibraryCheck_Changed;
            check.Unchecked += LibraryCheck_Changed;
            LibrariesPanel.Children.Add(check);
        }
    }

    private void LibraryCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox check || check.Tag is not int libraryId)
            return;

        if (check.IsChecked == true)
        {
            if (!ViewModel.SelectedLibraryIds.Contains(libraryId))
                ViewModel.SelectedLibraryIds.Add(libraryId);
        }
        else
        {
            ViewModel.SelectedLibraryIds.Remove(libraryId);
        }
    }

    private void BuildRulesPanel()
    {
        RulesPanel.Children.Clear();
        foreach (var rule in ViewModel.Rules)
            RulesPanel.Children.Add(BuildRuleRow(rule));
    }

    private Grid BuildRuleRow(QueryRule rule)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var fieldCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var (label, value) in RuleFields)
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            fieldCombo.Items.Add(item);
            if (value == rule.Field)
                fieldCombo.SelectedItem = item;
        }
        if (fieldCombo.SelectedItem == null)
            fieldCombo.SelectedIndex = 0;
        fieldCombo.SelectionChanged += (_, _) =>
        {
            if (fieldCombo.SelectedItem is ComboBoxItem item && item.Tag is string field)
                rule.Field = field;
        };

        var opCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var (label, value) in RuleOperators)
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            opCombo.Items.Add(item);
            if (value == rule.Op)
                opCombo.SelectedItem = item;
        }
        if (opCombo.SelectedItem == null)
            opCombo.SelectedIndex = 0;
        opCombo.SelectionChanged += (_, _) =>
        {
            if (opCombo.SelectedItem is ComboBoxItem item && item.Tag is string op)
                rule.Op = op;
        };

        var valueBox = new TextBox
        {
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            PlaceholderText = "Value",
            Text = rule.Value?.ToString() ?? ""
        };
        valueBox.TextChanged += (_, _) => rule.Value = valueBox.Text;
        valueBox.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
                _ = ViewModel.PreviewAsync();
        };

        var removeButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(removeButton, "Remove rule");
        removeButton.Click += (_, _) => ViewModel.RemoveRule(rule);

        Grid.SetColumn(fieldCombo, 0);
        Grid.SetColumn(opCombo, 1);
        Grid.SetColumn(valueBox, 2);
        Grid.SetColumn(removeButton, 3);
        row.Children.Add(fieldCombo);
        row.Children.Add(opCombo);
        row.Children.Add(valueBox);
        row.Children.Add(removeButton);

        return row;
    }

    private void BuildPreviewItemsPanel()
    {
        PreviewItemsPanel.Children.Clear();

        if (ViewModel.PreviewItems.Count == 0)
        {
            PreviewItemsPanel.Children.Add(new TextBlock
            {
                Text = "No preview loaded yet.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"]
            });
            Bindings.Update();
            return;
        }

        foreach (var item in ViewModel.PreviewItems.Take(24))
        {
            var title = new TextBlock
            {
                Text = item.Title,
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var badge = MakeTypeBadge(item.Type);
            var grid = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };
            Grid.SetColumn(title, 0);
            Grid.SetColumn(badge, 1);
            grid.Children.Add(title);
            grid.Children.Add(badge);

            PreviewItemsPanel.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7, 10, 7),
                Child = grid
            });
        }

        Bindings.Update();
    }

    private static Border MakeTypeBadge(string type)
    {
        return new Border
        {
            Background = (Brush)Application.Current.Resources["AccentBackgroundBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(type) ? "ITEM" : type.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"]
            }
        };
    }

    private void MediaScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionChanges && MediaScopeCombo.SelectedItem is ComboBoxItem item && item.Tag is string value)
            ViewModel.MediaScope = value;
    }

    private void SortFieldCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionChanges && SortFieldCombo.SelectedItem is ComboBoxItem item && item.Tag is string value)
            ViewModel.SortField = value;
    }

    private void SortOrderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionChanges && SortOrderCombo.SelectedItem is ComboBoxItem item && item.Tag is string value)
            ViewModel.SortOrder = value;
    }

    private void MatchModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionChanges && MatchModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string value)
            ViewModel.MatchMode = value;
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddRule();
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PreviewAsync();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        App.Services.GetRequiredService<NavigationService>().GoBack();
    }

    private void OnSaved()
    {
        DispatcherQueue.TryEnqueue(() =>
            App.Services.GetRequiredService<NavigationService>().GoBack());
    }

    private static readonly (string Label, string Value)[] RuleFields =
    [
        ("Title", "title"),
        ("Genre", "genre"),
        ("Year", "year"),
        ("Studio", "studio"),
        ("Network", "network"),
        ("Country", "country"),
        ("Content Rating", "content_rating"),
        ("Type", "type"),
        ("Runtime", "runtime"),
        ("IMDb Rating", "rating_imdb"),
        ("Added Date", "added_at")
    ];

    private static readonly (string Label, string Value)[] RuleOperators =
    [
        ("is", "is"),
        ("is not", "is_not"),
        ("contains", "contains"),
        ("greater than", "greater_than"),
        ("less than", "less_than")
    ];
}
