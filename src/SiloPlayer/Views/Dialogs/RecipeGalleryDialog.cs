using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Controls;

namespace SiloPlayer.Views.Dialogs;

/// <summary>
/// Native counterpart of the WebUI RecipeGalleryModal + RecipeConfigDrawer.
/// Profile-facing callers intentionally hide admin-only recipes and do not
/// expose the admin bulk-apply/enabled controls.
/// </summary>
public static class RecipeGalleryDialog
{
    private static readonly (string Key, string Label)[] CategoryLabels =
    [
        ("keep", "Keep watching"), ("new", "What's new"), ("popular", "Popular"),
        ("picked", "Picked for you"), ("moods", "Moods & themes"), ("collections", "Collections & rules"),
    ];
    private static string PickerGroup(string type) => type switch
    {
        "continue_watching" or "next_up" or "next_in_series" or "watchlist" or "favorites" => "keep",
        "recently_added" or "recently_released" => "new",
        "trending_on_server" or "most_watched" or "trending_discover" or "profile_activity_feed" => "popular",
        "recommended_for_you" or "because_you_watched" or "similar_users_liked" or "taste_match" => "picked",
        "collection" or "custom_filter" => "collections", _ => "moods"
    };

    private static readonly (string Key, string Label, string Window, string Icon)[] SeasonalThemes =
    [
        ("valentines", "Valentine's Day", "Feb 7–14", "💝"),
        ("st_patricks", "St. Patrick's Day", "Mar 15–17", "🍀"),
        ("thanksgiving", "Thanksgiving", "Nov 22–30", "🦃"),
        ("christmas", "Christmas", "Dec 1–31", "🎄"),
        ("halloween", "Halloween", "All October", "🎃"),
        ("saturday_morning", "Saturday Morning Cartoons", "Saturday before 1pm", "📺"),
        ("family_movie_night", "Family Movie Night", "Fri & Sat from 5pm", "🍿"),
        ("summer_blockbuster", "Summer Blockbusters", "June – August", "🌴"),
    ];

    public static async Task<RecipeConfigurationResult?> ShowAsync(
        XamlRoot xamlRoot,
        RecipeCatalogResponse catalog, string pageLabel = "Home")
    {
        while (true)
        {
            var choice = await ShowGalleryAsync(xamlRoot, catalog);
            if (choice is null)
                return null;

            var configured = await ShowConfigurationAsync(xamlRoot, choice, pageLabel);
            if (configured.BackToGallery)
                continue;
            return configured.Result;
        }
    }

    public static async Task<RecipeConfigurationResult?> ShowEditorAsync(
        XamlRoot xamlRoot,
        RecipeCatalogResponse catalog,
        SettingsSectionEntry? section, string pageLabel = "Home")
    {
        var definitions = catalog.Categories
            .SelectMany(category => category.Value)
            .Where(definition => !definition.AdminOnly || catalog.AllowAdminOnlyRecipes)
            .GroupBy(definition => definition.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var fallbackTypes = new (string Type, string Label)[]
        {
            ("recently_added", "Recently Added"),
            ("recently_released", "Recently Released"),
            ("genre", "Genre"),
            ("custom_filter", "Custom Filter"),
            ("random", "Random"),
            ("continue_watching", "Continue Watching"),
            ("recommended_for_you", "Recommended For You"),
            ("because_you_watched", "Because You Watched"),
            ("similar_users_liked", "Profiles Like You Enjoyed"),
            ("taste_match", "Top Picks Today"),
            ("next_up", "On Deck"),
            ("next_in_series", "Next in Series"),
            ("watchlist", "Watchlist"),
            ("favorites", "Favorites"),
            ("collection", "Collection"),
        };
        var options = definitions.Values
            .Select(definition => new SectionTypeOption(
                definition.Type,
                definition.Presets.FirstOrDefault()?.DisplayName ?? SectionTypeLabel(definition.Type),
                definition.Category))
            .Concat(fallbackTypes
                .Where(fallback => !definitions.ContainsKey(fallback.Type))
                .Select(fallback => new SectionTypeOption(fallback.Type, fallback.Label, "")))
            .Where(option => option.Type is not ("genre" or "admin_curated_list" or "award_winners"))
            .OrderBy(option => option.Category)
            .ThenBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var selectedType = section?.SectionType ?? "recently_added";
        if (options.All(option => option.Type != selectedType))
            options.Insert(0, new SectionTypeOption(selectedType, SectionTypeLabel(selectedType), ""));
        var config = section?.Config is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(section.Config);
        var title = new TextBox
        {
            Header = "Row name",
            Text = section?.Title ?? "",
            PlaceholderText = SectionTypeLabel(selectedType),
        };
        var itemLimit = new NumberBox
        {
            Header = "Item Limit",
            Minimum = 1,
            Maximum = 100,
            Value = section?.ItemLimit is > 0 ? section.ItemLimit : 20,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        var featured = new ToggleSwitch { IsOn = section?.Featured ?? false };
        var featuredRow = new Grid
        {
            ColumnSpacing = 16,
            Padding = new Thickness(12),
            BorderBrush = ResourceBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
        featuredRow.ColumnDefinitions.Add(new ColumnDefinition());
        featuredRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var featuredCopy = new StackPanel { Spacing = 3 };
        featuredCopy.Children.Add(new TextBlock { Text = "Hero banner", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium });
        featuredCopy.Children.Add(new TextBlock
        {
            Text = "Use this section as the hero banner on the home screen.",
            FontSize = 13,
            Foreground = ResourceBrush("SecondaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        featuredRow.Children.Add(featuredCopy);
        Grid.SetColumn(featured, 1);
        featuredRow.Children.Add(featured);

        var paramHost = new StackPanel { Spacing = 12 };
        var validation = new TextBlock
        {
            Foreground = ResourceBrush("WarningBrush"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var buildGeneration = 0;
        async Task BuildParamsAsync(string type, bool reset)
        {
            var generation = ++buildGeneration;
            if (reset)
            {
                config.Clear();
                if (definitions.TryGetValue(type, out var selectedDefinition) &&
                    selectedDefinition.Presets.FirstOrDefault()?.DefaultParams is { } defaults)
                {
                    foreach (var pair in defaults) config[pair.Key] = pair.Value;
                }
            }
            paramHost.Children.Clear();
            validation.Visibility = Visibility.Collapsed;
            if (type is "genre" or "custom_filter")
                await BuildLegacyFilterFieldsAsync(config, paramHost, type == "genre");
            else
                await BuildParameterFieldsAsync(
                    definitions.GetValueOrDefault(type) ?? new RecipeDefinition { Type = type },
                    config,
                    paramHost,
                    validation);
            if (generation != buildGeneration) return;
        }

        FrameworkElement typeControl;
        var contentLocked = section is { IsCustom: false } && !string.IsNullOrEmpty(section.Id) || HomeSectionWritePolicy.IsTrakt(section?.Config);
        var variantHost = new StackPanel();
        void RenderVariants()
        {
            variantHost.Children.Clear();
            if (contentLocked || !definitions.TryGetValue(selectedType, out var definition)) return;
            var variants = CreateVariantPicker(definition, config, title, () => BuildParamsAsync(selectedType, reset: false));
            if (variants != null) variantHost.Children.Add(variants);
        }
        if (contentLocked)
        {
            typeControl = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Label("Section Type"),
                    new Border
                    {
                        Background = ResourceBrush("SurfaceBrush"),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12, 9, 12, 9),
                        Child = new TextBlock
                        {
                            Text = options.FirstOrDefault(option => option.Type == selectedType)?.Label ?? SectionTypeLabel(selectedType),
                            FontSize = 14,
                            Foreground = ResourceBrush("SecondaryTextBrush"),
                        },
                    },
                },
            };
        }
        else
        {
            var typeCombo = new ComboBox
            {
                Header = "Section Type",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            foreach (var option in options) typeCombo.Items.Add(option);
            typeCombo.SelectedItem = options.First(option => option.Type == selectedType);
            typeCombo.SelectionChanged += async (_, _) =>
            {
                if (typeCombo.SelectedItem is not SectionTypeOption option || option.Type == selectedType) return;
                selectedType = option.Type;
                title.PlaceholderText = option.Label;
                await BuildParamsAsync(selectedType, reset: true);
                RenderVariants();
            };
            typeControl = typeCombo;
        }

        await BuildParamsAsync(selectedType, reset: false);
        var parameterGuard = new ContentControl { Content = paramHost, IsEnabled = !contentLocked, IsTabStop = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var root = new StackPanel { Width = 500, Spacing = 14 };
        root.Children.Add(new TextBlock
        {
            Text = section is null ? "Pick what this row shows, then make it yours." : "Changes apply only to this profile.",
            FontSize = 13,
            Foreground = ResourceBrush("SecondaryTextBrush"),
        });
        root.Children.Add(typeControl);
        root.Children.Add(CreateProfilePreview(title, selectedType, section != null, pageLabel));
        RenderVariants(); root.Children.Add(variantHost);
        root.Children.Add(title);
        root.Children.Add(new Border { Height = 1, Background = ResourceBrush("BorderBrush") });
        root.Children.Add(parameterGuard);
        root.Children.Add(validation);
        root.Children.Add(new Expander { Header = "More options", HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = new StackPanel { Spacing = 12, Children = { itemLimit, featuredRow } } });

        root.Width = double.NaN;
        if (!await HomeRowDialog.ShowAsync(xamlRoot, section is null ? "Add row" : "Edit row", root, section is null ? "Add row" : "Save", () =>
        {
            var definition = definitions.GetValueOrDefault(selectedType) ?? new RecipeDefinition { Type = selectedType };
            var message = Validate(definition, config);
            if (message == null && !FilterEditorsValid(paramHost)) message = "Check the highlighted filter values.";
            return message;
        })) return null;
        return new RecipeConfigurationResult(
            selectedType,
            string.IsNullOrWhiteSpace(title.Text) ? SectionTypeLabel(selectedType) : title.Text.Trim(),
            double.IsNaN(itemLimit.Value) ? 20 : (int)itemLimit.Value,
            featured.IsOn,
            config);
    }

    private static async Task<RecipeChoice?> ShowGalleryAsync(
        XamlRoot xamlRoot,
        RecipeCatalogResponse catalog)
    {
        var choices = catalog.Categories
            .SelectMany(category => category.Value)
            .Where(definition => definition.Type is not ("admin_curated_list" or "genre" or "award_winners")
                && (!definition.AdminOnly || catalog.AllowAdminOnlyRecipes) && (definition.Presets.Count > 0 || definition.Type == "custom_filter"))
            .Select(definition => new RecipeChoice(new RecipeDefinition { Type = definition.Type, Category = PickerGroup(definition.Type), Presets = definition.Presets,
                SupportsRotation = definition.SupportsRotation, AvoidDuplicates = definition.AvoidDuplicates },
                definition.Presets.FirstOrDefault() ?? new GalleryPreset { DisplayName = "Titles matching rules", DescriptionShort = "Build a row from rules, like genre and decade." }))
            .ToList();

        RecipeChoice? selected = null;
        var activeCategory = "all";
        var search = new TextBox
        {
            PlaceholderText = "Search, e.g. trending, 4K, Ghibli, Christmas",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(search, "Search rows");
        var categoryBar = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        var cards = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        var empty = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Padding = new Thickness(0, 38, 0, 38),
            Visibility = Visibility.Collapsed,
        };
        empty.Children.Add(new TextBlock { Text = "🔍", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center });
        var emptyText = new TextBlock
        {
            Foreground = ResourceBrush("SecondaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        empty.Children.Add(emptyText);
        var clearFilters = new Button
        {
            Content = "Clear filters",
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = ResourceStyle("GhostButtonStyle"),
        };
        empty.Children.Add(clearFilters);

        var dialog = CreateDialog(xamlRoot, 1000);
        var header = new Grid { Padding = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "Add a row · Step 1 of 2",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var close = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 13 },
            Style = ResourceStyle("GhostButtonStyle"),
            Padding = new Thickness(7),
        };
        AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => dialog.Hide();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        void RenderCards()
        {
            var columnCount = xamlRoot.Size.Width < 640 ? 1 : xamlRoot.Size.Width < 900 ? 2 : 3;
            cards.ColumnDefinitions.Clear();
            for (var column = 0; column < columnCount; column++) cards.ColumnDefinitions.Add(new ColumnDefinition());
            cards.Children.Clear();
            cards.RowDefinitions.Clear();
            var query = search.Text.Trim();
            var filtered = choices
                .Where(choice => activeCategory == "all" || choice.Definition.Category == activeCategory)
                .Select(choice => (Choice: choice, Score: Score(query, choice.Preset)))
                .Where(entry => string.IsNullOrWhiteSpace(query) || entry.Score > 0)
                .OrderByDescending(entry => string.IsNullOrWhiteSpace(query) ? 0 : entry.Score)
                .Select(entry => entry.Choice)
                .ToList();

            empty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            emptyText.Text = $"No rows match {(query.Length > 0 ? $"\"{query}\"" : "this filter")}.";
            for (var index = 0; index < filtered.Count; index++)
            {
                if (index % columnCount == 0)
                    cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var choice = filtered[index];
                var button = CreateRecipeCard(choice);
                button.Click += (_, _) =>
                {
                    selected = choice;
                    dialog.Hide();
                };
                Grid.SetColumn(button, index % columnCount);
                Grid.SetRow(button, index / columnCount);
                cards.Children.Add(button);
            }
        }

        void RenderCategories()
        {
            categoryBar.Children.Clear();
            var available = choices.Select(choice => choice.Definition.Category).ToHashSet(StringComparer.Ordinal);
            AddCategoryButton("all", "All");
            foreach (var (key, label) in CategoryLabels.Where(category => available.Contains(category.Key)))
                AddCategoryButton(key, label);

            void AddCategoryButton(string key, string label)
            {
                var chip = new ToggleButton
                {
                    Content = label,
                    IsChecked = activeCategory == key,
                    Padding = new Thickness(12, 5, 12, 5),
                    CornerRadius = new CornerRadius(9999),
                    MinHeight = 30,
                };
                chip.Click += (_, _) =>
                {
                    activeCategory = key;
                    RenderCategories();
                    RenderCards();
                };
                categoryBar.Children.Add(chip);
            }
        }

        search.TextChanged += (_, _) => RenderCards();
        clearFilters.Click += (_, _) =>
        {
            activeCategory = "all";
            search.Text = "";
            RenderCategories();
            RenderCards();
        };

        RenderCategories();
        RenderCards();

        var root = new StackPanel { Spacing = 16, Width = Math.Min(752, Math.Max(240, xamlRoot.Size.Width - 80)) };
        root.Children.Add(header);
        root.Children.Add(search);
        root.Children.Add(categoryBar);
        var cardHost = new StackPanel();
        cardHost.Children.Add(cards);
        cardHost.Children.Add(empty);
        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = cardHost,
        });
        dialog.Content = root;
        void ResizeGallery(XamlRoot sender, XamlRootChangedEventArgs args) { root.Width = Math.Min(752, Math.Max(240, xamlRoot.Size.Width - 80)); RenderCards(); }
        xamlRoot.Changed += ResizeGallery;
        try { await dialog.ShowAsync(); }
        finally { xamlRoot.Changed -= ResizeGallery; }
        return selected;
    }

    private static Button CreateRecipeCard(RecipeChoice choice)
    {
        var content = new StackPanel { Spacing = 5 };
        content.Children.Add(new TextBlock { Text = choice.Preset.Icon, FontSize = 19 });
        content.Children.Add(new TextBlock
        {
            Text = choice.Preset.DisplayName,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = choice.Preset.DescriptionShort,
            FontSize = 12,
            Foreground = ResourceBrush("SecondaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
        });
        content.Children.Add(new TextBlock
        {
            Text = choice.Definition.Category.Replace('_', ' ').ToUpperInvariant(),
            FontSize = 10,
            CharacterSpacing = 80,
            Foreground = ResourceBrush("TertiaryTextBrush"),
        });
        var button = new Button
        {
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(12),
            MinHeight = 132,
            Background = ResourceBrush("SurfaceBrush"),
            BorderBrush = ResourceBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        AutomationProperties.SetName(button, choice.Preset.DisplayName);
        return button;
    }

    private static async Task<ConfigurationDialogResult> ShowConfigurationAsync(
        XamlRoot xamlRoot,
        RecipeChoice choice, string pageLabel)
    {
        var config = new Dictionary<string, object>(choice.Preset.DefaultParams);
        var title = new TextBox { Header = "Row name", Text = choice.Preset.DisplayName };
        var itemLimit = new NumberBox
        {
            Header = "Item limit",
            Minimum = 1,
            Maximum = 100,
            Value = 20,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        var featured = new ToggleSwitch { Header = "Use as hero banner", IsOn = false };
        var paramHost = new StackPanel { Spacing = 12 };
        var validation = new TextBlock
        {
            Foreground = ResourceBrush("WarningBrush"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        await BuildParameterFieldsAsync(choice.Definition, config, paramHost, validation);

        using var backCancellation = new CancellationTokenSource();
        var root = new StackPanel { Spacing = 14 };
        var backRequested = false;
        var header = new Grid { Padding = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = $"{choice.Preset.Icon} {choice.Preset.DisplayName}",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var back = new Button
        {
            Content = "← All rows",
            Style = ResourceStyle("GhostButtonStyle"),
            FontSize = 12,
        };
        back.Click += (_, _) =>
        {
            backRequested = true;
            backCancellation.Cancel();
        };
        Grid.SetColumn(back, 1);
        header.Children.Add(back);
        root.Children.Add(header);
        root.Children.Add(new TextBlock { Text = "Step 2 of 2 · New rows go to the bottom of this page", FontSize = 13, Foreground = ResourceBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(CreateProfilePreview(title, choice.Definition.Type, false, pageLabel));
        var variants = CreateVariantPicker(choice.Definition, config, title, async () =>
        { paramHost.Children.Clear(); await BuildParameterFieldsAsync(choice.Definition, config, paramHost, validation); });
        if (variants != null) root.Children.Add(variants);
        root.Children.Add(title);
        root.Children.Add(paramHost);
        root.Children.Add(validation);
        root.Children.Add(new Expander { Header = "More options", HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = new StackPanel { Spacing = 12, Children = { itemLimit, featured } } });

        var result = await HomeRowDialog.ShowAsync(xamlRoot, choice.Preset.DisplayName, root, "Add row", () =>
        {
            var error = Validate(choice.Definition, config);
            return error ?? (FilterEditorsValid(paramHost) ? null : "Check the highlighted filter values.");
        }, backCancellation.Token);
        if (backRequested) return new ConfigurationDialogResult(true, null);
        if (!result) return new ConfigurationDialogResult(false, null);

        return new ConfigurationDialogResult(false, new RecipeConfigurationResult(
            choice.Definition.Type,
            string.IsNullOrWhiteSpace(title.Text) ? choice.Preset.DisplayName : title.Text.Trim(),
            double.IsNaN(itemLimit.Value) ? 20 : (int)itemLimit.Value,
            featured.IsOn,
            config));
    }

    private static async Task BuildParameterFieldsAsync(
        RecipeDefinition definition,
        Dictionary<string, object> config,
        StackPanel host,
        TextBlock validation)
    {
        switch (definition.Type)
        {
            case "collection":
                await AddCollectionFieldAsync(config, host);
                break;
            case "continue_watching":
                if (StringValue(config, "continue_type") == "reading")
                { host.Children.Add(Hint("Continue reading (ebooks, beta)")); break; }
                AddCombo(host, "Continue type", [
                    new Option("watching", "Watching"), new Option("listening", "Listening")],
                    StringValue(config, "continue_type", "watching"),
                    value => config["continue_type"] = value);
                break;
            case "recently_added":
            case "watchlist":
            case "favorites":
                await AddPersonalListFieldsAsync(config, host);
                break;
            case "seasonal_themed":
                AddSeasonalFields(config, host);
                break;
            case "returning_shows":
                AddPositiveInteger(host, config, "lookback_days", "Lookback window (days)", "30",
                    "How far back a new season counts as “just arrived”.");
                break;
            case "short_watches":
                AddPositiveInteger(host, config, "max_minutes", "Maximum runtime (minutes)", "95",
                    "Movies at or under this runtime qualify.");
                break;
            case "anniversaries":
                AddPositiveInteger(host, config, "milestone_years", "Milestone (years)", "5",
                    "Only anniversaries that are a multiple of this many years. Set 1 for every anniversary.");
                break;
            case "editorial_spotlight":
                AddEditorialFields(config, host);
                break;
            case "because_you_watched":
                AddOptionalText(host, config, "anchor_item_id", "Anchor item",
                    "Auto-pick latest watched (leave blank)", "Leave blank to auto-pick the most recent watch.");
                break;
            case "taste_match":
                AddOptionalText(host, config, "genre", "Genre (optional)",
                    "Auto-pick your strongest genre (leave blank)",
                    "Leave blank to follow the profile's strongest taste automatically.");
                break;
            case "admin_curated_list":
                validation.Text = "This recipe is available only from the administrative section editor.";
                validation.Visibility = Visibility.Visible;
                break;
            case "custom_filter":
                await BuildLegacyFilterFieldsAsync(config, host, false);
                break;
        }
    }

    private static FrameworkElement? CreateVariantPicker(RecipeDefinition definition, Dictionary<string, object> config, TextBox title, Func<Task> rebuild)
    {
        var family = HomeRowVariants.Family(definition.Type);
        if (family == null || HomeSectionWritePolicy.IsTrakt(config)
            || definition.Type == "continue_watching" && StringValue(config, "continue_type") == "reading") return null;
        var root = new StackPanel { Name = "HomeRowVariants", Spacing = 8 };
        root.Children.Add(Label(family.Label));
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        var group = Guid.NewGuid().ToString();
        var selectedKey = HomeRowVariants.Selected(definition.Type, config);
        var selectedName = definition.Presets.FirstOrDefault(preset => preset.Key == selectedKey)?.DisplayName;
        var buttons = new List<RadioButton>();
        foreach (var option in family.Options)
        {
            var preset = definition.Presets.FirstOrDefault(preset => preset.Key == option.Key);
            var text = new StackPanel { Spacing = 3 };
            text.Children.Add(new TextBlock { Text = option.Label, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
            var hint = option.Hint ?? preset?.DescriptionShort.TrimEnd('.');
            if (!string.IsNullOrEmpty(hint)) text.Children.Add(new TextBlock { Text = hint, FontSize = 12, Foreground = ResourceBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
            var button = new RadioButton { Content = text, Tag = option.Key, GroupName = group, IsChecked = option.Key == selectedKey,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(10), BorderThickness = new Thickness(1), BorderBrush = ResourceBrush("BorderBrush"), CornerRadius = new CornerRadius(12) };
            AutomationProperties.SetName(button, option.Label);
            button.Checked += async (_, _) =>
            {
                if (option.Key == selectedKey) return;
                var follows = string.IsNullOrWhiteSpace(title.Text) || selectedName != null && title.Text == selectedName;
                HomeRowVariants.Apply(definition.Type, config, option.Key);
                selectedKey = option.Key; selectedName = preset?.DisplayName ?? option.Label;
                if (follows) title.Text = selectedName;
                foreach (var choiceButton in buttons) choiceButton.IsEnabled = false;
                try { await rebuild(); }
                finally { foreach (var choiceButton in buttons) choiceButton.IsEnabled = true; }
            };
            buttons.Add(button); grid.Children.Add(button);
        }
        void Resize(double width)
        {
            var columns = width < 560 ? family.Options.Length == 4 ? 2 : 1 : family.Options.Length is 2 or 3 or 4 ? family.Options.Length : 4;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var n = 0; n < columns; n++) grid.ColumnDefinitions.Add(new());
            for (var n = 0; n < (buttons.Count + columns - 1) / columns; n++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var n = 0; n < buttons.Count; n++) { Grid.SetColumn(buttons[n], n % columns); Grid.SetRow(buttons[n], n / columns); }
        }
        root.SizeChanged += (_, args) => Resize(args.NewSize.Width);
        Resize(700); root.Children.Add(grid); return root;
    }

    private static FrameworkElement CreateProfilePreview(TextBox title, string type, bool editing, string pageLabel)
    {
        var root = new StackPanel { Spacing = 12 };
        var heading = new TextBlock { Text = string.IsNullOrWhiteSpace(title.Text) ? "Untitled row" : title.Text, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        title.TextChanged += (_, _) => heading.Text = string.IsNullOrWhiteSpace(title.Text) ? "Untitled row" : title.Text;
        root.Children.Add(heading);
        var body = new Grid { ColumnSpacing = 12 };
        body.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); body.ColumnDefinitions.Add(new());
        body.Children.Add(new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(12), Background = ResourceBrush("SurfaceBrush"), Child = WebUiIcon.Create(type == "collection" ? "folder" : "sparkles", 20) });
        var copy = new TextBlock { Name = "HomeRowPreviewMessage", Text = editing ? $"You'll see your changes on {pageLabel} after you save." : $"You'll see it on {pageLabel} after you add it.", FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = ResourceBrush("SecondaryTextBrush") };
        Grid.SetColumn(copy, 1); body.Children.Add(copy); root.Children.Add(body);
        return new Border { Name = "HomeRowProfilePreview", Child = root, Padding = new Thickness(18, 16, 18, 16), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), BorderBrush = ResourceBrush("BorderBrush") };
    }

    private static async Task BuildLegacyFilterFieldsAsync(
        Dictionary<string, object> config,
        StackPanel host,
        bool seedGenreRule)
    {
        Action? scopeChanged = null;
        AddCombo(host, "Media Scope", [
            new Option("", "All Media"), new Option("movie", "Movies"),
            new Option("series", "Series"), new Option("episode", "Episodes"),
            new Option("audiobook", "Audiobooks"), new Option("ebook", "Ebooks"),
            new Option("manga", "Manga")],
            StringValue(config, "media_scope"),
            value => { SetOptional(config, "media_scope", value); scopeChanged?.Invoke(); });

        try
        {
            var libraries = await App.Services.GetRequiredService<CatalogApi>().GetLibrariesAsync();
            var selected = IntListValue(config, "library_ids").ToHashSet();
            var checks = new StackPanel { Spacing = 4 };
            foreach (var library in libraries)
            {
                var check = new CheckBox { Content = library.Name, IsChecked = selected.Contains(library.Id) };
                check.Checked += (_, _) =>
                {
                    selected.Add(library.Id);
                    config["library_ids"] = selected.Order().ToList();
                    scopeChanged?.Invoke();
                };
                check.Unchecked += (_, _) =>
                {
                    selected.Remove(library.Id);
                    config["library_ids"] = selected.Order().ToList();
                    scopeChanged?.Invoke();
                };
                checks.Children.Add(check);
            }
            host.Children.Add(new Expander { Header = "Libraries", Content = checks });
        }
        catch
        {
            host.Children.Add(Hint("Libraries could not be loaded."));
        }

        var query = JsonSerializer.Deserialize<SiloPlayer.Core.Models.Collections.QueryDefinition>(JsonSerializer.Serialize(config), V2Json.Options) ?? new();
        if (query.Groups.Count == 0) query.Groups.Add(new() { Rules = seedGenreRule ? [new() { Field = "genre", Op = "is", Value = "" }] : [] });
        var editor = new QueryFilterEditor();
        var scopeGeneration = 0;
        async Task LoadScopeAsync()
        {
            var generation = ++scopeGeneration;
            var scope = StringValue(config, "media_scope");
            var selectedLibraries = IntListValue(config, "library_ids");
            int? libraryId = selectedLibraries.Count == 1 ? selectedLibraries[0] : null;
            CatalogFiltersResponse? filters = null;
            try { filters = await App.Services.GetRequiredService<CatalogApi>().GetFiltersAsync(libraryId, scope: scope); }
            catch { }
            if (generation == scopeGeneration) editor.Load(query, scope, libraryId, filters);
        }
        scopeChanged = () => _ = LoadScopeAsync();
        await LoadScopeAsync();
        void SaveQuery()
        {
            var snapshot = JsonSerializer.SerializeToElement(editor.Query, V2Json.Options);
            foreach (var key in new[] { "match", "groups", "sort" })
                if (snapshot.TryGetProperty(key, out var value)) config[key] = value.Clone();
        }
        editor.Changed += SaveQuery;
        var templates = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        foreach (var (label, field, value) in new[] { ("Unwatched", "watched", (object)false), ("Favorites", "favorited", (object)true), ("Recently added", "added_at", (object)30) })
        {
            var button = new Button { Content = label };
            button.Click += (_, _) =>
            {
                if (query.Groups.Count == 0) query.Groups.Add(new());
                query.Groups[0].Rules.Add(new() { Field = field, Op = field == "added_at" ? "in_last" : "is", Value = value });
                editor.Load(query, StringValue(config, "media_scope"), IntListValue(config, "library_ids").Count == 1 ? IntListValue(config, "library_ids")[0] : null); SaveQuery();
            };
            templates.Children.Add(button);
        }
        host.Children.Add(Hint("Quick starts add a rule. Switching modes keeps every saved group and sort choice."));
        host.Children.Add(templates);
        host.Children.Add(editor);
        var sort = CreateCombo("Sort by", new[] { "title", "year", "rating_imdb", "rating_tmdb", "added_at", "release_date", "runtime" }.Select(field => new Option(field, field switch { "title" => "Title", "year" => "Year", "rating_imdb" => "IMDb rating", "rating_tmdb" => "TMDb rating", "added_at" => "Date added", "release_date" => "Release date", "runtime" => "Runtime", _ => field })).ToArray(), query.Sort?.Field ?? "added_at");
        var order = CreateCombo("Sort order", [new Option("asc", "Ascending"), new Option("desc", "Descending")], query.Sort?.Order ?? "desc");
        var sorting = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed, Children = { sort, order } };
        if (query.Sort != null && !sort.Items.OfType<Option>().Any(option => option.Value == query.Sort.Field))
        { var retained = new Option(query.Sort.Field, query.Sort.Field); sort.Items.Add(retained); sort.SelectedItem = retained; }
        sort.SelectionChanged += (_, _) =>
        {
            if (sort.SelectedItem is not Option selected) return;
            query.Sort ??= new(); query.Sort.Field = selected.Value; query.Sort.Order = selected.Value == "title" ? "asc" : "desc";
            order.SelectedItem = order.Items.OfType<Option>().First(item => item.Value == query.Sort.Order); SaveQuery();
        };
        order.SelectionChanged += (_, _) => { if (order.SelectedItem is Option selected) { query.Sort ??= new(); query.Sort.Order = selected.Value; SaveQuery(); } };
        sorting.Visibility = Visibility.Visible;
        host.Children.Add(new Expander { Header = "Sort settings", Content = sorting, HorizontalAlignment = HorizontalAlignment.Stretch });
    }

    private static bool FilterEditorsValid(DependencyObject root)
    {
        if (root is QueryRulesEditor editor && !editor.IsValid || root is QueryFilterEditor filter && !filter.IsValid) return false;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (!FilterEditorsValid(VisualTreeHelper.GetChild(root, index))) return false;
        return true;
    }

    private static async Task AddCollectionFieldAsync(Dictionary<string, object> config, StackPanel host)
    {
        var isAutoBacked = StringValue(config, "source_provider") == "trakt" &&
            StringValue(config, "source_preset") is "trending" or "popular";
        if (isAutoBacked && string.IsNullOrWhiteSpace(StringValue(config, "library_collection_id")) &&
            string.IsNullOrWhiteSpace(StringValue(config, "user_collection_id")))
        {
            host.Children.Add(Hint($"A synced Trakt {StringValue(config, "source_preset")} " +
                $"{(StringValue(config, "media_type") == "tv" ? "shows" : "movies")} collection will be created automatically."));
            return;
        }

        var combo = new ComboBox { Header = "Collection", Width = double.NaN, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.Items.Add(new CollectionOption("", "Choose a collection…", "", ""));
        try
        {
            var collectionsApi = App.Services.GetRequiredService<CollectionsApi>();
            var catalogApi = App.Services.GetRequiredService<CatalogApi>();
            var userTask = collectionsApi.GetCollectionsAsync();
            var libraries = await catalogApi.GetLibrariesAsync();
            var libraryTasks = libraries.Select(async library => (Library: library,
                Response: await catalogApi.GetLibraryCollectionsAsync(library.Id))).ToArray();
            await Task.WhenAll(libraryTasks);

            foreach (var collection in (await userTask).Collections)
                if (StringValue(config, "source_provider") != "trakt") combo.Items.Add(new CollectionOption(collection.Id, collection.Name, "My Collections", "user"));

            var libraryOptions = new Dictionary<string, CollectionOption>(StringComparer.Ordinal);
            foreach (var task in libraryTasks)
            {
                var (library, response) = await task;
                foreach (var collection in response.Collections.Where(collection => collection.Visibility == "visible"))
                {
                    if (StringValue(config, "source_provider") == "trakt" && (collection.CollectionType != "trakt" || collection.SourceConfig == null
                        || StringValue(collection.SourceConfig, "preset") != StringValue(config, "source_preset") || StringValue(collection.SourceConfig, "media_type") != StringValue(config, "media_type"))) continue;
                    if (libraryOptions.TryGetValue(collection.Id, out var existing))
                    {
                        existing.Group = $"{existing.Group}, {library.Name}";
                        continue;
                    }
                    var option = new CollectionOption(collection.Id, collection.Title, library.Name, "library");
                    libraryOptions[collection.Id] = option;
                    combo.Items.Add(option);
                }
            }
        }
        catch
        {
            host.Children.Add(Hint("Collections could not be loaded. Close this panel and try again."));
        }

        var filtering = false;
        var selectedId = StringValue(config, "user_collection_id");
        if (selectedId.Length == 0)
            selectedId = StringValue(config, "library_collection_id");
        combo.SelectedItem = combo.Items.OfType<CollectionOption>().FirstOrDefault(option => option.Id == selectedId) ?? combo.Items[0];
        combo.SelectionChanged += (_, _) =>
        {
            if (filtering) return;
            if (combo.SelectedItem is not CollectionOption option || option.Id.Length == 0)
            {
                config.Remove("library_collection_id");
                config.Remove("user_collection_id");
            }
            else if (option.Source == "user")
            {
                config["user_collection_id"] = option.Id;
                config.Remove("library_collection_id");
            }
            else
            {
                config["library_collection_id"] = option.Id;
                config.Remove("user_collection_id");
            }
        };
        var choices = combo.Items.OfType<CollectionOption>().ToList();
        var search = new TextBox { PlaceholderText = "Search collection title or group" };
        search.TextChanged += (_, _) =>
        {
            filtering = true;
            var selected = combo.SelectedItem;
            combo.Items.Clear();
            foreach (var option in choices.Where(option => option.Id.Length == 0 || option.Id == (selected as CollectionOption)?.Id || $"{option.Title} {option.Group}".Contains(search.Text, StringComparison.CurrentCultureIgnoreCase))) combo.Items.Add(option);
            combo.SelectedItem = selected;
            filtering = false;
        };
        host.Children.Add(search);
        host.Children.Add(combo);
    }

    private static async Task AddPersonalListFieldsAsync(Dictionary<string, object> config, StackPanel host)
    {
        AddCombo(host, "Media type", [
            new Option("", "All Media"), new Option("movie", "Movies"),
            new Option("series", "TV Shows"), new Option("audiobook", "Audiobooks")],
            StringValue(config, "filter_type"), value => SetOptional(config, "filter_type", value));

        try
        {
            var libraries = await App.Services.GetRequiredService<CatalogApi>().GetLibrariesAsync();
            var selected = IntListValue(config, "filter_library_ids").ToHashSet();
            var panel = new StackPanel { Spacing = 5 };
            panel.Children.Add(Label("Libraries"));
            foreach (var library in libraries)
            {
                var check = new CheckBox { Content = library.Name, IsChecked = selected.Contains(library.Id) };
                check.Checked += (_, _) =>
                {
                    selected.Add(library.Id);
                    config["filter_library_ids"] = selected.ToList();
                };
                check.Unchecked += (_, _) =>
                {
                    selected.Remove(library.Id);
                    if (selected.Count == 0) config.Remove("filter_library_ids");
                    else config["filter_library_ids"] = selected.ToList();
                };
                panel.Children.Add(check);
            }
            host.Children.Add(new Expander { Header = "Libraries", Content = panel });
        }
        catch
        {
            host.Children.Add(Hint("Libraries could not be loaded."));
        }

        var sort = StringValue(config, "sort");
        var order = StringValue(config, "order", sort == "title" ? "asc" : "desc");
        AddCombo(host, "Sort", [
            new Option("", "List order (default)"),
            new Option("added_at:desc", "Date added (newest first)"),
            new Option("added_at:asc", "Date added (oldest first)"),
            new Option("title:asc", "Title (A–Z)"), new Option("title:desc", "Title (Z–A)"),
            new Option("release_date:desc", "Release date (newest first)"),
            new Option("release_date:asc", "Release date (oldest first)"),
            new Option("rating_imdb:desc", "IMDb rating (highest first)"),
        ], sort.Length == 0 ? "" : $"{sort}:{order}", value =>
        {
            if (value.Length == 0)
            {
                config.Remove("sort");
                config.Remove("order");
                return;
            }
            var pieces = value.Split(':', 2);
            config["sort"] = pieces[0];
            config["order"] = pieces[1];
        });
    }

    private static void AddSeasonalFields(Dictionary<string, object> config, StackPanel host)
    {
        host.Children.Add(Label("Holidays to celebrate"));
        var enabled = StringListValue(config, "enabled_themes").ToHashSet(StringComparer.Ordinal);
        if (enabled.Count == 0 && StringValue(config, "theme") is { Length: > 0 } legacy)
            enabled.Add(legacy);
        var titles = DictionaryValue(config, "theme_titles");
        var rows = new StackPanel { Spacing = 7 };

        void Commit()
        {
            config["enabled_themes"] = enabled.ToList();
            config["theme_titles"] = titles
                .Where(pair => enabled.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value?.ToString()))
                .ToDictionary(pair => pair.Key, pair => pair.Value?.ToString()!.Trim() ?? "");
            config["theme"] = "";
            config["mode"] = "";
        }

        foreach (var theme in SeasonalThemes)
        {
            var titleBox = new TextBox
            {
                PlaceholderText = $"Section title in season — defaults to \"{theme.Label}\"",
                Text = titles.TryGetValue(theme.Key, out var value) ? value?.ToString() ?? "" : "",
                Margin = new Thickness(28, 0, 0, 0),
                Visibility = enabled.Contains(theme.Key) ? Visibility.Visible : Visibility.Collapsed,
            };
            var check = new CheckBox
            {
                Content = $"{theme.Icon}  {theme.Label}   {theme.Window}",
                IsChecked = enabled.Contains(theme.Key),
            };
            check.Checked += (_, _) => { enabled.Add(theme.Key); titleBox.Visibility = Visibility.Visible; Commit(); };
            check.Unchecked += (_, _) => { enabled.Remove(theme.Key); titleBox.Visibility = Visibility.Collapsed; Commit(); };
            titleBox.TextChanged += (_, _) => { titles[theme.Key] = titleBox.Text; Commit(); };
            rows.Children.Add(check);
            rows.Children.Add(titleBox);
        }
        host.Children.Add(new Border
        {
            BorderBrush = ResourceBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Background = ResourceBrush("SurfaceBrush"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Child = rows,
        });
        host.Children.Add(Hint("The section auto-cycles: it shows whichever enabled holiday is currently in season, and hides itself when none match. Per-holiday titles override the section name only while that holiday is active."));
    }

    private static void AddEditorialFields(Dictionary<string, object> config, StackPanel host)
    {
        AddCombo(host, "Subject type", [new Option("director", "Director"), new Option("studio", "Studio"),
            new Option("actor", "Actor"), new Option("era", "Era")],
            StringValue(config, "subject_type", "director"), value => config["subject_type"] = value);
        var autoRotate = BoolValue(config, "auto_rotate", !config.ContainsKey("subject"));
        var subject = new TextBox { Header = "Subject", PlaceholderText = "e.g. Christopher Nolan", Text = StringValue(config, "subject") };
        var cadence = CreateCombo("Rotation cadence", [new Option("daily", "Daily"),
            new Option("weekly", "Weekly (default)"), new Option("monthly", "Monthly")],
            StringValue(config, "rotation_cadence", "weekly"));
        var toggle = new CheckBox { Content = "Auto-rotate", IsChecked = autoRotate };
        void ApplyMode()
        {
            var on = toggle.IsChecked == true;
            config["auto_rotate"] = on;
            cadence.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            subject.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        }
        toggle.Checked += (_, _) => ApplyMode();
        toggle.Unchecked += (_, _) => ApplyMode();
        cadence.SelectionChanged += (_, _) => { if (cadence.SelectedItem is Option option) config["rotation_cadence"] = option.Value; };
        subject.TextChanged += (_, _) => SetOptional(config, "subject", subject.Text);
        host.Children.Add(toggle);
        host.Children.Add(cadence);
        host.Children.Add(subject);
        ApplyMode();
    }

    private static void AddPositiveInteger(StackPanel host, Dictionary<string, object> config,
        string key, string label, string placeholder, string hint)
    {
        var box = new NumberBox
        {
            Header = label,
            PlaceholderText = placeholder,
            Minimum = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        var current = IntValue(config, key);
        if (current > 0) box.Value = current;
        box.ValueChanged += (_, _) =>
        {
            if (double.IsNaN(box.Value) || box.Value < 1 || box.Value != Math.Truncate(box.Value)) config.Remove(key);
            else config[key] = (int)box.Value;
        };
        host.Children.Add(box);
        host.Children.Add(Hint(hint));
    }

    private static void AddOptionalText(StackPanel host, Dictionary<string, object> config,
        string key, string label, string placeholder, string hint)
    {
        var box = new TextBox { Header = label, PlaceholderText = placeholder, Text = StringValue(config, key) };
        box.TextChanged += (_, _) => SetOptional(config, key, box.Text);
        host.Children.Add(box);
        host.Children.Add(Hint(hint));
    }

    private static void AddCombo(StackPanel host, string header, IReadOnlyList<Option> options,
        string selected, Action<string> changed)
    {
        var combo = CreateCombo(header, options, selected);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is Option option)
                changed(option.Value);
        };
        host.Children.Add(combo);
    }

    private static ComboBox CreateCombo(string header, IReadOnlyList<Option> options, string selected)
    {
        var combo = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var option in options) combo.Items.Add(option);
        combo.SelectedItem = combo.Items.OfType<Option>().FirstOrDefault(option => option.Value == selected) ?? combo.Items[0];
        return combo;
    }

    private static string? Validate(RecipeDefinition definition, Dictionary<string, object> config)
    {
        if (definition.Type == "collection")
        {
            var hasCollection = StringValue(config, "library_collection_id").Length > 0 || StringValue(config, "user_collection_id").Length > 0;
            var autoBacked = StringValue(config, "source_provider") == "trakt" && StringValue(config, "source_preset") is "trending" or "popular";
            if (!hasCollection && !autoBacked) return "Choose a collection before adding this row.";
        }
        if (definition.Type == "seasonal_themed" && config.TryGetValue("enabled_themes", out var themes)
            && JsonSerializer.SerializeToElement(themes) is { ValueKind: JsonValueKind.Array } list && list.GetArrayLength() == 0)
            return "Choose at least one seasonal theme.";
        if (definition.Type == "admin_curated_list" && StringListValue(config, "item_ids").Count == 0)
            return "Add at least one title to the curated list.";
        return null;
    }

    private static int Score(string query, GalleryPreset preset)
    {
        if (string.IsNullOrWhiteSpace(query)) return 1;
        var name = preset.DisplayName;
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 80;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 60;
        return preset.DescriptionShort.Contains(query, StringComparison.OrdinalIgnoreCase) ? 30 : 0;
    }

    private static string SectionTypeLabel(string type) => type switch
    {
        "recently_added" => "Recently Added",
        "recently_released" => "Recently Released",
        "genre" => "Genre",
        "custom_filter" => "Custom Filter",
        "random" => "Random",
        "continue_watching" => "Continue Watching",
        "recommended_for_you" => "Recommended For You",
        "because_you_watched" => "Because You Watched",
        "similar_users_liked" => "Profiles Like You Enjoyed",
        "taste_match" => "Top Picks Today",
        "next_up" => "On Deck",
        "next_in_series" => "Next in Series",
        "watchlist" => "Watchlist",
        "favorites" => "Favorites",
        "collection" => "Collection",
        _ => type.Replace('_', ' '),
    };

    private static List<EditableFilterGroup> ReadFilterGroups(Dictionary<string, object> config)
    {
        if (!config.TryGetValue("groups", out var raw) || raw is null) return [];
        JsonElement json;
        if (raw is JsonElement element)
            json = element;
        else
        {
            try { json = JsonSerializer.SerializeToElement(raw); }
            catch { return []; }
        }
        if (json.ValueKind != JsonValueKind.Array) return [];
        var result = new List<EditableFilterGroup>();
        foreach (var groupElement in json.EnumerateArray())
        {
            if (groupElement.ValueKind != JsonValueKind.Object) continue;
            var group = new EditableFilterGroup
            {
                Match = groupElement.TryGetProperty("match", out var match) && match.GetString() == "any" ? "any" : "all",
            };
            if (groupElement.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
            {
                foreach (var ruleElement in rules.EnumerateArray())
                {
                    if (ruleElement.ValueKind != JsonValueKind.Object) continue;
                    var op = ruleElement.TryGetProperty("op", out var opElement)
                        ? opElement.GetString()
                        : ruleElement.TryGetProperty("operator", out var legacyOp) ? legacyOp.GetString() : null;
                    group.Rules.Add(new EditableFilterRule
                    {
                        Field = ruleElement.TryGetProperty("field", out var field) ? field.GetString() ?? "genre" : "genre",
                        Operator = op ?? "contains",
                        Value = ruleElement.TryGetProperty("value", out var value) ? FormatFilterValue(value) : "",
                    });
                }
            }
            result.Add(group);
        }
        return result;
    }

    private static object ParseFilterValue(EditableFilterRule rule)
    {
        var value = rule.Value.Trim();
        if (rule.Operator == "between")
        {
            var pieces = value.Split([',', '-'], 2, StringSplitOptions.TrimEntries);
            return pieces.Length == 2
                ? new[] { ParseFilterScalar(rule.Field, pieces[0]), ParseFilterScalar(rule.Field, pieces[1]) }
                : new[] { (object)value, value };
        }
        return ParseFilterScalar(rule.Field, value);
    }

    private static object ParseFilterScalar(string field, string value)
    {
        if (field == "watched" && bool.TryParse(value, out var boolean)) return boolean;
        if (field is "year" or "runtime" && int.TryParse(value, out var integer)) return integer;
        if (field == "rating_imdb" && double.TryParse(value, out var number)) return number;
        return value;
    }

    private static string FormatFilterValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return string.Join(", ", value.EnumerateArray().Select(FormatFilterValue));
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static ContentDialog CreateDialog(XamlRoot xamlRoot, double maxWidth)
    {
        var dialog = new ContentDialog { XamlRoot = xamlRoot };
        dialog.Resources["ContentDialogMaxWidth"] = maxWidth;
        return dialog;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = ResourceBrush("SecondaryTextBrush"),
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = ResourceBrush("TertiaryTextBrush"),
        TextWrapping = TextWrapping.Wrap,
    };

    private static Brush ResourceBrush(string key) => (Brush)Application.Current.Resources[key];
    private static Style ResourceStyle(string key) => (Style)Application.Current.Resources[key];

    private static void SetOptional(Dictionary<string, object> config, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) config.Remove(key);
        else config[key] = value.Trim();
    }

    private static string StringValue(Dictionary<string, object> config, string key, string fallback = "")
    {
        if (!config.TryGetValue(key, out var value) || value is null) return fallback;
        if (value is JsonElement json && json.ValueKind == JsonValueKind.String) return json.GetString() ?? fallback;
        return value as string ?? value.ToString() ?? fallback;
    }

    private static bool BoolValue(Dictionary<string, object> config, string key, bool fallback)
    {
        if (!config.TryGetValue(key, out var value) || value is null) return fallback;
        if (value is JsonElement json && json.ValueKind is JsonValueKind.True or JsonValueKind.False) return json.GetBoolean();
        return value is bool boolean ? boolean : fallback;
    }

    private static int IntValue(Dictionary<string, object> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value is null) return 0;
        if (value is JsonElement json && json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out var parsed)) return parsed;
        return value is IConvertible convertible ? Convert.ToInt32(convertible) : 0;
    }

    private static List<string> StringListValue(Dictionary<string, object> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value is null) return [];
        if (value is JsonElement json && json.ValueKind == JsonValueKind.Array)
            return json.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList();
        return value is IEnumerable<string> strings ? strings.ToList() : [];
    }

    private static List<int> IntListValue(Dictionary<string, object> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value is null) return [];
        if (value is JsonElement json && json.ValueKind == JsonValueKind.Array)
            return json.EnumerateArray().Where(item => item.TryGetInt32(out _)).Select(item => item.GetInt32()).ToList();
        return value is IEnumerable<int> ints ? ints.ToList() : [];
    }

    private static Dictionary<string, object> DictionaryValue(Dictionary<string, object> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value is null) return [];
        if (value is Dictionary<string, object> dictionary) return new(dictionary);
        if (value is JsonElement json && json.ValueKind == JsonValueKind.Object)
            return json.EnumerateObject().ToDictionary(property => property.Name, property => (object)(property.Value.GetString() ?? ""));
        return [];
    }

    private sealed record RecipeChoice(RecipeDefinition Definition, GalleryPreset Preset);
    private sealed record ConfigurationDialogResult(bool BackToGallery, RecipeConfigurationResult? Result);
    private sealed record SectionTypeOption(string Type, string Label, string Category)
    {
        public override string ToString() => Category.Length == 0
            ? Label
            : $"{Category.Replace('_', ' ')} — {Label}";
    }
    private sealed class EditableFilterGroup
    {
        public string Match { get; set; } = "all";
        public List<EditableFilterRule> Rules { get; } = [];
    }
    private sealed class EditableFilterRule
    {
        public string Field { get; set; } = "genre";
        public string Operator { get; set; } = "contains";
        public string Value { get; set; } = "";
    }
    private sealed record Option(string Value, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed class CollectionOption(string id, string title, string group, string source)
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public string Group { get; set; } = group;
        public string Source { get; } = source;
        public override string ToString() => Group.Length == 0 ? Title : $"{Title} — {Group}";
    }
}

public sealed record RecipeConfigurationResult(
    string SectionType,
    string Title,
    int ItemLimit,
    bool Featured,
    Dictionary<string, object> Config);
