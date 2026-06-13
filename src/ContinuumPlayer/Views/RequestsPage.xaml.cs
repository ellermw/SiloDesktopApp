using ContinuumPlayer.Core.Models.Requests;
using ContinuumPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ContinuumPlayer.Views;

public sealed partial class RequestsPage : Page
{
    public RequestsViewModel ViewModel { get; }

    public RequestsPage()
    {
        ViewModel = App.Services.GetRequiredService<RequestsViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
        Render();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
        Render();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        await RunSearchAsync();
    }

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        ViewModel.SearchQuery = SearchBox.Text;
        ViewModel.SelectedMediaType = SelectedTag(MediaTypeComboBox, "all");
        await ViewModel.SearchCommand.ExecuteAsync(null);
        Render();
    }

    private void Render()
    {
        LoadingRing.IsActive = ViewModel.IsLoading || ViewModel.IsSearching;
        LoadingRing.Visibility = LoadingRing.IsActive ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = ViewModel.StatusMessage;
        ErrorText.Text = ViewModel.ErrorMessage ?? "";
        ErrorText.Visibility = string.IsNullOrWhiteSpace(ViewModel.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

        BuildMyRequests();
        BuildSearchResults();
        BuildDiscovery();
    }

    private void BuildMyRequests()
    {
        MyRequestsList.Children.Clear();
        if (!ViewModel.RequestsEnabled)
        {
            MyRequestsList.Children.Add(MakeMutedText("Requests are disabled on this server."));
            return;
        }

        if (ViewModel.MyRequests.Count == 0)
        {
            MyRequestsList.Children.Add(MakeMutedText("No requests yet."));
            return;
        }

        foreach (var request in ViewModel.MyRequests)
            MyRequestsList.Children.Add(BuildRequestRow(request));
    }

    private void BuildSearchResults()
    {
        SearchResultsList.Children.Clear();
        SearchSection.Visibility = ViewModel.SearchResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in ViewModel.SearchResults)
            SearchResultsList.Children.Add(BuildMediaResultRow(item));
    }

    private void BuildDiscovery()
    {
        DiscoveryList.Children.Clear();
        DiscoverySection.Visibility = ViewModel.DiscoverySections.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var section in ViewModel.DiscoverySections)
        {
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = section.Title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("PrimaryTextBrush"),
            });

            foreach (var item in section.Results.Take(8))
                panel.Children.Add(BuildMediaResultRow(item));

            DiscoveryList.Children.Add(panel);
        }
    }

    private Border BuildRequestRow(MediaRequest request)
    {
        var card = BaseCard();
        var grid = ThreeColumnGrid();

        var info = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(TitleText($"{request.Title}{YearSuffix(request.Year)}"));
        info.Children.Add(new TextBlock
        {
            Text = $"{FormatMediaType(request.MediaType)} - {FormatStatus(request.Status)} - {FormatOutcome(request.Outcome)}{DateSuffix(request.CreatedAt)}",
            FontSize = 12,
            Foreground = Brush("SecondaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(request.LastError))
        {
            info.Children.Add(new TextBlock
            {
                Text = request.LastError,
                FontSize = 12,
                Foreground = Brush("ErrorBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (string.Equals(request.Outcome, "active", StringComparison.OrdinalIgnoreCase))
        {
            var cancel = new Button { Content = "Cancel", Tag = request };
            cancel.Click += CancelRequest_Click;
            actions.Children.Add(cancel);
        }

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        card.Child = grid;
        return card;
    }

    private Border BuildMediaResultRow(RequestMediaResult result)
    {
        var card = BaseCard();
        var grid = ThreeColumnGrid();

        var info = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(TitleText($"{result.Title}{YearSuffix(result.Year)}"));
        info.Children.Add(new TextBlock
        {
            Text = $"{FormatMediaType(result.MediaType)} - {FormatAvailability(result.Availability)} - {FormatVote(result.VoteAverage)}",
            FontSize = 12,
            Foreground = Brush("SecondaryTextBrush"),
        });
        if (!string.IsNullOrWhiteSpace(result.Overview))
        {
            info.Children.Add(new TextBlock
            {
                Text = result.Overview,
                FontSize = 12,
                Foreground = Brush("TertiaryTextBrush"),
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
            });
        }

        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var request = new Button
        {
            Content = result.Request.Requestable ? "Request" : RequestUnavailableLabel(result),
            Tag = result,
            IsEnabled = result.Request.Requestable,
            VerticalAlignment = VerticalAlignment.Center,
        };
        request.Click += SubmitRequest_Click;
        Grid.SetColumn(request, 2);
        grid.Children.Add(request);

        card.Child = grid;
        return card;
    }

    private async void SubmitRequest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RequestMediaResult result }) return;
        await ViewModel.SubmitRequestCommand.ExecuteAsync(result);
        Render();
    }

    private async void CancelRequest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MediaRequest request }) return;
        await ViewModel.CancelRequestCommand.ExecuteAsync(request);
        Render();
    }

    private static Grid ThreeColumnGrid()
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    private static Border BaseCard() => new()
    {
        Background = Brush("CardBackgroundBrush"),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16, 12, 16, 12),
    };

    private static TextBlock TitleText(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush("PrimaryTextBrush"),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static TextBlock MakeMutedText(string text) => new()
    {
        Text = text,
        Foreground = Brush("SecondaryTextBrush"),
        FontSize = 13,
    };

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static string SelectedTag(ComboBox comboBox, string fallback)
        => comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : fallback;

    private static string YearSuffix(int? year) => year.HasValue && year.Value > 0 ? $" ({year.Value})" : "";
    private static string DateSuffix(string date) => string.IsNullOrWhiteSpace(date) ? "" : $" - {FormatDate(date)}";
    private static string FormatMediaType(string value) => string.Equals(value, "series", StringComparison.OrdinalIgnoreCase) ? "Series" : "Movie";
    private static string FormatAvailability(string value) => string.Equals(value, "available", StringComparison.OrdinalIgnoreCase) ? "Already available" : "Missing";
    private static string FormatStatus(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : char.ToUpperInvariant(value[0]) + value[1..];
    private static string FormatOutcome(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : char.ToUpperInvariant(value[0]) + value[1..];
    private static string FormatVote(double? vote) => vote.HasValue && vote.Value > 0 ? $"{vote.Value:0.0}/10" : "Unrated";
    private static string RequestUnavailableLabel(RequestMediaResult result)
        => !string.IsNullOrWhiteSpace(result.Request.Reason) ? result.Request.Reason : "Unavailable";

    private static string FormatDate(string value)
        => DateTime.TryParse(value, out var dt) ? dt.ToLocalTime().ToString("MMM d, yyyy") : value;
}
