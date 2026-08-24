using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

/// <summary>
/// Current WebUI add-to-collection picker. It includes the user's manual
/// collections and, while acting as an administrator, manual collections from
/// every accessible library grouped beneath the library name.
/// </summary>
public sealed partial class AddToCollectionDialog : ContentDialog
{
    private readonly string _mediaItemId;
    private readonly CollectionsApi _collectionsApi;
    private readonly AdminApi _adminApi;
    private readonly CatalogApi _catalogApi;
    private readonly bool _isActingAdmin;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CollectionPick? _selected;
    private Button? _selectedButton;

    public event Action? ItemAdded;

    public AddToCollectionDialog(string mediaItemId, string? itemTitle)
    {
        _mediaItemId = mediaItemId;
        _collectionsApi = App.Services.GetRequiredService<CollectionsApi>();
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        _catalogApi = App.Services.GetRequiredService<CatalogApi>();
        _isActingAdmin = AuthorizationPolicy.IsActingAdmin(
            App.Services.GetRequiredService<AuthService>());

        InitializeComponent();
        DescriptionText.Text = string.IsNullOrWhiteSpace(itemTitle)
            ? "Pick a manual collection."
            : $"Pick a manual collection to add \u201c{itemTitle}\u201d to.";
        IsPrimaryButtonEnabled = false;
        Opened += OnOpened;
        Closed += (_, _) => _lifetimeCts.Cancel();
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        try
        {
            var picks = new List<CollectionPick>();
            var userTask = _collectionsApi.GetCollectionsAsync(_lifetimeCts.Token);
            var librariesTask = _isActingAdmin
                ? _catalogApi.GetLibrariesAsync(_lifetimeCts.Token)
                : Task.FromResult(new List<SiloPlayer.Core.Models.Catalog.Library>());
            var userCollections = await userTask;
            picks.AddRange(userCollections.Collections
                .Where(collection => collection.CollectionType.Equals("manual", StringComparison.OrdinalIgnoreCase))
                .Select(collection => new CollectionPick(collection.Id, collection.Name, "My Collections", false)));

            if (_isActingAdmin)
            {
                List<SiloPlayer.Core.Models.Catalog.Library> libraries;
                try
                {
                    libraries = await librariesTask;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    libraries = [];
                    ShowPartialLoadWarning("Some library collections could not be loaded.");
                }
                var requests = libraries.Select(async library =>
                {
                    try
                    {
                        var response = await _adminApi.GetCollectionsAsync(library.Id, _lifetimeCts.Token);
                        return (Library: library, Response: response, Failed: false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        return (Library: library, Response: (SiloPlayer.Core.Models.Admin.AdminCollectionsResponse?)null, Failed: true);
                    }
                }).ToList();
                var results = await Task.WhenAll(requests);
                foreach (var (library, response, failed) in results)
                {
                    if (failed || response == null)
                    {
                        ShowPartialLoadWarning("Some library collections could not be loaded.");
                        continue;
                    }
                    picks.AddRange(response.Collections
                        .Where(collection => collection.CollectionType.Equals("manual", StringComparison.OrdinalIgnoreCase))
                        .Select(collection => new CollectionPick(collection.Id, collection.Title, library.Name, true)));
                }
            }

            RenderPicks(picks);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LoadingRing.IsActive = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = ex.Message;
            StatusText.Visibility = Visibility.Visible;
            EmptyText.Text = "Collections could not be loaded.";
            EmptyText.Visibility = Visibility.Visible;
        }
    }

    private void ShowPartialLoadWarning(string message)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFD, 0xE0, 0x68));
        StatusText.Visibility = Visibility.Visible;
    }

    private void RenderPicks(IReadOnlyList<CollectionPick> picks)
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        GroupsPanel.Children.Clear();

        var groups = picks
            .GroupBy(pick => pick.Group)
            .Select(group => new { Name = group.Key, Picks = group.OrderBy(pick => pick.Title, StringComparer.CurrentCultureIgnoreCase).ToList() })
            .ToList();
        if (groups.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        foreach (var group in groups)
        {
            GroupsPanel.Children.Add(new Border
            {
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock
                {
                    Text = group.Name.ToUpperInvariant(),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    CharacterSpacing = 120,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                },
            });

            foreach (var pick in group.Picks)
            {
                var title = new TextBlock
                {
                    Text = pick.Title,
                    FontSize = 13,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var icon = new FontIcon { Glyph = "\uE8F7", FontSize = 16, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
                Grid.SetColumn(icon, 0);
                Grid.SetColumn(title, 1);
                row.Children.Add(icon);
                row.Children.Add(title);
                if (pick.IsLibrary)
                {
                    var badge = new TextBlock
                    {
                        Text = "LIBRARY",
                        FontSize = 10,
                        CharacterSpacing = 100,
                        Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    Grid.SetColumn(badge, 2);
                    row.Children.Add(badge);
                }

                var button = new Button
                {
                    Content = row,
                    Tag = pick,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(12, 10, 12, 10),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(0),
                };
                button.Click += (_, _) =>
                {
                    if (_selectedButton != null)
                        _selectedButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    _selected = pick;
                    _selectedButton = button;
                    button.Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"];
                    IsPrimaryButtonEnabled = true;
                };
                AutomationProperties.SetName(button, $"Select {pick.Title}");
                GroupsPanel.Children.Add(button);
            }
        }

        CollectionsScroller.Visibility = Visibility.Visible;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_selected == null)
        {
            args.Cancel = true;
            return;
        }

        var deferral = args.GetDeferral();
        args.Cancel = true;
        IsPrimaryButtonEnabled = false;
        StatusText.Text = "Adding to collection…";
        StatusText.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        StatusText.Visibility = Visibility.Visible;
        try
        {
            if (_selected.IsLibrary)
                await _adminApi.AddCollectionItemAsync(_selected.Id, _mediaItemId, _lifetimeCts.Token);
            else
                await _collectionsApi.AddCollectionItemAsync(_selected.Id, _mediaItemId, _lifetimeCts.Token);

            ItemAdded?.Invoke();
            args.Cancel = false;
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            StatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFC, 0xA5, 0xA5));
            IsPrimaryButtonEnabled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private sealed record CollectionPick(string Id, string Title, string Group, bool IsLibrary);
}
