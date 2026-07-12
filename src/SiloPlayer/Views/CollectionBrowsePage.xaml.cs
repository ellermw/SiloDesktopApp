using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

/// <summary>
/// Browse view for a collection (user-defined or library-discovered).
/// B40 + B41: before this, clicking a collection card opened the editor or
/// mutated LibraryPage in place. This page mirrors the webui
/// <c>/catalog?source=user_collection&amp;collection_id=X</c> route — a
/// standalone grid of items with a back button.
/// </summary>
public sealed partial class CollectionBrowsePage : Page
{
    private readonly CatalogApi _catalogApi;

    /// <summary>Parameter passed via NavigationService.Navigate.</summary>
    public sealed class NavArgs
    {
        public string CollectionId { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        /// <summary>true = user_collection, false = library_collection.</summary>
        public bool IsUserCollection { get; set; }
        /// <summary>Library ID for library_collection pins; null for user_collection.</summary>
        public int? LibraryId { get; set; }
    }

    private NavArgs? _currentArgs;

    public CollectionBrowsePage()
    {
        _catalogApi = App.Services.GetRequiredService<CatalogApi>();
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not NavArgs args) return;

        _currentArgs = args;
        TitleText.Text = args.Title;
        SubtitleText.Text = args.Subtitle ?? (args.IsUserCollection ? "User collection" : "Library collection");

        // Pin button only applies to library collections (user collections
        // aren't scoped to a library's sidebar section).
        if (!args.IsUserCollection && args.LibraryId.HasValue)
        {
            PinButton.Visibility = Visibility.Visible;
            _ = SyncPinStateAsync();
        }
        else
        {
            PinButton.Visibility = Visibility.Collapsed;
        }

        ShowLoading();
        try
        {
            var resp = args.IsUserCollection
                ? await _catalogApi.BrowseUserCollectionAsync(args.CollectionId)
                : await _catalogApi.BrowseLibraryCollectionAsync(args.CollectionId);

            var items = resp.Items ?? new List<MediaItem>();
            if (items.Count == 0)
            {
                ShowEmpty();
                return;
            }

            PosterRepeater.ItemsSource = items;
            ItemCountText.Text = items.Count.ToString();
            ItemCountLabel.Text = items.Count == 1 ? "item" : "items";
            CountPanel.Visibility = Visibility.Visible;
            ShowContent();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.Frame?.CanGoBack == true)
        {
            nav.Frame.GoBack();
        }
        else
        {
            nav.Navigate<CollectionsPage>();
        }
    }

    private void ShowLoading()
    {
        LoadingRing.Visibility = Visibility.Visible;
        ContentScroll.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowContent()
    {
        LoadingRing.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowEmpty()
    {
        LoadingRing.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        LoadingRing.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorText.Text = $"Could not load collection: {message}";
        ErrorText.Visibility = Visibility.Visible;
    }

    // ===== Sidebar pins (webui parity: sidebar_pins JSON user setting) =====
    //
    // Shape: { "<libraryId>": [ { "type": "collection", "id": "<collectionId>", "label": "..." }, ... ] }
    // Stored as a JSON string under the single settings key "sidebar_pins".

    private const string SidebarPinsKey = "sidebar_pins";
    private bool _isPinned;

    private async Task SyncPinStateAsync()
    {
        if (_currentArgs?.LibraryId == null) return;
        var libraryKey = _currentArgs.LibraryId.Value.ToString();
        try
        {
            var settingsApi = App.Services.GetRequiredService<SettingsApi>();
            var entry = await settingsApi.GetSettingAsync(SidebarPinsKey);
            _isPinned = IsPinnedInMap(entry.Value, libraryKey, _currentArgs.CollectionId);
        }
        catch { _isPinned = false; }
        UpdatePinButtonVisual();
    }

    private async void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArgs?.LibraryId == null) return;
        var libraryKey = _currentArgs.LibraryId.Value.ToString();

        try
        {
            PinButton.IsEnabled = false;
            var settingsApi = App.Services.GetRequiredService<SettingsApi>();
            var entry = await settingsApi.GetSettingAsync(SidebarPinsKey);
            var map = ParsePinsMap(entry.Value);

            var list = map.TryGetValue(libraryKey, out var existing) ? existing : [];
            var idx = list.FindIndex(p =>
                string.Equals(p.Type, "collection", StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Id, _currentArgs.CollectionId, StringComparison.Ordinal));

            if (idx >= 0)
            {
                list.RemoveAt(idx);
                if (list.Count == 0) map.Remove(libraryKey);
                else map[libraryKey] = list;
                _isPinned = false;
            }
            else
            {
                list.Add(new PinEntry { Type = "collection", Id = _currentArgs.CollectionId, Label = _currentArgs.Title });
                map[libraryKey] = list;
                _isPinned = true;
            }

            await settingsApi.PutSettingAsync(SidebarPinsKey, SerializePinsMap(map));
            UpdatePinButtonVisual();

            // Tell the main window to refresh the sidebar so the pin appears /
            // disappears immediately.
            if (App.MainWindowInstance is MainWindow mw)
            {
                await mw.RefreshSidebarPinsAsync();
            }
        }
        catch { /* non-fatal; button visual just won't update */ }
        finally
        {
            PinButton.IsEnabled = true;
        }
    }

    private void UpdatePinButtonVisual()
    {
        // MDL2: E840 = Pinned (outline) — using it both states with label change
        // to keep the visual compact. Active state uses accent foreground.
        if (_isPinned)
        {
            PinLabel.Text = "Pinned";
            PinIcon.Glyph = "\uE841"; // Pinned (filled)
            PinIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
            PinLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
        }
        else
        {
            PinLabel.Text = "Pin to sidebar";
            PinIcon.Glyph = "\uE840"; // Pinned (outline)
            PinIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
            PinLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
        }
    }

    // ----- JSON helpers for sidebar_pins setting -----

    private sealed class PinEntry
    {
        public string Type { get; set; } = "";
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
    }

    private static bool IsPinnedInMap(string? rawJson, string libraryKey, string collectionId)
    {
        var map = ParsePinsMap(rawJson);
        if (!map.TryGetValue(libraryKey, out var list)) return false;
        return list.Any(p =>
            string.Equals(p.Type, "collection", StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Id, collectionId, StringComparison.Ordinal));
    }

    private static Dictionary<string, List<PinEntry>> ParsePinsMap(string? rawJson)
    {
        var result = new Dictionary<string, List<PinEntry>>();
        if (string.IsNullOrWhiteSpace(rawJson)) return result;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                var entries = new List<PinEntry>();
                foreach (var el in prop.Value.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    entries.Add(new PinEntry
                    {
                        Type = el.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                        Id = el.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                        Label = el.TryGetProperty("label", out var lb) ? lb.GetString() ?? "" : "",
                    });
                }
                if (entries.Count > 0) result[prop.Name] = entries;
            }
        }
        catch { /* malformed — treat as empty map */ }
        return result;
    }

    private static string SerializePinsMap(Dictionary<string, List<PinEntry>> map)
    {
        // Match web shape exactly: camelCase type/id/label keys.
        var serializable = map.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(p => new Dictionary<string, string>
            {
                ["type"] = p.Type,
                ["id"] = p.Id,
                ["label"] = p.Label,
            }).ToList());
        return JsonSerializer.Serialize(serializable);
    }
}
