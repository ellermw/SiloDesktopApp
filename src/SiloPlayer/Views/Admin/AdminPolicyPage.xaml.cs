using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPolicyPage : Page
{
    public AdminPolicyViewModel ViewModel { get; } = App.Services.GetRequiredService<AdminPolicyViewModel>();
    private bool _loaded;
    private bool _syncing;

    public AdminPolicyPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        DataContext = ViewModel;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        _loaded = true;
        UnavailablePanel.Visibility = ViewModel.IsAvailable ? Visibility.Collapsed : Visibility.Visible;
        PipelineStrip.Visibility = ViewModel.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        PolicyTabs.Visibility = ViewModel.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        RebuildDomainCards();
        UpdateDecisionPager();
        RefreshMessages();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.ErrorMessage) or nameof(ViewModel.StatusMessage))
            DispatcherQueue.TryEnqueue(RefreshMessages);
        if (e.PropertyName is nameof(ViewModel.DecisionNextCursor))
            DispatcherQueue.TryEnqueue(UpdateDecisionPager);
    }

    private void RefreshMessages()
    {
        ErrorText.Text = ViewModel.ErrorMessage ?? "";
        ErrorPanel.Visibility = string.IsNullOrWhiteSpace(ViewModel.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = ViewModel.StatusMessage ?? "";
        StatusPanel.Visibility = string.IsNullOrWhiteSpace(ViewModel.StatusMessage) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RebuildDomainCards()
    {
        DocumentDomainCards.Children.Clear();
        foreach (var domain in ViewModel.Capability?.DecisionTypes ?? [])
            DocumentDomainCards.Children.Add(BuildDomainCard(domain));
    }

    private FrameworkElement BuildDomainCard(string domain)
    {
        var (title, description, example, glyph) = DomainPresentation(domain);
        var panel = new StackPanel { Spacing = 0 };
        var header = new Grid { ColumnSpacing = 14, Padding = new Thickness(20, 18, 20, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(11),
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            Child = new FontIcon { Glyph = glyph, FontSize = 18 },
        };
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        var add = new Button
        {
            Content = "+  New override", Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        add.Click += async (_, _) => await CreateDocumentAsync(domain, title, example);
        Grid.SetColumn(icon, 0); Grid.SetColumn(text, 1); Grid.SetColumn(add, 2);
        header.Children.Add(icon); header.Children.Add(text); header.Children.Add(add);
        panel.Children.Add(header);

        var documents = ViewModel.Documents.Where(d => d.Domain == domain).ToList();
        if (documents.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"The Silo baseline applies unchanged. Write an override like {example}",
                Margin = new Thickness(20, 0, 20, 18), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }
        else
        {
            foreach (var document in documents)
            {
                panel.Children.Add(new Border { Height = 1, Background = (SolidColorBrush)Application.Current.Resources["BorderBrush"] });
                panel.Children.Add(BuildDocumentRow(document));
            }
        }

        return new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(16), Child = panel,
        };
    }

    private FrameworkElement BuildDocumentRow(PolicyDocument document)
    {
        var row = new Grid { Padding = new Thickness(20, 13, 20, 13), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new Button
        {
            Content = document.Name, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold,
        };
        name.Click += async (_, _) => await OpenDocumentAsync(document);
        var status = MakeStatusBadge(DocumentStatus(document));
        var updated = new TextBlock
        {
            Text = $"Updated {document.UpdatedAt.ToLocalTime():g}", FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        var toggle = new ToggleSwitch { IsOn = document.Enabled, VerticalAlignment = VerticalAlignment.Center };
        toggle.Toggled += async (_, _) =>
        {
            if (_syncing) return;
            toggle.IsEnabled = false;
            await ViewModel.ToggleDocumentEnabledAsync(document, toggle.IsOn);
            toggle.IsEnabled = true;
            RebuildDomainCards();
        };
        Grid.SetColumn(name, 0); Grid.SetColumn(status, 1); Grid.SetColumn(updated, 2); Grid.SetColumn(toggle, 3);
        row.Children.Add(name); row.Children.Add(status); row.Children.Add(updated); row.Children.Add(toggle);
        return row;
    }

    private async Task OpenDocumentAsync(PolicyDocument document)
    {
        await ViewModel.SelectDocumentAsync(document);
        _syncing = true;
        DocumentEnabledToggle.IsOn = document.Enabled;
        _syncing = false;
        EditorDocumentName.Text = document.Name;
        EditorDomainText.Text = DomainPresentation(document.Domain).Title + " · " + document.Domain;
        var status = DocumentStatus(document);
        EditorStatusText.Text = status;
        ApplyStatusColors(EditorStatusBadge, EditorStatusText, status);
        var live = ViewModel.Versions.FirstOrDefault(v => v.Id == document.ActiveVersionId);
        LiveVersionText.Text = live is null ? "Live" : $"Live · v{live.VersionNumber}";
        RebuildVersionRows();
        OverridesOverview.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
    }

    private void RebuildVersionRows()
    {
        VersionRows.Children.Clear();
        if (ViewModel.Versions.Count == 0)
        {
            VersionRows.Children.Add(new TextBlock { Text = "No versions have been saved for this document.", FontSize = 12, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] });
            return;
        }
        foreach (var version in ViewModel.Versions)
        {
            var row = new Grid { Padding = new Thickness(12, 10, 12, 10), ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var active = version.Id == ViewModel.SelectedDocument?.ActiveVersionId;
            row.Children.Add(new TextBlock { Text = active ? $"v{version.VersionNumber}  LIVE" : $"v{version.VersionNumber}", FontSize = 12, FontWeight = FontWeights.SemiBold });
            var author = new TextBlock { Text = version.CreatedByUserId is int id ? $"User {id}" : "—", FontSize = 12 }; Grid.SetColumn(author, 1); row.Children.Add(author);
            var date = new TextBlock { Text = version.CreatedAt.ToLocalTime().ToString("g"), FontSize = 12 }; Grid.SetColumn(date, 2); row.Children.Add(date);
            var comment = new TextBlock { Text = string.IsNullOrWhiteSpace(version.Comment) ? "—" : version.Comment, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(comment, 3); row.Children.Add(comment);
            var activate = new Button { Content = version.CompiledOk ? "Make live" : "Compile failed", IsEnabled = version.CompiledOk && !active, Tag = version, Style = (Style)Application.Current.Resources["OutlineButtonStyle"] };
            activate.Click += ActivateVersion_Click; Grid.SetColumn(activate, 4); row.Children.Add(activate);
            VersionRows.Children.Add(row);
            VersionRows.Children.Add(new Border { Height = 1, Background = (SolidColorBrush)Application.Current.Resources["BorderBrush"] });
        }
    }

    private async Task CreateDocumentAsync(string domain, string title, string example)
    {
        var name = new TextBox { Header = "Name", PlaceholderText = example, Width = 420 };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = $"New {title} override", Content = name, PrimaryButtonText = "Create", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(name.Text)) return;
        await ViewModel.CreateDocumentAsync(domain, name.Text.Trim());
        if (ViewModel.SelectedDocument is { } document) await OpenDocumentAsync(document);
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var domain = ViewModel.Capability?.DecisionTypes.FirstOrDefault();
        if (domain is null) return;
        var presentation = DomainPresentation(domain);
        await CreateDocumentAsync(domain, presentation.Title, presentation.Example);
    }

    private void BackToOverrides_Click(object sender, RoutedEventArgs e)
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        OverridesOverview.Visibility = Visibility.Visible;
        RebuildDomainCards();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveVersionAsync();
        RebuildVersionRows();
        var latest = ViewModel.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        if (latest != null) LiveVersionText.Text = $"Live · v{latest.VersionNumber}";
    }

    private async void Simulate_Click(object sender, RoutedEventArgs e) => await ViewModel.SimulateAsync();
    private async void Filter_Click(object sender, RoutedEventArgs e) { await ViewModel.FilterDecisionsAsync(); UpdateDecisionPager(); }
    private async void ResetFilters_Click(object sender, RoutedEventArgs e) { await ViewModel.ResetDecisionFiltersAsync(); UpdateDecisionPager(); }
    private async void NextDecisions_Click(object sender, RoutedEventArgs e) { await ViewModel.NextDecisionPageAsync(); UpdateDecisionPager(); }
    private async void PreviousDecisions_Click(object sender, RoutedEventArgs e) { await ViewModel.PreviousDecisionPageAsync(); UpdateDecisionPager(); }
    private void UpdateDecisionPager()
    {
        DecisionPreviousButton.IsEnabled = ViewModel.HasPreviousDecisionPage && !ViewModel.IsBusy;
        DecisionNextButton.IsEnabled = !string.IsNullOrWhiteSpace(ViewModel.DecisionNextCursor) && !ViewModel.IsBusy;
    }

    private async void Enabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loaded && !_syncing && ViewModel.SelectedDocument is not null)
            await ViewModel.ToggleEnabledAsync(DocumentEnabledToggle.IsOn);
    }

    private async void ActivateVersion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PolicyVersionSummary version) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = $"Make v{version.VersionNumber} the live policy?",
            Content = "New requests start using it immediately on every server node. The version it replaces stays in history.",
            PrimaryButtonText = "Activate", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.ActivateAsync(version);
        LiveVersionText.Text = $"Live · v{version.VersionNumber}";
        RebuildVersionRows();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDocument is null) return;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Delete override?", Content = "This removes the document and its version history. The Silo baseline will apply unchanged.", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.DeleteAsync();
        BackToOverrides_Click(sender, e);
    }

    private async void Decision_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PolicyDecisionEntry row) return;
        var detail = await ViewModel.GetDecisionAsync(row.Id);
        var content = new Grid { ColumnSpacing = 12, Width = 760 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var input = MakeJsonPanel("Input", detail.InputDigest, detail.InputSample);
        var result = MakeJsonPanel("Result", detail.Error, detail.ResultSample); Grid.SetColumn(result, 1);
        content.Children.Add(input); content.Children.Add(result);
        await new ContentDialog { XamlRoot = XamlRoot, Title = $"Decision {detail.Id}", Content = content, CloseButtonText = "Close" }.ShowAsync();
    }

    private static FrameworkElement MakeJsonPanel(string title, string? subtitle, object? value)
    {
        var panel = new StackPanel { Spacing = 7 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = subtitle ?? "—", FontSize = 11, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] });
        panel.Children.Add(new TextBox { Text = value is null ? "No verbose sample was logged." : JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), MinHeight = 260, TextWrapping = TextWrapping.NoWrap });
        return panel;
    }

    private static Border MakeStatusBadge(string status)
    {
        var text = new TextBlock { Text = status, FontSize = 11, FontWeight = FontWeights.SemiBold };
        var badge = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center, Child = text };
        ApplyStatusColors(badge, text, status);
        return badge;
    }

    private static string DocumentStatus(PolicyDocument document) => !document.Enabled ? "Disabled" : document.ActiveVersionId is null ? "Draft" : "Live";
    private static void ApplyStatusColors(Border badge, TextBlock text, string status)
    {
        var color = status == "Live" ? Color.FromArgb(255, 52, 211, 153) : status == "Draft" ? Color.FromArgb(255, 251, 191, 36) : Color.FromArgb(255, 148, 163, 184);
        badge.Background = new SolidColorBrush(Color.FromArgb(25, color.R, color.G, color.B));
        text.Foreground = new SolidColorBrush(color);
    }

    private static (string Title, string Description, string Example, string Glyph) DomainPresentation(string domain)
    {
        var key = domain.ToLowerInvariant();
        if (key.Contains("playback")) return ("Playback", "Rules governing whether playback may start and which household restrictions apply.", "Kids profile evening limit", "\uE768");
        if (key.Contains("download")) return ("Downloads", "Rules governing offline downloads and download transcoding.", "Block downloads for guests", "\uE896");
        if (key.Contains("request")) return ("Requests", "Rules governing media requests, approval, and household access.", "Require approval for restricted ratings", "\uE8A5");
        if (key.Contains("admin") || key.Contains("action")) return ("Administrative actions", "Rules governing privileged server operations and management actions.", "Restrict destructive actions", "\uE72E");
        var title = string.Join(" ", domain.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return (title, $"Custom narrowing rules for {title.ToLowerInvariant()} decisions.", $"Narrow {title.ToLowerInvariant()} access", "\uE8D7");
    }
}
