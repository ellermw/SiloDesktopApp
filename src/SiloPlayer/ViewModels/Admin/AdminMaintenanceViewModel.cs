using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.ViewModels.Admin;

/// <summary>
/// Backing view model for the rewritten AdminMaintenancePage. Mirrors the
/// webui AdminCatalogMaintenance + AdminJobHistory components: catalog
/// import/export workflows backed by background jobs, plus a global job
/// history list across all job types.
/// </summary>
public partial class AdminMaintenanceViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminMaintenanceViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    // Jobs filtered by type, matching the three card sections on the page.
    public ObservableCollection<AdminJob> ImportJobs { get; } = [];
    public ObservableCollection<AdminJob> ExportJobs { get; } = [];
    public ObservableCollection<AdminJob> AllJobs { get; } = [];

    // Detected import sources for the Import dialog (bucket + local file system).
    public ObservableCollection<CatalogSeedImportSource> BucketImportSources { get; } = [];
    public ObservableCollection<CatalogSeedImportSource> LocalImportSources { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isStartingExport;
    [ObservableProperty] private bool _isSubmittingImport;

    // ─── Load / refresh ──────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await Task.WhenAll(
                RefreshImportJobsAsync(),
                RefreshExportJobsAsync(),
                RefreshAllJobsAsync(),
                RefreshBucketSourcesAsync(),
                RefreshLocalSourcesAsync());
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    public async Task RefreshImportJobsAsync()
    {
        try
        {
            var resp = await _adminApi.GetJobsAsync("catalog_import", 50);
            ImportJobs.Clear();
            foreach (var j in resp.Jobs) ImportJobs.Add(j);
        }
        catch (Exception ex) { ErrorMessage = $"Failed to load import jobs: {ex.Message}"; }
    }

    public async Task RefreshExportJobsAsync()
    {
        try
        {
            var resp = await _adminApi.GetJobsAsync("catalog_export", 50);
            ExportJobs.Clear();
            foreach (var j in resp.Jobs) ExportJobs.Add(j);
        }
        catch (Exception ex) { ErrorMessage = $"Failed to load export jobs: {ex.Message}"; }
    }

    public async Task RefreshAllJobsAsync()
    {
        try
        {
            var resp = await _adminApi.GetJobsAsync(null, 50);
            AllJobs.Clear();
            foreach (var j in resp.Jobs) AllJobs.Add(j);
        }
        catch (Exception ex) { ErrorMessage = $"Failed to load job history: {ex.Message}"; }
    }

    public async Task RefreshBucketSourcesAsync()
    {
        try
        {
            var resp = await _adminApi.GetCatalogImportSourcesAsync();
            BucketImportSources.Clear();
            foreach (var s in resp.Sources) BucketImportSources.Add(s);
        }
        catch { /* non-fatal — dialog degrades gracefully */ }
    }

    public async Task RefreshLocalSourcesAsync()
    {
        try
        {
            var resp = await _adminApi.GetLocalImportSourcesAsync();
            LocalImportSources.Clear();
            foreach (var s in resp.Sources) LocalImportSources.Add(s);
        }
        catch { /* non-fatal */ }
    }

    // ─── Actions ─────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task StartExportAsync()
    {
        if (IsStartingExport) return;
        IsStartingExport = true;
        StatusMessage = null;
        try
        {
            // All libraries (null library_ids) mirrors the webui's default "Start Export" button.
            await _adminApi.CreateExportJobAsync(new CatalogSeedExportRequest());
            StatusMessage = "Export job queued.";
            await RefreshExportJobsAsync();
            await RefreshAllJobsAsync();
        }
        catch (Exception ex) { ErrorMessage = $"Failed to start export: {ex.Message}"; }
        finally { IsStartingExport = false; }
    }

    public async Task SubmitImportAsync(CatalogSeedImportRequest request)
    {
        if (IsSubmittingImport) return;
        IsSubmittingImport = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await _adminApi.CreateImportJobAsync(request);
            StatusMessage = "Import job queued.";
            await RefreshImportJobsAsync();
            await RefreshAllJobsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to start import: {ex.Message}";
            throw;
        }
        finally { IsSubmittingImport = false; }
    }

    public async Task PublishExportAsync(string jobId)
    {
        try
        {
            await _adminApi.PublishExportJobAsync(jobId);
            StatusMessage = "Export published.";
            await RefreshExportJobsAsync();
        }
        catch (Exception ex) { ErrorMessage = $"Failed to publish: {ex.Message}"; }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes a job's progress into a 0–100 percentage for the progress bar.
    /// Mirrors webui getJobProgressPercent — when no total is known, shows a
    /// shallow default so the bar isn't completely empty during early "running" frames.
    /// </summary>
    public static double GetJobProgressPercent(AdminJob job)
    {
        if (job.ProgressTotal > 0)
            return Math.Min(100, Math.Max(0, (double)job.ProgressCurrent / job.ProgressTotal * 100));
        return job.Status switch
        {
            "completed" => 100,
            "running" => 12,
            _ => 4,
        };
    }
}
