using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminSectionsViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;
    private readonly CatalogApi _catalogApi;

    public AdminSectionsViewModel(AdminApi adminApi, CatalogApi catalogApi)
    {
        _adminApi = adminApi;
        _catalogApi = catalogApi;
    }

    public ObservableCollection<AdminSection> Sections { get; } = [];
    public ObservableCollection<Library> Libraries { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _scope = "home";
    [ObservableProperty] private int? _selectedLibraryId;

    // ===== Load =====

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            // Load libraries if not yet loaded
            if (Libraries.Count == 0)
            {
                var libs = await _catalogApi.GetLibrariesAsync();
                Libraries.Clear();
                foreach (var l in libs) Libraries.Add(l);
            }

            // Build scope string: "home" or library ID
            string? scopeParam = Scope == "home" ? "home"
                : SelectedLibraryId.HasValue ? SelectedLibraryId.Value.ToString()
                : null;

            var rawList = await _adminApi.GetSectionsAsync(scopeParam);
            Sections.Clear();
            foreach (var raw in rawList)
            {
                var section = ParseSection(raw);
                if (section != null) Sections.Add(section);
            }
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Create Section =====

    [RelayCommand]
    public async Task CreateSectionAsync(object body)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await _adminApi.CreateSectionAsync(body);
            await LoadAsync();
            StatusMessage = "Section created.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Update Section =====

    [RelayCommand]
    public async Task UpdateSectionAsync((string Id, object Body) args)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await _adminApi.UpdateSectionAsync(args.Id, args.Body);
            await LoadAsync();
            StatusMessage = "Section updated.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Delete Section =====

    [RelayCommand]
    public async Task DeleteSectionAsync(string id)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await _adminApi.DeleteSectionAsync(id);
            await LoadAsync();
            StatusMessage = "Section deleted.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Restore Defaults =====

    [RelayCommand]
    public async Task RestoreDefaultsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await _adminApi.RestoreSectionDefaultsAsync();
            await LoadAsync();
            StatusMessage = "Default sections restored.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Reorder =====

    public async Task ReorderSectionsAsync(List<string> orderedIds)
    {
        try
        {
            var entries = orderedIds.Select((id, index) => new { id, sort_order = index }).ToList();
            await _adminApi.ReorderSectionsAsync(new { sections = entries });
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Toggle Enabled =====

    public async Task ToggleEnabledAsync(AdminSection section)
    {
        try
        {
            bool newEnabled = !section.Enabled;
            var body = new
            {
                title = section.Title,
                section_type = section.SectionType,
                item_limit = section.ItemLimit,
                featured = section.Featured,
                enabled = newEnabled
            };
            await _adminApi.UpdateSectionAsync(section.Id, body);
            section.Enabled = newEnabled;
            StatusMessage = newEnabled ? "Section enabled." : "Section disabled.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Build Create/Update Body =====

    public object BuildCreateBody(string title, string sectionType, int itemLimit, bool featured, bool enabled)
    {
        return new
        {
            title,
            section_type = sectionType,
            item_limit = itemLimit,
            featured,
            enabled,
            scope = Scope,
            library_id = Scope == "library" ? SelectedLibraryId : null
        };
    }

    // ===== Parse raw object from API =====

    private static AdminSection? ParseSection(object raw)
    {
        try
        {
            string json = JsonSerializer.Serialize(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            string title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
            string sectionType = root.TryGetProperty("section_type", out var stEl) ? stEl.GetString() ?? "" : "";
            int itemLimit = root.TryGetProperty("item_limit", out var ilEl) ? ilEl.GetInt32() : 20;
            bool featured = root.TryGetProperty("featured", out var featEl) && featEl.GetBoolean();
            bool enabled = !root.TryGetProperty("enabled", out var enEl) || enEl.GetBoolean();
            string? scope = root.TryGetProperty("scope", out var scopeEl) ? scopeEl.GetString() : null;
            int? libraryId = root.TryGetProperty("library_id", out var libEl) && libEl.ValueKind == JsonValueKind.Number
                ? libEl.GetInt32() : null;
            int sortOrder = root.TryGetProperty("sort_order", out var soEl) ? soEl.GetInt32() : 0;

            return new AdminSection
            {
                Id = id,
                Title = title,
                SectionType = sectionType,
                ItemLimit = itemLimit,
                Featured = featured,
                Enabled = enabled,
                Scope = scope,
                LibraryId = libraryId,
                SortOrder = sortOrder
            };
        }
        catch { return null; }
    }
}
