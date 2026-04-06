using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.HistoryImport;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminHistoryImportPage : Page
{
    private readonly HistoryImportApi _api;

    public AdminHistoryImportPage()
    {
        _api = App.Services.GetRequiredService<HistoryImportApi>();
        this.InitializeComponent();
    }

    public AdminHistoryImportViewModel ViewModel { get; } = new();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(_api);
        EmptyText.Visibility = ViewModel.Runs.Count == 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void NewImport_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Show import wizard dialog
    }
}

public partial class AdminHistoryImportViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<string> Runs { get; } = [];

    public async Task LoadAsync(HistoryImportApi api)
    {
        IsLoading = true;
        try
        {
            var response = await api.GetImportRunsAsync();
            Runs.Clear();
            foreach (var run in response.Runs)
            {
                var status = run.Status ?? "unknown";
                var source = run.SourceType ?? "unknown";
                var created = run.CreatedAt ?? "";
                var stats = $"Fetched: {run.Fetched}, Matched: {run.Matched}, Skipped: {run.Skipped}";
                Runs.Add($"{source} — {status} — {created}\n{stats}");
            }
        }
        catch { }
        finally { IsLoading = false; }
    }
}
