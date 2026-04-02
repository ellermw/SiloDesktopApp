using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminProvidersPage : Page
{
    public AdminProvidersViewModel ViewModel { get; }
    private bool _rebuildPending;

    public AdminProvidersPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminProvidersViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Providers.CollectionChanged += (_, _) => ScheduleRebuild();
        try { await ViewModel.LoadCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Error: {ex.Message}"; }
    }

    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        DispatcherQueue.TryEnqueue(() => { _rebuildPending = false; RebuildRows(); });
    }

    private void RebuildRows()
    {
        ProvidersPanel.Children.Clear();
        if (ViewModel.Providers.Count == 0) { EmptyState.Visibility = Visibility.Visible; return; }
        EmptyState.Visibility = Visibility.Collapsed;

        bool first = true;
        foreach (var p in ViewModel.Providers)
        {
            if (!first) ProvidersPanel.Children.Add(new Border
            {
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(0, 1, 0, 0)
            });
            first = false;
            ProvidersPanel.Children.Add(BuildRow(p));
        }
    }

    private FrameworkElement BuildRow(MetadataProvider provider)
    {
        var row = new Grid { Padding = new Thickness(20, 14, 20, 14), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var slug = new TextBlock
        {
            Text = provider.Slug, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(slug, 0); row.Children.Add(slug);

        var type = new TextBlock
        {
            Text = provider.ProviderType, FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(type, 1); row.Children.Add(type);

        var statusBadge = provider.Enabled
            ? MakeBadge("On", Color.FromArgb(40, 34, 197, 94), Color.FromArgb(255, 34, 197, 94))
            : MakeBadge("Off", Color.FromArgb(40, 120, 120, 120), Color.FromArgb(255, 160, 160, 160));
        Grid.SetColumn(statusBadge, 2); row.Children.Add(statusBadge);

        // Settings (masked)
        string settingsStr = provider.Settings.Count > 0
            ? string.Join(", ", provider.Settings.Keys.Select(k => $"{k}: ***"))
            : "\u2014";
        var settings = new TextBlock
        {
            Text = settingsStr, FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(settings, 3); row.Children.Add(settings);

        // Actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var capturedProvider = provider;
        var editBtn = MakeIconButton("\uE70F", "Edit");
        editBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedProvider);
        actions.Children.Add(editBtn);

        var deleteBtn = MakeIconButton("\uE74D", "Delete", 28, Color.FromArgb(255, 220, 90, 90));
        deleteBtn.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Provider",
                Content = $"Delete provider \"{capturedProvider.Slug}\"?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteProviderCommand.ExecuteAsync(capturedProvider.Id);
                ShowStatus("Provider deleted.");
            }
        };
        actions.Children.Add(deleteBtn);
        Grid.SetColumn(actions, 4); row.Children.Add(actions);

        return row;
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var slugBox = new TextBox { PlaceholderText = "e.g. tmdb", CornerRadius = new CornerRadius(8), FontSize = 13 };
        var typeBox = new TextBox { PlaceholderText = "e.g. movie, tv, multi", CornerRadius = new CornerRadius(8), FontSize = 13 };
        var settingsBox = new TextBox
        {
            PlaceholderText = "key=value (one per line)",
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            AcceptsReturn = true,
            Height = 80,
            TextWrapping = TextWrapping.Wrap
        };

        var form = new StackPanel { Width = 380, Spacing = 16 };
        AddField(form, "Slug", slugBox);
        AddField(form, "Provider Type", typeBox);
        AddField(form, "Settings", settingsBox);

        var dialog = new ContentDialog
        {
            Title = "Add Metadata Provider",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = form,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(slugBox.Text)) return;

        var settings = ParseKeyValues(settingsBox.Text);
        await ViewModel.CreateProviderAsync(new CreateProviderRequest
        {
            Slug = slugBox.Text.Trim(),
            ProviderType = typeBox.Text.Trim(),
            Settings = settings.Count > 0 ? settings : null
        });
        ShowStatus("Provider created.");
    }

    private async Task OpenEditDialogAsync(MetadataProvider provider)
    {
        var slugBox = new TextBox { Text = provider.Slug, CornerRadius = new CornerRadius(8), FontSize = 13, IsReadOnly = true };
        var typeBox = new TextBox { Text = provider.ProviderType, CornerRadius = new CornerRadius(8), FontSize = 13 };
        var enabledToggle = new ToggleSwitch { IsOn = provider.Enabled };
        var settingsBox = new TextBox
        {
            Text = string.Join("\n", provider.Settings.Select(kv => $"{kv.Key}={kv.Value}")),
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            AcceptsReturn = true,
            Height = 80,
            TextWrapping = TextWrapping.Wrap
        };

        var form = new StackPanel { Width = 380, Spacing = 16 };
        AddField(form, "Slug", slugBox);
        AddField(form, "Provider Type", typeBox);
        AddField(form, "Enabled", enabledToggle);
        AddField(form, "Settings", settingsBox);

        var dialog = new ContentDialog
        {
            Title = "Edit Provider",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = form,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var settings = ParseKeyValues(settingsBox.Text);
        await ViewModel.UpdateProviderAsync(provider.Id, new
        {
            provider_type = typeBox.Text.Trim(),
            enabled = enabledToggle.IsOn,
            settings
        });
        ShowStatus("Provider updated.");
    }

    private static Dictionary<string, string> ParseKeyValues(string text)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf('=');
            if (idx > 0) result[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        return result;
    }

    private static void AddField(StackPanel form, string label, FrameworkElement control)
    {
        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(new TextBlock
        {
            Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        group.Children.Add(control);
        form.Children.Add(group);
    }

    private static Border MakeBadge(string text, Color bg, Color fg)
    {
        return new Border
        {
            Background = new SolidColorBrush(bg), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(fg) }
        };
    }

    private static Button MakeIconButton(string glyph, string tooltip, int size = 28, Color? fgColor = null)
    {
        var fg = fgColor.HasValue ? new SolidColorBrush(fgColor.Value)
            : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        var btn = new Button
        {
            Width = size, Height = size, Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon { Glyph = glyph, FontSize = 12, Foreground = fg }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
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
