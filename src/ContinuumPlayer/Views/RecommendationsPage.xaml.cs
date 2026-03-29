using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Controls;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class RecommendationsPage : Page
{
    public RecommendationsViewModel ViewModel { get; }

    public RecommendationsPage()
    {
        ViewModel = App.Services.GetRequiredService<RecommendationsViewModel>();
        this.InitializeComponent();

        ViewModel.Rows.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(BuildRows);
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
        BuildRows();
    }

    private void BuildRows()
    {
        RowsPanel.Children.Clear();

        if (ViewModel.Rows.Count == 0 && !ViewModel.IsLoading)
        {
            EmptyState.Visibility = Visibility.Visible;
            CountPanel.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        // Count total items across all rows
        int totalItems = 0;
        foreach (var row in ViewModel.Rows)
            totalItems += row.Items.Count;

        if (totalItems > 0)
        {
            CountPanel.Visibility = Visibility.Visible;
            RowCountText.Text = totalItems.ToString();
            RowCountLabel.Text = totalItems == 1 ? "suggestion" : "suggestions";
        }
        else
        {
            CountPanel.Visibility = Visibility.Collapsed;
        }

        foreach (var row in ViewModel.Rows)
        {
            if (row.Items.Count == 0) continue;

            var section = new HomeSectionWithItems
            {
                Title = row.Label,
                Items = new List<MediaItem>(row.Items)
            };

            var sectionRow = new SectionRow
            {
                Section = section
            };
            RowsPanel.Children.Add(sectionRow);
        }
    }
}
