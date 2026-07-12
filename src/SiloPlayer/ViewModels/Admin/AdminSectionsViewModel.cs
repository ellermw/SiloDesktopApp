using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminSectionsViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminSectionsViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    public ObservableCollection<AdminSection> Sections { get; } = [];
    public ObservableCollection<Library> Libraries { get; } = [];

    /// <summary>Admin collections loaded once for the collection picker + badge labels.</summary>
    public List<LibraryCollection> Collections { get; private set; } = [];

    /// <summary>collection_id -> title. Used to display actual collection names in row badges.</summary>
    public Dictionary<string, string> CollectionLabels { get; private set; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _scope = "home";
    [ObservableProperty] private int? _selectedLibraryId;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            if (Libraries.Count == 0)
            {
                var libs = await _adminApi.GetAdminLibrariesAsync();
                Libraries.Clear();
                foreach (var l in libs) Libraries.Add(l);
            }

            // Load collections for the section form collection picker + badge labels
            if (Collections.Count == 0)
            {
                try
                {
                    var resp = await _adminApi.GetCollectionsAsync();
                    Collections = resp?.Collections ?? [];
                    CollectionLabels = Collections.ToDictionary(c => c.Id.ToString(), c => c.Title);
                }
                catch { Collections = []; CollectionLabels = new(); }
            }

            // B10: pass scope=library with library_id rather than the bare id as the scope.
            var sections = Scope == "library" && SelectedLibraryId.HasValue
                ? await _adminApi.GetSectionsAsync("library", SelectedLibraryId.Value)
                : await _adminApi.GetSectionsAsync("home");
            Sections.Clear();
            foreach (var s in sections) Sections.Add(s);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateSectionAsync(object body)
    {
        try
        {
            await _adminApi.CreateSectionAsync(body);
            await LoadAsync();
            StatusMessage = "Section created.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task UpdateSectionAsync((string Id, object Body) args)
    {
        try
        {
            await _adminApi.UpdateSectionAsync(args.Id, args.Body);
            await LoadAsync();
            StatusMessage = "Section updated.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task DeleteSectionAsync(string id)
    {
        try
        {
            await _adminApi.DeleteSectionAsync(id);
            await LoadAsync();
            StatusMessage = "Section deleted.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task RestoreDefaultsAsync(bool resetProfiles)
    {
        try
        {
            int? libraryId = Scope == "library" ? SelectedLibraryId : null;
            await _adminApi.RestoreSectionDefaultsAsync(Scope, libraryId, resetProfiles);
            await LoadAsync();
            StatusMessage = "Default sections restored.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    public async Task ReorderSectionsAsync(List<string> orderedIds)
    {
        try
        {
            var entries = orderedIds.Select((id, index) => new { id, sort_order = index }).ToList();
            await _adminApi.ReorderSectionsAsync(new { sections = entries });
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    public async Task MoveSectionAsync(AdminSection section, int direction)
    {
        // direction: -1 = up, +1 = down
        var list = Sections.ToList();
        int idx = list.IndexOf(section);
        int newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= list.Count) return;

        // Swap in the observable collection
        Sections.Move(idx, newIdx);

        // Persist new order
        try
        {
            var ids = Sections.Select(s => s.Id).ToList();
            var entries = ids.Select((id, i) => new { id, sort_order = i }).ToList();
            await _adminApi.ReorderSectionsAsync(new { sections = entries });
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    public async Task ToggleEnabledAsync(AdminSection section)
    {
        try
        {
            bool newEnabled = !section.Enabled;
            // NOTE: Dictionary<string, object> instead of anonymous type —
            // .NET 8 Release publish enables trimming, which strips anonymous
            // type property names and silently serializes them as {}. See
            // feedback_build_release memory. Anonymous types here produced
            // empty PUT bodies, which is why "featured toggle doesn't save".
            var body = new Dictionary<string, object>
            {
                ["title"] = section.Title,
                ["section_type"] = section.SectionType,
                ["item_limit"] = section.ItemLimit,
                ["featured"] = section.Featured,
                ["enabled"] = newEnabled,
            };
            await _adminApi.UpdateSectionAsync(section.Id, body);
            section.Enabled = newEnabled;
            StatusMessage = newEnabled ? "Section enabled." : "Section disabled.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    public object BuildCreateBody(string title, string sectionType, int itemLimit, bool featured, bool enabled,
        Dictionary<string, object?>? config = null)
    {
        // Same trimming concern — use a dictionary so the JSON body survives
        // .NET 8 Release publish with trimming enabled.
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["section_type"] = sectionType,
            ["item_limit"] = itemLimit,
            ["featured"] = featured,
            ["enabled"] = enabled,
            ["scope"] = Scope,
            ["library_id"] = Scope == "library" ? SelectedLibraryId : null,
        };
        if (config != null && config.Count > 0)
            body["config"] = config;
        return body;
    }

    // ===== Config extraction helpers (for populating edit form from existing section) =====

    public static string? GetConfigString(AdminSection section, string key)
    {
        if (section.Config == null || !section.Config.TryGetValue(key, out var val)) return null;
        if (val is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String)
            return je.GetString();
        return val?.ToString();
    }

    public static List<int> GetConfigLibraryIds(AdminSection section)
    {
        if (section.Config == null || !section.Config.TryGetValue("library_ids", out var val)) return [];
        if (val is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
            return je.EnumerateArray()
                .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.Number)
                .Select(e => e.GetInt32())
                .ToList();
        return [];
    }

    public static string? GetConfigMediaScope(AdminSection section)
        => GetConfigString(section, "media_scope");

    public static string? GetConfigCollectionId(AdminSection section)
        => GetConfigString(section, "library_collection_id");
}
