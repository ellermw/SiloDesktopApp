using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminSectionsViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminSectionsViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    public ObservableCollection<AdminSection> Sections { get; } = [];
    public ObservableCollection<Library> Libraries { get; } = [];

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

    public object BuildCreateBody(string title, string sectionType, int itemLimit, bool featured, bool enabled)
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
        return body;
    }
}
