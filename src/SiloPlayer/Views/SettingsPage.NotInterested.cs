using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private NotInterestedListState? _notInterested;
    private readonly Dictionary<string, FrameworkElement> _notInterestedRowElements = new(StringComparer.Ordinal);

    private void InitializeNotInterested()
    {
        Loaded += (_, _) =>
        {
            WeakReferenceMessenger.Default.Unregister<NotInterestedChange>(this);
            WeakReferenceMessenger.Default.Register<NotInterestedChange>(this, (_, message) => DispatcherQueue.TryEnqueue(() =>
            {
                var catalog = App.Services.GetRequiredService<CatalogApi>();
                if (!IsLoaded || !catalog.IsCurrentContext(message.Context) || PersonalizePanel.Visibility != Visibility.Visible) return;
                _ = LoadNotInterestedAsync();
            }));
        };
        Unloaded += (_, _) => { WeakReferenceMessenger.Default.Unregister<NotInterestedChange>(this); _notInterested?.Cancel(); };
    }

    private async Task LoadNotInterestedAsync()
    {
        if (_notInterested == null)
        {
            _notInterested = new(App.Services.GetRequiredService<CatalogApi>());
            _notInterested.PropertyChanged += (_, _) => RenderNotInterested();
            _notInterested.Items.CollectionChanged += (_, _) => RenderNotInterestedRows();
        }
        await _notInterested.LoadAsync();
        RenderNotInterested();
    }

    private void RenderNotInterested()
    {
        if (_notInterested == null) return;
        var state = _notInterested;
        NotInterestedPanel.Visibility = state.Supported || state.IsLoading || state.Error != null ? Visibility.Visible : Visibility.Collapsed;
        NotInterestedLoading.Visibility = state.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        NotInterestedErrorPanel.Visibility = state.Error != null ? Visibility.Visible : Visibility.Collapsed;
        NotInterestedError.Text = state.Error ?? "";
        NotInterestedRetry.IsEnabled = !state.IsLoading;
        NotInterestedEmpty.Visibility = !state.IsLoading && state.Error == null && state.Items.Count == 0 && !state.HasMore ? Visibility.Visible : Visibility.Collapsed;
        NotInterestedMore.Visibility = state.HasMore ? Visibility.Visible : Visibility.Collapsed;
        NotInterestedMore.Content = state.IsLoadingMore ? "Loading…" : state.MoreError != null ? "Retry loading more" : "Show more";
        NotInterestedMore.IsEnabled = !state.IsLoading && !state.IsLoadingMore;
        NotInterestedMoreError.Text = state.MoreError ?? "";
        NotInterestedMoreError.Visibility = state.MoreError != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderNotInterestedRows()
    {
        if (_notInterested == null) return;
        var ids = _notInterested.Items.Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _notInterestedRowElements.Keys.Where(id => !ids.Contains(id)).ToArray())
        { NotInterestedRows.Children.Remove(_notInterestedRowElements[id]); _notInterestedRowElements.Remove(id); }
        foreach (var item in _notInterested.Items)
        {
            if (_notInterestedRowElements.ContainsKey(item.ContentId)) continue;
            var row = CreateNotInterestedRow(item);
            _notInterestedRowElements.Add(item.ContentId, row); NotInterestedRows.Children.Add(row);
        }
        for (var index = 0; index < NotInterestedRows.Children.Count; index++)
            if (NotInterestedRows.Children[index] is Grid row)
            {
                row.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
                row.BorderThickness = index == 0 ? new Thickness(0) : new Thickness(0, 1, 0, 0);
            }
        RenderNotInterested();
    }

    private FrameworkElement CreateNotInterestedRow(MediaItem item)
    {
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var context = catalog.CaptureContext();
        var row = new Grid { ColumnSpacing = 12, RowSpacing = 8, Padding = new Thickness(0, 8, 0, 8) };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var poster = new Image { Width = 44, Height = 64, Stretch = Stretch.UniformToFill };
        if (Uri.TryCreate(item.PosterUrl, UriKind.Absolute, out var posterUri)) poster.Source = new BitmapImage(posterUri);
        var artwork = new Button { Content = poster, Padding = new Thickness(0), Width = 44, Height = 64, BorderThickness = new Thickness(0) };
        void Navigate() { if (catalog.IsCurrentContext(context)) App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(item.ContentId); }
        artwork.Click += (_, _) => Navigate(); AutomationProperties.SetName(artwork, item.Title);
        Grid.SetRowSpan(artwork, 2); row.Children.Add(artwork);
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock { Text = item.Title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"], TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        var title = new HyperlinkButton { Content = titleText, Padding = new Thickness(0), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left, MaxWidth = 480, Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] };
        title.Click += (_, _) => Navigate();
        text.Children.Add(title);
        text.Children.Add(new TextBlock { Text = (item.Year > 0 ? item.Year + " · " : "") + (item.Type == "series" ? "Series" : "Movie"), FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        Grid.SetColumn(text, 1); row.Children.Add(text);
        var action = new Button { Content = "Show in Recommendations", Style = (Style)Application.Current.Resources["OutlineButtonStyle"], HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 6, 10, 6), FontSize = 13 };
        AutomationProperties.SetName(action, "Show " + item.Title + " in Recommendations");
        action.Click += async (_, _) =>
        {
            if (!catalog.IsCurrentContext(context) || !action.IsEnabled) return;
            action.IsEnabled = false;
            try { (item.UserState ??= new()).NotInterested = true; await NotInterestedActions.ToggleAsync(item); }
            finally { action.IsEnabled = true; }
        };
        Grid.SetColumn(action, 2); row.Children.Add(action);
        row.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 570;
            Grid.SetRow(action, narrow ? 1 : 0); Grid.SetColumn(action, narrow ? 1 : 2);
            title.MaxWidth = Math.Max(60, args.NewSize.Width - 68 - (narrow ? 0 : 205));
            titleText.MaxWidth = title.MaxWidth;
        };
        return row;
    }

    private async void NotInterestedRetry_Click(object sender, RoutedEventArgs e) => await LoadNotInterestedAsync();
    private async void NotInterestedMore_Click(object sender, RoutedEventArgs e) { if (_notInterested != null) await _notInterested.LoadMoreAsync(); }
}
