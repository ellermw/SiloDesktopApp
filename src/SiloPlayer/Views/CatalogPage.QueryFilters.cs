using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SiloPlayer.Views;

public sealed partial class CatalogPage
{
    private QueryFilterEditor? _queryFilters;
    private QueryDefinition _catalogQuery = new();
    private WrapPanel? _queryChips;
    private CancellationTokenSource? _queryOptionsOwner;

    private async Task RefreshQueryScopeAsync()
    {
        if (_queryFilters == null) return;
        var owner = new CancellationTokenSource();
        Interlocked.Exchange(ref _queryOptionsOwner, owner)?.Cancel();
        var library = SelectedLibrary(); var scope = SelectedTag(TypeCombo) ?? _scope;
        _queryFilters.Load(_catalogQuery, scope, library);
        try
        {
            var options = await _api.GetFiltersAsync(libraryId: library, scope: scope,
                source: _source == "library" ? null : _source, sectionId: _sectionId, ct: owner.Token);
            if (_queryOptionsOwner == owner && !owner.IsCancellationRequested && library == SelectedLibrary() && scope == (SelectedTag(TypeCombo) ?? _scope))
                _queryFilters.Load(_catalogQuery, scope, library, options);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (_queryOptionsOwner == owner) App.Services.GetRequiredService<SiloPlayer.Services.ToastService>().Error("Filter suggestions could not be loaded: " + ex.Message); }
        finally { Interlocked.CompareExchange(ref _queryOptionsOwner, null, owner); owner.Dispose(); }
    }

    private void InitializeQueryFilters(CatalogFiltersResponse filters)
    {
        if (_queryFilters != null) return;
        // A closed sheet has not realized its visual tree. Resolve the query
        // host from SheetContent instead of FrameworkElement.Parent, which is
        // null until the template and its scroll presenter have been laid out.
        if (FiltersSheet.SheetContent is not Grid sheetBody ||
            sheetBody.Children.OfType<ScrollViewer>().Single().Content is not Grid host) return;
        var librarySelector = LibraryCombo;
        _catalogQuery.Groups.Add(new QueryGroup { Rules = BuildGuidedRules() });
        if (!string.IsNullOrWhiteSpace(_initialGenre)) _catalogQuery.Groups[0].Rules.Add(new() { Field = "genre", Op = "is", Value = _initialGenre });
        foreach (var (field, value) in new[] { ("genre", SelectedTag(GenreCombo)), ("studio", SelectedTag(StudioCombo)), ("country", SelectedTag(CountryCombo)), ("content_rating", SelectedTag(RatingCombo)), ("resolution", SelectedTag(ResolutionCombo)), ("audio_language", SelectedTag(AudioLanguageCombo)) })
            if (!string.IsNullOrWhiteSpace(value)) _catalogQuery.Groups[0].Rules.Add(new() { Field = field, Op = "is", Value = value });
        _catalogQuery.Groups.RemoveAll(group => group.Rules.Count == 0);
        _queryFilters = new QueryFilterEditor();
        _queryFilters.ConfigureSort(_source is not ("favorites" or "watchlist" or "history"), _source == "history" ? "date_viewed" : null);
        _queryFilters.Load(_catalogQuery, _scope, SelectedLibrary(), filters);
        FiltersSheet.ConfigureFilterHeader(_queryFilters.DetachModeSelector(), "Refine your catalog results");
        ConfigureCatalogFilterSheetBody();
        SizeChanged += (_, _) => SizeCatalogFilterSheet();
        SizeCatalogFilterSheet();
        GuidedFiltersPanel.Children.Remove(librarySelector);
        GuidedFiltersPanel.Visibility = Visibility.Collapsed;
        AdvancedFiltersPanel.Visibility = Visibility.Collapsed;
        GuidedModeButton.Visibility = AdvancedModeButton.Visibility = Visibility.Collapsed;
        var shared = new StackPanel { Spacing = 16 };
        shared.Children.Add(new TextBlock { Text = "Library", FontSize = 14, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
        shared.Children.Add(librarySelector);
        shared.Children.Add(_queryFilters);
        host.Children.Add(shared);
        _queryChips = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        FilterPanel.Children.Add(_queryChips);
        _queryFilters.Changed += () => { UpdateQueryFilterChips(); Filter_Changed(_queryFilters, new RoutedEventArgs()); };
        _queryFilters.SortChanged += () =>
        {
            var sort = _catalogQuery.Sort; if (sort == null) return;
            _initializing = true;
            try
            {
                SortCombo.SelectedItem = SortCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == sort.Field);
                OrderCombo.SelectedItem = OrderCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == sort.Order);
                OrderCombo.Visibility = Visibility.Visible;
            }
            finally { _initializing = false; }
            if (PersonalCatalogSortPolicy.SupportsSourceOrder(_source)) QueuePersonalSortPreferenceSave();
        };
        UpdateQueryFilterChips();
    }

    private void SizeCatalogFilterSheet()
    {
        if (ActualWidth <= 0) return;
        var viewportWidth = SiloPlayer.Helpers.WebUiViewport.Width(this, ActualWidth);
        FiltersSheet.PreferredWidth = Math.Min(ActualWidth,
            Math.Min(viewportWidth * .75, viewportWidth >= 640 ? 448 : double.PositiveInfinity));
    }

    private void ConfigureCatalogFilterSheetBody()
    {
        if (FiltersSheet.SheetContent is not Grid body) return;
        var oldHeader = body.Children.OfType<Grid>().Single(child => Grid.GetRow(child) == 0);
        body.Children.Remove(oldHeader);
        var scroll = body.Children.OfType<ScrollViewer>().Single();
        Grid.SetRow(scroll, 0);
        if (scroll.Content is Grid panel) panel.Padding = new Thickness(0, 0, 0, 16);
        var footer = body.Children.OfType<Grid>().Single();
        Grid.SetRow(footer, 1);
        foreach (var button in footer.Children.OfType<Button>()) { button.Height = 32; button.MinHeight = 32; button.FontSize = 14; }
        body.Children.Remove(footer);
        var footerBorder = new Border { Child = footer, BorderThickness = new(0, 1, 0, 0), Padding = new(0, 16, 0, 0), BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"] };
        Grid.SetRow(footerBorder, 1);
        body.Children.Add(footerBorder);
        body.RowDefinitions.RemoveAt(0);
        body.RowSpacing = 16;
    }

    private void ClearQueryFilters()
    {
        _catalogQuery = new();
        _queryFilters?.Load(_catalogQuery, _scope, SelectedLibrary());
        UpdateQueryFilterChips();
    }

    private void UpdateQueryFilterChips()
    {
        if (_queryChips == null) return;
        _queryChips.Children.Clear();
        var badges = CatalogFilterBadges.Create(_catalogQuery, _scope);
        foreach (var badge in badges)
        {
            var chip = CatalogFilterBadgeView.Build(badge.Label, () => { badge.Remove(); _queryFilters?.Load(_catalogQuery, _scope, SelectedLibrary()); UpdateQueryFilterChips(); if (_queryFilters != null) Filter_Changed(_queryFilters, new RoutedEventArgs()); });
            _queryChips.Children.Add(chip);
        }
        var count = CatalogFilterBadges.ActiveCount(_catalogQuery, _scope);
        FilterCountText.Text = count.ToString(); FilterCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
