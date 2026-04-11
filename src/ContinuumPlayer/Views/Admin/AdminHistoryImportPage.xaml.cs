using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.HistoryImport;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminHistoryImportPage : Page
{
    private readonly AdminApi _adminApi;
    private readonly HistoryImportApi _importApi;

    public AdminHistoryImportPage()
    {
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        _importApi = App.Services.GetRequiredService<HistoryImportApi>();
        this.InitializeComponent();
    }

    public AdminHistoryImportViewModel ViewModel { get; } = new();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(_adminApi, _importApi);
        EmptyText.Visibility = ViewModel.Sources.Count == 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private async void NewSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Add Import Source",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 350 };

        var nameBox = new Microsoft.UI.Xaml.Controls.TextBox { PlaceholderText = "Source name", Header = "Name" };
        var urlBox = new Microsoft.UI.Xaml.Controls.TextBox { PlaceholderText = "https://plex.example.com", Header = "Server URL" };
        var typeBox = new Microsoft.UI.Xaml.Controls.ComboBox { Header = "Source Type" };
        typeBox.Items.Add("plex");
        typeBox.Items.Add("emby");
        typeBox.Items.Add("jellyfin");
        typeBox.SelectedIndex = 0;

        panel.Children.Add(nameBox);
        panel.Children.Add(typeBox);
        panel.Children.Add(urlBox);
        dialog.Content = panel;

        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            try
            {
                await _adminApi.CreateHistoryImportSourceAsync(new CreateHistoryImportSourceRequest
                {
                    Name = nameBox.Text,
                    SourceType = typeBox.SelectedItem?.ToString() ?? "plex",
                    BaseUrl = urlBox.Text,
                    Enabled = true
                });
                await ViewModel.LoadAsync(_adminApi, _importApi);
            }
            catch (Exception ex)
            {
                var errDialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = "Error",
                    Content = ex.Message,
                    CloseButtonText = "OK"
                };
                await errDialog.ShowAsync();
            }
        }
    }

    private async void DeleteSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int id)
        {
            try
            {
                await _adminApi.DeleteHistoryImportSourceAsync(id);
                await ViewModel.LoadAsync(_adminApi, _importApi);
            }
            catch { }
        }
    }
}

public partial class AdminHistoryImportViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isLoading;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<HistoryImportSourceDisplay> Sources { get; } = [];
    public ObservableCollection<HistoryImportRunDisplay> Runs { get; } = [];

    public async Task LoadAsync(AdminApi adminApi, HistoryImportApi importApi)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var sources = await adminApi.GetHistoryImportSourcesAsync();
            Sources.Clear();
            foreach (var s in sources)
            {
                Sources.Add(new HistoryImportSourceDisplay
                {
                    Id = s.Id,
                    Name = s.Name,
                    SourceType = s.SourceType,
                    BaseUrl = s.BaseUrl ?? "",
                    Enabled = s.Enabled,
                    Display = $"{s.Name} ({s.SourceType})",
                    SubDisplay = s.BaseUrl ?? "No URL configured"
                });
            }

            var runs = await importApi.GetImportRunsAsync();
            Runs.Clear();
            foreach (var run in runs)
            {
                Runs.Add(new HistoryImportRunDisplay
                {
                    Display = $"{run.SourceType} — {run.Status}",
                    SubDisplay = $"Fetched: {run.Fetched}, Matched: {run.Matched}, Skipped: {run.Skipped} — {run.CreatedAt}"
                });
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsLoading = false; }
    }
}

public class HistoryImportSourceDisplay
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public bool Enabled { get; set; }
    public string Display { get; set; } = "";
    public string SubDisplay { get; set; } = "";
}

public class HistoryImportRunDisplay
{
    public string Display { get; set; } = "";
    public string SubDisplay { get; set; } = "";
}
