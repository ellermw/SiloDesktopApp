using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminMaintenancePage : Page
{
    public AdminMaintenanceViewModel ViewModel { get; }
    private bool _rebuildPending;

    public AdminMaintenancePage()
    {
        ViewModel = App.Services.GetRequiredService<AdminMaintenanceViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.StaleIds.CollectionChanged += (_, _) => ScheduleRebuild();
        ViewModel.SkippedRoots.CollectionChanged += (_, _) => ScheduleRebuild();
        ViewModel.UnmatchedItems.CollectionChanged += (_, _) => ScheduleRebuild();
        try { await ViewModel.LoadCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Error: {ex.Message}"; }
    }

    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        DispatcherQueue.TryEnqueue(() => { _rebuildPending = false; RebuildAll(); });
    }

    private void RebuildAll()
    {
        RebuildStaleIds();
        RebuildSkippedRoots();
        RebuildUnmatched();
    }

    private void RebuildStaleIds()
    {
        StaleIdsPanel.Children.Clear();
        if (ViewModel.StaleIds.Count == 0) { StaleIdsEmpty.Visibility = Visibility.Visible; return; }
        StaleIdsEmpty.Visibility = Visibility.Collapsed;

        bool first = true;
        foreach (var item in ViewModel.StaleIds)
        {
            if (!first) StaleIdsPanel.Children.Add(MakeSeparator());
            first = false;
            StaleIdsPanel.Children.Add(BuildStaleRow(item));
        }
    }

    private FrameworkElement BuildStaleRow(StaleMediaId item)
    {
        var row = new Grid { Padding = new Thickness(20, 12, 20, 12), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var titlePanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(new TextBlock
        {
            Text = $"{item.Title} ({item.Year})", FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(titlePanel, 0); row.Children.Add(titlePanel);

        var provider = new TextBlock
        {
            Text = $"{item.Provider}: {item.ProviderId}", FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(provider, 1); row.Children.Add(provider);

        var library = new TextBlock
        {
            Text = item.LibraryName, FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(library, 2); row.Children.Add(library);

        string lastSeenText = "\u2014";
        if (!string.IsNullOrEmpty(item.LastSeenAt) && DateTime.TryParse(item.LastSeenAt, out var dt))
            lastSeenText = dt.ToLocalTime().ToString("d");
        var lastSeen = new TextBlock
        {
            Text = lastSeenText, FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(lastSeen, 3); row.Children.Add(lastSeen);

        var capturedItem = item;
        var rematchBtn = new Button
        {
            Content = "Rematch",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            Padding = new Thickness(10, 4, 10, 4), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        rematchBtn.Click += async (_, _) =>
        {
            rematchBtn.IsEnabled = false;
            await ViewModel.RematchStaleIdCommand.ExecuteAsync(capturedItem.ContentId);
            ShowStatus("Rematch initiated.");
        };
        Grid.SetColumn(rematchBtn, 4); row.Children.Add(rematchBtn);

        return row;
    }

    private void RebuildSkippedRoots()
    {
        SkippedRootsPanel.Children.Clear();
        if (ViewModel.SkippedRoots.Count == 0) { SkippedRootsEmpty.Visibility = Visibility.Visible; return; }
        SkippedRootsEmpty.Visibility = Visibility.Collapsed;

        bool first = true;
        foreach (var root in ViewModel.SkippedRoots)
        {
            if (!first) SkippedRootsPanel.Children.Add(MakeSeparator());
            first = false;

            var row = new Grid { Padding = new Thickness(20, 12, 20, 12), ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            var path = new TextBlock
            {
                Text = root.RootPath, FontSize = 13, FontFamily = new FontFamily("Consolas, Courier New"),
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(path, 0); row.Children.Add(path);

            var lib = new TextBlock
            {
                Text = root.LibraryName, FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lib, 1); row.Children.Add(lib);

            var reason = new TextBlock
            {
                Text = root.Reason, FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(reason, 2); row.Children.Add(reason);

            SkippedRootsPanel.Children.Add(row);
        }
    }

    private void RebuildUnmatched()
    {
        UnmatchedPanel.Children.Clear();
        if (ViewModel.UnmatchedItems.Count == 0) { UnmatchedEmpty.Visibility = Visibility.Visible; return; }
        UnmatchedEmpty.Visibility = Visibility.Collapsed;

        bool first = true;
        foreach (var item in ViewModel.UnmatchedItems)
        {
            if (!first) UnmatchedPanel.Children.Add(MakeSeparator());
            first = false;

            var row = new Grid { Padding = new Thickness(20, 12, 20, 12), ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var title = new TextBlock
            {
                Text = $"{item.Title} ({item.Year})", FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(title, 0); row.Children.Add(title);

            var lib = new TextBlock
            {
                Text = item.LibraryName, FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lib, 1); row.Children.Add(lib);

            var typeBlock = new TextBlock
            {
                Text = item.ContentType, FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(typeBlock, 2); row.Children.Add(typeBlock);

            var statusBadge = MakeBadge(item.Status,
                Color.FromArgb(40, 234, 179, 8), Color.FromArgb(255, 234, 179, 8));
            Grid.SetColumn(statusBadge, 3); row.Children.Add(statusBadge);

            UnmatchedPanel.Children.Add(row);
        }
    }

    private static Border MakeSeparator() => new Border
    {
        BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
        BorderThickness = new Thickness(0, 1, 0, 0)
    };

    private static Border MakeBadge(string text, Color bg, Color fg)
    {
        return new Border
        {
            Background = new SolidColorBrush(bg), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(fg) }
        };
    }

    private void ShowStatus(string message)
    {
        StatusBannerText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) => { StatusBanner.Visibility = Visibility.Collapsed; timer.Stop(); };
        timer.Start();
    }
}
