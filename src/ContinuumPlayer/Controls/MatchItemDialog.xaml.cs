using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.Controls;

public sealed partial class MatchItemDialog : ContentDialog
{
    private readonly AdminApi _adminApi;
    private readonly string _itemId;
    private MatchCandidate? _selectedCandidate;

    public MatchCandidate? SelectedCandidate => _selectedCandidate;

    public MatchItemDialog(string itemId)
    {
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        _itemId = itemId;
        this.InitializeComponent();
    }

    private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            _ = DoSearchAsync();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await DoSearchAsync();
    }

    private async Task DoSearchAsync()
    {
        string query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        ResultsPanel.Children.Clear();
        EmptyText.Visibility = Visibility.Collapsed;
        _selectedCandidate = null;
        IsPrimaryButtonEnabled = false;

        try
        {
            var request = new ItemMatchSearchRequest { Title = query };

            // Check if it looks like an ID
            if (query.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                request = new ItemMatchSearchRequest { ImdbId = query };
            else if (int.TryParse(query, out _))
                request = new ItemMatchSearchRequest { TmdbId = query };

            var response = await _adminApi.MatchSearchAsync(_itemId, request);

            if (response.Candidates.Count == 0)
            {
                EmptyText.Text = "No matches found. Try a different search.";
                EmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                foreach (var candidate in response.Candidates)
                {
                    ResultsPanel.Children.Add(BuildCandidateRow(candidate));
                }
            }
        }
        catch (Exception ex)
        {
            EmptyText.Text = $"Error: {ex.Message}";
            EmptyText.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private FrameworkElement BuildCandidateRow(MatchCandidate candidate)
    {
        var rowBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(12)
        };

        var root = new Grid { ColumnSpacing = 12 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Poster thumbnail
        if (!string.IsNullOrEmpty(candidate.ImageUrl))
        {
            var poster = new Border
            {
                Width = 50, Height = 75,
                CornerRadius = new CornerRadius(6),
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"]
            };
            var img = new Image
            {
                Source = new BitmapImage(new Uri(candidate.ImageUrl)),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };
            poster.Child = img;
            Grid.SetColumn(poster, 0);
            root.Children.Add(poster);
        }

        // Info
        var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = $"{candidate.Title} ({candidate.Year})",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        // Provider IDs
        string ids = string.Join(" | ", candidate.ProviderIds.Select(kv => $"{kv.Key}: {kv.Value}"));
        if (!string.IsNullOrEmpty(ids))
        {
            info.Children.Add(new TextBlock
            {
                Text = ids,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            });
        }

        // Sources
        if (candidate.Sources.Count > 0)
        {
            info.Children.Add(new TextBlock
            {
                Text = "Sources: " + string.Join(", ", candidate.Sources),
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
        }

        Grid.SetColumn(info, 1);
        root.Children.Add(info);

        rowBtn.Content = root;

        var capturedCandidate = candidate;
        rowBtn.Click += (_, _) =>
        {
            // Deselect all, select this one
            foreach (var child in ResultsPanel.Children)
            {
                if (child is Button btn)
                {
                    btn.Background = new SolidColorBrush(Colors.Transparent);
                    btn.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
                }
            }
            rowBtn.Background = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"];
            rowBtn.BorderBrush = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            _selectedCandidate = capturedCandidate;
            IsPrimaryButtonEnabled = true;
        };

        return rowBtn;
    }
}
