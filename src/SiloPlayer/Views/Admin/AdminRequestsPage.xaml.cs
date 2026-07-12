using SiloPlayer.Core.Models.Requests;
using SiloPlayer.ViewModels.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminRequestsPage : Page
{
    public AdminRequestsViewModel ViewModel { get; }
    private bool _ready;

    public AdminRequestsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminRequestsViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _ready = true;
        await LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        ViewModel.StatusFilter = SelectedTag(StatusFilterComboBox, "all");
        ViewModel.OutcomeFilter = SelectedTag(OutcomeFilterComboBox, "all");
        await ViewModel.LoadCommand.ExecuteAsync(null);
        Render();
    }

    private void Render()
    {
        LoadingRing.IsActive = ViewModel.IsLoading;
        LoadingRing.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = ViewModel.StatusMessage;
        ErrorText.Text = ViewModel.ErrorMessage ?? "";
        ErrorText.Visibility = string.IsNullOrWhiteSpace(ViewModel.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

        RequestsListView.Items.Clear();
        if (ViewModel.Requests.Count == 0)
        {
            RequestsListView.Items.Add(new TextBlock
            {
                Text = "No media requests match these filters.",
                Margin = new Thickness(20),
                Foreground = Brush("SecondaryTextBrush"),
            });
            return;
        }

        foreach (var request in ViewModel.Requests)
            RequestsListView.Items.Add(BuildRequestRow(request));
    }

    private FrameworkElement BuildRequestRow(MediaRequest request)
    {
        var root = new Grid
        {
            ColumnSpacing = 16,
            Padding = new Thickness(16, 12, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = $"{request.Title}{YearSuffix(request.Year)}",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var meta = $"{FormatMediaType(request.MediaType)} - {FormatStatus(request.Status)} - {FormatStatus(request.Outcome)} - {FormatDate(request.CreatedAt)}";
        if (!string.IsNullOrWhiteSpace(request.IntegrationKind))
            meta += $" - {request.IntegrationKind}";
        stack.Children.Add(new TextBlock
        {
            Text = meta,
            FontSize = 12,
            Foreground = Brush("SecondaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (!string.IsNullOrWhiteSpace(request.LastError))
        {
            stack.Children.Add(new TextBlock
            {
                Text = request.LastError,
                FontSize = 12,
                Foreground = Brush("ErrorBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Grid.SetColumn(stack, 0);
        root.Children.Add(stack);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (string.Equals(request.Outcome, "active", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            var approve = new Button { Content = "Approve", Tag = request };
            approve.Click += Approve_Click;
            actions.Children.Add(approve);

            var decline = new Button { Content = "Decline", Tag = request };
            decline.Click += Decline_Click;
            actions.Children.Add(decline);
        }

        if (string.Equals(request.Outcome, "failed", StringComparison.OrdinalIgnoreCase))
        {
            var retry = new Button { Content = "Retry", Tag = request };
            retry.Click += Retry_Click;
            actions.Children.Add(retry);
        }

        Grid.SetColumn(actions, 1);
        root.Children.Add(actions);
        return root;
    }

    private async void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MediaRequest request }) return;
        await ViewModel.ApproveCommand.ExecuteAsync(request);
        Render();
    }

    private async void Decline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MediaRequest request }) return;
        await ViewModel.DeclineCommand.ExecuteAsync(request);
        Render();
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MediaRequest request }) return;
        await ViewModel.RetryCommand.ExecuteAsync(request);
        Render();
    }

    private static string SelectedTag(ComboBox comboBox, string fallback)
        => comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : fallback;

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private static string YearSuffix(int? year) => year.HasValue && year.Value > 0 ? $" ({year.Value})" : "";
    private static string FormatMediaType(string value) => string.Equals(value, "series", StringComparison.OrdinalIgnoreCase) ? "Series" : "Movie";
    private static string FormatStatus(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : char.ToUpperInvariant(value[0]) + value[1..];
    private static string FormatDate(string value) => DateTime.TryParse(value, out var dt) ? dt.ToLocalTime().ToString("MMM d, yyyy") : value;
}
